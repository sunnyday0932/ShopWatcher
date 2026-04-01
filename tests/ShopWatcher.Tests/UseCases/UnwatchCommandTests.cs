using Microsoft.EntityFrameworkCore;
using NSubstitute;
using ShopWatcher.Data;
using ShopWatcher.Data.Models;
using ShopWatcher.Services;
using Telegram.Bot;
using Telegram.Bot.Requests;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

namespace ShopWatcher.Tests.UseCases;

public class UnwatchCommandTests
{
    private static AppDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        var db = new AppDbContext(options);
        db.Database.EnsureCreated();
        return db;
    }

    private static Update MakeUnwatchUpdate(long chatId, string url) => new()
    {
        Message = new Message
        {
            Chat = new Chat { Id = chatId },
            Text = $"/unwatch {url}",
            Entities = [new MessageEntity { Type = MessageEntityType.BotCommand, Offset = 0, Length = 8 }]
        }
    };

    [Fact]
    public async Task UnwatchCommand_DeactivatesItem()
    {
        var db = CreateDb();
        var url = "https://24h.pchome.com.tw/prod/TEST-001";
        db.WatchItems.Add(new WatchItem { ChatId = 123, Url = url, IsActive = true });
        await db.SaveChangesAsync();

        var botClient = Substitute.For<ITelegramBotClient>();
        var service = new TelegramBotService(db, botClient);

        await service.HandleUpdateAsync(MakeUnwatchUpdate(123, url), CancellationToken.None);

        var item = await db.WatchItems.SingleAsync();
        Assert.False(item.IsActive);
    }

    [Fact]
    public async Task UnwatchCommand_UrlNotFound_RepliesWithErrorMessage()
    {
        var db = CreateDb();
        var botClient = Substitute.For<ITelegramBotClient>();
        var service = new TelegramBotService(db, botClient);

        await service.HandleUpdateAsync(
            MakeUnwatchUpdate(123, "https://24h.pchome.com.tw/prod/NOT-EXIST"),
            CancellationToken.None);

        await botClient.Received(1).SendRequest(
            Arg.Is<SendMessageRequest>(r => r.ChatId == 123 && r.Text.Contains("找不到")),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task UnwatchCommand_DoesNotAffectOtherUsers()
    {
        var db = CreateDb();
        var url = "https://24h.pchome.com.tw/prod/SHARED-001";
        db.WatchItems.AddRange(
            new WatchItem { ChatId = 111, Url = url, IsActive = true },
            new WatchItem { ChatId = 222, Url = url, IsActive = true }
        );
        await db.SaveChangesAsync();

        var botClient = Substitute.For<ITelegramBotClient>();
        var service = new TelegramBotService(db, botClient);

        await service.HandleUpdateAsync(MakeUnwatchUpdate(111, url), CancellationToken.None);

        var items = await db.WatchItems.ToListAsync();
        Assert.False(items.Single(w => w.ChatId == 111).IsActive);
        Assert.True(items.Single(w => w.ChatId == 222).IsActive);
    }
}
