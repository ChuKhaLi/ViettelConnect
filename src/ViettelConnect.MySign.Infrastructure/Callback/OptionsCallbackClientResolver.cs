using Microsoft.Extensions.Options;
using ViettelConnect.MySign.Application.Abstractions;
using ViettelConnect.MySign.Infrastructure.Configuration;

namespace ViettelConnect.MySign.Infrastructure.Callback;

/// <summary>Mặc định: đúng một client callback lấy từ <c>options.Callback</c>, thuộc đơn vị "default". Để trống = tắt callback.</summary>
internal sealed class OptionsCallbackClientResolver : ICallbackClientResolver
{
    internal const string DefaultTenantKey = "default";

    private readonly IOptions<MySignOptions> _options;

    public OptionsCallbackClientResolver(IOptions<MySignOptions> options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    public ValueTask<CallbackClient?> FindAsync(string clientId, CancellationToken ct)
    {
        var callback = _options.Value.Callback;
        if (string.IsNullOrWhiteSpace(callback.ClientId)
            || string.IsNullOrWhiteSpace(callback.ClientSecret)
            || !string.Equals(callback.ClientId, clientId, StringComparison.Ordinal))
        {
            return ValueTask.FromResult<CallbackClient?>(null);
        }

        return ValueTask.FromResult<CallbackClient?>(new CallbackClient(DefaultTenantKey, callback.ClientId, callback.ClientSecret));
    }
}
