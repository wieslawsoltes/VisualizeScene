using VisualizeScene.Core;

namespace VisualizeScene.Editing;

public enum NavigationDirection { Left, Right, Up, Down }
public sealed record NavigationTarget(string Id, RectD Bounds);

/// <summary>Immutable spatial keyboard navigation with deterministic reading order and beam preference.</summary>
public sealed class GraphNavigationIndex
{
    private readonly NavigationTarget[] targets;
    private readonly Dictionary<string, NavigationTarget> byId;
    public GraphNavigationIndex(IEnumerable<NavigationTarget> targets)
    {
        ArgumentNullException.ThrowIfNull(targets);
        this.targets = targets.ToArray(); var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var target in this.targets)
        {
            if (target is null || string.IsNullOrWhiteSpace(target.Id) || !ids.Add(target.Id))
                throw new ArgumentException("Navigation targets require unique, nonempty IDs.", nameof(targets));
            Geometry.RectangleContainsRectangle(target.Bounds, target.Bounds);
        }
        this.targets = this.targets.OrderBy(t => t.Bounds.Y).ThenBy(t => t.Bounds.X).ThenBy(t => t.Id, StringComparer.Ordinal).ToArray();
        byId = this.targets.ToDictionary(t => t.Id, StringComparer.Ordinal);
    }
    public string? Boundary(bool last = false) => targets.Length == 0 ? null : (last ? targets[^1] : targets[0]).Id;
    public string? Next(NavigationDirection direction, string? fromId, PointD fallback)
    {
        if (!Enum.IsDefined(direction)) throw new ArgumentOutOfRangeException(nameof(direction));
        if (!fallback.IsFinite) throw new ArgumentOutOfRangeException(nameof(fallback));
        if (fromId is null || !byId.TryGetValue(fromId, out var from))
        {
            NavigationTarget? nearest = null; var nearestDistance = double.PositiveInfinity;
            foreach (var target in targets)
            {
                var distance = Distance(target.Bounds.Center, fallback);
                if (nearest is null || distance < nearestDistance || distance == nearestDistance && StringComparer.Ordinal.Compare(target.Id, nearest.Id) < 0)
                { nearest = target; nearestDistance = distance; }
            }
            return nearest?.Id;
        }
        var horizontal = direction is NavigationDirection.Left or NavigationDirection.Right;
        var positive = direction is NavigationDirection.Right or NavigationDirection.Down;
        var source = from.Bounds; var center = source.Center;
        (int Beam, double Cost, double Distance, string Id)? best = null;
        foreach (var target in targets)
        {
            if (target.Id == from.Id) continue;
            var b = target.Bounds; var delta = horizontal ? b.Center.X - center.X : b.Center.Y - center.Y;
            if (positive ? delta <= 0 : delta >= 0) continue;
            var crossGap = horizontal ? Math.Max(0, Math.Max(source.Y - b.Bottom, b.Y - source.Bottom))
                : Math.Max(0, Math.Max(source.X - b.Right, b.X - source.Right));
            var primaryGap = horizontal ? positive ? b.X - source.Right : source.X - b.Right
                : positive ? b.Y - source.Bottom : source.Y - b.Bottom;
            var score = (Beam: crossGap == 0 ? 0 : 1, Cost: Math.Max(0, primaryGap) + crossGap * 2,
                Distance: Distance(center, b.Center), Id: target.Id);
            if (best is null || Compare(score, best.Value) < 0) best = score;
        }
        return best?.Id;
    }
    private static int Compare((int Beam, double Cost, double Distance, string Id) a, (int Beam, double Cost, double Distance, string Id) b)
    {
        var c = a.Beam.CompareTo(b.Beam); if (c != 0) return c;
        c = a.Cost.CompareTo(b.Cost); if (c != 0) return c;
        c = a.Distance.CompareTo(b.Distance); return c != 0 ? c : StringComparer.Ordinal.Compare(a.Id, b.Id);
    }
    private static double Distance(PointD a, PointD b)
    {
        var x = Math.Abs(a.X - b.X); var y = Math.Abs(a.Y - b.Y); var m = Math.Max(x, y);
        return m == 0 || double.IsInfinity(m) ? m : m * Math.Sqrt((x / m) * (x / m) + (y / m) * (y / m));
    }
}
