using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using ZstdSharp;

namespace ClickHouse.Client.Http;

internal class ZstdDecompressionHandler : DelegatingHandler
{
    public ZstdDecompressionHandler(HttpMessageHandler innerHandler)
            : base(innerHandler) { }

    protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
    {
        // به سرور بگو ZSTD می‌خواهیم
        request.Headers.TryAddWithoutValidation("Accept-Encoding", "zstd");

        var response = await base.SendAsync(request, cancellationToken)
                .ConfigureAwait(false);

        // اگر سرور ZSTD فرستاد، دستی decompress کن
        if (response.Content.Headers.ContentEncoding.Contains("zstd"))
        {
            var compressedStream = await response.Content
                    .ReadAsStreamAsync()
                    .ConfigureAwait(false);

            var decompressedStream = new DecompressionStream(compressedStream);

            var newContent = new StreamContent(decompressedStream);

            // هدرهای اصلی را نگه دار، ولی Content-Encoding و Length را حذف کن
            foreach (var header in response.Content.Headers)
            {
                if (header.Key != "Content-Encoding"
                        && header.Key != "Content-Length")
                {
                    newContent.Headers.TryAddWithoutValidation(
                            header.Key, header.Value);
                }
            }

            response.Content = newContent;
        }

        return response;
    }
}
