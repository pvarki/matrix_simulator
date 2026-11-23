using Twin.Testing;

namespace Twin.Acceptance.Tests.SmokeTest1;

/// <summary>
/// Every scenario checked into <c>scenarios/</c> loads: the loader's checks (known keys, JSON paths, positions on
/// passable ground) do not reject a committed scenario (pvarki/markdown-projectmatrix-pm#86). A new scenario directory
/// is checked as soon as it has a <c>scenario.json</c>.
/// </summary>
public class CommittedScenariosTests
{
    /// <summary>The name of every directory under <c>scenarios/</c> that holds a <c>scenario.json</c>.</summary>
    public static TheoryData<string> CommittedScenarios()
    {
        var names = new TheoryData<string>();
        foreach (var directory in Directory.GetDirectories(ScenarioFiles.ScenariosRoot).Order(StringComparer.Ordinal))
        {
            if (File.Exists(Path.Combine(directory, "scenario.json")))
            {
                names.Add(Path.GetFileName(directory));
            }
        }

        return names;
    }

    [Theory]
    [MemberData(nameof(CommittedScenarios))]
    public void Every_committed_scenario_loads(string name)
    {
        var scenario = ScenarioOnDisk.Load(name);

        Assert.NotNull(scenario.Definition);
    }
}
