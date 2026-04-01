namespace ShopWatcher.Data.Models;

public class WatchItem
{
    public int Id { get; set; }
    public long ChatId { get; set; }
    public string Url { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
