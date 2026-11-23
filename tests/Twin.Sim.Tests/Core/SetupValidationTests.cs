using Twin.Sim.Combat;
using Twin.Sim.Command;
using Twin.Sim.Geometry;
using Twin.Sim.Movement;
using Twin.Testing;

namespace Twin.Sim.Tests.Core;

/// <summary>
/// A simulation's configuration and the agents placed on it are checked where they enter it (the constructor and
/// <c>AddUnit</c>): a value no rule can work with throws <see cref="ArgumentOutOfRangeException"/> naming the field
/// (the leaf property for a nested value, such as a weapon inside a fighter).
/// </summary>
public class SetupValidationTests
{
    private static readonly Vec2 At = new(50, 50);

    private static Simulation Flat(SimulationConfig? config = null) => new(TestTerrain.Flat(100), config);

    private static void Rejected(string paramName, Action action)
    {
        var error = Assert.Throws<ArgumentOutOfRangeException>(action);
        Assert.Equal(paramName, error.ParamName);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void A_tick_rate_that_is_not_positive_is_rejected(int hz) =>
        Rejected("TickRateHz", () => Flat(new SimulationConfig(TickRateHz: hz)));

    [Theory]
    [InlineData("DelaySeconds")]
    [InlineData("PermitRadiusMeters")]
    [InlineData("Answer")]
    public void An_invalid_scripted_leader_is_rejected(string field)
    {
        var leader = new ScriptedLeader(LeaderAnswer.Grant, DelaySeconds: 10, PermitRadiusMeters: 25);
        leader = field switch
        {
            "DelaySeconds" => leader with { DelaySeconds = -1 },
            "PermitRadiusMeters" => leader with { PermitRadiusMeters = -1 },
            "Answer" => leader with { Answer = (LeaderAnswer)9 },
            _ => throw new ArgumentOutOfRangeException(nameof(field)),
        };

        Rejected(field, () => Flat(new SimulationConfig { Leader = leader }));
    }

    [Theory]
    [InlineData("Threshold")]
    [InlineData("TimeoutSeconds")]
    public void Invalid_permission_rules_are_rejected(string field)
    {
        var rules = field switch
        {
            "Threshold" => PermissionRules.Default with { Threshold = -1 },
            "TimeoutSeconds" => PermissionRules.Default with { TimeoutSeconds = -1 },
            _ => throw new ArgumentOutOfRangeException(nameof(field)),
        };

        Rejected(field, () => Flat(new SimulationConfig { Permission = rules }));
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void A_maximum_speed_that_is_not_finite_is_rejected(double maxSpeedMps) =>
        Rejected("maxSpeedMps", () => Flat().AddUnit("a", At, maxSpeedMps));

    [Theory]
    [InlineData("OpenMps")]
    [InlineData("ForestMps")]
    [InlineData("MaxSlope")]
    [InlineData("EyeHeightMeters")]
    public void An_invalid_movement_profile_is_rejected(string field)
    {
        var profile = field switch
        {
            "OpenMps" => MovementProfile.Infantry with { OpenMps = -1 },
            "ForestMps" => MovementProfile.Infantry with { ForestMps = double.NaN },
            "MaxSlope" => MovementProfile.Infantry with { MaxSlope = -1 },
            "EyeHeightMeters" => MovementProfile.Infantry with { EyeHeightMeters = -1 },
            _ => throw new ArgumentOutOfRangeException(nameof(field)),
        };

        Rejected(field, () => Flat().AddUnit("a", At, profile));
    }

    [Theory]
    [InlineData("HitProbabilityAtZero")]
    [InlineData("HitFalloffMeters")]
    [InlineData("SecondsPerShot")]
    [InlineData("SnapSecondsPerShot")]
    [InlineData("Rounds")]
    [InlineData("RoundsPerBurst")]
    public void A_fighter_with_an_invalid_weapon_is_rejected(string field)
    {
        var weapon = field switch
        {
            "HitProbabilityAtZero" => WeaponProfile.Rifle with { HitProbabilityAtZero = 1.5 },
            "HitFalloffMeters" => WeaponProfile.Rifle with { HitFalloffMeters = 0 },
            "SecondsPerShot" => WeaponProfile.Rifle with { SecondsPerShot = 0 },
            "SnapSecondsPerShot" => WeaponProfile.Rifle with { SnapSecondsPerShot = 0 },
            "Rounds" => WeaponProfile.Rifle with { Rounds = -1 },
            "RoundsPerBurst" => WeaponProfile.Rifle with { RoundsPerBurst = 0 },
            _ => throw new ArgumentOutOfRangeException(nameof(field)),
        };
        var fighter = new Combatant(Side.Blue, RulesOfEngagement.HoldFire, weapon);

        Rejected(field, () => Flat().AddUnit("a", At, MovementProfile.Infantry, fighter));
    }

    [Theory]
    [InlineData("Side")]
    [InlineData("Rules")]
    [InlineData("Experience")]
    [InlineData("Class")]
    public void A_fighter_with_an_undefined_enum_value_is_rejected(string field)
    {
        var fighter = new Combatant(Side.Blue, RulesOfEngagement.HoldFire, WeaponProfile.Rifle);
        fighter = field switch
        {
            "Side" => fighter with { Side = (Side)9 },
            "Rules" => fighter with { Rules = (RulesOfEngagement)9 },
            "Experience" => fighter with { Experience = (Experience)9 },
            "Class" => fighter with { Weapon = WeaponProfile.Rifle with { Class = (WeaponClass)9 } },
            _ => throw new ArgumentOutOfRangeException(nameof(field)),
        };

        Rejected(field, () => Flat().AddUnit("a", At, MovementProfile.Infantry, fighter));
    }

    [Fact]
    public void Valid_presets_are_accepted()
    {
        var sim = Flat(new SimulationConfig { Leader = new ScriptedLeader(LeaderAnswer.NoAnswer, 0, 0) });

        sim.AddUnit("speed", new Vec2(10, 10), maxSpeedMps: 1.5);
        sim.AddUnit("walker", new Vec2(20, 10), MovementProfile.Infantry);
        sim.AddUnit("rifle", new Vec2(30, 10), MovementProfile.Infantry,
            new Combatant(Side.Blue, RulesOfEngagement.HoldFire, WeaponProfile.Rifle));
        sim.AddUnit("mg", new Vec2(40, 10), MovementProfile.Infantry,
            new Combatant(Side.Red, RulesOfEngagement.FreeFire, WeaponProfile.MachineGun, Experience: Experience.Conscript));
        sim.AddUnit("hmg", new Vec2(50, 10), MovementProfile.Infantry,
            new Combatant(Side.Red, RulesOfEngagement.ReturnFire, WeaponProfile.HeavyMachineGun, Experience: Experience.SpecialForces));

        Assert.Equal(5, sim.Units.Count);
    }
}
