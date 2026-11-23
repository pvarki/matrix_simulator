using System.Diagnostics;
using System.Globalization;
using Twin.Sim.Combat;
using Twin.Sim.Events;
using Twin.Sim.Geometry;
using Twin.Sim.Orders;
using Twin.Sim.World;
using Twin.Testing;
using Xunit.Abstractions;

namespace Twin.Acceptance.Tests.Performance;

/// <summary>
/// Q5: a large fire fight on the real MML area <c>scenarios/st1</c> stays within a time budget per tick. Two lines of
/// 100 free-fire fighters, 5 m apart, face each other across the K2 field (<c>EngagementOnSt1Tests</c>) about 375 m
/// apart, with spotting on, each side watching the other. Wall-clock time is fine here: this is a measurement in a
/// test, not simulation logic. See the output with <c>./dev smoke</c>.
/// </summary>
[Collection(TimedCollection.Name)]
public class FireFightBudgetTests(ITestOutputHelper output)
{
    /// <summary>
    /// ≈5× the slower of the Debug and Release times measured on 2026-11-23, rounded up. CI runs on slower shared
    /// machines and beside other test assemblies; the margin is for that, not for slower code.
    /// </summary>
    private const double BudgetMsPerTick = 900;

    private const int PerSide = 100;
    private const double SpacingMeters = 5;
    private const double GapMeters = 375;
    private const int WarmUpTicks = 2;
    private const int TimedTicks = 40;
    // Short, so ./dev test stays fast; the state hash printed at this time lets a refactor show it changes nothing.
    private static readonly TimeSpan RunTo = TimeSpan.FromSeconds(25);

    private static readonly LoadedWorld St1 = ScenarioOnDisk.Load("st1").Definition.World;

    private static Vec2 Etrs(double easting, double northing) => St1.Origin.ToLocal(new EtrsPoint(easting, northing));

    [Fact]
    public void A_fire_fight_of_200_agents_on_st1_stays_within_its_time_budget()
    {
        // The K2 line across the field, lengthened to the gap on both sides of its middle.
        var k2Blue = Etrs(403871, 6687191);
        var k2Red = Etrs(404009, 6687083);
        var middle = (k2Blue + k2Red) * 0.5;
        var towardRed = (k2Red - k2Blue).Normalized();
        var blueCentre = middle - (towardRed * (GapMeters / 2));
        var redCentre = middle + (towardRed * (GapMeters / 2));
        var sim = Scenario.ForSpotting().WithTerrain(St1.World)
            .WithFireLine("blue", blueCentre, towardRed, PerSide, SpacingMeters, Side.Blue, RulesOfEngagement.FreeFire)
            .WithFireLine("red", redCentre, towardRed * -1, PerSide, SpacingMeters, Side.Red, RulesOfEngagement.FreeFire)
            .Build();
        foreach (var unit in sim.Simulation.Units)
        {
            sim.Order(unit.Id, new Watch(unit.Combatant!.Side == Side.Blue ? towardRed : towardRed * -1));
        }

        sim.Step(WarmUpTicks); // JIT and first-tick work
        var started = Stopwatch.GetTimestamp();
        sim.Step(TimedTicks);
        var msPerTick = Stopwatch.GetElapsedTime(started).TotalMilliseconds / TimedTicks;
        sim.RunFor(RunTo - TimeSpan.FromSeconds(sim.TimeSeconds)); // untimed: shots, flashes, posture changes

        var shots = sim.Events.OfType<ShotFired>().Count();
        var spotted = sim.Events.OfType<EnemySpotted>().Count();
        var hits = sim.Events.OfType<ShotFired>().Count(e => e.Hit);
        output.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"Q5 st1 fire fight, {2 * PerSide} agents: {msPerTick:0.0} ms/tick over {TimedTicks} ticks (budget {BudgetMsPerTick} ms); " +
            $"by t={sim.TimeSeconds:0} s {shots} shots, {hits} hits, {spotted} spotted, " +
            $"state hash {sim.Simulation.ComputeStateHash():x16}"));
        Assert.True(shots > 0, "the two lines should engage each other by 25 s");
        Assert.True(msPerTick <= BudgetMsPerTick,
            string.Create(CultureInfo.InvariantCulture, $"{msPerTick:0.0} ms/tick is over the budget of {BudgetMsPerTick} ms"));
    }
}
