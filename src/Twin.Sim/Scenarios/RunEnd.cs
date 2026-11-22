using Twin.Sim.Combat;
using Twin.Sim.Events;
using Twin.Sim.Units;

namespace Twin.Sim.Scenarios;

/// <summary>
/// When a headless run is over. A squad scenario's run (<see cref="ForSquad"/>, backlog 0004 S2d) ends when its leader has
/// arrived (<see cref="Arrived"/>; a leader left idle by a halt on contact has not) or is blocked, when one side has
/// nobody left in action, or when shots have been fired and nobody has fired for <see cref="QuietSeconds"/>. The fire
/// fight demo (<see cref="ForFireFight"/>) ends when fewer than two sides are in action, or when nobody has fired for
/// <see cref="QuietSeconds"/>, counted from the start if nobody has fired at all. Create one per run and call
/// <see cref="IsOver"/> after each tick: it reads only the events logged since the last call.
/// </summary>
public sealed class RunEnd
{
    /// <summary>The run ends when nobody has fired for this long, in seconds (for a squad run, only once shots have been fired).</summary>
    public const double QuietSeconds = 120;

    private readonly Simulation _simulation;
    private readonly bool _squad;
    private int _seen;
    private double? _lastShot;
    private bool _arrived;

    private RunEnd(Simulation simulation, bool squad)
    {
        _simulation = simulation;
        _squad = squad;
    }

    /// <summary>The end of a squad scenario's run. The simulation must have a squad; the first one counts.</summary>
    public static RunEnd ForSquad(Simulation simulation) => new(simulation ?? throw new ArgumentNullException(nameof(simulation)), squad: true);

    /// <summary>The end of the fire fight demo.</summary>
    public static RunEnd ForFireFight(Simulation simulation) =>
        new(simulation ?? throw new ArgumentNullException(nameof(simulation)), squad: false);

    /// <summary>True when the run is over (see the class remarks).</summary>
    public bool IsOver()
    {
        var events = _simulation.EventLog;
        for (; _seen < events.Count; _seen++)
        {
            switch (events[_seen])
            {
                case ShotFired shot:
                    _lastShot = shot.Time;
                    break;
                case Arrived arrived when _squad && _simulation.Squads.Any(s => s.LeaderId == arrived.AgentId):
                    _arrived = true;
                    break;
                default:
                    break;
            }
        }

        return _squad ? SquadIsOver() : FireFightIsOver();
    }

    private bool SquadIsOver()
    {
        var squad = _simulation.Squads.First();
        if (_arrived || (squad.LeaderId is { } leaderId && _simulation.GetUnit(leaderId).Activity == UnitActivity.Blocked))
        {
            return true;
        }

        var sides = _simulation.Units.Where(u => u.Combatant is not null).Select(u => u.Combatant!.Side).Distinct().Count();
        if (SidesInAction() < sides)
        {
            return true;
        }

        return _lastShot is { } time && _simulation.TimeSeconds - time >= QuietSeconds;
    }

    private bool FireFightIsOver()
    {
        if (SidesInAction() < 2)
        {
            return true;
        }

        return _simulation.TimeSeconds - (_lastShot ?? 0) >= QuietSeconds;
    }

    private int SidesInAction() => _simulation.Units
        .Where(u => u.Combatant is not null && u.Health != Health.OutOfAction)
        .Select(u => u.Combatant!.Side)
        .Distinct()
        .Count();
}
