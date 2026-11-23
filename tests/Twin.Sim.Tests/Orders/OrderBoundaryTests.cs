using Twin.Sim.Combat;
using Twin.Sim.Geometry;
using Twin.Sim.Orders;
using Twin.Sim.Squads;
using Twin.Sim.Systems;
using Twin.Testing;

namespace Twin.Sim.Tests.Orders;

/// <summary>
/// Orders are checked where they enter the simulation (<see cref="Simulation.Issue"/>): an undefined enum value or an
/// order type that is not meant to be issued is rejected, and the order system throws on an order type it does not handle.
/// </summary>
public class OrderBoundaryTests
{
    private static ScenarioRun SquadWithEnemy() => Scenario.ForSquad()
        .WithTerrain(TestMap.Create(widthMeters: 100, heightMeters: 60).Build())
        .WithSquad(SpecSquad.Reference(), new Vec2(21, 21))
        .WithFighter("red", new Vec2(21, 41), Side.Red, RulesOfEngagement.HoldFire)
        .Build();

    [Fact]
    public void A_posture_order_with_an_unknown_posture_is_rejected()
    {
        var sim = SquadWithEnemy();

        var error = Assert.Throws<OrderRejectedException>(() => sim.Simulation.Issue("red", new SetPosture((Posture)7)));

        Assert.Contains("Unknown posture", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_squad_posture_order_with_an_unknown_posture_is_rejected()
    {
        var sim = SquadWithEnemy();

        var error = Assert.Throws<OrderRejectedException>(
            () => sim.Simulation.Issue("rj", new SetSquadPosture((Posture)7)));

        Assert.Contains("Unknown posture", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void An_internal_command_cannot_be_issued_as_an_order()
    {
        var sim = SquadWithEnemy();

        Assert.Throws<OrderRejectedException>(() => sim.Simulation.Issue("rj", new DeployAtForestEdge()));
    }

    [Fact]
    public void The_order_system_throws_on_an_order_type_it_does_not_know()
    {
        // "red" is a fighter in no squad, so the code before the order switch (ending a drill, answering fire) does
        // nothing that could throw: the only exception expected is the switch's own.
        var sim = SquadWithEnemy();
        var orders = new OrderSystem();
        orders.Enqueue("red", new DeployAtForestEdge());

        Assert.Throws<InvalidOperationException>(() => orders.Update(sim.Simulation, sim.Simulation.Config.TickSeconds));
    }
}
