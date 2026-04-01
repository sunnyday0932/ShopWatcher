using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
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
    private static (AppDbContext db, IServiceScopeFactory scopeFactory) CreateDbWithScope()
    {
        var dbName = Guid.NewGuid().ToString();
        var services = new ServiceCollection();
        services.AddDbContext<AppDbContext>(options =>
            options.UseInMemoryDatabase(dbName));
        var provider = services.BuildServiceProvider();
        var db = provider.GetRequiredService<AppDbContext>();
        db.Database.EnsureCreated();
        return (db, provider.GetRequiredService<IServiceScopeFactory>());
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
        var (db, scopeFactory) = CreateDbWithScope();
        var botClient = Substitute.For<ITelegramBotClient>();
        var service = new TelegramBotService(scopeFactory, botClient);
        var url = "https://24h.pchome.com.tw/prod/TEST-001";

        await service.HandleUpdateAsync(MakeWatchUpdate(123, url), CancellationToken.None);

        var item = await db.WatchItems.AsNoTracking().SingleAsync();
        Assert.Equal(123, item.ChatId);
        Assert.Equal(url, item.Url);
        Assert.True(item.IsActive);
    }

    [Fact]
    public async Task WatchCommand_DuplicateUrl_DoesNotAddAgain()
    {
        var (db, scopeFactory) = CreateDbWithScope();
        db.WatchItems.Add(new WatchItem { ChatId = 123, Url = "https://24h.pchome.com.tw/prod/TEST-002", IsActive = true });
        await db.SaveChangesAsync();

        var botClient = Substitute.For<ITelegramBotClient>();
        var service = new TelegramBotService(scopeFactory, botClient);

        await service.HandleUpdateAsync(MakeWatchUpdate(123, "https://24h.pchome.com.tw/prod/TEST-002"), CancellationToken.None);

        Assert.Equal(1, await db.WatchItems.AsNoTracking().CountAsync());
        await botClient.Received(1).SendRequest(
            Arg.Is<SendMessageRequest>(r => r.ChatId == 123),
            Arg.Any<CancellationToken>());
    }
}
