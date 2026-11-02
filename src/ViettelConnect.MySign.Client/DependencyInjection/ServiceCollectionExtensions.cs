using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using ViettelConnect.MySign.Application.Abstractions;
using ViettelConnect.MySign.Infrastructure.Configuration;
using ViettelConnect.MySign.Infrastructure.DependencyInjection;

namespace ViettelConnect.MySign.Client.DependencyInjection;

/// <summary>Điểm đăng ký DI của SDK Viettel MySign.</summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Đọc cấu hình từ mục <c>Viettel:MySign</c>, kiểm tra options khi khởi động (<c>ValidateOnStart</c>), đăng ký
    /// mặc định của mọi cổng và facade <see cref="IViettelMySignClient"/> (scoped).
    /// </summary>
    /// <remarks>
    /// Chế độ <c>CredentialsMode = Dynamic</c>: đăng ký <c>IMySignCredentialsAccessor</c> của ứng dụng (singleton),
    /// trước hoặc sau lời gọi này đều được.
    /// </remarks>
    public static IServiceCollection AddViettelMySign(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddViettelMySignCore(configuration);
        AddClientFacade(services);
        return services;
    }

    /// <summary>Như <see cref="AddViettelMySign(IServiceCollection, IConfiguration)"/> nhưng cấu hình bằng code.</summary>
    public static IServiceCollection AddViettelMySign(this IServiceCollection services, Action<MySignOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);

        services.AddViettelMySignCore(configure);
        AddClientFacade(services);
        return services;
    }

    private static void AddClientFacade(IServiceCollection services)
    {
        services.TryAddScoped<ICorrelationIdAccessor, LibraryCorrelationIdAccessor>();
        services.TryAddScoped<IViettelMySignClient, ViettelMySignClient>();
    }
}
