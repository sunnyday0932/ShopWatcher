using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ShopWatcher.Data;
using ShopWatcher.Scrapers;
using Telegram.Bot;
using Telegram.Bot.Requests;

namespace ShopWatcher.Services;

public class StockCheckerService(
    IServiceScopeFactory scopeFactory,
    IEnumerable<IScraper> scrapers,
    ITelegramBotClient botClient,
    TimeSpan interval) : BackgroundService
{
    public StockCheckerService(IServiceScopeFactory scopeFactory, IEnumerable<IScraper> scrapers, ITelegramBotClient botClient)
        : this(scopeFactory, scrapers, botClient, TimeSpan.FromSeconds(30)) { }

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

        foreach (var url in uniqueUrls)
        {
            var scraper = scrapers.FirstOrDefault(s => s.CanHandle(url));
            if (scraper is null) continue;

            try
            {
                stockResults[url] = await scraper.IsInStockAsync(url, ct);
            }
            catch (Exception ex)
            {
                // 例外時不寫入 stockResults，保持「未知」語意，不發通知
                Console.Error.WriteLine($"[StockChecker] Error checking {url}: {ex.Message}");
            }
        }

        foreach (var item in activeItems)
        {
            if (!stockResults.TryGetValue(item.Url, out var inStock) || !inStock) continue;

            await botClient.SendRequest(new SendMessageRequest
            {
                ChatId = item.ChatId,
                Text = $"✅ 補貨通知！\n{item.Url}\n\n商品現在有貨，快去搶購！"
            }, ct);
        }
    }
}
