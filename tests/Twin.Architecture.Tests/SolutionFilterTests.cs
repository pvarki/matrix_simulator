using System.Text.Json;

namespace Twin.Architecture.Tests;

/// <summary>
/// ./dev test, ./dev check and CI run the tests through Twin.Core.slnf. A test project missing from that
/// filter would build but never run, so its tests could fail without anyone noticing.
/// </summary>
public class SolutionFilterTests
{
    [Fact]
    public void Every_test_project_is_in_the_core_solution_filter()
    {
        var root = RepoRoot();
        var testProjects = Directory.EnumerateDirectories(Path.Combine(root, "tests"))
            .SelectMany(dir => Directory.EnumerateFiles(dir, "*.csproj"))
            .Select(file => Path.GetRelativePath(root, file).Replace('\\', '/'))
            .Order(StringComparer.Ordinal)
            .ToList();

        using var filter = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "Twin.Core.slnf")));
        var listed = filter.RootElement.GetProperty("solution").GetProperty("projects").EnumerateArray()
            .Select(project => project.GetString()!.Replace('\\', '/'))
            .ToList();

        var missing = testProjects.Except(listed, StringComparer.Ordinal).ToList();
        if (missing.Count > 0)
        {
            Assert.Fail("Test projects missing from Twin.Core.slnf, so ./dev test and CI never run them: " +
                        string.Join(", ", missing) + ". Add them to Twin.Core.slnf.");
        }
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Twin.sln")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? throw new InvalidOperationException("Could not find Twin.sln above " + AppContext.BaseDirectory);
    }
}
