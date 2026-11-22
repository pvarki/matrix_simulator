using System.Text.Json;
using Twin.Sim.Combat;
using Twin.Sim.Command;
using Twin.Sim.Corridors;
using Twin.Sim.Geometry;
using Twin.Sim.Movement;
using Twin.Sim.Orders;
using Twin.Sim.Squads;
using Twin.Sim.World;

namespace Twin.Sim.Scenarios;

/// <summary>
/// A scenario read from <c>scenarios/&lt;name&gt;/</c>: <c>scenario.json</c> plus the preprocessed world in
/// <c>world/</c>, or the directory its <c>world</c> key names (format: scenarios/README.md). Points in the file are
/// ETRS-TM35FIN; here they are local meters. A scenario has either one agent or a squad (backlog 0004, S2d), and
/// optionally enemies.
/// </summary>
public sealed class ScenarioDefinition
{
    private ScenarioDefinition(
        LoadedWorld world, string agentId, Vec2 start, Corridor corridor, ScriptedLeader leader, PermissionRules permission,
        MovementProfile profile, ulong seed, int tickRateHz, Doctrine doctrine, SquadDefinition? squad,
        IReadOnlyDictionary<string, Combatant> squadCombatants, IReadOnlyList<ScenarioEnemy> enemies)
    {
        Doctrine = doctrine;
        World = world;
        AgentId = agentId;
        Start = start;
        Squad = squad;
        SquadCombatants = squadCombatants;
        Enemies = enemies;
        Corridor = corridor;
        Leader = leader;
        Permission = permission;
        Profile = profile;
        Seed = seed;
        TickRateHz = tickRateHz;
    }

    /// <summary>The world and its ETRS origin.</summary>
    public LoadedWorld World { get; }

    /// <summary>The agent the corridor order goes to: the scenario's agent, or the squad's leader in a squad scenario.</summary>
    public string AgentId { get; }

    /// <summary>Where the agent, or the squad's leader, starts (A), local meters.</summary>
    public Vec2 Start { get; }

    /// <summary>The squad (<c>squad</c> in scenario.json, backlog 0004 S2d); null for an agent scenario.</summary>
    public SquadDefinition? Squad { get; }

    /// <summary>Where the squad's leader starts, local meters (the same as <see cref="Start"/>).</summary>
    public Vec2 SquadStart => Start;

    /// <summary>Each squad member's fighter, by member id; empty for an agent scenario.</summary>
    public IReadOnlyDictionary<string, Combatant> SquadCombatants { get; }

    /// <summary>The enemies (<c>enemies</c> in scenario.json), in file order; empty if there are none.</summary>
    public IReadOnlyList<ScenarioEnemy> Enemies { get; }

    /// <summary>The corridor of the agent's order; its last centerline point is B.</summary>
    public Corridor Corridor { get; }

    /// <summary>The scripted leader as the file describes it.</summary>
    public ScriptedLeader Leader { get; }

    /// <summary>When the agent asks for permission and how long it waits.</summary>
    public PermissionRules Permission { get; }

    /// <summary>
    /// How the agent moves: <see cref="MovementProfile.Infantry"/>, able to rappel if <c>agent.canRappel</c> is true.
    /// </summary>
    public MovementProfile Profile { get; }

    /// <summary>The scenario's doctrine (<c>doctrine</c> in scenario.json), or the default doctrine.</summary>
    public Doctrine Doctrine { get; }

    /// <summary>Random seed.</summary>
    public ulong Seed { get; }

    /// <summary>Ticks per simulated second (1 / dtSeconds).</summary>
    public int TickRateHz { get; }

    /// <summary>Reads the scenario in <paramref name="directory"/>.</summary>
    /// <exception cref="InvalidDataException">The file is not a valid scenario.</exception>
    public static ScenarioDefinition Load(string directory)
    {
        ArgumentNullException.ThrowIfNull(directory);
        try
        {
            return Read(directory);
        }
        catch (Exception e) when (e is JsonException or KeyNotFoundException or InvalidOperationException or FormatException)
        {
            throw new InvalidDataException($"Invalid scenario in '{directory}': {e.Message}", e);
        }
    }

    private static ScenarioDefinition Read(string directory)
    {
        using var json = JsonDocument.Parse(File.ReadAllText(Path.Combine(directory, "scenario.json")));
        var root = Section(
            json.RootElement, "$", "schemaVersion", "seed", "dtSeconds", "world", "agent", "squad", "enemies", "corridor", "leader",
            "permission", "expect", "doctrine");
        var schema = Get(root, "$", "schemaVersion", Number);
        if (schema != 1)
        {
            throw Error("$.schemaVersion", $"unsupported version {schema}; expected 1.");
        }

        var world = WorldFiles.Load(Path.Combine(directory, Get(root, "$", "world", Text, "world")));
        Vec2 Local(JsonElement point, string path)
        {
            var (easting, northing) = Point(point, path);
            return world.Origin.ToLocal(new EtrsPoint(easting, northing));
        }

        Vec2[] Line(JsonElement points, string path) => Items(points, path).Select(p => Local(p.Item, p.Path)).ToArray();

        var hasAgent = root.TryGetProperty("agent", out var agent);
        var hasSquad = root.TryGetProperty("squad", out var squadJson);
        if (hasAgent == hasSquad)
        {
            throw Error("$", "a scenario needs exactly one of 'agent' and 'squad'.");
        }

        var corridorJson = Get(
            root, "$", "corridor", (e, path) => Section(e, path, "centerline", "width", "softMargin", "softPenalty", "hardBoundaries"));
        var boundaries = Get(
            corridorJson, "$.corridor", "hardBoundaries",
            (e, path) => Items(e, path).Select(b => ReadBoundary(Section(b.Item, b.Path, "id", "line"), b.Path, Line)).ToArray(),
            []);
        var centerline = Get(corridorJson, "$.corridor", "centerline", Line);
        if (centerline.Length == 0)
        {
            throw Error("$.corridor.centerline", "needs at least one point.");
        }

        CheckPosition(world, centerline[^1], $"$.corridor.centerline[{centerline.Length - 1}]", "the corridor's end B");
        var corridor = new Corridor(
            centerline,
            Get(corridorJson, "$.corridor", "width", Number),
            Get(corridorJson, "$.corridor", "softMargin", Number),
            Get(corridorJson, "$.corridor", "softPenalty", Number),
            boundaries);

        var leaderJson = Get(root, "$", "leader", (e, path) => Section(e, path, "answer", "delaySeconds", "permitRadius"));
        var leader = new ScriptedLeader(
            Get(leaderJson, "$.leader", "answer", ParseAnswer),
            Get(leaderJson, "$.leader", "delaySeconds", Number),
            Get(leaderJson, "$.leader", "permitRadius", Number));

        var permission = Get(
            root, "$", "permission",
            (p, path) =>
            {
                Section(p, path, "threshold", "timeoutSeconds");
                return new PermissionRules(Get(p, path, "threshold", Number), Get(p, path, "timeoutSeconds", Number));
            },
            PermissionRules.Default);
        if (root.TryGetProperty("expect", out var expect))
        {
            Section(expect, "$.expect", "detourWithoutCrossing"); // read by tests only (ScenarioOnDisk)
        }

        var seed = Get(
            root, "$", "seed",
            (e, path) => e.ValueKind == JsonValueKind.Number && e.TryGetUInt64(out var s) ? s : throw Error(path, "must be a whole number from 0 up."),
            1UL);
        var dt = Get(root, "$", "dtSeconds", Number, 0.5);
        if (dt <= 0)
        {
            throw Error("$.dtSeconds", $"{dt} must be greater than 0.");
        }

        var tickRate = (int)Math.Round(1 / dt);
        if (Math.Abs((1.0 / tickRate) - dt) > 1e-12)
        {
            throw Error("$.dtSeconds", $"{dt} must be 1 / a whole number of ticks per second.");
        }

        var enemies = Get(
            root, "$", "enemies", (e, path) => Items(e, path).Select(x => (Enemy: ReadEnemy(x.Item, x.Path, Local), x.Path)).ToList(), []);
        var ids = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var (enemy, path) in enemies)
        {
            CheckPosition(world, enemy.Position, $"{path}.position", $"enemy '{enemy.Id}'");
        }

        var enemyIds = enemies.Select(e => (e.Enemy.Id, Path: $"{e.Path}.id"));
        if (hasSquad)
        {
            var (squad, combatants) = ReadSquad(Section(squadJson, "$.squad", "id", "start", "formation", "rules", "teams", "members"));
            var start = Get(squadJson, "$.squad", "start", Local);
            CheckSquad(world, squad, combatants, start, corridor);
            CheckUnique(ids, squad.Members.Select((m, i) => (m.UnitId, $"$.squad.members[{i}].id")).Concat(enemyIds));
            return new ScenarioDefinition(
                world, squad.Members.First(m => m.Role == SquadRole.SquadLeader).UnitId, start, corridor, leader, permission, MovementProfile.Infantry, seed, tickRate, ReadDoctrine(root),
                squad, combatants, enemies.Select(e => e.Enemy).ToList());
        }

        Section(agent, "$.agent", "id", "start", "canRappel");
        var agentId = Get(agent, "$.agent", "id", Text);
        var agentStart = Get(agent, "$.agent", "start", Local);
        CheckPosition(world, agentStart, "$.agent.start", $"agent '{agentId}'");
        CheckUnique(ids, enemyIds.Prepend((agentId, "$.agent.id")));
        return new ScenarioDefinition(
            world, agentId, agentStart, corridor, leader,
            permission, MovementProfile.Infantry with { CanRappel = Get(agent, "$.agent", "canRappel", Flag, false) },
            seed, tickRate, ReadDoctrine(root), null, new SortedDictionary<string, Combatant>(StringComparer.Ordinal),
            enemies.Select(e => e.Enemy).ToList());
    }

    /// <summary>One entry of <c>corridor.hardBoundaries</c>.</summary>
    private static HardBoundary ReadBoundary(JsonElement b, string path, Func<JsonElement, string, Vec2[]> line) =>
        new(Get(b, path, "id", Text), Get(b, path, "line", line), BoundarySource.Order);

    /// <summary>The <c>squad</c> section: the definition and each member's fighter (backlog 0004 S2d).</summary>
    private static (SquadDefinition Squad, SortedDictionary<string, Combatant> Combatants) ReadSquad(JsonElement json)
    {
        const string path = "$.squad";
        var id = Get(json, path, "id", Text);
        var rules = Get(json, path, "rules", Name<RulesOfEngagement>, RulesOfEngagement.ReturnFire);
        var formation = Get(json, path, "formation", Name<SquadFormation>, SquadFormation.File);
        var teams = Get(json, path, "teams", (e, p) => Items(e, p).Select(t => Text(t.Item, t.Path)).ToList());
        var members = new List<SquadMember>();
        var combatants = new SortedDictionary<string, Combatant>(StringComparer.Ordinal);
        foreach (var (m, memberPath) in Get(json, path, "members", Items))
        {
            Section(m, memberPath, "id", "role", "team", "experience", "weapon", "rules");
            var memberId = Get(m, memberPath, "id", Text);
            members.Add(new SquadMember(memberId, Get(m, memberPath, "role", Name<SquadRole>), Get(m, memberPath, "team", Text)));
            var combatant = new Combatant(
                Side.Blue,
                Get(m, memberPath, "rules", Name<RulesOfEngagement>, rules),
                Get(m, memberPath, "weapon", Weapon, WeaponProfile.Rifle),
                id,
                Get(m, memberPath, "experience", Name<Experience>, Experience.Regular));
            if (!combatants.TryAdd(memberId, combatant))
            {
                throw Error($"{memberPath}.id", $"'{memberId}' is listed twice in squad '{id}'.");
            }
        }

        return (new SquadDefinition(id, teams, members, formation), combatants);
    }

    /// <summary>Every id is used once across the agent or the squad and the enemies.</summary>
    private static void CheckUnique(SortedSet<string> ids, IEnumerable<(string Id, string Path)> all)
    {
        foreach (var (id, path) in all)
        {
            if (!ids.Add(id))
            {
                throw Error(path, $"the id '{id}' is used twice.");
            }
        }
    }

    /// <summary>
    /// Checks that the squad can be placed (its start and every member's starting slot on passable ground on the map, a
    /// corridor to lay it out along) and is valid as <see cref="Simulation.AddSquad"/> checks it, so an unusable squad fails when it is loaded.
    /// </summary>
    private static void CheckSquad(
        LoadedWorld world, SquadDefinition squad, SortedDictionary<string, Combatant> combatants, Vec2 start, Corridor corridor)
    {
        CheckPosition(world, start, "$.squad.start", $"squad '{squad.Id}' start");

        if (corridor.Centerline.Count < 2 || corridor.Centerline[0].DistanceTo(corridor.Centerline[1]) < 1e-9)
        {
            throw Error("$.corridor.centerline", $"the corridor's first segment must have a length, to lay squad '{squad.Id}' out along it.");
        }

        try
        {
            var check = new Simulation(world.World, new SimulationConfig());
            foreach (var (id, combatant) in combatants)
            {
                check.AddUnit(id, start, MovementProfile.Infantry, combatant);
            }

            check.AddSquad(squad);
            foreach (var (id, at) in StartingPositions(squad, start, corridor))
            {
                var index = squad.Members.Select(m => m.UnitId).ToList().IndexOf(id);
                CheckPosition(world, at, $"$.squad.members[{index}]", $"the starting slot of '{id}'");
            }
        }
        catch (ArgumentException e)
        {
            throw new InvalidDataException($"$.squad: {e.Message}", e);
        }
    }

    /// <summary>
    /// Checks that a unit can stand at <paramref name="at"/>: on the map, on terrain infantry can move on (the rule
    /// <see cref="Simulation.Issue"/> applies to a corridor order's start and end, so water and buildings fail).
    /// </summary>
    private static void CheckPosition(LoadedWorld world, Vec2 at, string path, string what)
    {
        if (!world.World.Contains(at))
        {
            throw Error(path, $"{what} at {at} is off the map.");
        }

        var terrain = world.World.ClassAt(at.X, at.Y);
        if (MovementProfile.Infantry.BaseSpeed(terrain) <= 0)
        {
            throw Error(path, $"{what} at {at} is in impassable terrain ({terrain}).");
        }
    }

    /// <summary>
    /// Where each member starts: its slot of the starting formation around <paramref name="start"/>, laid out along the
    /// corridor's first segment (the squad leader at the start).
    /// </summary>
    private static IEnumerable<(string Id, Vec2 At)> StartingPositions(SquadDefinition squad, Vec2 start, Corridor corridor)
    {
        var forward = (corridor.Centerline[1] - corridor.Centerline[0]).Normalized();
        var left = new Vec2(-forward.Y, forward.X);
        return FormationSlots.For(squad.Formation, squad, SquadDoctrine.Default.IntervalMeters)
            .Select(slot => (slot.UnitId, start + (forward * slot.Along) + (left * slot.Across)));
    }

    /// <summary>One entry of <c>enemies</c> (backlog 0004 S2d).</summary>
    private static ScenarioEnemy ReadEnemy(JsonElement e, string path, Func<JsonElement, string, Vec2> local)
    {
        Section(e, path, "id", "position", "side", "posture", "rules", "weapon", "experience", "watchToward");
        return new(
            Get(e, path, "id", Text),
            Get(e, path, "position", local),
            new Combatant(
                Get(e, path, "side", Name<Side>, Side.Red),
                Get(e, path, "rules", Name<RulesOfEngagement>, RulesOfEngagement.FreeFire),
                Get(e, path, "weapon", Weapon, WeaponProfile.Rifle),
                Experience: Get(e, path, "experience", Name<Experience>, Experience.Regular)),
            Get(e, path, "posture", Name<Posture>, Posture.Standing),
            e.TryGetProperty("watchToward", out var watch) ? local(watch, $"{path}.watchToward") : null);
    }

    /// <summary>A weapon by its name in scenario.json.</summary>
    private static WeaponProfile Weapon(JsonElement e, string path) => Text(e, path) switch
    {
        "rifle" => WeaponProfile.Rifle,
        "machineGun" => WeaponProfile.MachineGun,
        "heavyMachineGun" => WeaponProfile.HeavyMachineGun,
        var name => throw Error(path, $"unknown weapon '{name}'; use rifle, machineGun or heavyMachineGun."),
    };

    /// <summary>An enum value by its name in scenario.json, in camel case (<c>squadLeader</c>, <c>freeFire</c>).</summary>
    private static T Name<T>(JsonElement e, string path)
        where T : struct, Enum
    {
        var text = Text(e, path);
        foreach (var name in Enum.GetNames<T>())
        {
            if (string.Equals(char.ToLowerInvariant(name[0]) + name[1..], text, StringComparison.Ordinal))
            {
                return Enum.Parse<T>(name);
            }
        }

        var names = string.Join(", ", Enum.GetNames<T>().Select(n => char.ToLowerInvariant(n[0]) + n[1..]));
        throw Error(path, $"unknown value '{text}'; use {names}.");
    }

    /// <summary>
    /// Creates the simulation with the agent placed at A and its corridor order issued, the leader answering
    /// <paramref name="answer"/> after the scenario's delay. No ticks have run yet.
    /// </summary>
    public Simulation CreateSimulation(LeaderAnswer answer)
    {
        var config = new SimulationConfig(TickRateHz, Seed)
        {
            Leader = Leader with { Answer = answer },
            Permission = Permission,
            Combat = CombatRules.Default with { Doctrine = Doctrine },
        };
        var simulation = new Simulation(World.World, config);
        if (Squad is { } squad)
        {
            foreach (var (id, at) in StartingPositions(squad, Start, Corridor))
            {
                simulation.AddUnit(id, at, MovementProfile.Infantry, SquadCombatants[id]);
            }

            simulation.AddSquad(squad);
        }
        else
        {
            simulation.AddUnit(AgentId, Start, Profile);
        }

        foreach (var enemy in Enemies)
        {
            simulation.AddUnit(enemy.Id, enemy.Position, MovementProfile.Infantry, enemy.Combatant);
            if (enemy.Posture != Posture.Standing)
            {
                simulation.Issue(enemy.Id, new SetPosture(enemy.Posture));
            }

            if (enemy.WatchToward is { } point && point.DistanceTo(enemy.Position) > 1e-9)
            {
                simulation.Issue(enemy.Id, new Watch(point - enemy.Position));
            }
        }

        simulation.Issue(AgentId, new MoveThroughCorridor(Corridor));
        return simulation;
    }

    private static Doctrine ReadDoctrine(JsonElement root)
    {
        if (!root.TryGetProperty("doctrine", out var d))
        {
            return Doctrine.Default;
        }

        const string path = "$.doctrine";
        Section(d, path, "returnFireOverridesFireAt", "permissionAnswers");
        return Doctrine.Default with
        {
            ReturnFireOverridesFireAt = Get(d, path, "returnFireOverridesFireAt", Flag, Doctrine.Default.ReturnFireOverridesFireAt),
            PermissionAnswers = Get(
                d, path, "permissionAnswers",
                (e, p) => Text(e, p) switch
                {
                    "whenFree" => PermissionAnswerTiming.WhenFree,
                    "immediately" => PermissionAnswerTiming.Immediately,
                    var other => throw Error(p, $"unknown permissionAnswers '{other}'; use whenFree or immediately."),
                },
                Doctrine.Default.PermissionAnswers),
        };
    }

    private static LeaderAnswer ParseAnswer(JsonElement e, string path) => Text(e, path) switch
    {
        "grant" => LeaderAnswer.Grant,
        "deny" => LeaderAnswer.Deny,
        "noAnswer" => LeaderAnswer.NoAnswer,
        var text => throw Error(path, $"unknown leader answer '{text}'; use grant, deny or noAnswer."),
    };

    /// <summary>
    /// Checks that <paramref name="e"/> at <paramref name="path"/> is an object whose keys are all in
    /// <paramref name="known"/>, so a mistyped key fails instead of being ignored. Call it before reading the object.
    /// </summary>
    private static JsonElement Section(JsonElement e, string path, params string[] known)
    {
        if (e.ValueKind != JsonValueKind.Object)
        {
            throw Error(path, "must be an object.");
        }

        foreach (var property in e.EnumerateObject())
        {
            if (Array.IndexOf(known, property.Name) < 0)
            {
                throw Error($"{path}.{property.Name}", $"unknown key; expected one of {string.Join(", ", known)}.");
            }
        }

        return e;
    }

    /// <summary>Reads the required key <paramref name="key"/> of the object at <paramref name="path"/>.</summary>
    private static T Get<T>(JsonElement obj, string path, string key, Func<JsonElement, string, T> read) =>
        obj.TryGetProperty(key, out var value) ? read(value, $"{path}.{key}") : throw Error($"{path}.{key}", "is missing.");

    /// <summary>Reads the optional key <paramref name="key"/>, or returns <paramref name="fallback"/> if it is absent.</summary>
    private static T Get<T>(JsonElement obj, string path, string key, Func<JsonElement, string, T> read, T fallback) =>
        obj.TryGetProperty(key, out var value) ? read(value, $"{path}.{key}") : fallback;

    private static double Number(JsonElement e, string path) =>
        e.ValueKind == JsonValueKind.Number && e.TryGetDouble(out var value) ? value : throw Error(path, "must be a number.");

    private static string Text(JsonElement e, string path) =>
        e.ValueKind == JsonValueKind.String ? e.GetString()! : throw Error(path, "must be a string.");

    private static bool Flag(JsonElement e, string path) =>
        e.ValueKind is JsonValueKind.True or JsonValueKind.False ? e.GetBoolean() : throw Error(path, "must be true or false.");

    /// <summary>The items of the array at <paramref name="path"/>, each with its own path (<c>path[i]</c>).</summary>
    private static List<(JsonElement Item, string Path)> Items(JsonElement e, string path) =>
        e.ValueKind == JsonValueKind.Array
            ? e.EnumerateArray().Select((item, i) => (item, $"{path}[{i}]")).ToList()
            : throw Error(path, "must be an array.");

    /// <summary>An ETRS-TM35FIN point: exactly two numbers.</summary>
    private static (double Easting, double Northing) Point(JsonElement e, string path) =>
        e.ValueKind == JsonValueKind.Array && e.GetArrayLength() == 2 &&
        e[0].ValueKind == JsonValueKind.Number && e[1].ValueKind == JsonValueKind.Number
            ? (e[0].GetDouble(), e[1].GetDouble())
            : throw Error(path, "must be [easting, northing].");

    private static InvalidDataException Error(string path, string problem) => new($"{path}: {problem}");
}

/// <summary>An enemy fighter in a scenario (<c>enemies</c> in scenario.json, backlog 0004 S2d). It gets no movement order.</summary>
/// <param name="Id">Its id.</param>
/// <param name="Position">Where it is, local meters.</param>
/// <param name="Combatant">Its side, rules of engagement, weapon and experience.</param>
/// <param name="Posture">The posture it takes at the start.</param>
/// <param name="WatchToward">The point it watches, local meters; null to keep the default watch.</param>
public sealed record ScenarioEnemy(string Id, Vec2 Position, Combatant Combatant, Posture Posture, Vec2? WatchToward);
