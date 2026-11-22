using Twin.Sim.World;
using Twin.Testing;

namespace Twin.Acceptance.Tests.Setup;

/// <summary>
/// Checks the plumbing the Smoke test I acceptance tests rely on: the test run can see the repository's
/// scenarios directory, locally and inside the Docker test container.
/// </summary>
public class ScenarioDataTests
{
    [Fact]
    public void Test_run_finds_the_smoke_test_scenario_directory()
    {
        var directory = ScenarioFiles.Directory("st1");

        Assert.True(File.Exists(Path.Combine(directory, "README.md")), $"Expected a README.md in {directory}.");
    }

    [Fact]
    public void Missing_scenario_file_fails_with_a_clear_message()
    {
        var error = Assert.Throws<ScenarioAssertionException>(() => ScenarioFiles.Require("st1", "no-such-file.bin"));

        Assert.Contains("scenarios/st1/no-such-file.bin", error.Message.Replace('\\', '/'), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("st1")]
    [InlineData("st1-2km")]
    public void World_carries_the_exact_MML_attribution_its_README_requires(string scenario)
    {
        const string Required = "Contains data from the National Land Survey of Finland (Maanmittauslaitos) Elevation model 2 m "
            + "and Topographic Database, downloaded 2026-11-08, licence CC BY 4.0.";

        var loaded = WorldFiles.Load(Path.Combine(ScenarioFiles.Directory(scenario), "world"));

        Assert.Equal(Required, loaded.Attribution);
    }
}
