namespace ShopWatcher.Scrapers;

public interface IScraper
{
    bool CanHandle(string url);
    Task<bool> IsInStockAsync(string url, CancellationToken ct = default);
}
