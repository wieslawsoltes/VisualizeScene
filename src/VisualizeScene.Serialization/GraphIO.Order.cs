using System.Collections.Immutable;
using System.Text.Json;

namespace VisualizeScene.Serialization;

public static partial class GraphIO
{
    // GraphML nesting determines traversal order, not the drawing order of our immutable arrays.
    // Optional native metadata must be a complete permutation, never a way to inject or drop items.
    private static ImmutableArray<T> RestoreOrder<T>(ImmutableArray<T> items, string? json, Func<T, string> identity)
    {
        if (json is null) return items;
        var ids = JsonSerializer.Deserialize(json, GraphJsonContext.Default.StringArray)
            ?? throw new FormatException("Drawing order must be an array of item IDs.");
        if (ids.Length != items.Length) throw new FormatException("Drawing order must list every item exactly once.");
        var remaining = items.ToDictionary(identity, StringComparer.Ordinal);
        var ordered = ImmutableArray.CreateBuilder<T>(items.Length);
        foreach (var id in ids)
        {
            if (id is null || !remaining.Remove(id, out var item)) throw new FormatException("Drawing order contains an unknown or duplicate item.");
            ordered.Add(item);
        }
        return ordered.MoveToImmutable();
    }
}
