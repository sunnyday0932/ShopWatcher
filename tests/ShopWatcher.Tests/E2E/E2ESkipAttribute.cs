namespace ShopWatcher.Tests.E2E;

/// <summary>
/// Skip E2E tests unless RUN_E2E environment variable is set to "true".
/// Run with: RUN_E2E=true dotnet test --filter "Category=E2E"
/// </summary>
public sealed class E2EFactAttribute : FactAttribute
{
    public E2EFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("RUN_E2E") != "true")
            Skip = "E2E 測試需設定環境變數 RUN_E2E=true 才會執行";
    }
}
