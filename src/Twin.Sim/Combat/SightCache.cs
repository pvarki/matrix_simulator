using Twin.Sim.Geometry;
using Twin.Sim.Units;
using Twin.Sim.World;

namespace Twin.Sim.Combat;

/// <summary>
/// Remembers the last sight line computed for each (observer, target) pair and gives it again while neither end has
/// moved: the observer's eye and the target's aim point are exactly the points the line was computed between. Most
/// pairs in a fire fight are blocked by forest or terrain and would otherwise recompute the same line every tick.
/// </summary>
/// <remarks>
/// Assumptions, which keep the result identical to <see cref="LineOfSight.Between"/>:
/// <list type="bullet">
/// <item>The world and the <see cref="CombatRules"/> are fixed for the run. Anything that later adds dynamic terrain
/// (smoke, destroyed buildings) must clear or re-key this cache.</item>
/// <item>The eye and aim points include the posture, so a posture change is a miss, like a move.</item>
/// <item>The stored value is the <see cref="SightLine"/>, never a visibility answer: muzzle flash, thresholds, the
/// field of view and distance are applied by the caller after it.</item>
/// <item>It is a memo, not simulation state, so it is not part of the state hash.</item>
/// </list>
/// It holds at most one entry per ordered pair of units.
/// </remarks>
internal sealed class SightCache
{
    // Looked up, never iterated; string hashing is randomized per process, which changes lookup speed only.
    private readonly Dictionary<(string Observer, string Target), (Vec3 From, Vec3 To, SightLine Line)> _lines = []; // determinism-ok

    /// <summary>The sight line from the observer's eye to the target's aim point, reused while neither has moved.</summary>
    public SightLine Between(IWorldQuery world, Unit observer, Unit target, CombatRules rules)
    {
        var from = observer.EyePosition;
        var to = target.AimPoint;
        var key = (observer.Id, target.Id);
        if (_lines.TryGetValue(key, out var stored) && stored.From == from && stored.To == to)
        {
            return stored.Line;
        }

        var line = LineOfSight.Between(world, from, to, rules);
        _lines[key] = (from, to, line);
        return line;
    }
}
