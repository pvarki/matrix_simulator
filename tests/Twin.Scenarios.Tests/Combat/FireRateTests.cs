using Twin.Sim.Combat;
using Twin.Sim.Events;
using Twin.Sim.Geometry;
using Twin.Sim.Orders;
using Twin.Testing;

namespace Twin.Scenarios.Tests.Combat;

/// <summary>
/// A shooter fires at its weapon's rate on average, whatever the tick rate (review finding M2). A shot can only fall on a
/// tick, so a single interval may be up to one tick long, but the lateness does not build up from shot to shot. Fire
/// that stops and starts again counts from the new start, never with a catch-up burst, and there is at most one shot
/// per tick.
/// </summary>
public class FireRateTests
{
    private const double Tolerance = 1e-9;
    private static readonly Vec2 Blue = new(21, 21);
    private static readonly Vec2 RedAt500 = new(541, 21);

    // Red fires at blue from 100 m with a rifle that never hits and shoots every `secondsPerShot`; blue holds fire.
    private static ScenarioRun Duel(int hz, double secondsPerShot) =>
        Scenario.ForCombat()
            .WithTickRate(hz)
            .WithTerrain(TestMap.Create(widthMeters: 140, heightMeters: 40).Build())
            .WithFighter("blue", Blue, Side.Blue, RulesOfEngagement.HoldFire)
            .WithFighter("red", Blue + new Vec2(100, 0), Side.Red, RulesOfEngagement.HoldFire,
                SpecSuppression.NeverHits(SpecShooting.Rifle) with { SecondsPerShot = secondsPerShot, Rounds = 1000 })
            .Build()
            .Order("red", new FireAt("blue"));

    private static List<double> Intervals(IReadOnlyList<ShotFired> shots) =>
        shots.Zip(shots.Skip(1), (a, b) => b.Time - a.Time).ToList();

    [Theory]
    [InlineData(2, 0.7)]
    [InlineData(20, 0.7)]
    [InlineData(20, 0.33)]
    [InlineData(2, 2.0)]
    public void Aimed_fire_keeps_the_weapon_rate_at_any_tick_rate(int hz, double secondsPerShot)
    {
        var sim = Duel(hz, secondsPerShot);

        sim.RunFor(TimeSpan.FromSeconds(60));

        var shots = sim.ShotsBy("red");
        Assert.True(shots.Count > 10, $"red fired {shots.Count} shots");
        Assert.All(shots, s => Assert.Equal(FireMode.Aimed, s.Mode));
        var tick = 1.0 / hz;
        // The span may be up to one tick longer than the weapon's rate gives, never shorter.
        var late = shots[^1].Time - shots[0].Time - ((shots.Count - 1) * secondsPerShot);
        Assert.InRange(late, -Tolerance, tick - Tolerance);
        Assert.All(Intervals(shots), interval => Assert.True(interval >= tick - Tolerance, $"interval {interval} s is under one tick"));
    }

    [Fact]
    public void Snap_shots_of_the_drill_keep_the_snap_interval_at_2_Hz()
    {
        // Red fires one round from 500 m; blue answers with the drill's three snap shots (as ContactDrillTests.DR1).
        var sim = Scenario.ForCombat()
            .WithTerrain(TestMap.Create(widthMeters: 600, heightMeters: 60).Build())
            .WithFighter("blue", Blue, Side.Blue, RulesOfEngagement.ReturnFire)
            .WithFighter("red", RedAt500, Side.Red, RulesOfEngagement.HoldFire,
                SpecSuppression.NeverHits(SpecShooting.Rifle) with { Rounds = 1 })
            .Build()
            .Order("red", new FireAt("blue"));

        sim.RunUntil(s => s.ShotsBy("blue").Count >= 3, TimeSpan.FromSeconds(20), "three snap shots");

        var snaps = sim.ShotsBy("blue").Take(3).ToList();
        Assert.All(snaps, s => Assert.Equal(FireMode.Snap, s.Mode));
        var interval = SpecShooting.Rifle.SnapSecondsPerShot;
        Assert.InRange(snaps[2].Time - snaps[0].Time, (2 * interval) - Tolerance, (2 * interval) + SpecV01.DtSeconds - Tolerance);

        // All three come in the snap-shot stage, before blue moves on in the drill.
        var events = sim.Events.ToList();
        var entered = events.FindIndex(e => e is ContactDrillStageChanged { AgentId: "blue", Stage: ContactDrillStage.SnapShots });
        var left = events.FindIndex(entered + 1, e => e is ContactDrillStageChanged { AgentId: "blue" });
        Assert.True(entered >= 0, "blue entered the snap-shot stage");
        Assert.All(snaps, s =>
            Assert.True(events.IndexOf(s) > entered && (left < 0 || events.IndexOf(s) < left), $"snap at t={s.Time} is outside the snap-shot stage"));
    }

    [Fact]
    public void A_shooter_that_resumes_fire_waits_a_full_interval()
    {
        const double SecondsPerShot = 0.7;
        var sim = Duel(SpecV01.TickRateHz, SecondsPerShot);
        sim.RunFor(TimeSpan.FromSeconds(5));
        sim.Order("red", new CeaseFire()).RunFor(TimeSpan.FromSeconds(10));
        var before = sim.ShotsBy("red").Count;

        sim.Order("red", new FireAt("blue")).RunFor(TimeSpan.FromSeconds(5));

        var resumed = sim.ShotsBy("red").Skip(before).ToList();
        Assert.True(resumed.Count >= 2, $"red fired {resumed.Count} shots after resuming");
        Assert.True(resumed[1].Time - resumed[0].Time >= SecondsPerShot - Tolerance,
            $"the first interval after resuming is {resumed[1].Time - resumed[0].Time} s");
    }

    [Fact]
    public void Fire_rate_is_capped_at_one_shot_per_tick()
    {
        var sim = Duel(SpecV01.TickRateHz, secondsPerShot: 0.3);

        sim.RunFor(TimeSpan.FromSeconds(10));

        var shots = sim.ShotsBy("red");
        Assert.InRange(shots.Count, 2, 21);
        Assert.All(Intervals(shots), interval => Assert.True(interval >= SpecV01.DtSeconds - Tolerance, $"interval {interval} s"));
    }
}
