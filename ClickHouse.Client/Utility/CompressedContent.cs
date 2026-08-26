using System;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;

namespace ClickHouse.Client.Utility;

/// <summary>
/// Supports GZip, Deflate and Zstd.
/// Zstd is fully buffered to provide Content-Length (workaround for ClickHouse chunked + Zstd bug).
/// GZip/Deflate use true streaming.
/// </summary>
public class CompressedContent : HttpContent
{
    private readonly HttpContent originalContent;
    private readonly ClickHouseCompression compressionMethod;
    private byte[] compressedBytes;

    public CompressedContent(HttpContent content, ClickHouseCompression compressionMethod = ClickHouseCompression.Zstd)
    {
        originalContent = content ?? throw new ArgumentNullException(nameof(content));
        this.compressionMethod = compressionMethod;

        foreach (var header in originalContent.Headers)
            Headers.TryAddWithoutValidation(header.Key, header.Value);

        Headers.ContentEncoding.Add(compressionMethod switch
        {
            ClickHouseCompression.GZip => "gzip",
            ClickHouseCompression.Deflate => "deflate",
            ClickHouseCompression.Zstd => "zstd",
            _ => throw new ArgumentException($"Unsupported compression method: {compressionMethod}")
        });
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            originalContent?.Dispose();
        base.Dispose(disposing);
    }

    protected override bool TryComputeLength(out long length)
    {
        if (compressionMethod == ClickHouseCompression.Zstd)
        {
            EnsureCompressed();
            length = compressedBytes!.Length;
            return true;
        }

        length = -1;
        return false;
    }

    protected override async Task SerializeToStreamAsync(Stream stream, TransportContext? context)
    {
        if (compressionMethod == ClickHouseCompression.Zstd)
        {
            EnsureCompressed();
            await stream.WriteAsync(compressedBytes.AsMemory()).ConfigureAwait(false);
            return;
        }

        // Explicit pattern to satisfy CA2007 on await using
        Stream compressedStream = compressionMethod switch
        {
            ClickHouseCompression.GZip => new GZipStream(stream, CompressionLevel.Fastest, leaveOpen: true),
            ClickHouseCompression.Deflate => new DeflateStream(stream, CompressionMode.Compress, leaveOpen: true),
            _ => throw new ArgumentOutOfRangeException(nameof(compressionMethod))
        };

        await using (compressedStream.ConfigureAwait(false))
        {
            await originalContent.CopyToAsync(compressedStream).ConfigureAwait(false);
        }
    }

    private void EnsureCompressed()
    {
        if (compressedBytes != null)
            return;

        using var ms = new MemoryStream();
        using (var zstd = new ZstdSharp.CompressionStream(ms, level: 3, leaveOpen: true))
        {
            originalContent.CopyTo(zstd, context: null, cancellationToken: default);
            zstd.Flush();
        }
        compressedBytes = ms.ToArray();
    }
}
