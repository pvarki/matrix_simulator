using Twin.Sim.Combat;
using Twin.Sim.Events;
using Twin.Sim.Geometry;
using Twin.Sim.Squads;
using Twin.Sim.Units;

namespace Twin.Sim.Tests.Core;

/// <summary>
/// The v1 event log format, pinned by a committed golden log (<c>Data/event-log-v1.jsonl</c>, ADR 0014).
/// <para>
/// The golden log is append-only: its existing lines are never changed or removed; a change that needs that is a
/// format change and a new schema version. An additive change within v1, such as a new event type, appends lines with
/// a time after 100 s, so the round trip and the snapshot at t = 100 still pass unchanged.
/// </para>
/// <para>
/// The story in the golden log, by line (all ids invented): blue1 (Blue, line 1), blue2 (no side, line 2) and red1
/// (Red, line 3) are placed; blue1 forms squad s1 with teams a and b led by blue1 and blue2, in file (4), and watches
/// north with sectors north 90° and east 45° and the air (5). blue1 commands the squad (6), blue2 receives it (7);
/// at t = 0.15000000000000002 blue1 plans a path costing 25.5 s and starts moving (8, 9), moves (10), asks to cross
/// line1 (11) and is granted (12). blue2 times out at line2 (13) and is blocked (14, 15). blue1 reaches (30, 40, 1.25)
/// and goes idle (16 to 18), spots red1 at (60, 80) and reports it by voice (19, 20); blue2 hears of it at (61, 79)
/// (21). The squad halts (22), resumes (23), changes to wedge (24) and halts again (25). red1 fires an aimed miss at
/// blue1 (26), heard by blue1 as a rifle (27) and blue2 as a machine gun (28), then three snap rounds that hit (29).
/// blue2 reports contact (30), starts snap shots (31), is suppressed (32) and goes prone (33). blue1 estimates two
/// fighters and a heavy machine gun (34); blue2 asks for orders (35), blue1 repeats them (36), blue2 deploys to the
/// edge (37). blue1 is out of action (38, 39); blue2 takes the squad and team a (40, 41). blue2's suppression ends
/// (42) and heavy suppression starts (43); blue2 is wounded (44) and watches east with one 120° sector (45). red1
/// crouches at exactly t = 100 (46) and moves at t = 100.5 (47), after the snapshot time.
/// </para>
/// </summary>
public class EventLogContractTests
{
    private static string GoldenV1 => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Core", "Data", "event-log-v1.jsonl"));

    [Fact]
    public void The_golden_v1_log_reads_and_writes_back_byte_identically()
    {
        var text = GoldenV1;

        Assert.Equal(text, EventLog.ToJsonLines(EventLog.FromJsonLines(text)));
    }

    [Fact]
    public void The_golden_v1_log_replays_to_the_known_snapshot()
    {
        var snapshot = EventReplay.SnapshotAt(EventLog.FromJsonLines(GoldenV1), 100);

        Assert.Equal(
            [
                new AgentSnapshot(
                    "blue1",
                    new Vec3(30, 40, 1.25),         // line 16
                    UnitActivity.Idle,              // line 18
                    Health.OutOfAction,             // line 38
                    Posture.Standing,               // never changed
                    Suppressed: false,
                    HeavilySuppressed: false,
                    DrillStage: ContactDrillStage.None,
                    AwaitingOrders: false,
                    Side: Side.Blue),               // line 1
                new AgentSnapshot(
                    "blue2",
                    new Vec3(12, 20, 1.5),          // line 2
                    UnitActivity.Blocked,           // line 14
                    Health.Wounded,                 // line 44
                    Posture.Prone,                  // line 33
                    Suppressed: true,               // line 43
                    HeavilySuppressed: true,        // line 43
                    DrillStage: ContactDrillStage.SnapShots, // line 31
                    AwaitingOrders: true,           // line 30, kept by line 31
                    Side: null),                    // line 2
                new AgentSnapshot(
                    "red1",
                    new Vec3(60, 80, 2),            // line 3; line 47 is after t = 100
                    UnitActivity.Idle,              // line 3
                    Health.Unhurt,
                    Posture.Crouching,              // line 46, exactly at t = 100
                    Side: Side.Red),                // line 3
            ],
            snapshot.Agents);

        var squad = Assert.Single(snapshot.Squads);
        Assert.Equal("s1", squad.Id);                              // line 4
        Assert.Equal("blue2", squad.LeaderId);                     // line 40, after line 38 emptied it
        Assert.Equal(["blue2", "blue2"], squad.TeamLeaderIds);     // line 4 (team b), line 41 (team a)
        Assert.Equal(SquadFormation.Wedge, squad.Formation);       // line 24
        Assert.True(squad.Halted);                                 // line 25, after line 23

        Assert.Equal(
            [
                new KnownEnemySnapshot("blue1", "red1", new Vec2(60, 80)), // line 19
                new KnownEnemySnapshot("blue2", "red1", new Vec2(61, 79)), // line 21
            ],
            snapshot.KnownEnemies);

        Assert.Equal(["blue1", "blue2", "red1"], snapshot.Watches.Select(w => w.AgentId));
        var blue1 = snapshot.Watches[0];
        Assert.Equal(new Vec2(0, 1), blue1.Direction);                                       // line 5
        Assert.True(blue1.Sectors.SequenceEqual(
            [new ObservationSector(new Vec2(0, 1), 90), new ObservationSector(new Vec2(1, 0), 45)])); // line 5
        Assert.True(blue1.WatchesAir);                                                       // line 5
        var blue2 = snapshot.Watches[1];
        Assert.Equal(new Vec2(1, 0), blue2.Direction);                                       // line 45
        Assert.True(blue2.Sectors.SequenceEqual([new ObservationSector(new Vec2(1, 0), 120)])); // line 45
        Assert.False(blue2.WatchesAir);                                                      // line 45
        var red1 = snapshot.Watches[2];
        Assert.Equal(Unit.InitialWatchDirection, red1.Direction);                            // line 3, never changed
        Assert.Empty(red1.Sectors);
        Assert.False(red1.WatchesAir);
    }

    [Fact]
    public void Every_event_type_appears_in_the_golden_v1_log()
    {
        var all = typeof(SimEvent).Assembly.GetTypes()
            .Where(t => t.IsSubclassOf(typeof(SimEvent)) && !t.IsAbstract)
            .Select(t => t.Name).ToHashSet(StringComparer.Ordinal);
        var logged = EventLog.FromJsonLines(GoldenV1).Select(e => e.GetType().Name).ToHashSet(StringComparer.Ordinal);

        var missing = all.Except(logged).Order(StringComparer.Ordinal).ToList();
        var unexpected = logged.Except(all).Order(StringComparer.Ordinal).ToList();

        Assert.True(missing.Count == 0 && unexpected.Count == 0,
            $"Missing from the golden log: [{string.Join(", ", missing)}]; unexpected: [{string.Join(", ", unexpected)}]. " +
            "Append a line for each new type after t = 100 (ADR 0014).");
    }

    [Fact]
    public void A_line_of_another_schema_version_is_rejected()
    {
        var line = "{\"v\":2,\"type\":\"SuppressionEnded\",\"time\":1,\"agent\":\"blue1\"}\n";

        var error = Assert.Throws<InvalidDataException>(() => EventLog.FromJsonLines(line));

        Assert.Contains("version", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Older_v1_lines_without_optional_fields_read_as_defaults()
    {
        var text =
            "{\"v\":1,\"type\":\"ShotFired\",\"time\":1,\"agent\":\"red1\",\"target\":\"blue1\",\"hit\":true}\n" +
            "{\"v\":1,\"type\":\"SuppressionStarted\",\"time\":1,\"agent\":\"blue1\"}\n" +
            "{\"v\":1,\"type\":\"SquadFormed\",\"time\":0,\"agent\":\"blue1\",\"squad\":\"s1\",\"teams\":[\"a\"],\"teamLeaders\":[\"blue1\"]}\n";

        var events = EventLog.FromJsonLines(text);

        var shot = Assert.IsType<ShotFired>(events[0]);
        Assert.Equal(FireMode.Aimed, shot.Mode);
        Assert.Equal(1, shot.Rounds);
        Assert.False(Assert.IsType<SuppressionStarted>(events[1]).Heavy);
        Assert.Equal(SquadFormation.File, Assert.IsType<SquadFormed>(events[2]).Formation);
    }

    [Fact]
    public void A_point_with_too_few_coordinates_is_reported_as_invalid_data()
    {
        var line = "{\"v\":1,\"type\":\"AgentMoved\",\"time\":1,\"agent\":\"blue1\",\"position\":[1,2]}\n";

        Assert.Throws<InvalidDataException>(() => EventLog.FromJsonLines(line));
    }

    [Fact]
    public void A_sector_with_too_few_numbers_is_reported_as_invalid_data()
    {
        var line = "{\"v\":1,\"type\":\"WatchChanged\",\"time\":1,\"agent\":\"blue1\",\"direction\":[0,1],\"sectors\":[[1,2]],\"air\":false}\n";

        Assert.Throws<InvalidDataException>(() => EventLog.FromJsonLines(line));
    }

    [Fact]
    public void Writing_a_non_finite_number_names_the_event_and_the_field()
    {
        SimEvent[] moved = [new AgentPlaced(0, "blue1", new Vec3(0, 0, 0)), new AgentMoved(1, "blue1", new Vec3(double.NaN, 0, 0))];
        SimEvent[] planned = [new PathPlanned(1, "blue1", [new Vec2(0, 0)], double.PositiveInfinity)];

        var movedError = Assert.Throws<ArgumentException>(() => EventLog.ToJsonLines(moved));
        var plannedError = Assert.Throws<ArgumentException>(() => EventLog.ToJsonLines(planned));

        foreach (var expected in new[] { "AgentMoved", "position", "event 1", "finite" })
        {
            Assert.Contains(expected, movedError.Message, StringComparison.Ordinal);
        }

        foreach (var expected in new[] { "PathPlanned", "cost", "event 0", "finite" })
        {
            Assert.Contains(expected, plannedError.Message, StringComparison.Ordinal);
        }
    }
}
