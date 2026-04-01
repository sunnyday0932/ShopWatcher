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

public class TelegramBotService(IServiceScopeFactory scopeFactory, ITelegramBotClient botClient) : BackgroundService
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

    private async Task SendMessageAsync(long chatId, string text, CancellationToken ct) =>
        await botClient.SendRequest(new SendMessageRequest { ChatId = chatId, Text = text }, ct);

    private static Task HandleErrorAsync(ITelegramBotClient _, Exception ex, CancellationToken __)
    {
        Console.Error.WriteLine($"[TelegramBot] Error: {ex.Message}");
        return Task.CompletedTask;
    }
}
