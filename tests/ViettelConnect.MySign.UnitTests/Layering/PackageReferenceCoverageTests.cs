using System.Xml.Linq;
using Xunit;

namespace ViettelConnect.MySign.UnitTests.Layering;

/// <summary>
/// Gói NuGet chỉ có project Client là <c>IsPackable</c>; Domain/Application/Infrastructure được gói vào DLL của Client,
/// nên mọi <c>PackageReference</c> của chúng phải được Client khai báo lại, nếu không nuspec thiếu phụ thuộc.
/// Đọc XML của csproj, không cần chạy pack.
/// </summary>
public class PackageReferenceCoverageTests
{
    private static readonly string[] BundledProjects = ["Domain", "Application", "Infrastructure"];

    [Fact]
    public void Client_declares_every_package_reference_of_the_bundled_projects()
    {
        var client = PackageReferences("Client");
        Assert.NotEmpty(client);

        foreach (var project in BundledProjects)
        {
            var missing = PackageReferences(project).Except(client).ToArray();
            Assert.True(
                missing.Length == 0,
                $"Client csproj is missing PackageReference(s) used by {project}: {string.Join(", ", missing)}");
        }
    }

    [Fact]
    public void Bundled_projects_are_not_packable()
    {
        foreach (var project in BundledProjects)
        {
            var packable = Load(project).Descendants("IsPackable").Select(e => e.Value.Trim()).SingleOrDefault();
            Assert.Equal("false", packable);
        }
    }

    private static HashSet<string> PackageReferences(string project) =>
        Load(project).Descendants("PackageReference")
            .Select(e => (string?)e.Attribute("Include") ?? string.Empty)
            .Where(n => n.Length > 0)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

    private static XDocument Load(string project)
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "ViettelConnect.slnx")))
        {
            root = root.Parent;
        }

        Assert.NotNull(root);
        var name = $"ViettelConnect.MySign.{project}";
        return XDocument.Load(Path.Combine(root.FullName, "src", name, name + ".csproj"));
    }
}
