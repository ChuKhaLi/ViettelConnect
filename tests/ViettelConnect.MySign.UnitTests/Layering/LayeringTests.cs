using System.Reflection;
using Xunit;

namespace ViettelConnect.MySign.UnitTests.Layering;

/// <summary>
/// Kiểm tra quy tắc phân tầng ở mức assembly: Domain không tham chiếu gì ngoài BCL;
/// Application chỉ tham chiếu Domain + Microsoft.Extensions.Logging.Abstractions;
/// không assembly nào tham chiếu thư viện bị cấm (PDF, Polly, Refit).
/// Assembly được nạp theo tên để không cần kiểu đánh dấu giả.
/// </summary>
public class LayeringTests
{
    private const string DomainName = "ViettelConnect.MySign.Domain";
    private const string ApplicationName = "ViettelConnect.MySign.Application";
    private const string InfrastructureName = "ViettelConnect.MySign.Infrastructure";
    private const string ClientName = "ViettelConnect.MySign.Client";

    private static readonly string[] ForbiddenFragments = ["itext", "polly", "refit", "pdfsharp"];

    [Fact]
    public void Domain_references_only_bcl()
    {
        foreach (var name in ReferencedNames(DomainName))
        {
            Assert.True(
                IsBcl(name),
                $"Domain references non-BCL assembly '{name}'. Domain must have zero external dependencies.");
        }
    }

    [Fact]
    public void Application_references_only_domain_and_logging_abstractions()
    {
        foreach (var name in ReferencedNames(ApplicationName))
        {
            if (IsBcl(name) || name == DomainName || name == "Microsoft.Extensions.Logging.Abstractions")
            {
                continue;
            }

            Assert.Fail($"Application references disallowed assembly '{name}'.");
        }
    }

    [Theory]
    [InlineData(DomainName)]
    [InlineData(ApplicationName)]
    [InlineData(InfrastructureName)]
    [InlineData(ClientName)]
    public void No_assembly_references_forbidden_libraries(string assemblyName)
    {
        foreach (var name in ReferencedNames(assemblyName))
        {
            foreach (var fragment in ForbiddenFragments)
            {
                Assert.False(
                    name.Contains(fragment, StringComparison.OrdinalIgnoreCase),
                    $"{assemblyName} references forbidden assembly '{name}'.");
            }
        }
    }

    private static IEnumerable<string> ReferencedNames(string assemblyName) =>
        Assembly.Load(assemblyName).GetReferencedAssemblies().Select(r => r.Name ?? string.Empty);

    private static bool IsBcl(string name) =>
        name == "System"
        || name == "mscorlib"
        || name == "netstandard"
        || name.StartsWith("System.", StringComparison.Ordinal);
}
