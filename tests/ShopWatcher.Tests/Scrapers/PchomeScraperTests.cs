using NSubstitute;
using ShopWatcher.Scrapers;
using ShopWatcher.Tests.Helpers;

namespace ShopWatcher.Tests.Scrapers;

public class PchomeScraperTests
{
    private readonly PchomeScraper _scraper = new(Substitute.For<IHttpClientFactory>());

    private static PchomeScraper CreateScraperWithResponse(string responseContent)
    {
        var handler = new MockHttpMessageHandler(responseContent);
        var httpClient = new HttpClient(handler);
        var factory = Substitute.For<IHttpClientFactory>();
        factory.CreateClient(Arg.Any<string>()).Returns(httpClient);
        return new PchomeScraper(factory);
    }

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

    [Fact]
    public async Task IsInStockAsync_QtyGreaterThanZero_ReturnsTrue()
    {
        const string response = """try{jsonp_prod({"DGCQ39-A900I3A09-000":{"Qty":20}});}catch(e){if(window.console){console.log(e);}}""";
        var scraper = CreateScraperWithResponse(response);

        var result = await scraper.IsInStockAsync("https://24h.pchome.com.tw/prod/DGCQ39-A900I3A09");

        Assert.True(result);
    }

    [Fact]
    public async Task IsInStockAsync_QtyIsZero_ReturnsFalse()
    {
        const string response = """try{jsonp_prod({"DGCQ39-A900JSZVL-000":{"Qty":0}});}catch(e){if(window.console){console.log(e);}}""";
        var scraper = CreateScraperWithResponse(response);

        var result = await scraper.IsInStockAsync("https://24h.pchome.com.tw/prod/DGCQ39-A900JSZVL");

        Assert.False(result);
    }

    [Fact]
    public async Task IsInStockAsync_InvalidUrl_ReturnsFalse()
    {
        var scraper = CreateScraperWithResponse(string.Empty);

        var result = await scraper.IsInStockAsync("not-a-url");

        Assert.False(result);
    }
}
