namespace ShopWatcher.Bookers;

public record BookingResult(
    bool Success,
    bool WasWaitlist,
    bool DryRun,
    DateOnly? BookedDate,
    TimeOnly? BookedTime,
    string? ErrorMessage);
