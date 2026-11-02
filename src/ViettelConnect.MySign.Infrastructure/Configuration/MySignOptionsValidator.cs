using Microsoft.Extensions.Options;
using ViettelConnect.MySign.Domain.Signing;

namespace ViettelConnect.MySign.Infrastructure.Configuration;

internal sealed class MySignOptionsValidator : IValidateOptions<MySignOptions>
{
    public ValidateOptionsResult Validate(string? name, MySignOptions options)
    {
        var errors = new List<string>();
        var s = MySignOptions.SectionName;

        if (string.IsNullOrWhiteSpace(options.BaseUrl))
        {
            errors.Add($"{s}:BaseUrl is required.");
        }
        else if (!Uri.TryCreate(options.BaseUrl, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttps && !IsLoopbackHttp(uri)))
        {
            errors.Add($"{s}:BaseUrl must be an absolute https URI.");
        }

        if (!Enum.IsDefined(options.CredentialsMode))
        {
            errors.Add($"{s}:CredentialsMode is not a known value (Static|Dynamic).");
        }
        else if (options.CredentialsMode == CredentialsMode.Static)
        {
            if (string.IsNullOrWhiteSpace(options.ClientId)) errors.Add($"{s}:ClientId is required.");
            if (string.IsNullOrWhiteSpace(options.ClientSecret)) errors.Add($"{s}:ClientSecret is required.");
            if (string.IsNullOrWhiteSpace(options.ProfileId)) errors.Add($"{s}:ProfileId is required.");
            if (string.IsNullOrWhiteSpace(options.UserId)) errors.Add($"{s}:UserId is required.");
        }

        if (!Enum.IsDefined(options.SignMode))
        {
            errors.Add($"{s}:SignMode is not a known value (Synchronous|Polling).");
        }

        if (options.Polling.Interval <= TimeSpan.Zero)
        {
            errors.Add($"{s}:Polling:Interval must be > 0.");
        }
        if (options.Polling.TotalTimeout <= options.Polling.Interval)
        {
            errors.Add($"{s}:Polling:TotalTimeout must be > Polling:Interval.");
        }
        if (options.TransportRetry.MaxAttempts < 1)
        {
            errors.Add($"{s}:TransportRetry:MaxAttempts must be >= 1.");
        }
        if (options.TransportRetry.MaxDelay < options.TransportRetry.BaseDelay)
        {
            errors.Add($"{s}:TransportRetry:MaxDelay must be >= BaseDelay.");
        }
        if (options.Callback.TokenLifetime <= TimeSpan.Zero)
        {
            errors.Add($"{s}:Callback:TokenLifetime must be > 0.");
        }
        // Cả hai trống = tắt callback (hợp lệ); chỉ một trong hai là cấu hình dở dang.
        if (string.IsNullOrWhiteSpace(options.Callback.ClientId) != string.IsNullOrWhiteSpace(options.Callback.ClientSecret))
        {
            errors.Add($"{s}:Callback:ClientId and {s}:Callback:ClientSecret must be set together (leave both empty to disable callbacks).");
        }
        if (options.Advanced.PollingAsyncCode is not (1 or 2))
        {
            errors.Add($"{s}:Advanced:PollingAsyncCode must be 1 or 2.");
        }
        // Timeout.InfiniteTimeSpan (-1 ms) cũng bị loại: HttpClient không bao giờ được chờ vô hạn.
        if (options.Advanced.HttpTimeout is { } httpTimeout && httpTimeout <= TimeSpan.Zero)
        {
            errors.Add($"{s}:Advanced:HttpTimeout must be > 0 (leave empty for Polling:TotalTimeout + 1 minute).");
        }

        return errors.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(errors);
    }

    /// <summary>Cho phép http chỉ với loopback để chạy máy chủ MySign giả trong test không cần TLS.</summary>
    private static bool IsLoopbackHttp(Uri uri) =>
        uri.Scheme == Uri.UriSchemeHttp
        && (uri.IsLoopback || string.Equals(uri.Host, "localhost", StringComparison.OrdinalIgnoreCase));
}
