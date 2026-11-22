using Twin.Sim.Combat;
using Twin.Sim.Command;
using Twin.Sim.Corridors;
using Twin.Sim.Events;
using Twin.Sim.Geometry;
using Twin.Sim.Orders;
using Twin.Sim.Scenarios;
using Twin.Sim.Units;
using Twin.Sim.World;
using Twin.Testing;

namespace Twin.Scenarios.Tests.Squad;

/// <summary>
/// When a headless run is over (<see cref="RunEnd"/>). A squad run (backlog 0004 S2d) ends when its leader arrives or is
/// blocked, when one side has nobody left in action, or when shots have been fired and nobody has fired for
/// <see cref="RunEnd.QuietSeconds"/>. The fire fight demo ends when fewer than two sides are in action, or when nobody has
/// fired for <see cref="RunEnd.QuietSeconds"/>, counted from the start if nobody has fired at all. Every quiet rule is
/// checked one tick before and on its boundary; dt = 0.5 s, so the boundary falls on a tick.
/// </summary>
public class RunEndTests
{
    private static readonly TimeSpan TenMinutes = TimeSpan.FromMinutes(10);
    private const double Tolerance = 1e-9;

    // Open flat ground, 500 × 200 m. The squad starts in a row on y = 101 with rj at x = 80.
    private static readonly Vec2 Start = new(80, 101);
    private static readonly Vec2 RedAt = new(200, 101);

    private static RasterWorld OpenGround() => TestMap.Create(widthMeters: 500, heightMeters: 200).Build();

    private static ScenarioRun SquadAlone() =>
        Scenario.ForSquad().WithTerrain(OpenGround()).WithSquad(SpecSquad.Reference(), Start).Build();

    private static ScenarioRun SquadFacingRed(RulesOfEngagement squadRules = RulesOfEngagement.HoldFire) =>
        Scenario.ForSquad()
            .WithTerrain(OpenGround())
            .WithSquad(SpecSquad.Reference(), Start, rules: squadRules)
            .WithFighter("red", RedAt, Side.Red, RulesOfEngagement.HoldFire)
            .Build();

    private static ScenarioRun FireFight(RulesOfEngagement blueRules = RulesOfEngagement.HoldFire) =>
        Scenario.ForCombat()
            .WithTerrain(OpenGround())
            .WithFighter("blue", Start, Side.Blue, blueRules)
            .WithFighter("red", RedAt, Side.Red, RulesOfEngagement.HoldFire)
            .Build();

    /// <summary>
    /// Red fires at <paramref name="targetId"/> until it has fired twice, at least a tick apart, then ceases fire; returns
    /// its last shot's time. Two shots tell a quiet clock that counts from the last shot from one that counts from the first.
    /// </summary>
    private static double RedFiresTwiceThenStops(ScenarioRun sim, string targetId)
    {
        sim.Order("red", new FireAt(targetId)).RunUntil(s => s.ShotsBy("red").Count >= 2, TenMinutes, "red fires twice");
        sim.Order("red", new CeaseFire()).Step();
        var shots = sim.ShotsBy("red");
        Assert.True(shots[^1].Time - shots[0].Time >= sim.Simulation.Config.TickSeconds - Tolerance, "shots a tick or more apart");
        return shots[^1].Time;
    }

    /// <summary>Runs until one tick before <paramref name="atSeconds"/>.</summary>
    private static void RunToOneTickBefore(ScenarioRun sim, double atSeconds)
    {
        var tick = sim.Simulation.Config.TickSeconds;
        sim.RunUntil(s => s.TimeSeconds >= atSeconds - tick - Tolerance, TimeSpan.FromSeconds(atSeconds + 60), "one tick before");
        Assert.Equal(atSeconds - tick, sim.TimeSeconds, Tolerance);
    }

    /// <summary>Runs until <paramref name="atSeconds"/>, which must fall on a tick so the boundary itself is checked.</summary>
    private static void RunTo(ScenarioRun sim, double atSeconds)
    {
        sim.RunUntil(s => s.TimeSeconds >= atSeconds - Tolerance, TimeSpan.FromSeconds(atSeconds + 60), "the boundary");
        Assert.Equal(atSeconds, sim.TimeSeconds);
    }

    private static int Shots(ScenarioRun sim) => sim.Events.OfType<ShotFired>().Count();

    [Fact]
    public void A_squad_run_ends_when_its_leader_arrives()
    {
        var corridor = new Corridor([Start, new Vec2(160, 101)], 60, SpecV01.SoftMargin, SpecV01.SoftPenalty, []);
        var sim = SquadAlone();
        var end = RunEnd.ForSquad(sim.Simulation);

        sim.Order("rj", new MoveThroughCorridor(corridor)).RunUntil(_ => end.IsOver(), TenMinutes, "run over");

        var arrived = Assert.Single(sim.Events.OfType<Arrived>(), e => e.AgentId == "rj");
        Assert.Equal(sim.TimeSeconds, arrived.Time);
    }

    [Fact]
    public void A_squad_run_ends_when_its_leader_is_blocked()
    {
        var road = new HardBoundary("road", [new(250, 0), new(250, 200)], BoundarySource.Order);
        var corridor = new Corridor([Start, new Vec2(420, 101)], 60, SpecV01.SoftMargin, SpecV01.SoftPenalty, [road]);
        var sim = Scenario.ForSquad()
            .WithTerrain(OpenGround())
            .WithSquad(SpecSquad.Reference() with { Formation = Twin.Sim.Squads.SquadFormation.File }, Start)
            .WithLeader(LeaderAnswer.Deny, after: TimeSpan.FromSeconds(20), permitRadiusMeters: 10)
            .WithPermissionRules(threshold: SpecV01.PermissionThreshold, timeout: TimeSpan.FromSeconds(60))
            .Build();
        var end = RunEnd.ForSquad(sim.Simulation);

        sim.Order("rj", new MoveThroughCorridor(corridor)).RunUntil(_ => end.IsOver(), TenMinutes, "run over");

        Assert.Equal(UnitActivity.Blocked, sim.Unit("rj").Activity);
        Assert.DoesNotContain(sim.Events.OfType<Arrived>(), e => e.AgentId == "rj");
    }

    [Fact]
    public void A_squad_run_ends_when_one_side_has_nobody_in_action()
    {
        var sim = SquadFacingRed(RulesOfEngagement.FreeFire);
        var end = RunEnd.ForSquad(sim.Simulation);

        sim.RunUntil(s => s.Unit("red").Health == Health.OutOfAction, TenMinutes, "red out of action");

        Assert.True(sim.TimeSeconds - sim.Events.OfType<ShotFired>().Last().Time < RunEnd.QuietSeconds, "not ended by the quiet rule");
        Assert.True(end.IsOver());
    }

    [Fact]
    public void A_squad_run_is_not_over_on_quiet_before_any_shot()
    {
        var sim = SquadFacingRed();
        var end = RunEnd.ForSquad(sim.Simulation);

        sim.RunFor(TimeSpan.FromSeconds(2 * RunEnd.QuietSeconds));

        Assert.Equal(0, Shots(sim));
        Assert.False(end.IsOver());
    }

    [Fact]
    public void A_squad_run_is_not_over_one_tick_before_the_quiet_time_after_the_last_shot()
    {
        var sim = SquadFacingRed();
        var end = RunEnd.ForSquad(sim.Simulation);
        var lastShot = RedFiresTwiceThenStops(sim, "rj");
        var shots = Shots(sim);

        RunToOneTickBefore(sim, lastShot + RunEnd.QuietSeconds);

        Assert.Equal(shots, Shots(sim));
        Assert.False(end.IsOver());
    }

    [Fact]
    public void A_squad_run_ends_once_nobody_has_fired_for_the_quiet_time()
    {
        var sim = SquadFacingRed();
        var end = RunEnd.ForSquad(sim.Simulation);
        var lastShot = RedFiresTwiceThenStops(sim, "rj");
        var shots = Shots(sim);

        RunTo(sim, lastShot + RunEnd.QuietSeconds);

        Assert.Equal(shots, Shots(sim));
        Assert.True(end.IsOver());
    }

    [Fact]
    public void A_fire_fight_ends_when_one_side_has_nobody_in_action()
    {
        var sim = FireFight(RulesOfEngagement.FreeFire);
        var end = RunEnd.ForFireFight(sim.Simulation);

        sim.RunUntil(s => s.Unit("red").Health == Health.OutOfAction, TenMinutes, "red out of action");

        Assert.True(sim.TimeSeconds - sim.Events.OfType<ShotFired>().Last().Time < RunEnd.QuietSeconds, "not ended by the quiet rule");
        Assert.True(end.IsOver());
    }

    [Fact]
    public void A_fire_fight_with_no_shot_is_not_over_one_tick_before_the_quiet_time()
    {
        var sim = FireFight();
        var end = RunEnd.ForFireFight(sim.Simulation);

        RunToOneTickBefore(sim, RunEnd.QuietSeconds);

        Assert.Equal(0, Shots(sim));
        Assert.False(end.IsOver());
    }

    [Fact]
    public void A_fire_fight_with_no_shot_ends_at_the_quiet_time_counted_from_the_start()
    {
        var sim = FireFight();
        var end = RunEnd.ForFireFight(sim.Simulation);

        RunTo(sim, RunEnd.QuietSeconds);

        Assert.Equal(0, Shots(sim));
        Assert.True(end.IsOver());
    }

    [Fact]
    public void A_fire_fight_is_not_over_one_tick_before_the_quiet_time_after_the_last_shot()
    {
        var sim = FireFight();
        var end = RunEnd.ForFireFight(sim.Simulation);
        sim.RunFor(TimeSpan.FromSeconds(30)); // the shot comes well after the start
        var lastShot = RedFiresTwiceThenStops(sim, "blue");
        var shots = Shots(sim);

        RunToOneTickBefore(sim, lastShot + RunEnd.QuietSeconds);

        Assert.Equal(shots, Shots(sim));
        Assert.False(end.IsOver());
    }

    [Fact]
    public void A_fire_fight_ends_once_nobody_has_fired_for_the_quiet_time()
    {
        var sim = FireFight();
        var end = RunEnd.ForFireFight(sim.Simulation);
        sim.RunFor(TimeSpan.FromSeconds(30));
        var lastShot = RedFiresTwiceThenStops(sim, "blue");
        var shots = Shots(sim);

        RunTo(sim, lastShot + RunEnd.QuietSeconds);

        Assert.Equal(shots, Shots(sim));
        Assert.True(end.IsOver());
    }
}
