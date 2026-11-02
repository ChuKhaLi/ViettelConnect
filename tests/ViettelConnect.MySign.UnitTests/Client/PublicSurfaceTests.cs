using System.Reflection;
using ViettelConnect.MySign.Application.Abstractions;
using ViettelConnect.MySign.Client;
using ViettelConnect.MySign.Domain.Errors;
using ViettelConnect.MySign.Infrastructure.Configuration;
using Xunit;

namespace ViettelConnect.MySign.UnitTests.Client;

/// <summary>
/// Bề mặt công khai không được lộ chi tiết wire: không tên chứa mã <c>async</c>, OID hay base64 trong assembly Client;
/// không kiểu Application/Domain lọt vào chữ ký công khai của Client; Infrastructure chỉ công khai options và enum chế độ.
/// </summary>
public class PublicSurfaceTests
{
    private static readonly Assembly ClientAssembly = typeof(IViettelMySignClient).Assembly;
    private static readonly Assembly InfrastructureAssembly = typeof(MySignOptions).Assembly;

    private static readonly Assembly ApplicationAssembly = typeof(ITokenCache).Assembly;
    private static readonly Assembly DomainAssembly = typeof(MySignException).Assembly;

    /// <summary>Kiểu Domain công khai: ngoại lệ + enum nhóm lỗi, chế độ ký và chứng thư (có trong cổng của ứng dụng).</summary>
    private static readonly string[] DomainAllowList =
    [
        "ViettelConnect.MySign.Domain.Certificates.Certificate",
        "ViettelConnect.MySign.Domain.Errors.MySignException",
        "ViettelConnect.MySign.Domain.Errors.MySignTimeoutException",
        "ViettelConnect.MySign.Domain.Errors.MySignErrorCategory",
        "ViettelConnect.MySign.Domain.Signing.SignMode",
    ];

    /// <summary>Chỉ các cổng ứng dụng có thể tự cài (thay qua DI) cùng kiểu xuất hiện trong chữ ký của chúng.</summary>
    private static readonly string[] ApplicationAllowList =
    [
        "ViettelConnect.MySign.Application.Abstractions.AccessToken",
        "ViettelConnect.MySign.Application.Abstractions.CallbackClient",
        "ViettelConnect.MySign.Application.Abstractions.ICallbackClientResolver",
        "ViettelConnect.MySign.Application.Abstractions.ICallbackTokenStore",
        "ViettelConnect.MySign.Application.Abstractions.ICertificateSelector",
        "ViettelConnect.MySign.Application.Abstractions.ICorrelationIdAccessor",
        "ViettelConnect.MySign.Application.Abstractions.IMySignCredentialsAccessor",
        "ViettelConnect.MySign.Application.Abstractions.IPendingSignatureStore",
        "ViettelConnect.MySign.Application.Abstractions.ITokenCache",
        "ViettelConnect.MySign.Application.Abstractions.MySignCredentials",
        "ViettelConnect.MySign.Application.Abstractions.PendingSignature",
    ];

    private static readonly string[] InfrastructureAllowList =
    [
        "ViettelConnect.MySign.Infrastructure.Configuration.MySignOptions",
        "ViettelConnect.MySign.Infrastructure.Configuration.PollingOptions",
        "ViettelConnect.MySign.Infrastructure.Configuration.TransportRetryOptions",
        "ViettelConnect.MySign.Infrastructure.Configuration.ErrorOptions",
        "ViettelConnect.MySign.Infrastructure.Configuration.CallbackOptions",
        "ViettelConnect.MySign.Infrastructure.Configuration.AdvancedOptions",
        "ViettelConnect.MySign.Infrastructure.Configuration.CredentialsMode",
    ];

    [Fact]
    public void Client_exports_the_facade_dtos_and_di_entry()
    {
        var names = ClientAssembly.GetExportedTypes().Select(t => t.Name).ToHashSet();
        foreach (var expected in new[]
        {
            "IViettelMySignClient", "ServiceCollectionExtensions", "CertificateDto", "HashDocumentDto",
            "SignHashesRequestDto", "SignHashesResultDto", "DocumentSignatureDto", "BeginSignResultDto",
            "CallbackResponseDto", "CallbackSignOutcomeDto",
        })
        {
            Assert.Contains(expected, names);
        }

        Assert.DoesNotContain("ViettelMySignClient", names);
        Assert.DoesNotContain("LibraryCorrelationIdAccessor", names);
    }

    [Fact]
    public void Client_public_names_do_not_mention_async_codes_oids_or_base64()
    {
        var offenders = new List<string>();
        foreach (var type in ClientAssembly.GetExportedTypes())
        {
            foreach (var name in PublicNames(type))
            {
                if (IsWireName(name))
                {
                    offenders.Add($"{type.FullName}: {name}");
                }
            }
        }

        Assert.True(offenders.Count == 0, "Wire-detail names in public Client API: " + string.Join(", ", offenders));
    }

    [Fact]
    public void Client_public_signatures_do_not_expose_application_domain_or_wire_types()
    {
        var offenders = new List<string>();
        foreach (var type in ClientAssembly.GetExportedTypes())
        {
            foreach (var (member, used) in PublicSignatureTypes(type))
            {
                foreach (var t in Flatten(used))
                {
                    var asm = t.Assembly.GetName().Name ?? string.Empty;
                    var ns = t.Namespace ?? string.Empty;
                    var disallowed = asm is "ViettelConnect.MySign.Application" or "ViettelConnect.MySign.Domain"
                        || (asm == "ViettelConnect.MySign.Infrastructure" && ns != "ViettelConnect.MySign.Infrastructure.Configuration");
                    if (disallowed)
                    {
                        offenders.Add($"{type.Name}.{member}: {t.FullName}");
                    }
                }
            }
        }

        Assert.True(offenders.Count == 0, "Non-public-layer types in Client API: " + string.Join(", ", offenders));
    }

    [Fact]
    public void Infrastructure_exports_only_the_allow_list()
    {
        var exported = InfrastructureAssembly.GetExportedTypes().Select(t => t.FullName!).ToArray();
        var unexpected = exported.Except(InfrastructureAllowList).ToArray();
        Assert.True(unexpected.Length == 0, "Unexpected public Infrastructure types: " + string.Join(", ", unexpected));
    }

    [Fact]
    public void Domain_exports_only_the_allow_list()
    {
        var unexpected = DomainAssembly.GetExportedTypes().Select(t => t.FullName!).Except(DomainAllowList).ToArray();
        Assert.True(unexpected.Length == 0, "Unexpected public Domain types: " + string.Join(", ", unexpected));
        Assert.Equal(DomainAllowList.Order(), DomainAssembly.GetExportedTypes().Select(t => t.FullName!).Order());
    }

    [Fact]
    public void Application_exports_only_the_allow_list()
    {
        var exported = ApplicationAssembly.GetExportedTypes().Select(t => t.FullName!).ToArray();
        var unexpected = exported.Except(ApplicationAllowList).ToArray();
        Assert.True(unexpected.Length == 0, "Unexpected public Application types: " + string.Join(", ", unexpected));
        Assert.Equal(ApplicationAllowList.Order(), exported.Order());
    }

    [Fact]
    public void Public_application_ports_only_expose_public_types()
    {
        // Mọi kiểu công khai của Application phải dùng được từ ngoài assembly: chữ ký không được trỏ vào kiểu nội bộ.
        var offenders = new List<string>();
        foreach (var type in ApplicationAssembly.GetExportedTypes())
        {
            foreach (var (member, used) in PublicSignatureTypes(type))
            {
                foreach (var t in Flatten(used).Where(t => !t.IsVisible))
                {
                    offenders.Add($"{type.Name}.{member}: {t.FullName}");
                }
            }
        }

        Assert.True(offenders.Count == 0, "Non-visible types in public ports: " + string.Join(", ", offenders));
    }

    [Theory]
    [InlineData("PollingAsyncCode", true)]
    [InlineData("asyncCode", true)]
    [InlineData("SignAlgorithmOids", true)]
    [InlineData("oid", true)]
    [InlineData("HashBase64", true)]
    [InlineData("base64Hash", true)]
    [InlineData("Void", false)]
    [InlineData("SignHashesAsync", false)]
    [InlineData("Signatures", false)]
    public void Wire_name_detector_matches_intended_names(string name, bool expected) =>
        Assert.Equal(expected, IsWireName(name));

    private static bool IsWireName(string name) =>
        name.Contains("asynccode", StringComparison.OrdinalIgnoreCase)
        || name.Contains("base64", StringComparison.OrdinalIgnoreCase)
        || name.Contains("Oid", StringComparison.Ordinal)
        || name.StartsWith("oid", StringComparison.Ordinal);

    private const BindingFlags PublicMembers =
        BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

    private static IEnumerable<string> PublicNames(Type type)
    {
        yield return type.Name;
        foreach (var p in type.GetProperties(PublicMembers))
        {
            yield return p.Name;
        }

        foreach (var f in type.GetFields(PublicMembers))
        {
            yield return f.Name;
        }

        foreach (var m in type.GetMethods(PublicMembers).Where(m => !m.IsSpecialName))
        {
            yield return m.Name;
            foreach (var p in m.GetParameters())
            {
                yield return p.Name ?? string.Empty;
            }
        }

        foreach (var c in type.GetConstructors())
        {
            foreach (var p in c.GetParameters())
            {
                yield return p.Name ?? string.Empty;
            }
        }
    }

    private static IEnumerable<(string Member, Type Type)> PublicSignatureTypes(Type type)
    {
        foreach (var p in type.GetProperties(PublicMembers))
        {
            yield return (p.Name, p.PropertyType);
        }

        foreach (var f in type.GetFields(PublicMembers).Where(f => !type.IsEnum))
        {
            yield return (f.Name, f.FieldType);
        }

        foreach (var m in type.GetMethods(PublicMembers).Where(m => !m.IsSpecialName))
        {
            yield return (m.Name, m.ReturnType);
            foreach (var p in m.GetParameters())
            {
                yield return ($"{m.Name}({p.Name})", p.ParameterType);
            }
        }

        foreach (var c in type.GetConstructors())
        {
            foreach (var p in c.GetParameters())
            {
                yield return ($".ctor({p.Name})", p.ParameterType);
            }
        }
    }

    private static IEnumerable<Type> Flatten(Type t)
    {
        if (t.IsByRef || t.IsArray || t.IsPointer)
        {
            foreach (var inner in Flatten(t.GetElementType()!))
            {
                yield return inner;
            }

            yield break;
        }

        if (t.IsGenericParameter)
        {
            yield break;
        }

        if (t.IsGenericType)
        {
            yield return t.GetGenericTypeDefinition();
            foreach (var arg in t.GetGenericArguments())
            {
                foreach (var inner in Flatten(arg))
                {
                    yield return inner;
                }
            }

            yield break;
        }

        yield return t;
    }
}
