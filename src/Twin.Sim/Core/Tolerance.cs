namespace Twin.Sim;

/// <summary>The shared floating-point tolerance of the simulation.</summary>
internal static class Tolerance
{
    /// <summary>
    /// The tolerance for comparing meters, seconds and fractions at simulation scale: two values closer than this are
    /// equal, and a length at most this long is zero.
    /// </summary>
    public const double Epsilon = 1e-9;
}
