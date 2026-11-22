using Twin.Sim.Combat;
using Twin.Sim.Corridors;
using Twin.Sim.Geometry;
using Twin.Sim.Orders;
using Twin.Sim.Squads;
using Twin.Sim.Systems;
using Twin.Sim.Units;
using Twin.Testing;

namespace Twin.Sim.Tests.Core;

/// <summary>
/// The state hash (docs/adr/0005) sees every piece of mutable unit and squad state, including internal fields and the
/// contents of orders, so a determinism break in any of them shows up as a hash mismatch.
/// </summary>
public class StateHashTests
{
    private static readonly Vec2 Start = new(20, 20);

    private static readonly Corridor SomeCorridor =
        new([Start, new Vec2(80, 20)], Width: 10, SoftMargin: 5, SoftPenalty: 1, HardBoundaries: []);

    /// <summary>Each case: an arrangement applied before the first hash, then the mutation applied before the second.</summary>
    private static readonly SortedDictionary<string, (Action<ScenarioRun> Arrange, Action<ScenarioRun> Mutate)> Cases =
        new(StringComparer.Ordinal)
        {
            ["PinnedUntil"] = (_ => { }, s => s.Unit("rj").PinnedUntil = 5),
            ["NextShotTime"] = (_ => { }, s => s.Unit("rj").NextShotTime = 5),
            ["FlashUntil"] = (_ => { }, s => s.Unit("rj").FlashUntil = 5),
            ["HeldBySquad"] = (_ => { }, s => s.Unit("rj").HeldBySquad = !s.Unit("rj").HeldBySquad),
            ["SquadSpeedFactor"] = (_ => { }, s => s.Unit("rj").SquadSpeedFactor = 0.5),
            ["DesiredPosture"] = (_ => { }, s => s.Unit("rj").DesiredPosture = Posture.Prone),
            ["Attackers"] = (_ => { }, s => s.Unit("rj").Attackers.Add("x")),
            ["KnownAttackers"] = (_ => { }, s => s.Unit("rj").KnownAttackers.Add("x")),
            ["ObservationSectors"] = (
                s => s.Unit("rj").ObservationSectors = [new ObservationSector(new Vec2(1, 0), 90)],
                s => s.Unit("rj").ObservationSectors = [new ObservationSector(new Vec2(1, 0), 120)]),
            ["Drill"] = (_ => { }, s => s.Unit("rj").Drill = new ContactDrill(0, null, Posture.Standing)),
            ["CorridorProgress.Route"] = (
                s => s.Unit("rj").CorridorProgress = new CorridorProgress(new MoveThroughCorridor(SomeCorridor))
                {
                    Route = [Start, new Vec2(50, 20), new Vec2(80, 20)],
                },
                s => s.Unit("rj").CorridorProgress!.Route[1] = new Vec2(50, 21)),
            ["WatchTarget null to NaN"] = (
                s => s.Unit("rj").WatchTarget = null,
                s => s.Unit("rj").WatchTarget = new Vec2(double.NaN, double.NaN)),
            ["Squad.TrailLeaderId"] = (_ => { }, s => s.Squad("r1").TrailLeaderId = "j1"),
            ["Squad.Corridor centerline"] = (
                s => s.Squad("r1").Corridor = SomeCorridor,
                s => s.Squad("r1").Corridor = SomeCorridor with { Centerline = [Start, new Vec2(80, 21)] }),
            ["PendingRequest.LeaderAskedAt"] = (
                s => s.Squad("r1").PendingRequest = new Squad.MemberRequest("j1", "road", Start, AskedAt: 1),
                s => s.Squad("r1").PendingRequest = s.Squad("r1").PendingRequest! with { LeaderAskedAt = 2 }),
            ["EdgeLine.Acted"] = (
                s => s.Squad("r1").EdgeDeployment = new EdgeLine(
                    new DeployAtForestEdge(), Start, new Vec2(0, 1), new SortedDictionary<string, Vec2>(StringComparer.Ordinal),
                    halfWidth: 10, leftEnd: null, rightEnd: null),
                s => s.Squad("r1").EdgeDeployment!.Acted.Add("j1")),
            ["Delivery.Command"] = (
                s => s.Squad("r1").Deliveries.Add(new Squad.Delivery("j1", 1, new SetPosture(Posture.Prone), 3, ["j2"])),
                s => s.Squad("r1").Deliveries[0] = s.Squad("r1").Deliveries[0] with { Command = new SetPosture(Posture.Crouching) }),
            ["Delivery.RelayTo"] = (
                s => s.Squad("r1").Deliveries.Add(new Squad.Delivery("j1", 1, new SetPosture(Posture.Prone), 3, ["j2"])),
                s => s.Squad("r1").Deliveries[0] = s.Squad("r1").Deliveries[0] with { RelayTo = ["k1"] }),
            ["Random"] = (_ => { }, s => s.Simulation.Random.NextUInt64()),
            ["NextCommandId"] = (_ => { }, s => s.Simulation.NextCommandId()),
        };

    /// <summary>Names of the mutation cases, for the theory.</summary>
    public static TheoryData<string> CaseNames => [.. Cases.Keys];

    private static ScenarioRun SquadSim() => Scenario.ForSquad()
        .WithTerrain(TestTerrain.Flat(200))
        .WithSquad(SpecSquad.Reference(), Start)
        .Build();

    [Theory]
    [MemberData(nameof(CaseNames))]
    public void Changing_any_piece_of_mutable_state_changes_the_hash(string name)
    {
        var (arrange, mutate) = Cases[name];
        var sim = SquadSim();
        arrange(sim);
        var before = sim.Simulation.ComputeStateHash();

        mutate(sim);

        Assert.NotEqual(before, sim.Simulation.ComputeStateHash());
    }

    [Fact]
    public void Moving_an_id_between_adjacent_lists_changes_the_hash()
    {
        var a = SquadSim();
        a.Unit("rj").Attackers.Add("x");
        var b = SquadSim();
        b.Unit("rj").KnownAttackers.Add("x");

        Assert.NotEqual(a.Simulation.ComputeStateHash(), b.Simulation.ComputeStateHash());
    }
}
