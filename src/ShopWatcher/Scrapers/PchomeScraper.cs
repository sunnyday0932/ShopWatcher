using System.Text.Json;
using System.Net.Http;

namespace ShopWatcher.Scrapers;

public class PchomeScraper(IHttpClientFactory httpClientFactory) : IScraper
{
    // 保留此建構子供測試直接傳入 HttpClient 使用
    private readonly HttpClient? _testHttpClient;

    public PchomeScraper(HttpClient httpClient) : this((IHttpClientFactory)null!)
    {
        _testHttpClient = httpClient;
    }

    public bool CanHandle(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return false;
        return uri.Host.EndsWith("pchome.com.tw", StringComparison.OrdinalIgnoreCase);
    }

    public async Task<bool> IsInStockAsync(string url, CancellationToken ct = default)
    {
        var httpClient = _testHttpClient ?? httpClientFactory.CreateClient(nameof(PchomeScraper));

        var productId = ExtractProductId(url);
        if (productId is null) return false;

        var apiUrl = $"https://ecshweb.pchome.com.tw/search/v3.3/all/results?q={productId}&page=1&sort=rnk/dc";
        var response = await httpClient.GetStringAsync(apiUrl, ct);
        var json = JsonDocument.Parse(response);

        if (json.RootElement.TryGetProperty("prods", out var prods) && prods.GetArrayLength() > 0)
        {
            var first = prods[0];
            if (first.TryGetProperty("inStock", out var inStock) &&
                inStock.ValueKind == JsonValueKind.True)
                return true;
        }

        return false;
    }

    private static string? ExtractProductId(string url)
    {
        var uri = new Uri(url);
        var segments = uri.AbsolutePath.Split('/');
        return segments.LastOrDefault(s => !string.IsNullOrEmpty(s));
    }
}
