using Twin.Sim.Combat;
using Twin.Sim.Geometry;
using Twin.Sim.Orders;
using Twin.Sim.World;
using Twin.Testing;

namespace Twin.Scenarios.Tests.Combat;

/// <summary>
/// Line of sight follows the agents as they move (backlog 0002): a sight line that a ridge blocked is clear once
/// either end has walked past the ridge. Each test asks first, while the ridge is between them, so that a sight line
/// remembered from before the move would show.
/// </summary>
public class SightLineTests
{
    private static readonly TimeSpan Generous = TimeSpan.FromMinutes(2);

    // A 1.5 m ridge across the middle: above a standing agent's sight line (eye 1.6 m, aim point 1.0 m) between the
    // two sides, and low enough to walk over.
    private static RasterWorld Ridge() => TestMap.Create(widthMeters: 100, heightMeters: 20).WithRidge(fromX: 48, toX: 52, height: 1.5).Build();

    private static ScenarioRun Pair(RasterWorld world, Vec2 blue, Vec2 red) => Scenario.ForCombat().WithTerrain(world)
        .WithFighter("blue", blue, Side.Blue, RulesOfEngagement.HoldFire)
        .WithFighter("red", red, Side.Red, RulesOfEngagement.HoldFire)
        .Build();

    [Fact]
    public void An_observer_that_walks_past_a_ridge_sees_the_target_it_could_not_see()
    {
        var destination = new Vec2(71, 11);
        var sim = Pair(Ridge(), blue: new Vec2(21, 11), red: new Vec2(91, 11));
        Assert.False(sim.Simulation.CanSee("blue", "red"), "the ridge is between them");

        sim.Order("blue", new MoveTo(destination)).RunUntil(s => s.Unit("blue").IsIdle, Generous, "blue past the ridge");

        sim.Unit("blue").Should().BeAt(destination);
        Assert.True(sim.Simulation.CanSee("blue", "red"), "both are east of the ridge");
    }

    [Fact]
    public void A_target_that_walks_past_a_ridge_is_seen()
    {
        var destination = new Vec2(31, 11);
        var sim = Pair(Ridge(), blue: new Vec2(11, 11), red: new Vec2(81, 11));
        Assert.False(sim.Simulation.CanSee("blue", "red"), "the ridge is between them");

        sim.Order("red", new MoveTo(destination)).RunUntil(s => s.Unit("red").IsIdle, Generous, "red past the ridge");

        sim.Unit("red").Should().BeAt(destination);
        Assert.True(sim.Simulation.CanSee("blue", "red"), "both are west of the ridge");
    }
}
