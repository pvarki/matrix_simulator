using Twin.Sim.Combat;
using Twin.Sim.Movement;

namespace Twin.Sim;

/// <summary>
/// Checks what a simulation is set up with where it enters (the <see cref="Simulation"/> constructor and
/// <see cref="Simulation.AddUnit(string, Geometry.Vec2, double)"/> and its overloads), so the rules never see a value they
/// cannot work with. Each check throws <see cref="ArgumentOutOfRangeException"/> naming the field (the leaf property for a
/// nested value). Every number must be finite; the checks run in a fixed order, so the first failing one decides.
/// </summary>
internal static class SetupValidator
{
    /// <summary>Checks the tick rate, the scripted leader and the permission rules.</summary>
    public static void Validate(SimulationConfig config)
    {
        if (config.TickRateHz <= 0)
        {
            throw Invalid(nameof(config.TickRateHz), config.TickRateHz, "must be positive");
        }

        if (config.Leader is { } leader)
        {
            Defined(nameof(leader.Answer), leader.Answer);
            NotNegative(nameof(leader.DelaySeconds), leader.DelaySeconds);
            NotNegative(nameof(leader.PermitRadiusMeters), leader.PermitRadiusMeters);
        }

        NotNegative(nameof(config.Permission.Threshold), config.Permission.Threshold);
        NotNegative(nameof(config.Permission.TimeoutSeconds), config.Permission.TimeoutSeconds);
    }

    /// <summary>Checks the maximum speed of a unit without a movement profile.</summary>
    public static void ValidateSpeed(double maxSpeedMps) => Positive(nameof(maxSpeedMps), maxSpeedMps);

    /// <summary>Checks a movement profile: speeds, slope limit and eye height finite and not negative.</summary>
    public static void Validate(MovementProfile profile)
    {
        NotNegative(nameof(profile.RoadMps), profile.RoadMps);
        NotNegative(nameof(profile.OpenMps), profile.OpenMps);
        NotNegative(nameof(profile.FieldMps), profile.FieldMps);
        NotNegative(nameof(profile.ForestMps), profile.ForestMps);
        NotNegative(nameof(profile.BogMps), profile.BogMps);
        NotNegative(nameof(profile.MaxSlope), profile.MaxSlope);
        NotNegative(nameof(profile.EyeHeightMeters), profile.EyeHeightMeters);
    }

    /// <summary>Checks a fighter: its side, rules and experience, and its weapon.</summary>
    public static void Validate(Combatant combatant)
    {
        Defined(nameof(combatant.Side), combatant.Side);
        Defined(nameof(combatant.Rules), combatant.Rules);
        Defined(nameof(combatant.Experience), combatant.Experience);

        var weapon = combatant.Weapon ?? throw new ArgumentNullException(nameof(combatant), "A fighter needs a weapon.");
        var p = weapon.HitProbabilityAtZero;
        if (!double.IsFinite(p) || p < 0 || p > 1)
        {
            throw Invalid(nameof(weapon.HitProbabilityAtZero), p, "must be between 0 and 1");
        }

        Positive(nameof(weapon.HitFalloffMeters), weapon.HitFalloffMeters);
        Positive(nameof(weapon.MaxRangeMeters), weapon.MaxRangeMeters);
        Positive(nameof(weapon.SecondsPerShot), weapon.SecondsPerShot);
        Positive(nameof(weapon.SnapSecondsPerShot), weapon.SnapSecondsPerShot);
        if (weapon.Rounds < 0)
        {
            throw Invalid(nameof(weapon.Rounds), weapon.Rounds, "must not be negative");
        }

        NotNegative(nameof(weapon.SoundRangeMeters), weapon.SoundRangeMeters);
        NotNegative(nameof(weapon.FlashSeconds), weapon.FlashSeconds);
        NotNegative(nameof(weapon.SuppressionPerRound), weapon.SuppressionPerRound);
        if (weapon.RoundsPerBurst < 1)
        {
            throw Invalid(nameof(weapon.RoundsPerBurst), weapon.RoundsPerBurst, "must be at least 1");
        }

        Defined(nameof(weapon.Class), weapon.Class);
    }

    private static void Positive(string name, double value)
    {
        if (!double.IsFinite(value) || value <= 0)
        {
            throw Invalid(name, value, "must be finite and positive");
        }
    }

    private static void NotNegative(string name, double value)
    {
        if (!double.IsFinite(value) || value < 0)
        {
            throw Invalid(name, value, "must be finite and not negative");
        }
    }

    private static void Defined<T>(string name, T value)
        where T : struct, Enum
    {
        if (!Enum.IsDefined(value))
        {
            throw Invalid(name, value, $"is not a defined {typeof(T).Name}");
        }
    }

    private static ArgumentOutOfRangeException Invalid(string name, object value, string why) =>
        new(name, value, $"{name} {why}.");
}
