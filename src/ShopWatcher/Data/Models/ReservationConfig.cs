namespace ShopWatcher.Data.Models;

public class ReservationConfig
{
    public int Id { get; set; }
    public long ChatId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public int PartySize { get; set; }
    public string RestaurantUrl { get; set; } = string.Empty;
    public int LookAheadDays { get; set; } = 14;
    public bool IsActive { get; set; } = true;
    public DateTime? LastBookedAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
