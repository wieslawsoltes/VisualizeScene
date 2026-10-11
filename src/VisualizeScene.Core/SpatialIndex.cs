namespace VisualizeScene.Core;

public readonly record struct SpatialQueryStatistics(int VisitedBranches, int ExaminedEntries, int Results);

/// <summary>Immutable packed bounding-volume hierarchy. Build uses bounded median partitioning, not a full sort at every level.</summary>
public sealed class SpatialIndex<T>
{
    private sealed record Entry(RectD Bounds, T Item, double CenterX, double CenterY);
    private readonly record struct Branch(RectD Bounds, int Left, int Right, int Start, int Count);
    private readonly Entry[] entries;
    private readonly Branch[] branches;
    private static readonly IComparer<Entry> xComparer = Comparer<Entry>.Create(static (a, b) => a.CenterX.CompareTo(b.CenterX));
    private static readonly IComparer<Entry> yComparer = Comparer<Entry>.Create(static (a, b) => a.CenterY.CompareTo(b.CenterY));
    public SpatialIndex(IEnumerable<(RectD Bounds, T Item)> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        entries = values.Select(v => new Entry(v.Bounds, v.Item, v.Bounds.X + v.Bounds.Width / 2, v.Bounds.Y + v.Bounds.Height / 2)).ToArray();
        foreach (var entry in entries)
            if (!entry.Bounds.IsValid || !double.IsFinite(entry.Bounds.Right) || !double.IsFinite(entry.Bounds.Bottom))
                throw new ArgumentException("Invalid index bounds.", nameof(values));
        if (entries.Length == 0) { branches = []; return; }
        var nodes = new List<Branch>(Math.Max(1, entries.Length / 4));
        Build(nodes, 0, entries.Length); branches = nodes.ToArray();
    }
    private int Build(List<Branch> nodes, int start, int length)
    {
        var bounds = entries[start].Bounds;
        for (var i = start + 1; i < start + length; i++) bounds = RectD.Union(bounds, entries[i].Bounds);
        if (!bounds.IsValid || !double.IsFinite(bounds.Right) || !double.IsFinite(bounds.Bottom)) throw new ArgumentException("The index extent exceeds the finite coordinate range.");
        var index = nodes.Count; nodes.Add(default);
        if (length <= 12) { nodes[index] = new(bounds, -1, -1, start, length); return index; }
        var half = length / 2;
        Partition(start, start + length - 1, start + half, bounds.Width >= bounds.Height);
        var left = Build(nodes, start, half); var right = Build(nodes, start + half, length - half);
        nodes[index] = new(bounds, left, right, 0, 0); return index;
    }
    private void Partition(int left, int right, int median, bool horizontal)
    {
        static double Coordinate(in Entry entry, bool x) => x ? entry.CenterX : entry.CenterY;
        var budget = 2 * (System.Numerics.BitOperations.Log2((uint)(right - left + 1)) + 1);
        while (left < right)
        {
            // An introspective fallback bounds adversarial partitions. Equal-coordinate inputs terminate in one partition.
            if (budget-- == 0) { Array.Sort(entries, left, right - left + 1, horizontal ? xComparer : yComparer); return; }
            var a = Coordinate(entries[left], horizontal); var b = Coordinate(entries[(left + right) / 2], horizontal); var c = Coordinate(entries[right], horizontal);
            var pivot = Math.Max(Math.Min(a, b), Math.Min(Math.Max(a, b), c));
            var lower = left; var upper = right; var i = left;
            while (i <= upper)
            {
                var value = Coordinate(entries[i], horizontal);
                if (value < pivot) { if (i != lower) (entries[i], entries[lower]) = (entries[lower], entries[i]); i++; lower++; }
                else if (value > pivot) { if (i != upper) (entries[i], entries[upper]) = (entries[upper], entries[i]); upper--; }
                else i++;
            }
            if (median < lower) right = lower - 1;
            else if (median > upper) left = upper + 1;
            else return;
        }
    }
    /// <summary>Appends to caller-owned storage. Warm traversal allocates no iterator, stack or per-leaf array.</summary>
    public SpatialQueryStatistics Query(RectD bounds, List<T> destination)
    {
        ArgumentNullException.ThrowIfNull(destination); Geometry.RectangleContainsRectangle(bounds, bounds);
        var visited = 0; var examined = 0; var count = 0;
        if (branches.Length > 0) Collect(0, bounds, destination, ref visited, ref examined, ref count);
        return new(visited, examined, count);
    }
    private void Collect(int node, RectD bounds, List<T> result, ref int visited, ref int examined, ref int count)
    {
        ref readonly var branch = ref branches[node]; visited++;
        if (!branch.Bounds.Intersects(bounds)) return;
        if (branch.Count > 0)
        {
            for (var i = branch.Start; i < branch.Start + branch.Count; i++)
            {
                examined++; ref readonly var entry = ref entries[i];
                if (!entry.Bounds.Intersects(bounds)) continue;
                result.Add(entry.Item); count++;
            }
        }
        else
        {
            Collect(branch.Right, bounds, result, ref visited, ref examined, ref count);
            Collect(branch.Left, bounds, result, ref visited, ref examined, ref count);
        }
    }
    /// <summary>Traversal order is spatial, not painter order. Both query overloads use the same right-before-left order.</summary>
    public IEnumerable<T> Query(RectD bounds)
    {
        Geometry.RectangleContainsRectangle(bounds, bounds); return Enumerate(bounds);
    }
    private IEnumerable<T> Enumerate(RectD bounds)
    {
        if (branches.Length == 0) yield break;
        var stack = new Stack<int>(); stack.Push(0);
        while (stack.TryPop(out var node))
        {
            var branch = branches[node];
            if (!branch.Bounds.Intersects(bounds)) continue;
            if (branch.Count > 0)
            {
                for (var i = branch.Start; i < branch.Start + branch.Count; i++)
                    if (entries[i].Bounds.Intersects(bounds)) yield return entries[i].Item;
            }
            else { stack.Push(branch.Left); stack.Push(branch.Right); }
        }
    }
}
