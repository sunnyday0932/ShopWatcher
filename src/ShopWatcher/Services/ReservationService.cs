using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ShopWatcher.Bookers;
using ShopWatcher.Data;

namespace ShopWatcher.Services;

public class ReservationService(
    IReservationRunner runner,
    ILogger<ReservationService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var now = DateTime.UtcNow;
            var next = now.Date.AddDays(1); // 明日 00:00 UTC
            var delay = next - now;

            logger.LogInformation("下一次預訂檢查時間：{NextBookingTime}", next);
            await Task.Delay(delay, stoppingToken);

            if (!stoppingToken.IsCancellationRequested)
                await runner.RunChecksAsync(stoppingToken);
        }
    }

    public class DefaultReservationRunner(
        IServiceScopeFactory scopeFactory,
        IEnumerable<IReservationBooker> bookers,
        ILogger<DefaultReservationRunner> logger) : IReservationRunner
    {
        public async Task RunChecksAsync(CancellationToken ct)
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var activeConfigs = await db.ReservationConfigs
                .Where(r => r.IsActive)
                .ToListAsync(ct);

            if (activeConfigs.Count == 0)
            {
                logger.LogDebug("沒有啟用的訂位設定");
                return;
            }

            logger.LogInformation("開始訂位檢查，共 {Count} 個設定", activeConfigs.Count);

            foreach (var config in activeConfigs)
            {
                var booker = bookers.FirstOrDefault(b => b.CanHandle(config.RestaurantUrl));
                if (booker is null)
                {
                    logger.LogWarning("找不到對應的 booker，URL: {Url}", config.RestaurantUrl);
                    continue;
                }

                try
                {
                    for (var daysAhead = 0; daysAhead < config.LookAheadDays; daysAhead++)
                    {
                        var date = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(daysAhead));
                        var info = new ReservationInfo(
                            config.Name,
                            config.Phone,
                            config.PartySize,
                            date);

                        var result = await booker.BookAsync(config.RestaurantUrl, info, ct);

                        if (result.Success)
                        {
                            config.LastBookedAt = DateTime.UtcNow;
                            logger.LogInformation(
                                "訂位成功 ChatId={ChatId}, 日期={Date}, 時間={Time}",
                                config.ChatId, result.BookedDate, result.BookedTime);
                            break; // 訂位成功，停止查看後續日期
                        }
                        else if (!result.DryRun)
                        {
                            logger.LogWarning(
                                "訂位失敗 ChatId={ChatId}, 日期={Date}, 原因: {Error}",
                                config.ChatId, date, result.ErrorMessage);
                        }
                    }
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "處理訂位設定時發生錯誤 ChatId={ChatId}", config.ChatId);
                }
            }

            await db.SaveChangesAsync(ct);
        }
    }
}
