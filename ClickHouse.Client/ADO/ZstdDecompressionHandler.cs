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

        // بررسی دقیق هدر Content-Encoding
        if (response.Content.Headers.ContentEncoding.Any(e =>
            e.Equals("zstd", StringComparison.OrdinalIgnoreCase)))
        {
            // دریافت استریم فشرده از محتوای اصلی
            var compressedStream = await response.Content
                .ReadAsStreamAsync(cancellationToken)
                .ConfigureAwait(false);

            // ایجاد استریم Decompress
            // نکته: DecompressionStream به طور خودکار compressedStream را مدیریت می‌کند
            var decompressedStream = new DecompressionStream(compressedStream);

            // ساخت محتوای جدید. 
            // نکته مهم: باید به StreamContent بگوییم که استریم را خودش Dispose کند
            var newContent = new StreamContent(decompressedStream);

            // انتقال هدرها
            foreach (var header in response.Content.Headers)
            {
                if (!header.Key.Equals("Content-Encoding", StringComparison.OrdinalIgnoreCase) &&
                    !header.Key.Equals("Content-Length", StringComparison.OrdinalIgnoreCase))
                {
                    newContent.Headers.TryAddWithoutValidation(header.Key, header.Value);
                }
            }

            // جایگزینی محتوا
            response.Content = newContent;

            // ⚠️ نکته کلیدی: اینجا نباید originalContent را دستی Dispose کنیم!
            // زیرا ممکن است استریم زیرین را ببندد. 
            // اجازه دهید Garbage Collector و Dispose نهایی response کار خود را انجام دهند.
        }

        return response;
    }
}
