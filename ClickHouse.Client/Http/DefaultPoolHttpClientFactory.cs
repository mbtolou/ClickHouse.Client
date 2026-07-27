using System;
using System.Net;
using System.Net.Http;

namespace ClickHouse.Client.Http;

internal class DefaultPoolHttpClientFactory : IHttpClientFactory, IDisposable
{
    private static readonly HttpClientHandler DefaultHandler = new()
    {
        AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,
    };

    private static readonly ZstdDecompressionHandler ZstdHandler = new(DefaultHandler);

    public TimeSpan Timeout { get; init; }

    public HttpClient CreateClient(string name) => new(ZstdHandler, false)
    {
        Timeout = Timeout,
    };

    // چون static است، Dispose معمولاً لازم نیست
    // ولی برای تست‌ها مفید است:
    public void Dispose()
    {
        ZstdHandler.Dispose();
        DefaultHandler.Dispose();
    }
}
