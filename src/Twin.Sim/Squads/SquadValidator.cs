using Twin.Sim.Combat;

namespace Twin.Sim.Squads;

/// <summary>
/// Checks a <see cref="SquadDefinition"/> before <see cref="Simulation.AddSquad"/> forms the squad. The first failing check
/// decides the message, so the checks run in a fixed order.
/// </summary>
internal static class SquadValidator
{
    /// <summary>
    /// Throws <see cref="ArgumentException"/> if the squad is invalid; otherwise returns its leader and its teams' leaders,
    /// in team order.
    /// </summary>
    public static (string LeaderId, List<string> TeamLeaderIds) Validate(Simulation simulation, SquadDefinition definition)
    {
        static ArgumentException Invalid(SquadDefinition d, string why) => new($"Squad '{d.Id}': {why}", nameof(definition));

        if (string.IsNullOrEmpty(definition.Id))
        {
            throw new ArgumentException("A squad needs an id.", nameof(definition));
        }

        if (simulation.HasSquad(definition.Id))
        {
            throw Invalid(definition, "a squad with this id already exists.");
        }

        if (definition.Teams is not { Count: > 0 } || definition.Members is not { Count: > 0 })
        {
            throw Invalid(definition, "a squad needs teams and members.");
        }

        if (definition.Teams.Distinct(StringComparer.Ordinal).Count() != definition.Teams.Count)
        {
            throw Invalid(definition, "team names must be unique.");
        }

        var seen = new SortedSet<string>(StringComparer.Ordinal);
        Side? side = null;
        foreach (var member in definition.Members)
        {
            if (member is null)
            {
                throw Invalid(definition, "a member is missing (null).");
            }

            if (!seen.Add(member.UnitId))
            {
                throw Invalid(definition, $"'{member.UnitId}' is listed twice.");
            }

            if (!simulation.TryGetUnit(member.UnitId, out var unit))
            {
                throw Invalid(definition, $"no unit with id '{member.UnitId}'.");
            }

            if (unit.Combatant is not { } combatant)
            {
                throw Invalid(definition, $"'{member.UnitId}' is not a fighter.");
            }

            if (unit.Health == Health.OutOfAction)
            {
                throw Invalid(definition, $"'{member.UnitId}' is out of action.");
            }

            side ??= combatant.Side;
            if (combatant.Side != side)
            {
                throw Invalid(definition, "all members must be on one side.");
            }

            if (simulation.SquadOf(member.UnitId) is { } other)
            {
                throw Invalid(definition, $"'{member.UnitId}' is already in squad '{other.Id}'.");
            }

            if (!definition.Teams.Contains(member.Team, StringComparer.Ordinal))
            {
                throw Invalid(definition, $"'{member.UnitId}' is in team '{member.Team}', which the squad does not list.");
            }
        }

        var leaders = definition.Members.Where(m => m.Role == SquadRole.SquadLeader).ToList();
        if (leaders.Count != 1)
        {
            throw Invalid(definition, $"a squad needs exactly one squad leader, not {leaders.Count}.");
        }

        if (definition.Members.Count(m => m.Role == SquadRole.SecondInCommand) > 1)
        {
            throw Invalid(definition, "a squad has at most one second-in-command.");
        }

        var teamLeaders = new List<string>();
        foreach (var team in definition.Teams)
        {
            var members = definition.Members.Where(m => m.Team == team).ToList();
            var teamLeader = members
                .Where(m => m.Role is SquadRole.SquadLeader or SquadRole.SecondInCommand or SquadRole.TeamLeader)
                .ToList();
            if (members.Count == 0 || teamLeader.Count != 1)
            {
                throw Invalid(definition, $"team '{team}' needs members and exactly one leader.");
            }

            teamLeaders.Add(teamLeader[0].UnitId);
        }

        return (leaders[0].UnitId, teamLeaders);
    }
}
