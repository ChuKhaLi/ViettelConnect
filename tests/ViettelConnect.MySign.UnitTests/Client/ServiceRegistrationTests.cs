using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using ViettelConnect.MySign.Application.Abstractions;
using ViettelConnect.MySign.Application.UseCases;
using ViettelConnect.MySign.Client;
using ViettelConnect.MySign.Client.DependencyInjection;
using ViettelConnect.MySign.Domain.Signing;
using ViettelConnect.MySign.Infrastructure.Callback;
using ViettelConnect.MySign.Infrastructure.Configuration;
using ViettelConnect.MySign.Infrastructure.Wire;
using Xunit;

namespace ViettelConnect.MySign.UnitTests.Client;

/// <summary>Đăng ký DI: options, mặc định của cổng (TryAdd), ánh xạ cài đặt ký, timeout HttpClient. Không gọi mạng.</summary>
public class ServiceRegistrationTests
{
    private static void Static(MySignOptions o)
    {
        o.ClientId = "client-A";
        o.ClientSecret = "secret-A";
        o.ProfileId = "profile-1";
        o.UserId = "user-1";
    }

    private sealed class AppCredentials : IMySignCredentialsAccessor
    {
        public MySignCredentials Get() => new("app-client", "app-secret", "app-profile", "app-user");
    }

    private static ServiceProvider Build(Action<IServiceCollection> register) =>
        BuildServices(register).BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });

    private static ServiceCollection BuildServices(Action<IServiceCollection> register)
    {
        var services = new ServiceCollection();
        register(services);
        return services;
    }

    [Fact]
    public void Facade_resolves_in_a_scope_with_scope_validation()
    {
        using var sp = Build(s => s.AddViettelMySign(Static));
        using var scope = sp.CreateScope();
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<IViettelMySignClient>());
    }

    [Fact]
    public void Configuration_overload_binds_the_Viettel_MySign_section()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Viettel:MySign:ClientId"] = "cfg-client",
            ["Viettel:MySign:ClientSecret"] = "cfg-secret",
            ["Viettel:MySign:ProfileId"] = "cfg-profile",
            ["Viettel:MySign:UserId"] = "cfg-user",
            ["Viettel:MySign:SignMode"] = "Synchronous",
            ["Viettel:MySign:Polling:Interval"] = "00:00:01",
        }).Build();

        using var sp = Build(s => s.AddViettelMySign(config));
        var o = sp.GetRequiredService<IOptions<MySignOptions>>().Value;
        Assert.Equal("cfg-client", o.ClientId);
        Assert.Equal(SignMode.Synchronous, o.SignMode);
        Assert.Equal(TimeSpan.FromSeconds(1), o.Polling.Interval);
    }

    [Fact]
    public void Invalid_options_fail_validation_on_access()
    {
        using var sp = Build(s => s.AddViettelMySign(o => o.BaseUrl = "http://example.com"));
        var ex = Assert.Throws<OptionsValidationException>(() => sp.GetRequiredService<IOptions<MySignOptions>>().Value);
        Assert.Contains(ex.Failures, f => f.Contains("BaseUrl"));
        Assert.Contains(ex.Failures, f => f.Contains("ClientId"));
    }

    [Fact]
    public void Signing_settings_are_built_from_all_option_fields()
    {
        using var sp = Build(s => s.AddViettelMySign(o =>
        {
            Static(o);
            o.SignMode = SignMode.Synchronous;
            o.Advanced.PollingAsyncCode = 1;
            o.Polling.Interval = TimeSpan.FromSeconds(7);
            o.Polling.TotalTimeout = TimeSpan.FromMinutes(9);
        }));

        var settings = sp.GetRequiredService<MySignSigningSettings>();
        Assert.Equal(
            new MySignSigningSettings(SignMode.Synchronous, 1, TimeSpan.FromSeconds(7), TimeSpan.FromMinutes(9)),
            settings);
    }

    [Fact]
    public void Every_use_case_is_registered()
    {
        using var sp = Build(s => s.AddViettelMySign(Static));
        using var scope = sp.CreateScope();
        var p = scope.ServiceProvider;
        Assert.NotNull(p.GetRequiredService<ListCertificates>());
        Assert.NotNull(p.GetRequiredService<PollSignStatus>());
        Assert.NotNull(p.GetRequiredService<SignHashes>());
        Assert.NotNull(p.GetRequiredService<BeginSignHashes>());
        Assert.NotNull(p.GetRequiredService<HandleCallbackTokenRequest>());
        Assert.NotNull(p.GetRequiredService<HandleCallbackResult>());
    }

    [Fact]
    public void Default_time_provider_is_system_and_app_override_wins()
    {
        using (var sp = Build(s => s.AddViettelMySign(Static)))
        {
            Assert.Same(TimeProvider.System, sp.GetRequiredService<TimeProvider>());
        }

        var custom = new Microsoft.Extensions.Time.Testing.FakeTimeProvider();
        using (var sp = Build(s =>
        {
            s.AddSingleton<TimeProvider>(custom);
            s.AddViettelMySign(Static);
        }))
        {
            Assert.Same(custom, sp.GetRequiredService<TimeProvider>());
        }
    }

    [Fact]
    public void Default_correlation_accessor_is_the_library_one()
    {
        using var sp = Build(s => s.AddViettelMySign(Static));
        using var scope = sp.CreateScope();
        var correlation = scope.ServiceProvider.GetRequiredService<ICorrelationIdAccessor>();
        Assert.Equal("LibraryCorrelationIdAccessor", correlation.GetType().Name);
        Assert.True(Guid.TryParse(correlation.Current, out _));
    }

    [Fact]
    public void Default_http_timeout_is_polling_total_timeout_plus_one_minute()
    {
        using var sp = Build(s => s.AddViettelMySign(Static));
        var http = sp.GetRequiredService<IHttpClientFactory>().CreateClient(nameof(MySignWireClient));
        Assert.Equal(TimeSpan.FromMinutes(6), http.Timeout);
    }

    [Fact]
    public void Http_timeout_follows_polling_total_timeout_or_explicit_option()
    {
        using (var sp = Build(s => s.AddViettelMySign(o =>
        {
            Static(o);
            o.Polling.TotalTimeout = TimeSpan.FromMinutes(10);
        })))
        {
            var http = sp.GetRequiredService<IHttpClientFactory>().CreateClient(nameof(MySignWireClient));
            Assert.Equal(TimeSpan.FromMinutes(11), http.Timeout);
        }

        using (var sp = Build(s => s.AddViettelMySign(o =>
        {
            Static(o);
            o.Advanced.HttpTimeout = TimeSpan.FromSeconds(90);
        })))
        {
            var http = sp.GetRequiredService<IHttpClientFactory>().CreateClient(nameof(MySignWireClient));
            Assert.Equal(TimeSpan.FromSeconds(90), http.Timeout);
        }
    }

    [Fact]
    public void App_credentials_accessor_registered_before_wins()
    {
        using var sp = Build(s =>
        {
            s.AddSingleton<IMySignCredentialsAccessor, AppCredentials>();
            s.AddViettelMySign(o => o.CredentialsMode = CredentialsMode.Dynamic);
        });

        Assert.IsType<AppCredentials>(sp.GetRequiredService<IMySignCredentialsAccessor>());
    }

    [Fact]
    public void App_credentials_accessor_registered_after_wins()
    {
        using var sp = Build(s =>
        {
            s.AddViettelMySign(o => o.CredentialsMode = CredentialsMode.Dynamic);
            s.AddSingleton<IMySignCredentialsAccessor, AppCredentials>();
        });

        Assert.IsType<AppCredentials>(sp.GetRequiredService<IMySignCredentialsAccessor>());
    }

    [Fact]
    public void Dynamic_mode_without_app_accessor_fails_clearly()
    {
        using var sp = Build(s => s.AddViettelMySign(o => o.CredentialsMode = CredentialsMode.Dynamic));
        var accessor = sp.GetRequiredService<IMySignCredentialsAccessor>();
        var ex = Assert.Throws<InvalidOperationException>(accessor.Get);
        Assert.Contains(nameof(IMySignCredentialsAccessor), ex.Message);
    }

    [Fact]
    public void Static_mode_default_accessor_returns_option_credentials()
    {
        using var sp = Build(s => s.AddViettelMySign(Static));
        Assert.Equal(
            new MySignCredentials("client-A", "secret-A", "profile-1", "user-1"),
            sp.GetRequiredService<IMySignCredentialsAccessor>().Get());
    }

    [Fact]
    public void Facade_default_tenant_key_matches_default_callback_resolver()
    {
        var parameter = typeof(IViettelMySignClient)
            .GetMethod(nameof(IViettelMySignClient.BeginSignHashesAsync))!
            .GetParameters()
            .Single(p => p.Name == "tenantKey");
        Assert.Equal(OptionsCallbackClientResolver.DefaultTenantKey, parameter.DefaultValue);
    }

    [Fact]
    public void Registering_twice_does_not_duplicate_http_handlers()
    {
        var services = BuildServices(s =>
        {
            s.AddViettelMySign(Static);
            s.AddViettelMySign(Static);
        });

        Assert.Single(services, d => d.ServiceType == typeof(IViettelMySignClient));
        using var sp = services.BuildServiceProvider();
        var factoryOptions = sp.GetRequiredService<IOptionsMonitor<Microsoft.Extensions.Http.HttpClientFactoryOptions>>()
            .Get(nameof(MySignWireClient));
        Assert.Equal(2, factoryOptions.HttpMessageHandlerBuilderActions.Count);
    }
}
