using Twin.Testing;

namespace Twin.Sim.Tests.Core;

/// <summary>The order the systems run in each tick is behaviour (ADR 0013).</summary>
public class SystemOrderTests
{
    [Fact]
    public void Systems_run_in_the_documented_order()
    {
        var sim = new Simulation(TestTerrain.Flat(100));

        string[] documented =
        [
            "OrderSystem", "CombatStatusSystem", "SquadFollowSystem", "SquadCohesionSystem", "MovementSystem",
            "CorridorMovementSystem", "ContactDrillSystem", "SpottingSystem", "SpotReportSystem", "FireSystem",
            "SquadSystem", "ObservationSystem",
        ];
        var actual = sim.Systems.Select(s => s.GetType().Name).ToArray();

        // A failure here means the order changed. Change it deliberately: update ADR 0013
        // (docs/adr/0013-system-order.md) and this list together.
        Assert.Equal(documented, actual);
    }
}
