using Microsoft.EntityFrameworkCore;
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

builder.Services.AddHostedService<TelegramBotService>();
builder.Services.AddHostedService<StockCheckerService>();

var host = builder.Build();

using (var scope = host.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.EnsureCreated();
}

host.Run();
