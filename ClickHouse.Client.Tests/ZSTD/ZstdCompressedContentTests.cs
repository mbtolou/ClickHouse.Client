using System.IO;
using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using ClickHouse.Client.Utility;
using ZstdSharp;
using NUnit.Framework;
using System;
using System.Linq;

namespace ClickHouse.Client.Tests.Utility;

[TestFixture]
public class ZstdCompressedContentTests
{
    [Test]
    public async Task Should_Set_ContentEncoding_Header()
    {
        // Arrange
        var original = new StringContent("Hello ClickHouse", Encoding.UTF8);

        // Act
        var compressed = new CompressedContent(original);

        // Assert
        Assert.That(compressed.Headers.ContentEncoding, Does.Contain("zstd"));
    }

    [Test]
    public async Task Should_Produce_Valid_Zstd_Stream()
    {
        // Arrange
        var testData = new string('A', 100_000); // داده تکراری → فشرده‌سازی خوب
        var original = new StringContent(testData, Encoding.UTF8);
        var compressed = new CompressedContent(original);

        // Act - محتوای فشرده را بخوان
        var compressedBytes = await compressed.ReadAsByteArrayAsync();

        // Assert - با ZstdSharp باز کن و بررسی کن
        using var inputStream = new MemoryStream(compressedBytes);
        using var decompressor = new DecompressionStream(inputStream);
        using var reader = new StreamReader(decompressor, Encoding.UTF8);

        var result = await reader.ReadToEndAsync();
        Assert.That(result, Is.EqualTo(testData));
    }

    [Test]
    public async Task Should_Be_Smaller_Than_Original()
    {
        // Arrange
        var testData = string.Join("\n",
            Enumerable.Repeat("INSERT INTO table VALUES (1, 'test', 'data')", 10_000));
        var original = new StringContent(testData, Encoding.UTF8);
        var compressed = new CompressedContent(original);

        // Act
        var compressedBytes = await compressed.ReadAsByteArrayAsync();
        var originalBytes = Encoding.UTF8.GetBytes(testData);

        // Assert
        Assert.That(compressedBytes.Length, Is.LessThan(originalBytes.Length));
        TestContext.Out.WriteLine($"Original: {originalBytes.Length:N0} bytes");
        TestContext.Out.WriteLine($"Compressed: {compressedBytes.Length:N0} bytes");
        TestContext.Out.WriteLine($"Ratio: {(double)compressedBytes.Length / originalBytes.Length:P1}");
    }

    [Test]
    public void Should_Throw_On_Null_Content()
    {
        Assert.Throws<ArgumentNullException>(() => new CompressedContent(null!));
    }

    [Test]
    public async Task Should_Copy_Original_Headers()
    {
        // Arrange
        var original = new StringContent("data", Encoding.UTF8, "application/json");

        // Act
        var compressed = new CompressedContent(original);

        // Assert
        Assert.That(compressed.Headers.ContentType?.MediaType, Is.EqualTo("application/json"));
    }
}
