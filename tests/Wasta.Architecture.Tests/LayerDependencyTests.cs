using System.Xml.Linq;

namespace Wasta.Architecture.Tests;

/// <summary>
/// The dependency rule is the only thing holding clean architecture together,
/// and it is broken by adding one project reference in a hurry. These tests
/// read the project files directly rather than inspecting compiled assemblies:
/// the compiler drops references that are declared but unused, so an assembly
/// scan would go quiet exactly when a layer is newly-violated but not yet
/// leaned on.
/// </summary>
public class LayerDependencyTests
{
    private static readonly string RepoRoot = FindRepoRoot();

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "WastaCareerCoach.sln")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? throw new InvalidOperationException(
            "Could not locate the repository root from " + AppContext.BaseDirectory);
    }

    private static string ProjectPath(string name) =>
        Path.Combine(RepoRoot, "src", name, name + ".csproj");

    private static XDocument Load(string project) => XDocument.Load(ProjectPath(project));

    private static IReadOnlyList<string> ProjectReferences(string project) =>
        Load(project).Descendants("ProjectReference")
            .Select(e => Path.GetFileNameWithoutExtension(
                (e.Attribute("Include")?.Value ?? string.Empty).Replace('\\', '/')))
            .Where(n => n.Length > 0)
            .ToList();

    private static IReadOnlyList<string> PackageReferences(string project) =>
        Load(project).Descendants("PackageReference")
            .Select(e => e.Attribute("Include")?.Value ?? string.Empty)
            .Where(n => n.Length > 0)
            .ToList();

    [Fact]
    public void Domain_references_no_other_project()
    {
        Assert.Empty(ProjectReferences("Wasta.Domain"));
    }

    [Fact]
    public void Domain_references_no_nuget_package()
    {
        // The domain is plain C# over the BCL. A package here - EF Core, ASP.NET,
        // a JSON library - is how persistence and transport concerns start
        // leaking into business rules.
        Assert.Empty(PackageReferences("Wasta.Domain"));
    }

    [Fact]
    public void Application_references_only_domain()
    {
        Assert.Equal(new[] { "Wasta.Domain" }, ProjectReferences("Wasta.Application"));
    }

    [Fact]
    public void Application_does_not_reference_entity_framework()
    {
        // Application declares the interfaces it needs; Infrastructure implements
        // them. A DbContext reachable from here defeats the point of the split.
        var offenders = PackageReferences("Wasta.Application")
            .Where(p => p.Contains("EntityFrameworkCore", StringComparison.OrdinalIgnoreCase)
                     || p.StartsWith("Npgsql", StringComparison.OrdinalIgnoreCase))
            .ToList();

        Assert.Empty(offenders);
    }

    [Fact]
    public void Application_does_not_reference_aspnetcore()
    {
        var offenders = PackageReferences("Wasta.Application")
            .Where(p => p.StartsWith("Microsoft.AspNetCore", StringComparison.OrdinalIgnoreCase))
            .ToList();

        Assert.Empty(offenders);
    }

    [Fact]
    public void Infrastructure_does_not_reference_the_web_layer()
    {
        Assert.DoesNotContain("Wasta.WebApi", ProjectReferences("Wasta.Infrastructure"));
    }

    [Fact]
    public void Nothing_references_the_web_layer()
    {
        foreach (var project in new[] { "Wasta.Domain", "Wasta.Application", "Wasta.Infrastructure" })
        {
            Assert.DoesNotContain("Wasta.WebApi", ProjectReferences(project));
        }
    }

    [Fact]
    public void WebApi_does_not_reference_domain_directly()
    {
        // Endpoints speak DTOs and go through Application. Referencing Domain
        // straight from the web layer is how entities end up serialised onto
        // the wire, which then makes every schema change a breaking API change.
        Assert.DoesNotContain("Wasta.Domain", ProjectReferences("Wasta.WebApi"));
    }
}
