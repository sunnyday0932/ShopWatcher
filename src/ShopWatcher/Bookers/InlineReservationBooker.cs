using Microsoft.Extensions.Logging;

namespace ShopWatcher.Bookers;

public class InlineReservationBooker(bool dryRun, ILogger<InlineReservationBooker> logger) : IReservationBooker
{
    public bool CanHandle(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) &&
        uri.Host.EndsWith("inline.app", StringComparison.OrdinalIgnoreCase);

    public Task<BookingResult> BookAsync(string url, ReservationInfo info, CancellationToken ct = default) =>
        throw new NotImplementedException();
}
