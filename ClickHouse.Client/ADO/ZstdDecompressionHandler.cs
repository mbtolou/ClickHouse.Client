using System;
using System.IO;
using System.Linq;
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
        var response = await base.SendAsync(request, cancellationToken)
            .ConfigureAwait(false);

        if (response.Content.Headers.ContentEncoding.Any(e =>
            e.Equals("zstd", StringComparison.OrdinalIgnoreCase)))
        {
            var originalContent = response.Content;
            Stream compressedStream = null;
            DecompressionStream decompressedStream = null;

            try
            {
                // دریافت استریم فشرده
                compressedStream = await originalContent
                    .ReadAsStreamAsync(cancellationToken)
                    .ConfigureAwait(false);

                // ایجاد لایه Decompress
                decompressedStream = new DecompressionStream(compressedStream);

                // ساخت محتوای جدید
                var newContent = new StreamContent(decompressedStream);

                // انتقال هدرها (به جز موارد فشرده‌سازی)
                foreach (var header in originalContent.Headers)
                {
                    if (!header.Key.Equals("Content-Encoding", StringComparison.OrdinalIgnoreCase) &&
                        !header.Key.Equals("Content-Length", StringComparison.OrdinalIgnoreCase))
                    {
                        newContent.Headers.TryAddWithoutValidation(header.Key, header.Value);
                    }
                }

                // جایگزینی محتوا
                response.Content = newContent;

                // آزادسازی منبع اصلی پس از جایگزینی موفق
                originalContent.Dispose();
            }
            catch
            {
                // پاکسازی منابع در صورت بروز خطا
                decompressedStream?.Dispose();
                compressedStream?.Dispose();
                throw;
            }
        }

        return response;
    }
}
