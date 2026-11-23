using System.Globalization;
using Twin.Sim.Geometry;

namespace Twin.Sim.Tests.Geometry;

public class VectorTests
{
    [Fact]
    public void Distance_is_euclidean()
    {
        Assert.Equal(5, new Vec2(0, 0).DistanceTo(new Vec2(3, 4)));
        Assert.Equal(3, new Vec3(1, 2, 2).Length);
    }

    [Fact]
    public void Normalizing_zero_vector_gives_zero_instead_of_NaN()
    {
        Assert.Equal(Vec2.Zero, Vec2.Zero.Normalized());
    }

    [Fact]
    public void Normalized_vector_has_unit_length()
    {
        Assert.Equal(1, new Vec2(3, -7).Normalized().Length, precision: 12);
    }

    [Fact]
    public void Vec2_and_Vec3_print_with_a_decimal_point_in_any_culture()
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fi-FI");

            Assert.Equal("(600.5, 550.25)", new Vec2(600.5, 550.25).ToString());
            Assert.Equal("(600.5, 550.25, 1.125)", new Vec3(600.5, 550.25, 1.125).ToString());
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }
}
