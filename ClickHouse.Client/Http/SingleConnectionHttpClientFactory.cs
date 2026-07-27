using System;
using System.Net;
using System.Net.Http;

namespace ClickHouse.Client.Http;

internal class SingleConnectionHttpClientFactory : IHttpClientFactory, IDisposable
{
    private readonly HttpClientHandler handler;
    private readonly ZstdDecompressionHandler zstdHandler;

    public TimeSpan Timeout { get; init; }

    public SingleConnectionHttpClientFactory()
    {
        handler = new HttpClientHandler()
        {
            // GZip/Deflate هنوز خودکار handle شود
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,
            MaxConnectionsPerServer = 1,
        };

        // ZSTD را دستی اضافه کن
        zstdHandler = new ZstdDecompressionHandler(handler);
    }

    public HttpClient CreateClient(string name)
    {
        // زنجیره: ZstdHandler → HttpClientHandler
        var zstdHandler = new ZstdDecompressionHandler(handler);
        return new HttpClient(zstdHandler, false) { Timeout = Timeout };
    }

    public void Dispose()
    {
        zstdHandler.Dispose();
        handler.Dispose();
    }
}
