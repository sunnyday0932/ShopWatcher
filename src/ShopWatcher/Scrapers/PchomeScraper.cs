using System.Text.Json;

namespace ShopWatcher.Scrapers;

public class PchomeScraper(HttpClient httpClient) : IScraper
{
    public bool CanHandle(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return false;
        return uri.Host.EndsWith("pchome.com.tw", StringComparison.OrdinalIgnoreCase);
    }

    public async Task<bool> IsInStockAsync(string url, CancellationToken ct = default)
    {
        // 從網址取出商品 ID，例如 DGCQ39-A900JSZVL
        var productId = ExtractProductId(url);
        if (productId is null) return false;

        var apiUrl = $"https://ecshweb.pchome.com.tw/search/v3.3/all/results?q={productId}&page=1&sort=rnk/dc";
        var response = await httpClient.GetStringAsync(apiUrl, ct);
        var json = JsonDocument.Parse(response);

        // 若 prods 陣列有資料且第一筆有 inStock 欄位為 true，則有貨
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
        // https://24h.pchome.com.tw/prod/DGCQ39-A900JSZVL → DGCQ39-A900JSZVL
        var uri = new Uri(url);
        var segments = uri.AbsolutePath.Split('/');
        return segments.LastOrDefault(s => !string.IsNullOrEmpty(s));
    }
}
