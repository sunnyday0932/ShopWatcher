namespace ShopWatcher.Bookers;

public record ReservationInfo(
    string Name,
    string Phone,
    int PartySize,
    DateOnly Date);
