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

public class WatchCommandTests
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

    private static Update MakeWatchUpdate(long chatId, string url) => new()
    {
        Message = new Message
        {
            Chat = new Chat { Id = chatId },
            Text = $"/watch {url}",
            Entities = [new MessageEntity { Type = MessageEntityType.BotCommand, Offset = 0, Length = 6 }]
        }
    };

    [Fact]
    public async Task WatchCommand_AddsWatchItem()
    {
        var db = CreateDb();
        var botClient = Substitute.For<ITelegramBotClient>();
        var service = new TelegramBotService(db, botClient);
        var url = "https://24h.pchome.com.tw/prod/TEST-001";

        await service.HandleUpdateAsync(MakeWatchUpdate(123, url), CancellationToken.None);

        var item = await db.WatchItems.SingleAsync();
        Assert.Equal(123, item.ChatId);
        Assert.Equal(url, item.Url);
        Assert.True(item.IsActive);
    }

    [Fact]
    public async Task WatchCommand_DuplicateUrl_DoesNotAddAgain()
    {
        var db = CreateDb();
        db.WatchItems.Add(new WatchItem { ChatId = 123, Url = "https://24h.pchome.com.tw/prod/TEST-002", IsActive = true });
        await db.SaveChangesAsync();

        var botClient = Substitute.For<ITelegramBotClient>();
        var service = new TelegramBotService(db, botClient);

        await service.HandleUpdateAsync(MakeWatchUpdate(123, "https://24h.pchome.com.tw/prod/TEST-002"), CancellationToken.None);

        Assert.Equal(1, await db.WatchItems.CountAsync());
        await botClient.Received(1).SendRequest(
            Arg.Is<SendMessageRequest>(r => r.ChatId == 123),
            Arg.Any<CancellationToken>());
    }
}
