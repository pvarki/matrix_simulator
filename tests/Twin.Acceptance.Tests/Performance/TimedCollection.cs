namespace Twin.Acceptance.Tests.Performance;

/// <summary>
/// The tests that measure wall-clock time against a budget. They run one after another, never beside each other, so
/// one does not slow another down. This only orders them within this assembly: the other test assemblies may still run
/// at the same time, so the budgets keep a wide margin (see each budget's comment).
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class TimedCollection
{
    /// <summary>The collection's name, for <c>[Collection(TimedCollection.Name)]</c>.</summary>
    public const string Name = "Timed";
}
