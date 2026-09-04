using System.Xml.Linq;

namespace Expenses.Application.Tests.Architecture;

/// <summary>
/// Asserts the reference rules of D1. The rules are declared in the project files,
/// so the project files are what is inspected — an assembly-level check would miss
/// a reference that the compiler happened to elide.
/// </summary>
public sealed class ProjectReferenceRulesTests
{
    [Fact]
    public void Domain_depends_on_nothing()
    {
        Assert.Empty(ProjectReferencesOf("Expenses.Domain"));
        Assert.Empty(PackageReferencesOf("Expenses.Domain"));
    }

    [Fact]
    public void Application_depends_only_on_Domain()
    {
        Assert.Equal(["Expenses.Domain"], ProjectReferencesOf("Expenses.Application"));
    }

    [Fact]
    public void Infrastructure_depends_only_on_Application()
    {
        Assert.Equal(["Expenses.Application"], ProjectReferencesOf("Expenses.Infrastructure"));
    }

    [Fact]
    public void Api_references_Application_and_Infrastructure_only()
    {
        Assert.Equal(
            ["Expenses.Application", "Expenses.Infrastructure"],
            ProjectReferencesOf("Expenses.Api"));
    }

    [Fact]
    public void Mcp_references_Application_and_Infrastructure_only()
    {
        Assert.Equal(
            ["Expenses.Application", "Expenses.Infrastructure"],
            ProjectReferencesOf("Expenses.Mcp"));
    }

    [Fact]
    public void Neither_adapter_references_the_other()
    {
        Assert.DoesNotContain("Expenses.Mcp", ProjectReferencesOf("Expenses.Api"));
        Assert.DoesNotContain("Expenses.Api", ProjectReferencesOf("Expenses.Mcp"));
    }

    [Fact]
    public void No_production_project_references_a_test_project()
    {
        foreach (string? project in new[]
                 {
                     "Expenses.Domain", "Expenses.Application", "Expenses.Infrastructure",
                     "Expenses.Api", "Expenses.Mcp",
                 })
        {
            Assert.DoesNotContain(ProjectReferencesOf(project), r => r.EndsWith(".Tests", StringComparison.Ordinal));
        }
    }

    private static IReadOnlyList<string> ProjectReferencesOf(string project)
        => [.. ReferencesOf(project, "ProjectReference")
            .Select(Path.GetFileNameWithoutExtension)
            .Select(name => name!)
            .OrderBy(name => name, StringComparer.Ordinal)];

    private static IReadOnlyList<string> PackageReferencesOf(string project)
        => [.. ReferencesOf(project, "PackageReference").OrderBy(name => name, StringComparer.Ordinal)];

    private static IEnumerable<string> ReferencesOf(string project, string element)
    {
        string path = Path.Combine(s_solutionRoot.Value, project, project + ".csproj");
        Assert.True(File.Exists(path), $"Expected project file at {path}");

        return XDocument.Load(path)
            .Descendants(element)
            .Select(e => (string?)e.Attribute("Include"))
            .Where(include => !string.IsNullOrWhiteSpace(include))
            .Select(include => include!.Replace('\\', Path.DirectorySeparatorChar));
    }

    private static readonly Lazy<string> s_solutionRoot = new(() =>
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Expenses.sln")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);
        return directory!.FullName;
    });
}
