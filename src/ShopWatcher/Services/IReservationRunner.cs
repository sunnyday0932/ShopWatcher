namespace ShopWatcher.Services;

public interface IReservationRunner
{
    Task RunChecksAsync(CancellationToken ct);
}
