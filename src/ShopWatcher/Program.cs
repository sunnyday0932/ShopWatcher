using Microsoft.EntityFrameworkCore;
using ShopWatcher;
using ShopWatcher.Data;
using ShopWatcher.Scrapers;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlite(builder.Configuration.GetConnectionString("Default") ?? "Data Source=shopwatcher.db"));

builder.Services.AddHostedService<Worker>();

builder.Services.AddHttpClient<PchomeScraper>();
builder.Services.AddSingleton<IScraper, PchomeScraper>();

var host = builder.Build();

// 確保資料庫已建立
using (var scope = host.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.EnsureCreated();
}

host.Run();
