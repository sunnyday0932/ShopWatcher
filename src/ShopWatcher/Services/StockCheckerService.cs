using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ShopWatcher.Data;
using ShopWatcher.Scrapers;
using Telegram.Bot;
using Telegram.Bot.Requests;

namespace ShopWatcher.Services;

public class StockCheckerService(
    IServiceScopeFactory scopeFactory,
    IEnumerable<IScraper> scrapers,
    ITelegramBotClient botClient,
    ILogger<StockCheckerService> logger,
    TimeSpan interval) : BackgroundService
{
    public StockCheckerService(IServiceScopeFactory scopeFactory, IEnumerable<IScraper> scrapers, ITelegramBotClient botClient, ILogger<StockCheckerService> logger)
        : this(scopeFactory, scrapers, botClient, logger, TimeSpan.FromSeconds(30)) { }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            await CheckOnceAsync(stoppingToken);
            await Task.Delay(interval, stoppingToken);
        }
    }

    public async Task CheckOnceAsync(CancellationToken ct)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var activeItems = await db.WatchItems
            .Where(w => w.IsActive)
            .ToListAsync(ct);

        if (activeItems.Count == 0) return;

        var uniqueUrls = activeItems.Select(w => w.Url).Distinct().ToList();
        var stockResults = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);

        logger.LogDebug("開始庫存檢查，共 {Count} 個不重複 URL", uniqueUrls.Count);

        foreach (var url in uniqueUrls)
        {
            var scraper = scrapers.FirstOrDefault(s => s.CanHandle(url));
            if (scraper is null)
            {
                logger.LogDebug("找不到對應的 scraper，略過 {Url}", url);
                continue;
            }

            try
            {
                var inStock = await scraper.IsInStockAsync(url, ct);
                stockResults[url] = inStock;
                if (inStock)
                    logger.LogDebug("{Url} → 有庫存", url);
                else
                    logger.LogDebug("{Url} → 無庫存", url);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "檢查 {Url} 時發生錯誤", url);
            }
        }

        foreach (var item in activeItems)
        {
            if (!stockResults.TryGetValue(item.Url, out var inStock) || !inStock) continue;

            logger.LogInformation("{Url} 補貨，通知 ChatId {ChatId}", item.Url, item.ChatId);
            await botClient.SendRequest(new SendMessageRequest
            {
                ChatId = item.ChatId,
                Text = $"✅ 補貨通知！\n{item.Url}\n\n商品現在有貨，快去搶購！"
            }, ct);
        }
    }
}
