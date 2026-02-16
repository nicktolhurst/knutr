using System.Net;

namespace Knutr.Sdk.Testing;

public class FakeHttpMessageHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, HttpResponseMessage> _handler;

    public FakeHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> handler)
        => _handler = handler;

    public FakeHttpMessageHandler(HttpStatusCode status, string content)
        : this(_ => new HttpResponseMessage(status) { Content = new StringContent(content) }) { }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        => Task.FromResult(_handler(request));
}

public class FakeHttpClientFactory : IHttpClientFactory
{
    private readonly HttpMessageHandler _handler;

    public FakeHttpClientFactory(HttpMessageHandler handler)
        => _handler = handler;

    public FakeHttpClientFactory(HttpStatusCode status, string content)
        : this(new FakeHttpMessageHandler(status, content)) { }

    public HttpClient CreateClient(string name) => new(_handler);
}
