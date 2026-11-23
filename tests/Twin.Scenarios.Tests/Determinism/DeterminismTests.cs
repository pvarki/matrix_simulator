using System.Globalization;
using Twin.Sim.Corridors;
using Twin.Sim.Geometry;
using Twin.Sim.Orders;
using Twin.Testing;

namespace Twin.Scenarios.Tests.Determinism;

/// <summary>
/// Guards docs/adr/0005-determinism.md: identical inputs must give identical state, tick by tick.
/// If this fails, some code reads time, uses unordered collections, or uses unseeded randomness.
/// </summary>
public class DeterminismTests
{
    [Fact]
    public void Same_scenario_and_orders_produce_identical_state_every_tick()
    {
        var first = RecordHashes(seed: 42);
        var second = RecordHashes(seed: 42);

        Assert.Equal(first, second);
    }

    [Fact]
    public void State_hash_changes_when_the_world_changes()
    {
        var hashes = RecordHashes(seed: 42);

        Assert.NotEqual(hashes[0], hashes[^1]);
    }

    [Fact]
    public void State_hash_is_the_same_under_fi_FI_and_en_US()
    {
        var sim = Scenario.Create().WithUnit("alpha", at: new(100, 100)).Build();
        sim.Order("alpha", new MoveTo(new Vec2(600.5, 550.25))).Step();

        var english = HashUnder(sim, "en-US");
        var finnish = HashUnder(sim, "fi-FI");

        Assert.Equal(english, finnish);
    }

    [Fact]
    public void Corridor_orders_that_differ_only_in_a_hard_boundary_hash_differently()
    {
        // Both boundaries lie outside the outer limit (y from 1 to 21), so they cannot change the route.
        var low = CorridorSim(boundaryY: 0.5);
        var high = CorridorSim(boundaryY: 0.6);

        Assert.Equal(low.Unit("alpha").Position, high.Unit("alpha").Position);
        Assert.Equal(low.Unit("alpha").Velocity, high.Unit("alpha").Velocity);
        Assert.NotEqual(low.Simulation.ComputeStateHash(), high.Simulation.ComputeStateHash());
    }

    private static ulong HashUnder(ScenarioRun sim, string culture)
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
            return sim.Simulation.ComputeStateHash();
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    private static ScenarioRun CorridorSim(double boundaryY)
    {
        var sim = Scenario.Create()
            .WithTerrain(TestMap.Create(widthMeters: 60, heightMeters: 20).Build())
            .WithTickRate(SpecV01.TickRateHz)
            .WithUnit("alpha", at: new(11, 11), profile: SpecV01.Infantry)
            .Build();
        var boundary = new HardBoundary("road", [new Vec2(0, boundaryY), new Vec2(60, boundaryY)], BoundarySource.Order);
        var corridor = new Corridor(
            [new Vec2(11, 11), new Vec2(51, 11)], Width: 10, SoftMargin: 5, SoftPenalty: SpecV01.SoftPenalty, HardBoundaries: [boundary]);
        sim.Order("alpha", new MoveThroughCorridor(corridor)).Step();
        return sim;
    }

    private static List<ulong> RecordHashes(ulong seed)
    {
        var sim = Scenario.Create()
            .WithSeed(seed)
            .WithTerrain(TestTerrain.Hill(peak: new(400, 600), heightMeters: 30))
            .WithUnit("alpha", at: new(100, 100))
            .WithUnit("bravo", at: new(800, 200), speedMps: 8)
            .Build();

        sim.Order("alpha", new MoveTo(new Vec2(700, 700)));
        sim.Order("bravo", new MoveTo(new Vec2(200, 800)));

        var hashes = new List<ulong>();
        for (var tick = 0; tick < 600; tick++)
        {
            if (tick == 300)
            {
                sim.Order("alpha", new MoveTo(new Vec2(50, 900)));
            }

            sim.Step();
            hashes.Add(sim.Simulation.ComputeStateHash());
        }

        return hashes;
    }
}
