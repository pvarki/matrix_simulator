using Twin.Sim.Geometry;
using Twin.Sim.Orders;

namespace Twin.Sim;

/// <summary>
/// FNV-1a over the exact bit patterns of the state, for <see cref="Simulation.ComputeStateHash"/>. Doubles go in by their
/// bits, bools as 0/1, enums as long; strings end with a terminator; a nullable value goes in as a presence flag and then
/// the value; a collection goes in as its count and then its items, so adjacent collections never run into each other.
/// Each type adds its own fields (an <c>AddTo(StateHasher)</c> next to them). Nothing here depends on the culture.
/// </summary>
internal sealed class StateHasher
{
    public ulong Value { get; private set; } = 14695981039346656037UL;

    public void Add(long value)
    {
        for (var i = 0; i < 8; i++)
        {
            AddByte((byte)(value >> (i * 8)));
        }
    }

    public void Add(long? value)
    {
        Add(value.HasValue);
        Add(value.GetValueOrDefault());
    }

    public void Add(bool value) => Add(value ? 1L : 0L);

    public void Add(double value) => Add(BitConverter.DoubleToInt64Bits(value));

    public void Add(double? value)
    {
        Add(value.HasValue);
        Add(value.GetValueOrDefault());
    }

    public void Add(Vec2 v)
    {
        Add(v.X);
        Add(v.Y);
    }

    public void Add(Vec2? v)
    {
        Add(v.HasValue);
        Add(v.GetValueOrDefault());
    }

    public void Add(Vec3 v)
    {
        Add(v.X);
        Add(v.Y);
        Add(v.Z);
    }

    public void Add(string? value)
    {
        Add(value is not null);
        foreach (var c in value ?? string.Empty)
        {
            Add(c);
        }

        AddByte(0);
    }

    public void Add(Order? order)
    {
        Add(order is not null);
        order?.AddTo(this);
    }

    /// <summary>Adds the count of <paramref name="items"/>, then each item with <paramref name="add"/>.</summary>
    public void AddAll<T>(IReadOnlyCollection<T> items, Action<T> add)
    {
        Add(items.Count);
        foreach (var item in items)
        {
            add(item);
        }
    }

    private void AddByte(byte b)
    {
        unchecked
        {
            Value = (Value ^ b) * 1099511628211UL;
        }
    }
}
