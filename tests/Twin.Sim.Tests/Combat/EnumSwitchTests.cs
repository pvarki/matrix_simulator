using Twin.Sim.Combat;
using Twin.Sim.Movement;
using Twin.Sim.World;

namespace Twin.Sim.Tests.Combat;

/// <summary>
/// The rule switches list every enum member: an undefined value throws instead of silently counting as some default
/// member.
/// </summary>
public class EnumSwitchTests
{
    [Fact]
    public void Posture_values_throw_on_an_undefined_posture() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => CombatRules.Default.For((Posture)7));

    [Fact]
    public void Cover_throws_on_an_undefined_posture() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => CombatRules.Default.Suppression.Cover((Posture)7));

    [Fact]
    public void Suppression_values_throw_on_an_undefined_experience() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => CombatRules.Default.Suppression.For((Experience)9));

    [Fact]
    public void Fire_mode_values_throw_on_an_undefined_fire_mode() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => CombatRules.Default.Suppression.For((FireMode)5));

    [Fact]
    public void Spotting_time_throws_on_an_undefined_experience() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => CombatRules.Default.Observation.BaseSeconds((Experience)9));

    [Fact]
    public void Base_speed_throws_on_an_undefined_terrain_class() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => MovementProfile.Infantry.BaseSpeed((TerrainClass)200));

    [Fact]
    public void Water_and_building_have_speed_zero()
    {
        Assert.Equal(0, MovementProfile.Infantry.BaseSpeed(TerrainClass.Water));
        Assert.Equal(0, MovementProfile.Infantry.BaseSpeed(TerrainClass.Building));
    }
}
