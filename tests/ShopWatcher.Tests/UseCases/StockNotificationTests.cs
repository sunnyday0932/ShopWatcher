using Microsoft.EntityFrameworkCore;
using NSubstitute;
using ShopWatcher.Data;
using ShopWatcher.Data.Models;
using ShopWatcher.Scrapers;
using ShopWatcher.Services;
using Telegram.Bot;
using Telegram.Bot.Requests;

namespace ShopWatcher.Tests.UseCases;

public class StockNotificationTests
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

    [Fact]
    public async Task CheckOnce_WhenInStock_SendsNotification()
    {
        var db = CreateDb();
        db.WatchItems.Add(new WatchItem { ChatId = 123, Url = "https://24h.pchome.com.tw/prod/TEST-001", IsActive = true });
        await db.SaveChangesAsync();

        var scraper = Substitute.For<IScraper>();
        scraper.CanHandle(Arg.Any<string>()).Returns(true);
        scraper.IsInStockAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(true);

        var botClient = Substitute.For<ITelegramBotClient>();
        var service = new StockCheckerService(db, new[] { scraper }, botClient, TimeSpan.Zero);

        await service.CheckOnceAsync(CancellationToken.None);

        await botClient.Received(1).MakeRequest(
            Arg.Is<SendMessageRequest>(r => r.ChatId == 123),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CheckOnce_WhenOutOfStock_NoNotification()
    {
        var db = CreateDb();
        db.WatchItems.Add(new WatchItem { ChatId = 123, Url = "https://24h.pchome.com.tw/prod/TEST-002", IsActive = true });
        await db.SaveChangesAsync();

        var scraper = Substitute.For<IScraper>();
        scraper.CanHandle(Arg.Any<string>()).Returns(true);
        scraper.IsInStockAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(false);

        var botClient = Substitute.For<ITelegramBotClient>();
        var service = new StockCheckerService(db, new[] { scraper }, botClient, TimeSpan.Zero);

        await service.CheckOnceAsync(CancellationToken.None);

        await botClient.DidNotReceive().MakeRequest(
            Arg.Any<SendMessageRequest>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CheckOnce_MultipleUsersWatchSameUrl_BothNotified()
    {
        var db = CreateDb();
        db.WatchItems.AddRange(
            new WatchItem { ChatId = 111, Url = "https://24h.pchome.com.tw/prod/SHARED-001", IsActive = true },
            new WatchItem { ChatId = 222, Url = "https://24h.pchome.com.tw/prod/SHARED-001", IsActive = true }
        );
        await db.SaveChangesAsync();

        var scraper = Substitute.For<IScraper>();
        scraper.CanHandle(Arg.Any<string>()).Returns(true);
        scraper.IsInStockAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(true);

        var botClient = Substitute.For<ITelegramBotClient>();
        var service = new StockCheckerService(db, new[] { scraper }, botClient, TimeSpan.Zero);

        await service.CheckOnceAsync(CancellationToken.None);

        // 兩個使用者各收到通知
        await botClient.Received(2).MakeRequest(
            Arg.Any<SendMessageRequest>(),
            Arg.Any<CancellationToken>());
        // HTTP 只打一次（去重）
        await scraper.Received(1).IsInStockAsync("https://24h.pchome.com.tw/prod/SHARED-001", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CheckOnce_InactiveItems_NotChecked()
    {
        var db = CreateDb();
        db.WatchItems.Add(new WatchItem { ChatId = 123, Url = "https://24h.pchome.com.tw/prod/TEST-003", IsActive = false });
        await db.SaveChangesAsync();

        var scraper = Substitute.For<IScraper>();
        scraper.CanHandle(Arg.Any<string>()).Returns(true);

        var botClient = Substitute.For<ITelegramBotClient>();
        var service = new StockCheckerService(db, new[] { scraper }, botClient, TimeSpan.Zero);

        await service.CheckOnceAsync(CancellationToken.None);

        await scraper.DidNotReceive().IsInStockAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }
}
