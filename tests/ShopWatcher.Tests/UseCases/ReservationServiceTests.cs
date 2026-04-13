using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using ShopWatcher.Bookers;
using ShopWatcher.Data;
using ShopWatcher.Data.Models;
using ShopWatcher.Services;

namespace ShopWatcher.Tests.UseCases;

public class ReservationServiceTests
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

    private static ReservationService.DefaultReservationRunner CreateRunner(
        IServiceScopeFactory scopeFactory,
        IReservationBooker? booker = null)
    {
        var bookers = booker is not null
            ? new[] { booker }
            : Array.Empty<IReservationBooker>();
        return new ReservationService.DefaultReservationRunner(
            scopeFactory,
            bookers,
            NullLogger<ReservationService.DefaultReservationRunner>.Instance);
    }

    // R2: 只讀取 IsActive=true 的 ReservationConfigs
    [Fact]
    public async Task RunChecks_OnlyProcessesActiveConfigs()
    {
        var (db, scopeFactory) = CreateDbWithScope();
        db.ReservationConfigs.Add(new ReservationConfig
        {
            ChatId = 100,
            Name = "Alice",
            Phone = "0912345678",
            PartySize = 2,
            RestaurantUrl = "https://inline.app/booking/123",
            IsActive = false  // 非啟用
        });
        await db.SaveChangesAsync();

        var booker = Substitute.For<IReservationBooker>();
        booker.CanHandle(Arg.Any<string>()).Returns(true);

        var runner = CreateRunner(scopeFactory, booker);
        await runner.RunChecksAsync(CancellationToken.None);

        await booker.DidNotReceive().BookAsync(
            Arg.Any<string>(), Arg.Any<ReservationInfo>(), Arg.Any<CancellationToken>());
    }

    // R3: 對每個 active config 呼叫對應的 IReservationBooker
    [Fact]
    public async Task RunChecks_CallsBookerForActiveConfig()
    {
        var (db, scopeFactory) = CreateDbWithScope();
        db.ReservationConfigs.Add(new ReservationConfig
        {
            ChatId = 100,
            Name = "Alice",
            Phone = "0912345678",
            PartySize = 2,
            RestaurantUrl = "https://inline.app/booking/123",
            IsActive = true
        });
        await db.SaveChangesAsync();

        var booker = Substitute.For<IReservationBooker>();
        booker.CanHandle("https://inline.app/booking/123").Returns(true);
        booker.BookAsync(Arg.Any<string>(), Arg.Any<ReservationInfo>(), Arg.Any<CancellationToken>())
            .Returns(new BookingResult(false, false, false, null, null, "no slots"));

        var runner = CreateRunner(scopeFactory, booker);
        await runner.RunChecksAsync(CancellationToken.None);

        await booker.Received().BookAsync(
            "https://inline.app/booking/123", Arg.Any<ReservationInfo>(), Arg.Any<CancellationToken>());
    }

    // R5: 成功時更新 LastBookedAt
    [Fact]
    public async Task RunChecks_OnSuccess_UpdatesLastBookedAt()
    {
        var (db, scopeFactory) = CreateDbWithScope();
        db.ReservationConfigs.Add(new ReservationConfig
        {
            ChatId = 100,
            Name = "Alice",
            Phone = "0912345678",
            PartySize = 2,
            RestaurantUrl = "https://inline.app/booking/123",
            IsActive = true
        });
        await db.SaveChangesAsync();

        var booker = Substitute.For<IReservationBooker>();
        booker.CanHandle(Arg.Any<string>()).Returns(true);
        booker.BookAsync(Arg.Any<string>(), Arg.Any<ReservationInfo>(), Arg.Any<CancellationToken>())
            .Returns(new BookingResult(true, false, false, DateOnly.FromDateTime(DateTime.UtcNow), TimeOnly.MinValue, null));

        var runner = CreateRunner(scopeFactory, booker);
        await runner.RunChecksAsync(CancellationToken.None);

        var config = await db.ReservationConfigs.AsNoTracking().SingleAsync();
        Assert.NotNull(config.LastBookedAt);
    }

    // R6: 失敗時不更新 LastBookedAt
    [Fact]
    public async Task RunChecks_OnFailure_DoesNotUpdateLastBookedAt()
    {
        var (db, scopeFactory) = CreateDbWithScope();
        db.ReservationConfigs.Add(new ReservationConfig
        {
            ChatId = 100,
            Name = "Alice",
            Phone = "0912345678",
            PartySize = 2,
            RestaurantUrl = "https://inline.app/booking/123",
            LookAheadDays = 1,  // 只查一天
            IsActive = true,
            LastBookedAt = null
        });
        await db.SaveChangesAsync();

        var booker = Substitute.For<IReservationBooker>();
        booker.CanHandle(Arg.Any<string>()).Returns(true);
        booker.BookAsync(Arg.Any<string>(), Arg.Any<ReservationInfo>(), Arg.Any<CancellationToken>())
            .Returns(new BookingResult(false, false, false, null, null, "no slots"));

        var runner = CreateRunner(scopeFactory, booker);
        await runner.RunChecksAsync(CancellationToken.None);

        var config = await db.ReservationConfigs.AsNoTracking().SingleAsync();
        Assert.Null(config.LastBookedAt);
    }

    // R7: 捕捉異常，不中斷下一輪
    [Fact]
    public async Task RunChecks_WhenBookerThrows_ContinuesProcessing()
    {
        var (db, scopeFactory) = CreateDbWithScope();
        db.ReservationConfigs.AddRange(
            new ReservationConfig
            {
                ChatId = 100,
                Name = "Alice",
                Phone = "0912345678",
                PartySize = 2,
                RestaurantUrl = "https://inline.app/booking/throw",
                IsActive = true
            },
            new ReservationConfig
            {
                ChatId = 200,
                Name = "Bob",
                Phone = "0987654321",
                PartySize = 4,
                RestaurantUrl = "https://inline.app/booking/succeed",
                IsActive = true
            }
        );
        await db.SaveChangesAsync();

        var booker = Substitute.For<IReservationBooker>();
        booker.CanHandle(Arg.Any<string>()).Returns(true);
        booker.BookAsync("https://inline.app/booking/throw", Arg.Any<ReservationInfo>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new Exception("Network error"));
        booker.BookAsync("https://inline.app/booking/succeed", Arg.Any<ReservationInfo>(), Arg.Any<CancellationToken>())
            .Returns(new BookingResult(true, false, false, DateOnly.FromDateTime(DateTime.UtcNow), TimeOnly.MinValue, null));

        var runner = CreateRunner(scopeFactory, booker);
        // 不應拋出例外
        await runner.RunChecksAsync(CancellationToken.None);

        // 第二個 config 應被更新
        var configs = await db.ReservationConfigs.AsNoTracking().ToListAsync();
        var bob = configs.Single(c => c.ChatId == 200);
        Assert.NotNull(bob.LastBookedAt);
    }

    // 無 booker 可處理時，跳過
    [Fact]
    public async Task RunChecks_WhenNoBookerCanHandle_SkipsConfig()
    {
        var (db, scopeFactory) = CreateDbWithScope();
        db.ReservationConfigs.Add(new ReservationConfig
        {
            ChatId = 100,
            Name = "Alice",
            Phone = "0912345678",
            PartySize = 2,
            RestaurantUrl = "https://unknown.example.com/booking/123",
            IsActive = true
        });
        await db.SaveChangesAsync();

        var booker = Substitute.For<IReservationBooker>();
        booker.CanHandle(Arg.Any<string>()).Returns(false);  // 無法處理

        var runner = CreateRunner(scopeFactory, booker);
        await runner.RunChecksAsync(CancellationToken.None);

        await booker.DidNotReceive().BookAsync(
            Arg.Any<string>(), Arg.Any<ReservationInfo>(), Arg.Any<CancellationToken>());
    }

    // 沒有 active config 時，直接返回
    [Fact]
    public async Task RunChecks_WithNoActiveConfigs_DoesNothing()
    {
        var (_, scopeFactory) = CreateDbWithScope();

        var booker = Substitute.For<IReservationBooker>();
        var runner = CreateRunner(scopeFactory, booker);
        await runner.RunChecksAsync(CancellationToken.None);

        await booker.DidNotReceive().BookAsync(
            Arg.Any<string>(), Arg.Any<ReservationInfo>(), Arg.Any<CancellationToken>());
    }
}
