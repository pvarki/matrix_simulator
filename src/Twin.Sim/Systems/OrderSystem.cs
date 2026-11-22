using Twin.Sim.Orders;
using Twin.Sim.Units;

namespace Twin.Sim.Systems;

/// <summary>
/// Applies the orders issued since the last tick, in issue order (ADR 0005), before every other system runs (ADR 0013).
/// <see cref="Simulation.Issue"/> validates an order and queues it here. A new order ends the drill under fire, takes a
/// member out of its squad's movement and replaces the unit's current order; posture, squad posture, squad formation and
/// cease-fire orders keep it.
/// </summary>
internal sealed class OrderSystem : ISimSystem
{
    private readonly List<(string UnitId, Order Order)> _pendingOrders = [];

    /// <summary>Queues a validated order for the next tick.</summary>
    internal void Enqueue(string unitId, Order order) => _pendingOrders.Add((unitId, order));

    /// <inheritdoc />
    public void Update(Simulation simulation, double dtSeconds)
    {
        ArgumentNullException.ThrowIfNull(simulation);
        foreach (var (unitId, order) in _pendingOrders)
        {
            var unit = simulation.GetUnit(unitId);
            if (order is not (SetPosture or SetSquadPosture) && !(order is Rejoin && simulation.SquadOf(unitId)?.IsFollowing(unitId) == true))
            {
                ContactDrillSystem.End(simulation, unit); // any new order ends the drill under fire
                ContactDrillSystem.Answer(unit); // and the leader's order stands against the fire it answers
            }

            if (order is not Rejoin && simulation.SquadOf(unitId) is { } detachedFrom && detachedFrom.LeaderId != unitId)
            {
                detachedFrom.Detach(unitId); // any order straight to a member takes it out of the squad's movement
            }

            if (order is not (SetPosture or SetSquadPosture or SetSquadFormation) && simulation.SquadOf(unitId) is { } waiting &&
                waiting.LeaderId == unitId)
            {
                waiting.OrderRequestedAt = null; // an order of its own replaces asking for the old one
            }

            switch (order)
            {
                case SetPosture posture:
                    CombatQueries.WantPosture(unit, posture.Posture); // keeps the current order
                    break;
                case CeaseFire:
                    if (unit.CurrentOrder is FireAt)
                    {
                        CombatQueries.EndOrder(unit);
                    }

                    break;
                case SetSquadPosture or SetSquadFormation when simulation.SquadOf(unitId) is { } squad && squad.LeaderId == unitId:
                    SquadSystem.Command(simulation, squad, unit, order); // keeps the current order
                    break;
                case SetSquadPosture or SetSquadFormation:
                    break; // the unit lost command of its squad after the order was issued
                case ResumeMovement when simulation.SquadOf(unitId) is { } squad && squad.LeaderId == unitId:
                    SquadAlerts.Resume(simulation, squad, unit);
                    break;
                case ResumeMovement:
                    break; // the unit lost command of its squad after the order was issued
                case Rejoin when simulation.SquadOf(unitId) is { } squad && !squad.IsFollowing(unitId):
                    CombatQueries.EndOrder(unit);
                    StandDown(unit);
                    squad.Rejoin(unitId);
                    break;
                case Rejoin:
                    break; // already following
                case Watch watch:
                    CombatQueries.EndOrder(unit); // stops, as with Stop
                    StandDown(unit);
                    unit.WatchDirection = watch.Direction.Normalized();
                    unit.HoldsWatch = true;
                    break;
                default:
                    CombatQueries.EndOrder(unit);
                    StandDown(unit);
                    unit.CurrentOrder = order is Stop ? null : order;
                    if (order is MoveTo or MoveThroughCorridor && simulation.SquadOf(unitId) is { } led && led.LeaderId == unitId)
                    {
                        SquadAlerts.Resume(simulation, led, unit, newOrder: order); // a new movement order ends a halt (S5a1)
                        led.StartAfresh();
                        led.NewMovementOrder(order, unitId);
                        SquadSystem.Command(simulation, led, unit, order); // the leader briefs its squad (backlog 0004, S2b)
                    }

                    break;
            }
        }

        _pendingOrders.Clear();
    }

    /// <summary>
    /// A new order for the unit itself ends what it did on knowing of an enemy (backlog 0004 S5a1): it no longer stands
    /// still for it nor watches it.
    /// </summary>
    private static void StandDown(Unit unit)
    {
        unit.IsAlerted = false;
        unit.WatchTarget = null;
        unit.HoldsWatch = false;
    }
}
