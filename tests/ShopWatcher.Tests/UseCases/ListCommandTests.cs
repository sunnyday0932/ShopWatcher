using NSubstitute;
using ShopWatcher.Data;
using ShopWatcher.Data.Models;
using ShopWatcher.Services;
using Telegram.Bot;
using Telegram.Bot.Requests;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ShopWatcher.Tests.UseCases;

public class ListCommandTests
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

    private static Update MakeListUpdate(long chatId) => new()
    {
        Message = new Message
        {
            Chat = new Chat { Id = chatId },
            Text = "/list",
            Entities = [new MessageEntity { Type = MessageEntityType.BotCommand, Offset = 0, Length = 5 }]
        }
    };

    [Fact]
    public async Task ListCommand_ReturnsActiveItemsForUser()
    {
        var (db, scopeFactory) = CreateDbWithScope();
        db.WatchItems.AddRange(
            new WatchItem { ChatId = 123, Url = "https://24h.pchome.com.tw/prod/A", IsActive = true },
            new WatchItem { ChatId = 123, Url = "https://24h.pchome.com.tw/prod/B", IsActive = false },
            new WatchItem { ChatId = 456, Url = "https://24h.pchome.com.tw/prod/C", IsActive = true }
        );
        await db.SaveChangesAsync();

        var botClient = Substitute.For<ITelegramBotClient>();
        var service = new TelegramBotService(scopeFactory, botClient);

        await service.HandleUpdateAsync(MakeListUpdate(123), CancellationToken.None);

        await botClient.Received(1).SendRequest(
            Arg.Is<SendMessageRequest>(r =>
                r.ChatId == 123 &&
                r.Text.Contains("prod/A") &&
                !r.Text.Contains("prod/B") &&
                !r.Text.Contains("prod/C")),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ListCommand_EmptyList_RepliesWithNoItemsMessage()
    {
        var (_, scopeFactory) = CreateDbWithScope();
        var botClient = Substitute.For<ITelegramBotClient>();
        var service = new TelegramBotService(scopeFactory, botClient);

        await service.HandleUpdateAsync(MakeListUpdate(123), CancellationToken.None);

        await botClient.Received(1).SendRequest(
            Arg.Is<SendMessageRequest>(r => r.ChatId == 123 && r.Text.Contains("沒有監控")),
            Arg.Any<CancellationToken>());
    }
}
