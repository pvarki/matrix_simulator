using Twin.Testing;

namespace Twin.Sim.Tests.Scenarios;

/// <summary>
/// A scenario file with a mistake fails to load with an <see cref="InvalidDataException"/> whose message names the JSON
/// path of the mistake (<c>$.squad.members[2].role</c>), so a person can find it in the file (pvarki/markdown-projectmatrix-pm#86):
/// an unknown key (a typo would otherwise be ignored silently), a value of the wrong type or shape, a missing key, and a
/// value out of range.
/// </summary>
public class ScenarioFileErrorTests
{
    // ETRS points; local = ETRS − (385000, 6672000). Every optional section is present, so each can carry a typo.
    private const string AgentScenario = """
        {
          "schemaVersion": 1,
          "seed": 1,
          "dtSeconds": 0.5,
          "agent": { "id": "alpha", "start": [385080.0, 6672101.0], "canRappel": false },
          "corridor": {
            "centerline": [[385080.0, 6672101.0], [385250.0, 6672101.0], [385420.0, 6672101.0]],
            "width": 80,
            "softMargin": 25,
            "softPenalty": 1,
            "hardBoundaries": [ { "id": "road-1", "line": [[385300.0, 6672000.0], [385300.0, 6672200.0]] } ]
          },
          "leader": { "answer": "grant", "delaySeconds": 20, "permitRadius": 10 },
          "permission": { "threshold": 1.5, "timeoutSeconds": 120 },
          "expect": { "detourWithoutCrossing": false },
          "doctrine": { "returnFireOverridesFireAt": true, "permissionAnswers": "whenFree" }
        }
        """;

    private const string SquadScenario = """
        {
          "schemaVersion": 1,
          "corridor": { "centerline": [[385080.0, 6672101.0], [385420.0, 6672101.0]], "width": 80, "softMargin": 25, "softPenalty": 1 },
          "leader": { "answer": "grant", "delaySeconds": 20, "permitRadius": 10 },
          "squad": {
            "id": "r1", "start": [385080.0, 6672101.0], "formation": "wedge", "teams": ["a"],
            "members": [
              { "id": "pj", "role": "rifleman", "team": "a" },
              { "id": "rj", "role": "squadLeader", "team": "a", "weapon": "rifle" },
              { "id": "k1", "role": "rifleman", "team": "a" }
            ]
          },
          "enemies": [ { "id": "red-1", "position": [385300.0, 6672150.0], "posture": "prone", "weapon": "machineGun" } ]
        }
        """;

    private static string Edit(bool squad, string find, string replace)
    {
        var json = squad ? SquadScenario : AgentScenario;
        Assert.Contains(find, json, StringComparison.Ordinal); // the edit must change something
        return json.Replace(find, replace, StringComparison.Ordinal);
    }

    private static InvalidDataException Fails(string json) =>
        Assert.Throws<InvalidDataException>(() => TestScenarioFolder.Load(json));

    [Fact]
    public void The_reference_scenarios_of_these_tests_load()
    {
        TestScenarioFolder.Load(AgentScenario);
        TestScenarioFolder.Load(SquadScenario);
    }

    [Theory]
    [InlineData(false, "\"seed\": 1", "\"seeed\": 1", "$.seeed")]
    [InlineData(false, "\"canRappel\"", "\"canRapel\"", "$.agent.canRapel")]
    [InlineData(false, "\"line\":", "\"lines\":", "$.corridor.hardBoundaries[0].lines")]
    [InlineData(false, "\"answer\":", "\"anwser\":", "$.leader.anwser")]
    [InlineData(false, "\"timeoutSeconds\"", "\"timeout\"", "$.permission.timeout")]
    [InlineData(false, "\"detourWithoutCrossing\"", "\"detour\"", "$.expect.detour")]
    [InlineData(false, "\"returnFireOverridesFireAt\"", "\"returnFire\"", "$.doctrine.returnFire")]
    [InlineData(true, "\"formation\"", "\"formaton\"", "$.squad.formaton")]
    [InlineData(true, "\"weapon\": \"rifle\"", "\"weapn\": \"rifle\"", "$.squad.members[1].weapn")]
    [InlineData(true, "\"posture\"", "\"postur\"", "$.enemies[0].postur")]
    public void An_unknown_key_is_rejected_with_its_JSON_path(bool squad, string find, string replace, string path)
    {
        var error = Fails(Edit(squad, find, replace));

        Assert.Contains(path + ":", error.Message, StringComparison.Ordinal);
        Assert.Contains("unknown", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(false, "\"start\": [385080.0, 6672101.0]", "\"start\": [385080.0]", "$.agent.start")]
    [InlineData(false, "\"start\": [385080.0, 6672101.0]", "\"start\": [385080.0, 6672101.0, 0.0]", "$.agent.start")]
    [InlineData(false, "[385250.0, 6672101.0]", "[1, \"x\"]", "$.corridor.centerline[1]")]
    [InlineData(false, "\"width\": 80", "\"width\": \"80\"", "$.corridor.width")]
    [InlineData(false, "\"delaySeconds\": 20, ", "", "$.leader.delaySeconds")]
    [InlineData(false, "\"answer\": \"grant\"", "\"answer\": \"yes\"", "$.leader.answer")]
    [InlineData(true, "{ \"id\": \"k1\", \"role\": \"rifleman\"", "{ \"id\": \"k1\", \"role\": \"captain\"", "$.squad.members[2].role")]
    [InlineData(false, "\"dtSeconds\": 0.5", "\"dtSeconds\": 0", "$.dtSeconds")]
    [InlineData(false, "\"schemaVersion\": 1", "\"schemaVersion\": 2", "$.schemaVersion")]
    [InlineData(false, "\"seed\": 1", "\"seed\": -1", "$.seed")]
    public void A_malformed_value_names_its_JSON_path(bool squad, string find, string replace, string path)
    {
        var error = Fails(Edit(squad, find, replace));

        Assert.Contains(path + ":", error.Message, StringComparison.Ordinal);
    }
}
