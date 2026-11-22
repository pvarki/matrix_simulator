using Twin.Sim.Geometry;
using Twin.Sim.World;
using Twin.Testing;

namespace Twin.Sim.Tests.Scenarios;

/// <summary>
/// Every position a unit starts at or heads for (the agent's or squad's start, each member's starting slot, each enemy,
/// and B, the corridor's last point) must be on the map and on ground infantry can stand on, which is the rule the
/// simulation applies when the order is issued: water and buildings are impassable, everything else is not. A scenario
/// that breaks it fails when it is loaded, naming the JSON path (pvarki/markdown-projectmatrix-pm#86).
/// </summary>
public class ScenarioPositionTests
{
    // ETRS points; local = ETRS − (385000, 6672000). The map is 500 m × 200 m.
    private static readonly Vec2 Start = new(80, 101);

    private static string AgentScenario(string start = "[385080.0, 6672101.0]", string end = "[385420.0, 6672101.0]") => $$"""
        {
          "schemaVersion": 1,
          "agent": { "id": "alpha", "start": {{start}} },
          "corridor": { "centerline": [[385080.0, 6672101.0], [385250.0, 6672101.0], {{end}}], "width": 80, "softMargin": 25, "softPenalty": 1 },
          "leader": { "answer": "grant", "delaySeconds": 20, "permitRadius": 10 }
        }
        """;

    private static string SquadScenario(string enemies = "[]") => $$"""
        {
          "schemaVersion": 1,
          "corridor": { "centerline": [[385080.0, 6672101.0], [385420.0, 6672101.0]], "width": 80, "softMargin": 25, "softPenalty": 1 },
          "leader": { "answer": "grant", "delaySeconds": 20, "permitRadius": 10 },
          "squad": {
            "id": "r1", "start": [385080.0, 6672101.0], "formation": "wedge", "teams": ["a"],
            "members": [
              { "id": "pj", "role": "rifleman", "team": "a" },
              { "id": "rj", "role": "squadLeader", "team": "a" },
              { "id": "k1", "role": "rifleman", "team": "a" }
            ]
          },
          "enemies": {{enemies}}
        }
        """;

    private static Func<Vec2, TerrainClass> Near(Vec2 point, double meters, TerrainClass terrain) =>
        at => at.DistanceTo(point) < meters ? terrain : TerrainClass.Open;

    private static InvalidDataException Fails(string json, Func<Vec2, TerrainClass> terrain) =>
        Assert.Throws<InvalidDataException>(() => TestScenarioFolder.Load(json, terrain: terrain));

    [Theory]
    [InlineData(TerrainClass.Water, false)]
    [InlineData(TerrainClass.Building, false)]
    [InlineData(TerrainClass.Water, true)]
    [InlineData(TerrainClass.Building, true)]
    public void A_start_in_impassable_terrain_fails_to_load(TerrainClass terrain, bool squad)
    {
        // Only the start's own cells: the squad's other slots are 7 m and more away.
        var error = Fails(squad ? SquadScenario() : AgentScenario(), Near(Start, 3, terrain));

        Assert.Contains(squad ? "$.squad.start:" : "$.agent.start:", error.Message, StringComparison.Ordinal);
        Assert.Contains(terrain.ToString(), error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(TerrainClass.Bog, false)]
    [InlineData(TerrainClass.Forest, false)]
    [InlineData(TerrainClass.Bog, true)]
    [InlineData(TerrainClass.Forest, true)]
    public void A_start_in_bog_or_forest_loads(TerrainClass terrain, bool squad)
    {
        var scenario = TestScenarioFolder.Load(squad ? SquadScenario() : AgentScenario(), terrain: _ => terrain);

        Assert.Equal(Start, scenario.Start);
    }

    [Fact]
    public void An_agent_start_off_the_map_fails_to_load()
    {
        var error = Fails(AgentScenario(start: "[386000.0, 6672101.0]"), _ => TerrainClass.Open);

        Assert.Contains("$.agent.start:", error.Message, StringComparison.Ordinal);
        Assert.Contains("map", error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("[385700.0, 6672101.0]", false)]
    [InlineData("[385420.0, 6672101.0]", true)]
    public void A_corridor_ending_off_the_map_or_in_water_fails_to_load(string end, bool water)
    {
        var error = Fails(AgentScenario(end: end), water ? Near(new Vec2(420, 101), 3, TerrainClass.Water) : _ => TerrainClass.Open);

        Assert.Contains("$.corridor.centerline[2]:", error.Message, StringComparison.Ordinal);
        Assert.Contains(water ? "Water" : "map", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void An_enemy_in_water_fails_to_load()
    {
        var error = Fails(
            SquadScenario("""[ { "id": "red-1", "position": [385300.0, 6672150.0] } ]"""), Near(new Vec2(300, 150), 3, TerrainClass.Water));

        Assert.Contains("$.enemies[0].position:", error.Message, StringComparison.Ordinal);
        Assert.Contains("red-1", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_member_starting_slot_in_water_fails_to_load()
    {
        // The wedge's apex, pj, stands about 7 m ahead and 7 m to one side of the leader (x = 87); rj and k1 stand at x = 80.
        var error = Fails(SquadScenario(), at => at.X is > 83 and < 91 && Math.Abs(at.Y - 101) < 10 ? TerrainClass.Water : TerrainClass.Open);

        Assert.Contains("$.squad.members[0]:", error.Message, StringComparison.Ordinal);
        Assert.Contains("pj", error.Message, StringComparison.Ordinal);
        Assert.Contains("Water", error.Message, StringComparison.Ordinal);
    }
}
