using ShopWatcher.Scrapers;

namespace ShopWatcher.Tests.Scrapers;

public class PchomeScraperTests
{
    private readonly PchomeScraper _scraper = new(new HttpClient());

    [Theory]
    [InlineData("https://24h.pchome.com.tw/prod/DGCQ39-A900JSZVL", true)]
    [InlineData("https://24h.pchome.com.tw/prod/ABC123", true)]
    public void CanHandle_PchomeUrl_ReturnsTrue(string url, bool expected)
    {
        Assert.Equal(expected, _scraper.CanHandle(url));
    }

    [Theory]
    [InlineData("https://www.momoshop.com.tw/goods/GoodsDetail.jsp?i_code=123")]
    [InlineData("https://shopee.tw/product/123")]
    public void CanHandle_NonPchomeUrl_ReturnsFalse(string url)
    {
        Assert.False(_scraper.CanHandle(url));
    }

    [Theory]
    [InlineData("https://pchome.com.tw.attacker.com/prod/FAKE")]
    [InlineData("not-a-url")]
    public void CanHandle_SpoofedOrInvalidUrl_ReturnsFalse(string url)
    {
        Assert.False(_scraper.CanHandle(url));
    }
}
