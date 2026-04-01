using System.Text.Json;

namespace ShopWatcher.Scrapers;

public class PchomeScraper(IHttpClientFactory httpClientFactory) : IScraper
{
    public bool CanHandle(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return false;
        return uri.Host.EndsWith("pchome.com.tw", StringComparison.OrdinalIgnoreCase);
    }

    public async Task<bool> IsInStockAsync(string url, CancellationToken ct = default)
    {
        var productId = ExtractProductId(url);
        if (productId is null) return false;

        var httpClient = httpClientFactory.CreateClient(nameof(PchomeScraper));
        var apiUrl = $"https://ecapi.pchome.com.tw/cdn/ecshop/prodapi/v2/prod/{productId}&fields=Qty&_callback=jsonp_prod";
        var response = await httpClient.GetStringAsync(apiUrl, ct);

        // Response format: try{jsonp_prod({"PROD-ID-000":{"Qty":N}});}catch(e){...}
        var callbackStart = response.IndexOf("jsonp_prod(");
        if (callbackStart < 0) return false;
        var jsonStart = callbackStart + "jsonp_prod(".Length;
        var jsonEnd = response.LastIndexOf(");}catch");
        if (jsonEnd < 0 || jsonEnd <= jsonStart) return false;

        var json = JsonDocument.Parse(response[jsonStart..jsonEnd]);
        foreach (var prop in json.RootElement.EnumerateObject())
        {
            if (prop.Value.TryGetProperty("Qty", out var qty))
                return qty.GetInt32() > 0;
        }

        return false;
    }

    private static string? ExtractProductId(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return null;
        var segments = uri.AbsolutePath.Split('/');
        return segments.LastOrDefault(s => !string.IsNullOrEmpty(s));
    }
}
