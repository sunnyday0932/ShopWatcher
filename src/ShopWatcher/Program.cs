using Microsoft.EntityFrameworkCore;
using ShopWatcher.Data;
using ShopWatcher.Scrapers;
using ShopWatcher.Services;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlite(builder.Configuration.GetConnectionString("Default") ?? "Data Source=shopwatcher.db"));

builder.Services.AddHttpClient<PchomeScraper>();
builder.Services.AddSingleton<IScraper, PchomeScraper>();

builder.Services.AddHostedService<StockCheckerService>();

var host = builder.Build();

// 確保資料庫已建立
using (var scope = host.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.EnsureCreated();
}

host.Run();
