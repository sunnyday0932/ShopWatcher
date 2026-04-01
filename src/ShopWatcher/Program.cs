using Microsoft.EntityFrameworkCore;
using ShopWatcher;
using ShopWatcher.Data;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlite(builder.Configuration.GetConnectionString("Default") ?? "Data Source=shopwatcher.db"));

builder.Services.AddHostedService<Worker>();

var host = builder.Build();

// 確保資料庫已建立
using (var scope = host.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.EnsureCreated();
}

host.Run();
