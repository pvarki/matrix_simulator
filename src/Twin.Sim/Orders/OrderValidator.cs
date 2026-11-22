using Twin.Sim.Combat;
using Twin.Sim.Geometry;
using Twin.Sim.Units;
using Twin.Sim.World;

namespace Twin.Sim.Orders;

/// <summary>
/// Checks an order when it is issued (<see cref="Simulation.Issue"/>). The first failing check decides the message, so the
/// checks run in a fixed order.
/// </summary>
internal static class OrderValidator
{
    /// <summary>Throws <see cref="OrderRejectedException"/> if <paramref name="unitId"/> cannot take <paramref name="order"/>.</summary>
    public static void Validate(Simulation simulation, string unitId, Order order)
    {
        if (!simulation.TryGetUnit(unitId, out var unit))
        {
            throw new OrderRejectedException($"No unit with id '{unitId}'.");
        }

        if (unit.Health == Health.OutOfAction)
        {
            throw new OrderRejectedException($"Unit '{unitId}' is out of action.");
        }

        if (order is FireAt fireAt)
        {
            ValidateFireAt(simulation, unit, fireAt);
        }

        if (order is SetPosture or CeaseFire && unit.Combatant is null)
        {
            throw new OrderRejectedException($"Unit '{unitId}' is not a fighter.");
        }

        if (order is SetSquadPosture or SetSquadFormation or ResumeMovement && simulation.SquadOf(unitId)?.LeaderId != unitId)
        {
            throw new OrderRejectedException($"Unit '{unitId}' does not lead a squad.");
        }

        if (order is SetSquadFormation { Formation: var formation } && !Enum.IsDefined(formation))
        {
            throw new OrderRejectedException($"Unknown formation {formation}.");
        }

        if (order is Rejoin && (simulation.SquadOf(unitId) is not { } own || own.LeaderId == unitId))
        {
            throw new OrderRejectedException($"Unit '{unitId}' is not a squad member who could rejoin its squad.");
        }

        if (order is Watch { Direction: var direction } &&
            (!double.IsFinite(direction.X) || !double.IsFinite(direction.Y) || direction.Length <= 1e-9))
        {
            throw new OrderRejectedException($"A watch order needs a finite direction, not {direction}.");
        }

        if (order is MoveTo move && !simulation.Terrain.Contains(move.Destination))
        {
            throw new OrderRejectedException($"Destination {move.Destination} is outside the map.");
        }

        if (order is MoveThroughCorridor corridorOrder)
        {
            ValidateCorridorOrder(simulation, unit, corridorOrder);
        }
    }

    private static void ValidateFireAt(Simulation simulation, Unit unit, FireAt order)
    {
        if (unit.Combatant is not { } shooter)
        {
            throw new OrderRejectedException($"Unit '{unit.Id}' is not a fighter.");
        }

        if (!simulation.TryGetUnit(order.TargetId, out var target) || target.Combatant is not { } other)
        {
            throw new OrderRejectedException($"No fighter with id '{order.TargetId}'.");
        }

        if (target == unit || other.Side == shooter.Side)
        {
            throw new OrderRejectedException($"'{order.TargetId}' is on {unit.Id}'s own side.");
        }
    }

    private static void ValidateCorridorOrder(Simulation simulation, Unit unit, MoveThroughCorridor order)
    {
        var corridor = order.Corridor ?? throw new OrderRejectedException("The order has no corridor.");
        if (simulation.Terrain is not IWorldQuery)
        {
            throw new OrderRejectedException("Corridor orders need a raster world with terrain classes.");
        }

        if (unit.Profile is null)
        {
            throw new OrderRejectedException($"Unit '{unit.Id}' has no movement profile.");
        }

        if (corridor.Centerline is not { Count: >= 2 })
        {
            throw new OrderRejectedException("A corridor's centerline needs at least two points.");
        }

        if (!simulation.Terrain.Contains(order.Destination))
        {
            throw new OrderRejectedException($"The corridor ends at {order.Destination}, outside the map.");
        }

        if (corridor.Width <= 0 || corridor.SoftMargin < 0 || corridor.SoftPenalty < 0)
        {
            throw new OrderRejectedException("A corridor needs a positive width and a non-negative soft margin and penalty.");
        }

        var world = (IWorldQuery)simulation.Terrain;
        foreach (var (name, point) in new[] { ("starts", unit.Position.Xy), ("would end", order.Destination) })
        {
            if (Segments.DistanceToPolyline(point, corridor.Centerline) > corridor.OuterLimit)
            {
                throw new OrderRejectedException($"Unit '{unit.Id}' {name} at {point}, outside the corridor's outer limit.");
            }

            var terrain = world.ClassAt(point.X, point.Y);
            if (unit.Profile.BaseSpeed(terrain) <= 0)
            {
                throw new OrderRejectedException($"Unit '{unit.Id}' {name} at {point}, in impassable terrain ({terrain}).");
            }
        }
    }
}
