using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ViettelConnect.MySign.Application.Abstractions;
using ViettelConnect.MySign.Application.UseCases;
using ViettelConnect.MySign.Infrastructure.Caching;
using ViettelConnect.MySign.Infrastructure.Callback;
using ViettelConnect.MySign.Infrastructure.Certificates;
using ViettelConnect.MySign.Infrastructure.Configuration;
using ViettelConnect.MySign.Infrastructure.Credentials;
using ViettelConnect.MySign.Infrastructure.Http;
using ViettelConnect.MySign.Infrastructure.Logging;
using ViettelConnect.MySign.Infrastructure.Signing;
using ViettelConnect.MySign.Infrastructure.Time;
using ViettelConnect.MySign.Infrastructure.Wire;

namespace ViettelConnect.MySign.Infrastructure.DependencyInjection;

/// <summary>
/// Đăng ký lõi (options, mặc định của mọi cổng, pipeline HttpClient, use case). Chỉ gói Client gọi; ứng dụng dùng
/// <c>AddViettelMySign</c>. Mọi cổng mặc định đăng ký bằng <c>TryAdd</c> nên ứng dụng thay được, đăng ký trước hay sau đều thắng
/// (với dịch vụ đơn, DI lấy đăng ký sau cùng). <c>ICorrelationIdAccessor</c> do tầng Client đăng ký.
/// </summary>
internal static class ServiceCollectionExtensions
{
    /// <summary>Biên cộng thêm vào <c>Polling.TotalTimeout</c> để ra timeout HttpClient mặc định.</summary>
    internal static readonly TimeSpan DefaultHttpTimeoutMargin = TimeSpan.FromMinutes(1);

    internal static IServiceCollection AddViettelMySignCore(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services
            .AddOptions<MySignOptions>()
            .Bind(configuration.GetSection(MySignOptions.SectionName))
            .ValidateOnStart();

        AddCoreServices(services);
        return services;
    }

    internal static IServiceCollection AddViettelMySignCore(this IServiceCollection services, Action<MySignOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);

        services
            .AddOptions<MySignOptions>()
            .Configure(configure)
            .ValidateOnStart();

        AddCoreServices(services);
        return services;
    }

    /// <summary>Timeout HttpClient: luôn hữu hạn, đủ dài cho người ký xác nhận ở chế độ đồng bộ.</summary>
    internal static TimeSpan ResolveHttpTimeout(MySignOptions options) =>
        options.Advanced.HttpTimeout ?? options.Polling.TotalTimeout + DefaultHttpTimeoutMargin;

    internal static MySignSigningSettings BuildSigningSettings(MySignOptions options) =>
        new(
            options.SignMode,
            options.Advanced.PollingAsyncCode,
            options.Polling.Interval,
            options.Polling.TotalTimeout);

    private static void AddCoreServices(IServiceCollection services)
    {
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<MySignOptions>, MySignOptionsValidator>());

        // Gọi AddViettelMySign hai lần chỉ cộng thêm cấu hình options; không đăng ký lại pipeline (tránh handler bị nhân đôi).
        if (services.Any(d => d.ServiceType == typeof(CoreMarker)))
        {
            return;
        }

        services.AddSingleton<CoreMarker>();

        // Mặc định của các cổng (singleton giữ trạng thái chung của tiến trình).
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<IDelayer, TaskDelayer>();
        services.TryAddSingleton<ITokenCache, InMemoryTokenCache>();
        services.TryAddSingleton<IMySignCredentialsAccessor, OptionsMySignCredentialsAccessor>();
        services.TryAddSingleton<ISignAuthorizationProvider, MobileConfirmationAuthorizationProvider>();
        services.TryAddSingleton<ICallbackTokenStore, InMemoryCallbackTokenStore>();
        services.TryAddSingleton<IPendingSignatureStore>(sp => new InMemoryPendingSignatureStore(
            sp.GetRequiredService<TimeProvider>(),
            sp.GetRequiredService<IOptions<MySignOptions>>().Value.Callback.PendingMaxAge));
        services.TryAddSingleton<ICallbackClientResolver, OptionsCallbackClientResolver>();
        services.TryAddSingleton<SingleFlightRefresh>();
        services.TryAddSingleton(sp => BuildSigningSettings(sp.GetRequiredService<IOptions<MySignOptions>>().Value));

        // Phụ thuộc ICorrelationIdAccessor (scoped) nên cũng scoped.
        services.TryAddScoped<ICertificateSelector, FirstUsableCertificateSelector>();

        // Pipeline: HttpClient -> TransientFailureRetryHandler (ngoài) -> MySignAuthHandler (trong) -> mạng.
        services.TryAddTransient<TransientFailureRetryHandler>(sp => new TransientFailureRetryHandler(
            sp.GetRequiredService<IOptions<MySignOptions>>(),
            sp.GetRequiredService<IDelayer>(),
            sp.GetRequiredService<ICorrelationIdAccessor>(),
            sp.GetRequiredService<TimeProvider>()));

        // Wire client lấy trễ qua Func để tránh vòng handler -> wire client -> HttpClient -> handler.
        // Login đi qua cùng HttpClient nhưng được đánh dấu bỏ qua handler xác thực.
        services.TryAddTransient<MySignAuthHandler>(sp => new MySignAuthHandler(
            () => sp.GetRequiredService<IMySignWireClient>(),
            sp.GetRequiredService<ITokenCache>(),
            sp.GetRequiredService<SingleFlightRefresh>(),
            sp.GetRequiredService<TimeProvider>(),
            sp.GetRequiredService<ICorrelationIdAccessor>(),
            sp.GetRequiredService<ILogger<MySignAuthHandler>>()));

        services
            .AddHttpClient<MySignWireClient>((sp, http) =>
            {
                // Không bao giờ Timeout.InfiniteTimeSpan; validator đã loại giá trị <= 0.
                http.Timeout = ResolveHttpTimeout(sp.GetRequiredService<IOptions<MySignOptions>>().Value);
            })
            .AddHttpMessageHandler<TransientFailureRetryHandler>()
            .AddHttpMessageHandler<MySignAuthHandler>();

        // Decorator ghi log bọc wire client thật.
        services.TryAddScoped<IMySignWireClient>(sp => new MySignCallLogger(
            sp.GetRequiredService<MySignWireClient>(),
            sp.GetRequiredService<ILogger<MySignCallLogger>>(),
            sp.GetRequiredService<ICorrelationIdAccessor>()));

        // Use case.
        services.TryAddScoped<ListCertificates>();
        services.TryAddScoped<PollSignStatus>();
        services.TryAddScoped<SignHashes>();
        services.TryAddScoped<BeginSignHashes>();
        services.TryAddScoped(sp => new HandleCallbackResult(
            sp.GetRequiredService<ICallbackTokenStore>(),
            sp.GetRequiredService<IPendingSignatureStore>(),
            sp.GetRequiredService<ILogger<HandleCallbackResult>>()));
        services.TryAddScoped(sp => new HandleCallbackTokenRequest(
            sp.GetRequiredService<ICallbackClientResolver>(),
            sp.GetRequiredService<ICallbackTokenStore>(),
            sp.GetRequiredService<IOptions<MySignOptions>>().Value.Callback.TokenLifetime,
            sp.GetRequiredService<TimeProvider>(),
            sp.GetRequiredService<ILogger<HandleCallbackTokenRequest>>()));
    }

    /// <summary>Đánh dấu lõi đã được đăng ký.</summary>
    private sealed class CoreMarker
    {
    }
}
