namespace VisualizeScene.Core;

/// <summary>Immutable packed bounding-volume hierarchy. Construct once per graph revision.</summary>
public sealed class SpatialIndex<T>
{
    private sealed record Entry(RectD Bounds, T Item);
    private sealed class Branch
    {
        public required RectD Bounds;
        public Branch? Left, Right;
        public Entry[]? Entries;
    }
    private readonly Branch? root;
    public SpatialIndex(IEnumerable<(RectD Bounds, T Item)> values)
    {
        var entries = values.Select(v => new Entry(v.Bounds, v.Item)).ToArray();
        if (entries.Any(e => !e.Bounds.IsValid)) throw new ArgumentException("Invalid index bounds.");
        root = entries.Length == 0 ? null : Build(entries, 0, entries.Length);
    }
    private static Branch Build(Entry[] entries, int start, int length)
    {
        var bounds = entries[start].Bounds;
        for (var i = start + 1; i < start + length; i++) bounds = RectD.Union(bounds, entries[i].Bounds);
        var result = new Branch { Bounds = bounds };
        if (length <= 12) { result.Entries = entries[start..(start + length)]; return result; }
        var horizontal = bounds.Width >= bounds.Height;
        Array.Sort(entries, start, length, Comparer<Entry>.Create((a, b) => (horizontal ? a.Bounds.Center.X : a.Bounds.Center.Y).CompareTo(horizontal ? b.Bounds.Center.X : b.Bounds.Center.Y)));
        var half = length / 2;
        result.Left = Build(entries, start, half); result.Right = Build(entries, start + half, length - half);
        return result;
    }
    public IEnumerable<T> Query(RectD bounds)
    {
        if (root is null) yield break;
        var stack = new Stack<Branch>(); stack.Push(root);
        while (stack.TryPop(out var branch))
        {
            if (!branch.Bounds.Intersects(bounds)) continue;
            if (branch.Entries is not null)
                foreach (var e in branch.Entries) { if (e.Bounds.Intersects(bounds)) yield return e.Item; }
            else { stack.Push(branch.Left!); stack.Push(branch.Right!); }
        }
    }
}
