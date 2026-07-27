using System;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;

/// <summary>
/// Originally sourced from https://stackoverflow.com/questions/16673714/how-to-compress-http-request-on-the-fly-and-without-loading-compressed-buffer-in
/// </summary>
namespace ClickHouse.Client.Utility;

public class CompressedContent : HttpContent
{
    private readonly HttpContent originalContent;
    private readonly ClickHouseCompression compressionMethod;

    public CompressedContent(HttpContent content, ClickHouseCompression compressionMethod = ClickHouseCompression.Zstd)
    {
        originalContent = content ?? throw new ArgumentNullException(nameof(content));
        this.compressionMethod = compressionMethod;

        foreach (var header in originalContent.Headers)
        {
            Headers.TryAddWithoutValidation(header.Key, header.Value);
        }

        Headers.ContentEncoding.Add(compressionMethod switch
        {
            ClickHouseCompression.GZip => "gzip",
            ClickHouseCompression.Deflate => "deflate",
            ClickHouseCompression.Zstd => "zstd",
            _ => throw new ArgumentException($"Unsupported: {compressionMethod}")
        });
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            originalContent?.Dispose();
        }
        base.Dispose(disposing);
    }

    protected override bool TryComputeLength(out long length)
    {
        length = -1;
        return false;
    }

    protected override async Task SerializeToStreamAsync(Stream stream, TransportContext context)
    {
        using Stream compressedStream = compressionMethod switch
        {
            ClickHouseCompression.GZip => new GZipStream(stream, CompressionLevel.Fastest, leaveOpen: true),
            ClickHouseCompression.Deflate => new DeflateStream(stream, CompressionMode.Compress, leaveOpen: true),
            ClickHouseCompression.Zstd => new ZstdSharp.CompressionStream(stream, level: 3, bufferSize: 0, leaveOpen: true),
            _ => throw new ArgumentOutOfRangeException(nameof(compressionMethod))
        };

        await originalContent.CopyToAsync(compressedStream).ConfigureAwait(false);
    }
}
