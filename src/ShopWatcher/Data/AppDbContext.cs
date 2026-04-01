using Microsoft.EntityFrameworkCore;
using ShopWatcher.Data.Models;

namespace ShopWatcher.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<WatchItem> WatchItems => Set<WatchItem>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<WatchItem>()
            .HasIndex(w => new { w.ChatId, w.Url })
            .IsUnique();
    }
}
