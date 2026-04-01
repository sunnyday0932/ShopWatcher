using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using ShopWatcher.Data;
using ShopWatcher.Data.Models;
using ShopWatcher.Scrapers;
using ShopWatcher.Services;
using Telegram.Bot;
using Telegram.Bot.Requests;

namespace ShopWatcher.Tests.UseCases;

public class StockNotificationTests
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

    [Fact]
    public async Task CheckOnce_WhenInStock_SendsNotification()
    {
        var (db, scopeFactory) = CreateDbWithScope();
        db.WatchItems.Add(new WatchItem { ChatId = 123, Url = "https://24h.pchome.com.tw/prod/TEST-001", IsActive = true });
        await db.SaveChangesAsync();

        var scraper = Substitute.For<IScraper>();
        scraper.CanHandle(Arg.Any<string>()).Returns(true);
        scraper.IsInStockAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(true);

        var botClient = Substitute.For<ITelegramBotClient>();
        var service = new StockCheckerService(scopeFactory, new[] { scraper }, botClient, TimeSpan.Zero);

        await service.CheckOnceAsync(CancellationToken.None);

        await botClient.Received(1).SendRequest(
            Arg.Is<SendMessageRequest>(r => r.ChatId == 123),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CheckOnce_WhenOutOfStock_NoNotification()
    {
        var (db, scopeFactory) = CreateDbWithScope();
        db.WatchItems.Add(new WatchItem { ChatId = 123, Url = "https://24h.pchome.com.tw/prod/TEST-002", IsActive = true });
        await db.SaveChangesAsync();

        var scraper = Substitute.For<IScraper>();
        scraper.CanHandle(Arg.Any<string>()).Returns(true);
        scraper.IsInStockAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(false);

        var botClient = Substitute.For<ITelegramBotClient>();
        var service = new StockCheckerService(scopeFactory, new[] { scraper }, botClient, TimeSpan.Zero);

        await service.CheckOnceAsync(CancellationToken.None);

        await botClient.DidNotReceive().SendRequest(
            Arg.Any<SendMessageRequest>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CheckOnce_MultipleUsersWatchSameUrl_BothNotified()
    {
        var (db, scopeFactory) = CreateDbWithScope();
        db.WatchItems.AddRange(
            new WatchItem { ChatId = 111, Url = "https://24h.pchome.com.tw/prod/SHARED-001", IsActive = true },
            new WatchItem { ChatId = 222, Url = "https://24h.pchome.com.tw/prod/SHARED-001", IsActive = true }
        );
        await db.SaveChangesAsync();

        var scraper = Substitute.For<IScraper>();
        scraper.CanHandle(Arg.Any<string>()).Returns(true);
        scraper.IsInStockAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(true);

        var botClient = Substitute.For<ITelegramBotClient>();
        var service = new StockCheckerService(scopeFactory, new[] { scraper }, botClient, TimeSpan.Zero);

        await service.CheckOnceAsync(CancellationToken.None);

        await botClient.Received(2).SendRequest(
            Arg.Any<SendMessageRequest>(),
            Arg.Any<CancellationToken>());
        await scraper.Received(1).IsInStockAsync("https://24h.pchome.com.tw/prod/SHARED-001", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CheckOnce_InactiveItems_NotChecked()
    {
        var (db, scopeFactory) = CreateDbWithScope();
        db.WatchItems.Add(new WatchItem { ChatId = 123, Url = "https://24h.pchome.com.tw/prod/TEST-003", IsActive = false });
        await db.SaveChangesAsync();

        var scraper = Substitute.For<IScraper>();
        scraper.CanHandle(Arg.Any<string>()).Returns(true);

        var botClient = Substitute.For<ITelegramBotClient>();
        var service = new StockCheckerService(scopeFactory, new[] { scraper }, botClient, TimeSpan.Zero);

        await service.CheckOnceAsync(CancellationToken.None);

        await scraper.DidNotReceive().IsInStockAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CheckOnce_WhenScraperThrows_SkipsItemAndDoesNotNotify()
    {
        var (db, scopeFactory) = CreateDbWithScope();
        db.WatchItems.Add(new WatchItem { ChatId = 123, Url = "https://24h.pchome.com.tw/prod/TEST-004", IsActive = true });
        await db.SaveChangesAsync();

        var scraper = Substitute.For<IScraper>();
        scraper.CanHandle(Arg.Any<string>()).Returns(true);
        scraper.IsInStockAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new HttpRequestException("Network error"));

        var botClient = Substitute.For<ITelegramBotClient>();
        var service = new StockCheckerService(scopeFactory, new[] { scraper }, botClient, TimeSpan.Zero);

        // scraper 拋出例外不應中斷，也不應發送通知
        await service.CheckOnceAsync(CancellationToken.None);

        await botClient.DidNotReceive().SendRequest(
            Arg.Any<SendMessageRequest>(),
            Arg.Any<CancellationToken>());
    }
}
