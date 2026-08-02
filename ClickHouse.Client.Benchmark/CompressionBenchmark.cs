using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Order;
using System;
using System.IO;
using System.IO.Compression;
using System.Text;
using ZstdSharp;

namespace ClickHouse.Client.Benchmark;

/// <summary>
/// تولید داده‌ی واقع‌گرایانه با entropy قابل کنترل — نه بیش‌ازحد تکراری، نه random خالص.
/// schema شبیه یک جدول event-based واقعی ClickHouse است.
/// </summary>
public static class DataGenerator
{
    public sealed record Options(int Rows, double NullRatio, double ValueEntropy, int Seed);

    // ستون‌های با cardinality مختلف → entropy متفاوت
    private static readonly string[] EventTypes =
        { "click", "view", "purchase", "login", "logout", "signup", "error", "timeout", "heartbeat", "refresh" };
    private static readonly string[] Countries =
        { "US","DE","FR","JP","BR","IN","GB","CA","AU","KR","NL","SE","ES","IT","RU","CN","MX","ZA","EG","TR","PL","CH","NO","FI","DK","IE","PT","GR","CZ","HU" };
    private static readonly string[] Devices =
        { "mobile", "desktop", "tablet", "smart_tv", "wearable" };

    public static byte[] Generate(Options o)
    {
        var rng = new Random(o.Seed);                 // deterministic → قابل تکرار
        using var ms = new MemoryStream(o.Rows * 96);
        using var w = new BinaryWriter(ms, Encoding.UTF8, leaveOpen: true);

        long baseTs = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc).Ticks;
        var uuid = new byte[16];

        for (int i = 0; i < o.Rows; i++)
        {
            // 1) event_id — sequential (compressible، مثل surrogate key)
            w.Write((long)i);

            // 2) timestamp — monotonic با jitter کم (compressible، مثل time-series)
            w.Write(baseTs + (long)i * TimeSpan.TicksPerMillisecond * 100 + rng.Next(0, 50));

            // 3) user_id — 16 بایت random (high entropy، مثل UUID)
            rng.NextBytes(uuid);
            w.Write(uuid);

            // 4) event_type — pool کوچک (compressible، مثل enum)
            w.Write(EventTypes[rng.Next(EventTypes.Length)]);

            // 5) country — pool متوسط (medium entropy)
            w.Write(Countries[rng.Next(Countries.Length)]);

            // 6) device — pool کوچک
            w.Write(Devices[rng.Next(Devices.Length)]);

            // 7) value — Float64 با entropy قابل کنترل
            double v = rng.NextDouble() * 1000.0;
            if (o.ValueEntropy < 1.0)
            {
                // quantize: ValueEntropy کمتر → مقادیر گسسته‌تر → compressible‌تر
                int buckets = Math.Max(2, (int)(o.ValueEntropy * 1000));
                double step = 1000.0 / buckets;
                v = Math.Round(v / step) * step;
            }
            w.Write(v);

            // 8) duration_ms — Int32 با توزیع skewed (medium، مثل latency)
            w.Write((int)(rng.NextDouble() * rng.NextDouble() * 5000));

            // 9) metadata — Nullable(String): گاهی null، گاهی JSON کوتاه
            if (rng.NextDouble() < o.NullRatio)
            {
                w.Write((byte)1);
            }
            else
            {
                w.Write((byte)0);
                w.Write($"{{\"src\":\"{EventTypes[rng.Next(EventTypes.Length)]}\",\"retry\":{rng.Next(0, 5)},\"ok\":{(rng.NextDouble() > 0.1 ? "true" : "false")}}}");
            }

            // 10) status — Nullable(Int32)
            if (rng.NextDouble() < o.NullRatio)
            {
                w.Write((byte)1);
            }
            else
            {
                w.Write((byte)0);
                w.Write(rng.Next(0, 600));
            }
        }

        w.Flush();
        return ms.ToArray();
    }
}

[MemoryDiagnoser]
[Orderer(SummaryOrderPolicy.FastestToSlowest)]
public class CompressionBenchmark
{
    private const int RowCount = 800_000;   // ≈ 60–70 MB داده‌ی خام

    private byte[] rawData = null!;
    private byte[] zstdL3Compressed = null!;      // cache برای decompress
    private byte[] gzipOptimalCompressed = null!; // cache برای decompress

    private Compressor zstdL1 = null!;
    private Compressor zstdL3 = null!;
    private Compressor zstdL9 = null!;
    private Decompressor zstdDecompressor = null!;

    // دو سناریو: Realistic (entropy متوسط) و HighEntropy (کمتر compressible)
    [Params("Realistic", "HighEntropy")]
    public string Dataset { get; set; } = "Realistic";

    [GlobalSetup]
    public void Setup()
    {
        // برای استفاده از داده‌ی واقعی خودتان، این خط را جایگزین کنید:
        // rawData = File.ReadAllBytes(@"path\to\real_clickhouse_export.bin");

        var options = Dataset switch
        {
            "Realistic" => new DataGenerator.Options(RowCount, NullRatio: 0.10, ValueEntropy: 0.50, Seed: 42),
            "HighEntropy" => new DataGenerator.Options(RowCount, NullRatio: 0.05, ValueEntropy: 0.98, Seed: 42),
            _ => throw new InvalidOperationException($"Unknown dataset: {Dataset}")
        };

        rawData = DataGenerator.Generate(options);

        // contextها یک‌بار ساخته و reuse می‌شوند (مثل production)
        zstdL1 = new Compressor(level: 1);
        zstdL3 = new Compressor(level: 3);
        zstdL9 = new Compressor(level: 9);
        zstdDecompressor = new Decompressor();

        // داده‌ی فشرده یک‌بار ساخته می‌شود → decompress دیگر compress نمی‌کند (رفع باگ قبلی)
        zstdL3Compressed = ZstdCompress(zstdL3, rawData);
        gzipOptimalCompressed = GZipCompress(rawData, CompressionLevel.Optimal);

        // گزارش ratio — برتری اصلی Zstd اینجاست
        Console.WriteLine($"=== Dataset: {Dataset} | Raw: {rawData.Length:N0} B ===");
        PrintRatio("Zstd L1", ZstdCompress(zstdL1, rawData));
        PrintRatio("Zstd L3", zstdL3Compressed);
        PrintRatio("Zstd L9", ZstdCompress(zstdL9, rawData));
        PrintRatio("GZip Fastest", GZipCompress(rawData, CompressionLevel.Fastest));
        PrintRatio("GZip Optimal", gzipOptimalCompressed);
        PrintRatio("GZip Smallest", GZipCompress(rawData, CompressionLevel.SmallestSize));
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        zstdL1?.Dispose();
        zstdL3?.Dispose();
        zstdL9?.Dispose();
        zstdDecompressor?.Dispose();
    }

    private void PrintRatio(string name, byte[] compressed)
    {
        double pct = 100.0 * compressed.Length / rawData.Length;
        double ratio = (double)rawData.Length / compressed.Length;
        Console.WriteLine($"  {name,-13} {compressed.Length,12:N0} B  ({pct,5:F1}%  →  {ratio,5:F2}:1)");
    }

    // ---------- Compress (سطح‌های برابر) ----------
    // مقایسه‌ی speed:  Zstd_L1  ↔  GZip_Fastest
    // مقایسه‌ی balanced: Zstd_L3 ↔  GZip_Optimal
    // مقایسه‌ی max-ratio: Zstd_L9 ↔  GZip_Smallest

    [Benchmark] public byte[] Zstd_L1_Compress() => ZstdCompress(zstdL1, rawData);
    [Benchmark] public byte[] Zstd_L3_Compress() => ZstdCompress(zstdL3, rawData);
    [Benchmark] public byte[] Zstd_L9_Compress() => ZstdCompress(zstdL9, rawData);
    [Benchmark] public byte[] GZip_Fastest_Compress() => GZipCompress(rawData, CompressionLevel.Fastest);
    [Benchmark] public byte[] GZip_Optimal_Compress() => GZipCompress(rawData, CompressionLevel.Optimal);
    [Benchmark] public byte[] GZip_Smallest_Compress() => GZipCompress(rawData, CompressionLevel.SmallestSize);

    // ---------- Decompress (از داده‌ی cache‌شده، بدون compress در iteration) ----------

    [Benchmark] public byte[] Zstd_Decompress() => zstdDecompressor.Unwrap(zstdL3Compressed).ToArray();
    [Benchmark] public byte[] GZip_Decompress() => GZipDecompress(gzipOptimalCompressed);

    // ---------- Helpers (یکسان برای هر دو → عادلانه) ----------

    private static byte[] ZstdCompress(Compressor c, byte[] data) => c.Wrap(data).ToArray();

    private static byte[] GZipCompress(byte[] data, CompressionLevel level)
    {
        using var ms = new MemoryStream();
        using (var gz = new GZipStream(ms, level, leaveOpen: true))
            gz.Write(data);
        return ms.ToArray();
    }

    private static byte[] GZipDecompress(byte[] compressed)
    {
        using var input = new MemoryStream(compressed);
        using var gz = new GZipStream(input, CompressionMode.Decompress);
        using var output = new MemoryStream();
        gz.CopyTo(output);
        return output.ToArray();
    }
}
