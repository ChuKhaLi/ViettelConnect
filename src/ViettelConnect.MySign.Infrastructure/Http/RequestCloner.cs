using System.Net.Http.Headers;

namespace ViettelConnect.MySign.Infrastructure.Http;

/// <summary>
/// Nhân bản <see cref="HttpRequestMessage"/> để gửi lại (retry/401). Thân được đệm thành byte nên gửi lại
/// được nhiều lần; <see cref="HttpRequestMessage.Options"/> (thông tin đăng nhập) cũng được sao chép.
/// </summary>
internal static class RequestCloner
{
    public static async Task<byte[]?> BufferContentAsync(HttpRequestMessage source, CancellationToken ct) =>
        source.Content is null ? null : await source.Content.ReadAsByteArrayAsync(ct).ConfigureAwait(false);

    public static HttpRequestMessage Clone(HttpRequestMessage source, byte[]? body, bool skipAuthorizationHeader = false)
    {
        var clone = new HttpRequestMessage(source.Method, source.RequestUri)
        {
            Version = source.Version,
            VersionPolicy = source.VersionPolicy,
        };

        if (body is not null)
        {
            var content = new ByteArrayContent(body);
            foreach (var header in source.Content!.Headers)
            {
                content.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }

            clone.Content = content;
        }

        foreach (var header in source.Headers)
        {
            if (skipAuthorizationHeader && header.Key.Equals(nameof(HttpRequestHeaders.Authorization), StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            clone.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }

        foreach (var option in (IDictionary<string, object?>)source.Options)
        {
            ((IDictionary<string, object?>)clone.Options).Add(option);
        }

        return clone;
    }
}
