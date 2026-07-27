using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Order;
using ZstdSharp;
using System.IO.Compression;
using System.IO;

namespace ClickHouse.Client.Benchmark;

[MemoryDiagnoser]
[Orderer(SummaryOrderPolicy.FastestToSlowest)]
public class CompressionBenchmark
{
    private byte[] rawData = null!;

    [GlobalSetup]
    public void Setup()
    {
        // شبیه‌سازی داده واقعی ClickHouse (TSV format)
        var sb = new System.Text.StringBuilder();
        for (int i = 0; i < 1_000_000; i++)
        {
            sb.AppendLine($"{i}\tUser_{i}\t{i * 1.5}\t2024-01-{(i % 28) + 1:D2}");
        }
        rawData = System.Text.Encoding.UTF8.GetBytes(sb.ToString());
    }

    [Benchmark(Baseline = true)]
    public byte[] NoCompression() => rawData;

    [Benchmark]
    public byte[] GZip_Fastest()
    {
        using var ms = new MemoryStream();
        using (var gzip = new GZipStream(ms, CompressionLevel.Fastest, leaveOpen: true))
        {
            gzip.Write(rawData);
        }
        return ms.ToArray();
    }

    [Benchmark]
    public byte[] Zstd_Level3()
    {
        using var compressor = new Compressor(level: 3);
        return compressor.Wrap(rawData).ToArray();
    }

    [Benchmark]
    public byte[] Zstd_Level9()
    {
        using var compressor = new Compressor(level: 9);
        return compressor.Wrap(rawData).ToArray();
    }

    // --- Decompression ---

    [Benchmark]
    public byte[] Zstd_Decompress()
    {
        using var compressor = new Compressor(level: 3);
        var compressed = compressor.Wrap(rawData).ToArray();

        using var decompressor = new Decompressor();
        return decompressor.Unwrap(compressed).ToArray();
    }

    [Benchmark]
    public byte[] GZip_Decompress()
    {
        using var ms = new MemoryStream();
        using (var gzip = new GZipStream(ms, CompressionLevel.Fastest, leaveOpen: true))
        {
            gzip.Write(rawData);
        }
        var compressed = ms.ToArray();

        using var inputMs = new MemoryStream(compressed);
        using var gzipIn = new GZipStream(inputMs, CompressionMode.Decompress);
        using var outputMs = new MemoryStream();
        gzipIn.CopyTo(outputMs);
        return outputMs.ToArray();
    }
}
