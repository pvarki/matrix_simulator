using System.Diagnostics;
using System.Globalization;
using Twin.Acceptance.Tests.Performance;
using Twin.Sim.Movement;
using Twin.Testing;
using Xunit.Abstractions;

namespace Twin.Acceptance.Tests.SmokeTest1;

/// <summary>
/// P1: pathfinding time is measured and recorded on the 1 x 1 km (st1) and 2 x 2 km (st1-2km) areas, and the faster of
/// three searches must stay within a generous budget, so that a large slowdown fails the test. See the output with
/// <c>./dev smoke</c>. Wall-clock time is fine here: this is a measurement in a test, not simulation logic.
/// </summary>
[Collection(TimedCollection.Name)]
public class PathfindingPerformanceTests(ITestOutputHelper output)
{
    private const int Runs = 3;

    // ≈5× the slower of the Debug and Release times measured on 2026-11-23, rounded up: CI machines are slower.
    private const double BudgetSt1Ms = 400;
    private const double BudgetSt12kmMs = 1200;

    [Theory]
    [InlineData("st1", BudgetSt1Ms)]
    [InlineData("st1-2km", BudgetSt12kmMs)]
    public void P1_pathfinding_time_is_measured_and_recorded(string area, double budgetMs)
    {
        var scenario = ScenarioOnDisk.Load(area).Definition;
        var world = scenario.World.World;
        var to = scenario.Corridor.Centerline[^1];
        var respecting = new PathOptions(Corridor: scenario.Corridor);
        var reference = respecting with { IgnoreHardBoundaries = true };
        Pathfinder.FindPath(world, scenario.Profile, scenario.Start, to, respecting); // warm-up: JIT, caches

        var respectingTime = Measure(() => Pathfinder.FindPath(world, scenario.Profile, scenario.Start, to, respecting), out var respectingPath);
        var referenceTime = Measure(() => Pathfinder.FindPath(world, scenario.Profile, scenario.Start, to, reference), out var referencePath);

        var size = $"{world.Bounds.MaxX - world.Bounds.MinX:0} x {world.Bounds.MaxY - world.Bounds.MinY:0} m";
        output.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"P1 {area} ({size}): respecting hard boundaries {respectingTime.TotalMilliseconds:0.0} ms (found {respectingPath.Found}), " +
            $"reference {referenceTime.TotalMilliseconds:0.0} ms (found {referencePath.Found})"));
        Assert.True(referencePath.Found, "the reference route (hard boundaries ignored) should exist in the scenario");
        var slower = Math.Max(respectingTime.TotalMilliseconds, referenceTime.TotalMilliseconds);
        Assert.True(slower <= budgetMs,
            string.Create(CultureInfo.InvariantCulture, $"P1 {area}: {slower:0.0} ms is over the budget of {budgetMs} ms"));
    }

    /// <summary>The fastest of <see cref="Runs"/> searches: the least disturbed by other work on the machine.</summary>
    private static TimeSpan Measure(Func<PathResult> find, out PathResult result)
    {
        var fastest = TimeSpan.MaxValue;
        result = null!;
        for (var i = 0; i < Runs; i++)
        {
            var started = Stopwatch.GetTimestamp();
            result = find();
            var elapsed = Stopwatch.GetElapsedTime(started);
            fastest = elapsed < fastest ? elapsed : fastest;
        }

        return fastest;
    }
}
