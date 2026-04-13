using Microsoft.Extensions.Logging;
using Microsoft.Playwright;

namespace ShopWatcher.Bookers;

public class InlineReservationBooker(bool dryRun, ILogger<InlineReservationBooker> logger) : IReservationBooker
{
    public bool CanHandle(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) &&
        (uri.Host.Equals("inline.app", StringComparison.OrdinalIgnoreCase) ||
         uri.Host.EndsWith(".inline.app", StringComparison.OrdinalIgnoreCase));

    public async Task<BookingResult> BookAsync(string url, ReservationInfo info, CancellationToken ct = default)
    {
        logger.LogInformation("開始訂位流程 URL={Url} Date={Date} DryRun={DryRun}", url, info.Date, dryRun);

        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
        {
            Headless = false,
            SlowMo = 600
        });

        var page = await browser.NewPageAsync();

        try
        {
            // 1. 導航到訂位頁面
            await page.GotoAsync(url, new PageGotoOptions { Timeout = 30_000 });
            await page.WaitForLoadStateAsync(LoadState.NetworkIdle, new PageWaitForLoadStateOptions { Timeout = 30_000 });
            logger.LogInformation("頁面載入完成");

            // 2. 選擇人數
            await SelectPartySizeAsync(page, info.PartySize);

            // 3. 選擇日期（若時段已可見則略過）
            var slotsAlreadyVisible = await TimeSlotsVisibleAsync(page);
            if (!slotsAlreadyVisible)
            {
                var dateSelected = await SelectDateAsync(page, info.Date);
                if (!dateSelected)
                    logger.LogWarning("找不到日期 {Date}，嘗試繼續看是否有預設時段", info.Date);
            }
            else
            {
                logger.LogInformation("時段已預設顯示，略過日期選擇");
            }

            // 4. 選擇時段
            var (timeSelected, bookedTime, wasWaitlist) = await SelectTimeSlotAsync(page);
            if (!timeSelected)
            {
                logger.LogWarning("沒有可用時段");
                return new BookingResult(false, wasWaitlist, dryRun, null, null, "沒有可用時段");
            }

            // 5. 填寫個人資料
            await FillPersonalInfoAsync(page, info);

            // 6. DryRun：不送出，直接回傳
            if (dryRun)
            {
                logger.LogInformation("DryRun 模式：停在確認頁，不送出訂位");
                await Task.Delay(3000, ct); // 讓使用者看到畫面
                return new BookingResult(false, false, DryRun: true, info.Date, bookedTime, null);
            }

            // 7. 送出訂位
            await SubmitBookingAsync(page);
            logger.LogInformation("訂位送出成功 Date={Date} Time={Time}", info.Date, bookedTime);
            return new BookingResult(true, false, DryRun: false, info.Date, bookedTime, null);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "訂位流程發生錯誤");
            return new BookingResult(false, false, dryRun, null, null, ex.Message);
        }
    }

    private async Task<bool> TimeSlotsVisibleAsync(IPage page)
    {
        var anySlot = page.Locator("button:has-text(':'), [class*='time']:has-text(':')").First;
        return await anySlot.IsVisibleAsync();
    }

    private async Task SelectPartySizeAsync(IPage page, int partySize)
    {
        logger.LogInformation("設定人數：{PartySize}", partySize);

        // 方法 1: select 元素（inline.app 常用下拉）
        var selectEl = page.Locator("select[name*='size'], select[name*='party'], select[name*='adult'], select[id*='guest']").First;
        if (await selectEl.IsVisibleAsync())
        {
            await selectEl.SelectOptionAsync(partySize.ToString());
            logger.LogDebug("透過 select 設定人數：{PartySize}", partySize);
            await page.WaitForLoadStateAsync(LoadState.NetworkIdle, new PageWaitForLoadStateOptions { Timeout = 5_000 });
            return;
        }

        // 方法 2: 人數數字卡片按鈕
        var partySizeButton = page.Locator($"[data-number='{partySize}'], button:has-text('{partySize}人')").First;
        if (await partySizeButton.IsVisibleAsync())
        {
            await partySizeButton.ClickAsync();
            logger.LogDebug("點選人數卡片：{PartySize}", partySize);
            await page.WaitForLoadStateAsync(LoadState.NetworkIdle, new PageWaitForLoadStateOptions { Timeout = 5_000 });
            return;
        }

        // 方法 3: stepper（+ / -）
        var plusButton = page.Locator("button[aria-label*='增加'], button:has-text('+')").First;
        var minusButton = page.Locator("button[aria-label*='減少'], button:has-text('-')").First;
        if (await plusButton.IsVisibleAsync())
        {
            for (var i = 0; i < 10; i++)
            {
                if (!await minusButton.IsEnabledAsync()) break;
                await minusButton.ClickAsync();
            }
            for (var i = 1; i < partySize; i++)
                await plusButton.ClickAsync();
            logger.LogDebug("用 stepper 設定人數：{PartySize}", partySize);
        }

        await page.WaitForLoadStateAsync(LoadState.NetworkIdle, new PageWaitForLoadStateOptions { Timeout = 5_000 });
    }

    private async Task<bool> SelectDateAsync(IPage page, DateOnly targetDate)
    {
        logger.LogInformation("選擇日期：{Date}", targetDate);
        await Task.Delay(1000); // 等日曆渲染

        // 方法 1: data-date attribute（最可靠）
        var isoDate = targetDate.ToString("yyyy-MM-dd");
        var dataDateEl = page.Locator($"[data-date='{isoDate}']:not([disabled]):not([aria-disabled='true'])").First;
        if (await dataDateEl.IsVisibleAsync())
        {
            await dataDateEl.ClickAsync();
            logger.LogDebug("點選日期（data-date）：{Date}", isoDate);
            await page.WaitForLoadStateAsync(LoadState.NetworkIdle, new PageWaitForLoadStateOptions { Timeout = 10_000 });
            return true;
        }

        // 方法 2: aria-label 含完整日期
        var ariaPatterns = new[]
        {
            isoDate,
            $"{targetDate.Year}/{targetDate.Month}/{targetDate.Day}",
            $"{targetDate.Month}月{targetDate.Day}日",
        };
        foreach (var p in ariaPatterns)
        {
            var ariaEl = page.Locator($"[aria-label*='{p}']:not([disabled]):not([aria-disabled='true'])").First;
            if (await ariaEl.IsVisibleAsync())
            {
                await ariaEl.ClickAsync();
                logger.LogDebug("點選日期（aria-label）：{Pattern}", p);
                await page.WaitForLoadStateAsync(LoadState.NetworkIdle, new PageWaitForLoadStateOptions { Timeout = 10_000 });
                return true;
            }
        }

        // 方法 3: 文字搜尋（月/日 格式）
        var textPatterns = new[]
        {
            $"{targetDate.Month:D2}/{targetDate.Day:D2}",
            $"{targetDate.Month}/{targetDate.Day}",
            $"{targetDate.Month}月{targetDate.Day}日",
        };
        foreach (var pattern in textPatterns)
        {
            var dateEl = page.Locator($"button:has-text('{pattern}'):not([disabled]), [role='button']:has-text('{pattern}'):not([aria-disabled='true'])").First;
            if (await dateEl.IsVisibleAsync())
            {
                await dateEl.ClickAsync();
                logger.LogDebug("點選日期（text）：{Pattern}", pattern);
                await page.WaitForLoadStateAsync(LoadState.NetworkIdle, new PageWaitForLoadStateOptions { Timeout = 10_000 });
                return true;
            }
        }

        // 方法 4: 找日曆中只包含當天日期數字的可點擊元素
        // inline.app 日曆通常顯示「週幾\n日期數字」的格式
        var dayNum = targetDate.Day.ToString();
        var calendarCells = page.Locator(
            $"td:has-text('{dayNum}'):not(.disabled), " +
            $".calendar-day:has-text('{dayNum}'):not(.disabled), " +
            $"[class*='day']:has-text('{dayNum}'):not(.disabled)");
        var cellCount = await calendarCells.CountAsync();
        logger.LogDebug("找到 {Count} 個含數字 {Day} 的日曆元素", cellCount, dayNum);
        for (var i = 0; i < cellCount; i++)
        {
            var cell = calendarCells.Nth(i);
            var text = await cell.InnerTextAsync();
            if (text.Trim() == dayNum || text.Contains(dayNum))
            {
                await cell.ClickAsync();
                logger.LogDebug("點選日期（calendar cell）：{Text}", text);
                await page.WaitForLoadStateAsync(LoadState.NetworkIdle, new PageWaitForLoadStateOptions { Timeout = 10_000 });
                return true;
            }
        }

        // 失敗時截圖以便除錯
        var screenshotPath = Path.Combine(Path.GetTempPath(), $"inline-date-fail-{DateTime.Now:yyyyMMddHHmmss}.png");
        await page.ScreenshotAsync(new PageScreenshotOptions { Path = screenshotPath });
        logger.LogWarning("找不到日期 {Date}，截圖存至 {Path}", targetDate, screenshotPath);

        return false;
    }

    private async Task<(bool selected, TimeOnly? time, bool wasWaitlist)> SelectTimeSlotAsync(IPage page)
    {
        logger.LogInformation("選擇時段");
        await Task.Delay(1000); // 等待時段載入

        // 找有效時段（非候補、非滿位）
        // inline.app 通常用 data-available, data-type, 或 class 區分
        var availableSlots = page.Locator(
            "button.time-slot:not(.disabled):not(.waitlist), " +
            "[role='button'].time-slot:not([aria-disabled='true']), " +
            "button:has-text('確認位'):not([disabled]), " +
            "[data-type='confirmed']:not([disabled])");

        var count = await availableSlots.CountAsync();
        if (count > 0)
        {
            var slot = availableSlots.First;
            var text = await slot.InnerTextAsync();
            logger.LogInformation("找到確認位時段：{Text}", text);
            await slot.ClickAsync();

            // 解析時間
            TimeOnly? parsedTime = null;
            if (TimeOnly.TryParse(text.Trim(), out var t))
                parsedTime = t;

            await page.WaitForLoadStateAsync(LoadState.NetworkIdle, new PageWaitForLoadStateOptions { Timeout = 10_000 });
            return (true, parsedTime, false);
        }

        // 檢查是否只有候補
        var waitlistSlots = page.Locator(
            "button.time-slot.waitlist, " +
            "[data-type='waitlist'], " +
            "button:has-text('候補')");
        if (await waitlistSlots.CountAsync() > 0)
        {
            logger.LogInformation("只有候補位");
            return (false, null, true);
        }

        // 最後嘗試：點選任何可見且可點擊的時段按鈕
        var anySlot = page.Locator("button:has-text(':'):not([disabled])").First;
        if (await anySlot.IsVisibleAsync())
        {
            var text = await anySlot.InnerTextAsync();
            logger.LogInformation("找到時段（fallback）：{Text}", text);
            await anySlot.ClickAsync();
            TimeOnly? parsedTime = null;
            if (TimeOnly.TryParse(text.Trim(), out var t))
                parsedTime = t;
            await page.WaitForLoadStateAsync(LoadState.NetworkIdle, new PageWaitForLoadStateOptions { Timeout = 10_000 });
            return (true, parsedTime, false);
        }

        return (false, null, false);
    }

    private async Task FillPersonalInfoAsync(IPage page, ReservationInfo info)
    {
        logger.LogInformation("填寫個人資料 Name={Name}", info.Name);
        await Task.Delay(500);

        // 姓名欄位
        var nameSelectors = new[] { "input[placeholder*='姓名'], input[name*='name'], input[aria-label*='姓名']" };
        foreach (var sel in nameSelectors)
        {
            var el = page.Locator(sel).First;
            if (await el.IsVisibleAsync())
            {
                await el.FillAsync(info.Name);
                logger.LogDebug("填入姓名");
                break;
            }
        }

        // 電話欄位
        var phoneSelectors = new[] { "input[placeholder*='電話'], input[placeholder*='手機'], input[name*='phone'], input[type='tel']" };
        foreach (var sel in phoneSelectors)
        {
            var el = page.Locator(sel).First;
            if (await el.IsVisibleAsync())
            {
                await el.FillAsync(info.Phone);
                logger.LogDebug("填入電話");
                break;
            }
        }

        await Task.Delay(500);
    }

    private async Task SubmitBookingAsync(IPage page)
    {
        logger.LogInformation("送出訂位");

        var submitSelectors = new[]
        {
            "button[type='submit']:has-text('確認'), button:has-text('送出預訂'), button:has-text('完成預訂'), button:has-text('Confirm')"
        };

        foreach (var sel in submitSelectors)
        {
            var el = page.Locator(sel).First;
            if (await el.IsVisibleAsync())
            {
                await el.ClickAsync();
                await page.WaitForLoadStateAsync(LoadState.NetworkIdle, new PageWaitForLoadStateOptions { Timeout = 15_000 });
                return;
            }
        }

        throw new InvalidOperationException("找不到送出按鈕");
    }
}
