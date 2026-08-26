using System;
using System.Net;
using System.Net.Http;
using ClickHouse.Client.Utility;

namespace ClickHouse.Client.Http;

internal class SingleConnectionHttpClientFactory : IHttpClientFactory, IDisposable
{
    private readonly HttpClientHandler handler;
    private readonly ZstdDecompressionHandler zstdHandler;
    public TimeSpan Timeout { get; init; }

    public SingleConnectionHttpClientFactory(ClickHouseCompression compression = ClickHouseCompression.Zstd)
    {
        handler = new HttpClientHandler()
        {
            MaxConnectionsPerServer = 1,
        };

        // فقط اگر GZip/Deflate است، از AutomaticDecompression استفاده کن
        if (compression is ClickHouseCompression.GZip or ClickHouseCompression.Deflate)
        {
            handler.AutomaticDecompression = compression switch
            {
                ClickHouseCompression.GZip => DecompressionMethods.GZip,
                ClickHouseCompression.Deflate => DecompressionMethods.Deflate,
                _ => DecompressionMethods.None,
            };
        }

        // اگر ZSTD است، handler دستی اضافه کن
        if (compression == ClickHouseCompression.Zstd)
        {
            zstdHandler = new ZstdDecompressionHandler(handler);
        }
    }

    public HttpClient CreateClient(string name)
    {
        var effectiveHandler = (HttpMessageHandler)zstdHandler ?? handler;
        return new HttpClient(effectiveHandler, false) { Timeout = Timeout };
    }

    public void Dispose()
    {
        zstdHandler?.Dispose();
        handler.Dispose();
    }
}
