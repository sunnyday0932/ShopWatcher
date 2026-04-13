using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ShopWatcher.Data;
using ShopWatcher.Data.Models;
using Telegram.Bot;
using Telegram.Bot.Polling;
using Telegram.Bot.Requests;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

namespace ShopWatcher.Services;

public class TelegramBotService(
    IServiceScopeFactory scopeFactory,
    ITelegramBotClient botClient,
    IReservationRunner reservationRunner) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var receiverOptions = new ReceiverOptions { AllowedUpdates = [UpdateType.Message] };
        await botClient.ReceiveAsync(
            (_, update, ct) => HandleUpdateAsync(update, ct),
            HandleErrorAsync,
            receiverOptions,
            stoppingToken);
    }

    public async Task HandleUpdateAsync(Update update, CancellationToken ct)
    {
        if (update.Message?.Text is not { } text) return;
        var chatId = update.Message.Chat.Id;

        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        if (text.StartsWith("/watch "))
            await HandleWatchAsync(db, chatId, text["/watch ".Length..].Trim(), ct);
        else if (text.StartsWith("/unwatch "))
            await HandleUnwatchAsync(db, chatId, text["/unwatch ".Length..].Trim(), ct);
        else if (text == "/list")
            await HandleListAsync(db, chatId, ct);
        else if (text.StartsWith("/reservation_add "))
            await HandleReservationAddAsync(db, chatId, text["/reservation_add ".Length..].Trim(), ct);
        else if (text.StartsWith("/reservation_remove "))
            await HandleReservationRemoveAsync(db, chatId, text["/reservation_remove ".Length..].Trim(), ct);
        else if (text == "/reservation_list")
            await HandleReservationListAsync(db, chatId, ct);
        else if (text.StartsWith("/reservation_activate "))
            await HandleReservationActivateAsync(db, chatId, text["/reservation_activate ".Length..].Trim(), ct);
        else if (text.StartsWith("/reservation_deactivate "))
            await HandleReservationDeactivateAsync(db, chatId, text["/reservation_deactivate ".Length..].Trim(), ct);
        else if (text == "/triggerbooking")
            await HandleTriggerBookingAsync(chatId, ct);
    }

    private async Task HandleWatchAsync(AppDbContext db, long chatId, string url, CancellationToken ct)
    {
        var existing = await db.WatchItems.FirstOrDefaultAsync(w => w.ChatId == chatId && w.Url == url, ct);
        if (existing is not null)
        {
            if (existing.IsActive)
            {
                await SendMessageAsync(chatId, $"⚠️ 這個商品已經在監控清單中了：\n{url}", ct);
                return;
            }

            // Re-activate a previously unwatched item
            existing.IsActive = true;
            await db.SaveChangesAsync(ct);
            await SendMessageAsync(chatId, $"✅ 已重新開始監控：\n{url}", ct);
            return;
        }

        db.WatchItems.Add(new WatchItem { ChatId = chatId, Url = url, IsActive = true });
        await db.SaveChangesAsync(ct);
        await SendMessageAsync(chatId, $"✅ 已開始監控：\n{url}", ct);
    }

    private async Task HandleUnwatchAsync(AppDbContext db, long chatId, string url, CancellationToken ct)
    {
        var item = await db.WatchItems.FirstOrDefaultAsync(w => w.ChatId == chatId && w.Url == url && w.IsActive, ct);
        if (item is null)
        {
            await SendMessageAsync(chatId, $"⚠️ 找不到此商品的監控：\n{url}", ct);
            return;
        }

        item.IsActive = false;
        await db.SaveChangesAsync(ct);
        await SendMessageAsync(chatId, $"🛑 已停止監控：\n{url}", ct);
    }

    private async Task HandleListAsync(AppDbContext db, long chatId, CancellationToken ct)
    {
        var items = await db.WatchItems
            .Where(w => w.ChatId == chatId && w.IsActive)
            .ToListAsync(ct);

        if (items.Count == 0)
        {
            await SendMessageAsync(chatId, "目前沒有監控中的商品。", ct);
            return;
        }

        var list = string.Join("\n", items.Select((w, i) => $"{i + 1}. {w.Url}"));
        await SendMessageAsync(chatId, $"📋 監控清單：\n{list}", ct);
    }

    private async Task HandleReservationAddAsync(AppDbContext db, long chatId, string args, CancellationToken ct)
    {
        var parts = args.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 4)
        {
            await SendMessageAsync(chatId, "用法: /reservation_add <name> <phone> <partysize> <url> [lookahead]", ct);
            return;
        }

        if (!int.TryParse(parts[2], out var partySize))
        {
            await SendMessageAsync(chatId, "partysize 必須是數字", ct);
            return;
        }

        var url = parts[3];
        var lookahead = parts.Length > 4 && int.TryParse(parts[4], out var la) ? la : 14;

        var existing = await db.ReservationConfigs.FirstOrDefaultAsync(r => r.ChatId == chatId && r.RestaurantUrl == url, ct);
        if (existing is not null)
        {
            await SendMessageAsync(chatId, $"此餐廳已經在訂位清單中：\n{url}", ct);
            return;
        }

        db.ReservationConfigs.Add(new ReservationConfig
        {
            ChatId = chatId,
            Name = parts[0],
            Phone = parts[1],
            PartySize = partySize,
            RestaurantUrl = url,
            LookAheadDays = lookahead,
            IsActive = true
        });
        await db.SaveChangesAsync(ct);
        await SendMessageAsync(chatId, $"已新增訂位設定：\n{url}", ct);
    }

    private async Task HandleReservationRemoveAsync(AppDbContext db, long chatId, string url, CancellationToken ct)
    {
        var config = await db.ReservationConfigs.FirstOrDefaultAsync(r => r.ChatId == chatId && r.RestaurantUrl == url, ct);
        if (config is null)
        {
            await SendMessageAsync(chatId, $"找不到此餐廳的訂位：\n{url}", ct);
            return;
        }

        config.IsActive = false;
        await db.SaveChangesAsync(ct);
        await SendMessageAsync(chatId, $"已停止訂位：\n{url}", ct);
    }

    private async Task HandleReservationListAsync(AppDbContext db, long chatId, CancellationToken ct)
    {
        var configs = await db.ReservationConfigs
            .Where(r => r.ChatId == chatId && r.IsActive)
            .ToListAsync(ct);

        if (configs.Count == 0)
        {
            await SendMessageAsync(chatId, "目前沒有訂位設定。", ct);
            return;
        }

        var list = string.Join("\n", configs.Select((c, i) =>
            $"{i + 1}. {c.Name} ({c.PartySize}人) - {c.RestaurantUrl}"));
        await SendMessageAsync(chatId, $"訂位清單：\n{list}", ct);
    }

    private async Task HandleReservationActivateAsync(AppDbContext db, long chatId, string url, CancellationToken ct)
    {
        var config = await db.ReservationConfigs.FirstOrDefaultAsync(r => r.ChatId == chatId && r.RestaurantUrl == url, ct);
        if (config is null)
        {
            await SendMessageAsync(chatId, $"找不到此餐廳的訂位：\n{url}", ct);
            return;
        }

        config.IsActive = true;
        await db.SaveChangesAsync(ct);
        await SendMessageAsync(chatId, $"已啟用訂位：\n{url}", ct);
    }

    private async Task HandleReservationDeactivateAsync(AppDbContext db, long chatId, string url, CancellationToken ct)
    {
        var config = await db.ReservationConfigs.FirstOrDefaultAsync(r => r.ChatId == chatId && r.RestaurantUrl == url, ct);
        if (config is null)
        {
            await SendMessageAsync(chatId, $"找不到此餐廳的訂位：\n{url}", ct);
            return;
        }

        config.IsActive = false;
        await db.SaveChangesAsync(ct);
        await SendMessageAsync(chatId, $"已暫停訂位：\n{url}", ct);
    }

    private async Task HandleTriggerBookingAsync(long chatId, CancellationToken ct)
    {
        try
        {
            await SendMessageAsync(chatId, "開始手動檢查訂位...", ct);
            await reservationRunner.RunChecksAsync(ct);
            await SendMessageAsync(chatId, "手動檢查完成", ct);
        }
        catch (Exception ex)
        {
            await SendMessageAsync(chatId, $"檢查失敗：{ex.Message}", ct);
        }
    }

    private async Task SendMessageAsync(long chatId, string text, CancellationToken ct) =>
        await botClient.SendRequest(new SendMessageRequest { ChatId = chatId, Text = text }, ct);

    private static Task HandleErrorAsync(ITelegramBotClient _, Exception ex, CancellationToken __)
    {
        Console.Error.WriteLine($"[TelegramBot] Error: {ex.Message}");
        return Task.CompletedTask;
    }
}
