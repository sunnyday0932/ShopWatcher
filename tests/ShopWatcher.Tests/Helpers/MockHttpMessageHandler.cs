namespace ShopWatcher.Tests.Helpers;

public class MockHttpMessageHandler(string responseContent) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var response = new HttpResponseMessage(System.Net.HttpStatusCode.OK)
        {
            Content = new StringContent(responseContent)
        };
        return Task.FromResult(response);
    }
}
