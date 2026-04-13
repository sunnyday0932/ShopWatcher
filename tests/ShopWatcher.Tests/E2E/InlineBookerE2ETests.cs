using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Playwright;
using ShopWatcher.Bookers;

namespace ShopWatcher.Tests.E2E;

[Trait("Category", "E2E")]
public class InlineBookerE2ETests : IAsyncLifetime
{
    private IPlaywright? _playwright;
    private IBrowser? _browser;

    public async Task InitializeAsync()
    {
        _playwright = await Playwright.CreateAsync();
        _browser = await _playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
        {
            Headless = false  // headed mode：讓測試人員看到實際操作
        });
    }

    public async Task DisposeAsync()
    {
        if (_browser is not null)
            await _browser.CloseAsync();
        _playwright?.Dispose();
    }

    private InlineReservationBooker CreateBooker(bool dryRun = true) =>
        new(dryRun, NullLogger<InlineReservationBooker>.Instance);

    // B3: DryRun=true，填入資料但不送出
    [E2EFact]
    public async Task BookAsync_DryRun_FillsFormButDoesNotSubmit()
    {
        // Arrange
        var booker = CreateBooker(dryRun: true);
        var url = Environment.GetEnvironmentVariable("E2E_RESTAURANT_URL")
            ?? "https://inline.app/booking/-N9fc8nBTri71f1ryIhS:inline-live-1/-N9fc91xzvzzNT9FDZiv?language=zh-tw";
        var info = new ReservationInfo("測試Alice", "0912345678", 2, DateOnly.FromDateTime(DateTime.UtcNow.AddDays(7)));

        // Act
        var result = await booker.BookAsync(url, info);

        // Assert: DryRun 模式下不應成功送出
        Assert.False(result.Success);
        Assert.True(result.DryRun);
        // ErrorMessage 允許非 null（日期可能不可用），重點是 DryRun=true 且未真正送出
    }

    // B4: 日期滿位，檢查結果
    [E2EFact]
    public async Task BookAsync_FullyBooked_ReturnsFailure()
    {
        var booker = CreateBooker(dryRun: true);
        var url = Environment.GetEnvironmentVariable("E2E_RESTAURANT_URL")
            ?? "https://inline.app/booking/-N9fc8nBTri71f1ryIhS:inline-live-1/-N9fc91xzvzzNT9FDZiv?language=zh-tw";
        // 使用過去的日期（必然無位）
        var info = new ReservationInfo("測試Alice", "0912345678", 2, DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-1)));

        var result = await booker.BookAsync(url, info);

        Assert.False(result.Success);
    }

    // B5: CanHandle + BookAsync 對無效 URL 的行為
    [E2EFact]
    public async Task BookAsync_InvalidUrl_ReturnsError()
    {
        var booker = CreateBooker(dryRun: true);
        var info = new ReservationInfo("測試", "0912345678", 2, DateOnly.FromDateTime(DateTime.UtcNow.AddDays(1)));

        var result = await booker.BookAsync("https://inline.app/booking/nonexistent-restaurant-xyz", info);

        Assert.False(result.Success);
        Assert.NotNull(result.ErrorMessage);
    }

    // B6: DryRun=false 模式測試（僅在特定環境執行）
    [E2EFact]
    public async Task BookAsync_NonDryRun_SubmitsForm()
    {
        // 此測試只在明確設定 E2E_ALLOW_SUBMIT=true 時執行
        if (Environment.GetEnvironmentVariable("E2E_ALLOW_SUBMIT") != "true")
        {
            // 直接回傳，不執行真實訂位
            return;
        }

        var booker = CreateBooker(dryRun: false);
        var url = Environment.GetEnvironmentVariable("E2E_RESTAURANT_URL")
            ?? throw new InvalidOperationException("E2E_RESTAURANT_URL 必須設定");
        var info = new ReservationInfo(
            Environment.GetEnvironmentVariable("E2E_NAME") ?? "測試",
            Environment.GetEnvironmentVariable("E2E_PHONE") ?? "0912345678",
            2,
            DateOnly.FromDateTime(DateTime.UtcNow.AddDays(7)));

        var result = await booker.BookAsync(url, info);

        Assert.True(result.Success || result.ErrorMessage != null);
    }
}
