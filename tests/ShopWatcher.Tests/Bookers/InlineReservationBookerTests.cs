using Microsoft.Extensions.Logging.Abstractions;
using ShopWatcher.Bookers;

namespace ShopWatcher.Tests.Bookers;

public class InlineReservationBookerTests
{
    private static InlineReservationBooker CreateBooker() =>
        new(dryRun: true, NullLogger<InlineReservationBooker>.Instance);

    [Fact]
    public void CanHandle_InlineAppUrl_ReturnsTrue() =>
        Assert.True(CreateBooker().CanHandle(
            "https://inline.app/booking/-N9fc8nBTri71f1ryIhS:inline-live-1/-N9fc91xzvzzNT9FDZiv?language=zh-tw"));

    [Fact]
    public void CanHandle_NonInlineUrl_ReturnsFalse() =>
        Assert.False(CreateBooker().CanHandle("https://24h.pchome.com.tw/prod/TEST-001"));
}
