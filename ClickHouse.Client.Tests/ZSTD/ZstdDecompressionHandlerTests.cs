using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ClickHouse.Client.Http;
using ZstdSharp;
using NUnit.Framework;
using System.Linq;

namespace ClickHouse.Client.Tests.Http;

[TestFixture]
public class ZstdDecompressionHandlerTests
{
    /// <summary>
    /// یک Handler جعلی که پاسخ ZSTD برمی‌گرداند
    /// </summary>
    private class FakeZstdServerHandler : HttpMessageHandler
    {
        private readonly string responseBody;

        public FakeZstdServerHandler(string responseBody)
        {
            this.responseBody = responseBody;
        }

        public HttpRequestMessage LastRequest { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequest = request;

            // داده را با ZSTD فشرده کن (شبیه‌سازی سرور ClickHouse)
            var plainBytes = Encoding.UTF8.GetBytes(responseBody);
            using var compressor = new Compressor(level: 3);
            var compressedBytes = compressor.Wrap(plainBytes).ToArray();

            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(compressedBytes)
            };
            response.Content.Headers.Add("Content-Encoding", "zstd");
            response.Content.Headers.ContentType =
                new System.Net.Http.Headers.MediaTypeHeaderValue("text/tab-separated-values");

            return Task.FromResult(response);
        }
    }

    /// <summary>
    /// یک Handler جعلی که پاسخ GZip برمی‌گرداند (بدون ZSTD)
    /// </summary>
    private class FakeGZipServerHandler : HttpMessageHandler
    {
        private readonly string responseBody;

        public FakeGZipServerHandler(string responseBody)
        {
            this.responseBody = responseBody;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var plainBytes = Encoding.UTF8.GetBytes(responseBody);
            using var ms = new System.IO.MemoryStream();
            using (var gzip = new System.IO.Compression.GZipStream(ms,
                System.IO.Compression.CompressionMode.Compress, leaveOpen: true))
            {
                gzip.Write(plainBytes);
            }

            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(ms.ToArray())
            };
            response.Content.Headers.Add("Content-Encoding", "gzip");

            return Task.FromResult(response);
        }
    }

    [Test]
    public async Task Should_Decompress_Zstd_Response()
    {
        // Arrange
        var expectedData = "1\tAlice\t30\n2\tBob\t25\n3\tCharlie\t35";
        var fakeServer = new FakeZstdServerHandler(expectedData);
        var handler = new ZstdDecompressionHandler(fakeServer);
        var client = new HttpClient(handler);

        // Act
        var response = await client.GetAsync("http://localhost:8123/?query=SELECT+*");
        var result = await response.Content.ReadAsStringAsync();

        // Assert
        Assert.That(result, Is.EqualTo(expectedData));
    }

    [Test]
    public async Task Should_Send_AcceptEncoding_Zstd_Header()
    {
        // Arrange
        var fakeServer = new FakeZstdServerHandler("data");
        var handler = new ZstdDecompressionHandler(fakeServer);
        var client = new HttpClient(handler);

        // Act
        await client.GetAsync("http://localhost:8123/");

        // Assert
        Assert.That(fakeServer.LastRequest!.Headers.AcceptEncoding
            .Any(e => e.Value == "zstd"), Is.True);
    }

    [Test]
    public async Task Should_Not_Touch_NonZstd_Response()
    {
        // Arrange - سرور GZip برمی‌گرداند، handler نباید دخالت کند
        var expectedData = "plain text response";
        var fakeServer = new FakeGZipServerHandler(expectedData);
        var handler = new ZstdDecompressionHandler(fakeServer);
        var client = new HttpClient(handler);

        // Act
        var response = await client.GetAsync("http://localhost:8123/");

        // Assert - Content-Encoding هنوز gzip است (دست نخورده)
        Assert.That(response.Content.Headers.ContentEncoding, Does.Contain("gzip"));
    }

    [Test]
    public async Task Should_Handle_Large_Response()
    {
        // Arrange - ۱ میلیون ردیف
        var sb = new StringBuilder();
        for (int i = 0; i < 1_000_000; i++)
        {
            sb.AppendLine($"{i}\tName_{i}\t{i * 1.5}");
        }
        var expectedData = sb.ToString();

        var fakeServer = new FakeZstdServerHandler(expectedData);
        var handler = new ZstdDecompressionHandler(fakeServer);
        var client = new HttpClient(handler);

        // Act
        var response = await client.GetAsync("http://localhost:8123/");
        var result = await response.Content.ReadAsStringAsync();

        // Assert
        Assert.That(result, Is.EqualTo(expectedData));
    }
}
