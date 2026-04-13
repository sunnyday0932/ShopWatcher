using Microsoft.EntityFrameworkCore;
using ShopWatcher.Bookers;
using ShopWatcher.Data;
using ShopWatcher.Scrapers;
using ShopWatcher.Services;
using Telegram.Bot;

var builder = Host.CreateApplicationBuilder(args);

var botToken = builder.Configuration["Telegram:BotToken"]
    ?? throw new InvalidOperationException("Telegram:BotToken is not configured.");

var intervalSeconds = builder.Configuration.GetValue<int>("Checker:IntervalSeconds", 30);

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlite(builder.Configuration.GetConnectionString("Default") ?? "Data Source=shopwatcher.db"));

builder.Services.AddHttpClient(nameof(PchomeScraper));
builder.Services.AddSingleton<IScraper, PchomeScraper>();
builder.Services.AddSingleton<ITelegramBotClient>(_ => new TelegramBotClient(botToken));
builder.Services.AddSingleton(typeof(TimeSpan), TimeSpan.FromSeconds(intervalSeconds));

var dryRun = builder.Configuration.GetValue<bool>("Reservation:DryRun", true);
builder.Services.AddSingleton<IReservationBooker>(sp =>
    new InlineReservationBooker(
        dryRun,
        sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<InlineReservationBooker>>()));
builder.Services.AddSingleton<IReservationRunner, ReservationService.DefaultReservationRunner>();

builder.Services.AddHostedService<TelegramBotService>();
builder.Services.AddHostedService<StockCheckerService>();
builder.Services.AddHostedService<ReservationService>();

var host = builder.Build();

using (var scope = host.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.EnsureCreated();
}

host.Run();
