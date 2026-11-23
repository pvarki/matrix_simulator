using Twin.Sim.Combat;
using Twin.Sim.Command;
using Twin.Sim.Movement;
using Twin.Sim.Squads;
using Twin.Testing;

namespace Twin.Sim.Tests.Core;

/// <summary>
/// The simulation's defaults equal the spec values the tests use. Scenario tests pass the Spec holders explicitly, so a
/// default changed without a spec decision would pass every scenario test while the shipped simulation runs on
/// unreviewed numbers. Each Spec holder states every value itself, so each comparison has two independent sides.
/// <see cref="WeaponProfile.MachineGun"/> and <see cref="WeaponProfile.HeavyMachineGun"/> are guarded elsewhere.
/// </summary>
public class SpecDefaultsTests
{
    [Fact]
    public void CombatRules_defaults_equal_backlog_spec() =>
        Assert.Equal(SpecSpotting.Rules, CombatRules.Default);

    [Fact]
    public void SuppressionRules_defaults_equal_backlog_spec() =>
        Assert.Equal(SpecSuppression.Rules, SuppressionRules.Default);

    [Fact]
    public void ObservationRules_defaults_equal_backlog_spec() =>
        Assert.Equal(SpecSpotting.Observation, ObservationRules.Default);

    [Fact]
    public void Doctrine_defaults_equal_backlog_spec() =>
        Assert.Equal(SpecShooting.Rules.Doctrine, Doctrine.Default);

    [Fact]
    public void ContactDrillRules_defaults_equal_backlog_spec() =>
        Assert.Equal(SpecSuppression.Drill, ContactDrillRules.Default);

    [Fact]
    public void SquadDoctrine_defaults_equal_backlog_spec() =>
        Assert.Equal(SpecSquad.Doctrine, SquadDoctrine.Default);

    [Fact]
    public void Infantry_movement_defaults_equal_spec_v01() =>
        Assert.Equal(SpecV01.Infantry, MovementProfile.Infantry);

    [Fact]
    public void Rifle_defaults_equal_backlog_spec() =>
        Assert.Equal(SpecShooting.Rifle, WeaponProfile.Rifle);

    /// <summary>Only the threshold: spec v0.1 has no value for the permission timeout.</summary>
    [Fact]
    public void Permission_threshold_default_equals_spec_v01() =>
        Assert.Equal(SpecV01.PermissionThreshold, PermissionRules.Default.Threshold);
}
