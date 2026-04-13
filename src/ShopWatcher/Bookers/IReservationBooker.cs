namespace ShopWatcher.Bookers;

public interface IReservationBooker
{
    bool CanHandle(string url);
    Task<BookingResult> BookAsync(string url, ReservationInfo info, CancellationToken ct = default);
}
