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

public class ReservationCommandTests
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

    private static TelegramBotService CreateService(
        IServiceScopeFactory scopeFactory,
        ITelegramBotClient? botClient = null,
        IReservationRunner? runner = null) =>
        new(scopeFactory,
            botClient ?? Substitute.For<ITelegramBotClient>(),
            runner ?? Substitute.For<IReservationRunner>());

    private static Update MakeUpdate(long chatId, string text) => new()
    {
        Message = new Message
        {
            Chat = new Chat { Id = chatId },
            Text = text,
            Entities = [new MessageEntity { Type = MessageEntityType.BotCommand, Offset = 0, Length = text.IndexOf(' ') is var i && i > 0 ? i : text.Length }]
        }
    };

    // T1: /reservation_add → 新增到 DB
    [Fact]
    public async Task ReservationAdd_ValidArgs_AddsConfig()
    {
        var (db, scopeFactory) = CreateDbWithScope();
        var service = CreateService(scopeFactory);

        await service.HandleUpdateAsync(
            MakeUpdate(123, "/reservation_add Alice 0912345678 2 https://inline.app/booking/123"),
            CancellationToken.None);

        var config = await db.ReservationConfigs.AsNoTracking().SingleAsync();
        Assert.Equal(123, config.ChatId);
        Assert.Equal("Alice", config.Name);
        Assert.Equal("0912345678", config.Phone);
        Assert.Equal(2, config.PartySize);
        Assert.Equal("https://inline.app/booking/123", config.RestaurantUrl);
        Assert.Equal(14, config.LookAheadDays);  // 預設值
        Assert.True(config.IsActive);
    }

    // T1b: /reservation_add with custom lookahead
    [Fact]
    public async Task ReservationAdd_WithLookahead_UsesCustomValue()
    {
        var (db, scopeFactory) = CreateDbWithScope();
        var service = CreateService(scopeFactory);

        await service.HandleUpdateAsync(
            MakeUpdate(123, "/reservation_add Alice 0912345678 2 https://inline.app/booking/123 7"),
            CancellationToken.None);

        var config = await db.ReservationConfigs.AsNoTracking().SingleAsync();
        Assert.Equal(7, config.LookAheadDays);
    }

    // T2: /reservation_add 重複 URL+ChatId → 拒絕
    [Fact]
    public async Task ReservationAdd_Duplicate_RejectsWithMessage()
    {
        var (db, scopeFactory) = CreateDbWithScope();
        db.ReservationConfigs.Add(new ReservationConfig
        {
            ChatId = 123,
            RestaurantUrl = "https://inline.app/booking/123",
            Name = "Alice",
            Phone = "0912345678",
            PartySize = 2
        });
        await db.SaveChangesAsync();

        var botClient = Substitute.For<ITelegramBotClient>();
        var service = CreateService(scopeFactory, botClient);

        await service.HandleUpdateAsync(
            MakeUpdate(123, "/reservation_add Alice 0912345678 2 https://inline.app/booking/123"),
            CancellationToken.None);

        Assert.Equal(1, await db.ReservationConfigs.AsNoTracking().CountAsync());
        await botClient.Received(1).SendRequest(
            Arg.Is<SendMessageRequest>(r => r.ChatId == 123 && r.Text.Contains("已存在") || r.Text.Contains("已經")),
            Arg.Any<CancellationToken>());
    }

    // T3: /reservation_remove → 標記 IsActive=false
    [Fact]
    public async Task ReservationRemove_ExistingUrl_DeactivatesConfig()
    {
        var (db, scopeFactory) = CreateDbWithScope();
        db.ReservationConfigs.Add(new ReservationConfig
        {
            ChatId = 123,
            RestaurantUrl = "https://inline.app/booking/123",
            Name = "Alice",
            Phone = "0912345678",
            PartySize = 2,
            IsActive = true
        });
        await db.SaveChangesAsync();

        var service = CreateService(scopeFactory);
        await service.HandleUpdateAsync(
            MakeUpdate(123, "/reservation_remove https://inline.app/booking/123"),
            CancellationToken.None);

        var config = await db.ReservationConfigs.AsNoTracking().SingleAsync();
        Assert.False(config.IsActive);
    }

    // T4: /reservation_remove 不存在 → 提示找不到
    [Fact]
    public async Task ReservationRemove_NotFound_SendsNotFoundMessage()
    {
        var (_, scopeFactory) = CreateDbWithScope();
        var botClient = Substitute.For<ITelegramBotClient>();
        var service = CreateService(scopeFactory, botClient);

        await service.HandleUpdateAsync(
            MakeUpdate(123, "/reservation_remove https://inline.app/booking/notexist"),
            CancellationToken.None);

        await botClient.Received(1).SendRequest(
            Arg.Is<SendMessageRequest>(r => r.ChatId == 123 && r.Text.Contains("找不到")),
            Arg.Any<CancellationToken>());
    }

    // T5: /reservation_list → 列出啟用設定
    [Fact]
    public async Task ReservationList_WithActiveConfigs_ListsThem()
    {
        var (db, scopeFactory) = CreateDbWithScope();
        db.ReservationConfigs.AddRange(
            new ReservationConfig { ChatId = 123, Name = "Alice", Phone = "0912345678", PartySize = 2, RestaurantUrl = "https://inline.app/booking/A", IsActive = true },
            new ReservationConfig { ChatId = 123, Name = "Bob", Phone = "0987654321", PartySize = 4, RestaurantUrl = "https://inline.app/booking/B", IsActive = false }
        );
        await db.SaveChangesAsync();

        var botClient = Substitute.For<ITelegramBotClient>();
        var service = CreateService(scopeFactory, botClient);

        await service.HandleUpdateAsync(MakeUpdate(123, "/reservation_list"), CancellationToken.None);

        await botClient.Received(1).SendRequest(
            Arg.Is<SendMessageRequest>(r => r.ChatId == 123 && r.Text.Contains("booking/A") && !r.Text.Contains("booking/B")),
            Arg.Any<CancellationToken>());
    }

    // T6: /reservation_list 沒有設定 → 提示「目前沒有」
    [Fact]
    public async Task ReservationList_Empty_SendsNoConfigMessage()
    {
        var (_, scopeFactory) = CreateDbWithScope();
        var botClient = Substitute.For<ITelegramBotClient>();
        var service = CreateService(scopeFactory, botClient);

        await service.HandleUpdateAsync(MakeUpdate(123, "/reservation_list"), CancellationToken.None);

        await botClient.Received(1).SendRequest(
            Arg.Is<SendMessageRequest>(r => r.ChatId == 123 && r.Text.Contains("沒有")),
            Arg.Any<CancellationToken>());
    }

    // T7: /reservation_activate → IsActive=true
    [Fact]
    public async Task ReservationActivate_ExistingUrl_ActivatesConfig()
    {
        var (db, scopeFactory) = CreateDbWithScope();
        db.ReservationConfigs.Add(new ReservationConfig
        {
            ChatId = 123,
            RestaurantUrl = "https://inline.app/booking/123",
            Name = "Alice",
            Phone = "0912345678",
            PartySize = 2,
            IsActive = false
        });
        await db.SaveChangesAsync();

        var service = CreateService(scopeFactory);
        await service.HandleUpdateAsync(
            MakeUpdate(123, "/reservation_activate https://inline.app/booking/123"),
            CancellationToken.None);

        var config = await db.ReservationConfigs.AsNoTracking().SingleAsync();
        Assert.True(config.IsActive);
    }

    // T8: /reservation_deactivate → IsActive=false
    [Fact]
    public async Task ReservationDeactivate_ExistingUrl_DeactivatesConfig()
    {
        var (db, scopeFactory) = CreateDbWithScope();
        db.ReservationConfigs.Add(new ReservationConfig
        {
            ChatId = 123,
            RestaurantUrl = "https://inline.app/booking/123",
            Name = "Alice",
            Phone = "0912345678",
            PartySize = 2,
            IsActive = true
        });
        await db.SaveChangesAsync();

        var service = CreateService(scopeFactory);
        await service.HandleUpdateAsync(
            MakeUpdate(123, "/reservation_deactivate https://inline.app/booking/123"),
            CancellationToken.None);

        var config = await db.ReservationConfigs.AsNoTracking().SingleAsync();
        Assert.False(config.IsActive);
    }

    // T9: /triggerbooking → 呼叫 IReservationRunner.RunChecksAsync
    [Fact]
    public async Task TriggerBooking_CallsReservationRunner()
    {
        var (_, scopeFactory) = CreateDbWithScope();
        var runner = Substitute.For<IReservationRunner>();
        var service = CreateService(scopeFactory, runner: runner);

        await service.HandleUpdateAsync(MakeUpdate(123, "/triggerbooking"), CancellationToken.None);

        await runner.Received(1).RunChecksAsync(Arg.Any<CancellationToken>());
    }

    // T10: 參數驗證錯誤 → 提示用法
    [Fact]
    public async Task ReservationAdd_InvalidArgs_SendsUsageMessage()
    {
        var (_, scopeFactory) = CreateDbWithScope();
        var botClient = Substitute.For<ITelegramBotClient>();
        var service = CreateService(scopeFactory, botClient);

        await service.HandleUpdateAsync(
            MakeUpdate(123, "/reservation_add Alice"),  // 缺少參數
            CancellationToken.None);

        await botClient.Received(1).SendRequest(
            Arg.Is<SendMessageRequest>(r => r.ChatId == 123 && r.Text.Contains("用法")),
            Arg.Any<CancellationToken>());
    }
}
