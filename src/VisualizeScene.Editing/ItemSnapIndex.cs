using System.Collections.Immutable;
using VisualizeScene.Core;

namespace VisualizeScene.Editing;

public sealed record ItemSnapResult(PointD Correction, ImmutableArray<AlignmentGuide> Guides);

/// <summary>Immutable, per-revision axis indexes. Range queries use screen-space tolerances converted to world units.</summary>
public sealed class ItemSnapIndex
{
    private sealed record Line(double Position, string NodeId, RectD Bounds, int Anchor);
    private sealed record Match(Line Line, double Delta, int Anchor);
    private readonly Line[] horizontal, vertical;
    public ItemSnapIndex(GraphSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot); GraphValidation.Validate(snapshot); var index = snapshot.Index();
        // Expanded group frames are containers, not alignment targets for the nodes inside them.
        var nodes = snapshot.Nodes.Where(n => index.IsVisible(n.Id) && (!n.IsGroup || n.Collapsed)).ToArray();
        vertical = Build(nodes, true); horizontal = Build(nodes, false);
    }
    private static double[] Anchors(RectD bounds, bool x) => x
        ? [bounds.X, bounds.Center.X, bounds.Right] : [bounds.Y, bounds.Center.Y, bounds.Bottom];
    private static Line[] Build(IEnumerable<GraphNode> nodes, bool x) => nodes.SelectMany(n => Anchors(n.Bounds, x)
        .Select((p, i) => new Line(p, n.Id, n.Bounds, i))).OrderBy(l => l.Position).ThenBy(l => l.NodeId, StringComparer.Ordinal).ThenBy(l => l.Anchor).ToArray();

    /// <param name="proposed">Unsnapped geometry. A zero-size rectangle snaps a resize corner.</param>
    /// <param name="fallbackCorrection">Grid correction used independently on axes without a nearby item.</param>
    public ItemSnapResult Snap(RectD proposed, PointD fallbackCorrection, IReadOnlySet<string> excluded,
        double zoom, double screenDistance = 6)
    {
        ArgumentNullException.ThrowIfNull(excluded);
        if (!proposed.IsValid || !double.IsFinite(proposed.Right) || !double.IsFinite(proposed.Bottom) || !fallbackCorrection.IsFinite ||
            !double.IsFinite(zoom) || zoom <= 0 || !double.IsFinite(screenDistance) || screenDistance < 0)
            throw new ArgumentOutOfRangeException(nameof(proposed), "Snap geometry, zoom and distance must be finite and valid.");
        var tolerance = screenDistance / zoom;
        if (!double.IsFinite(tolerance)) throw new ArgumentOutOfRangeException(nameof(zoom));
        var x = Nearest(vertical, Anchors(proposed, true), excluded, tolerance);
        var y = Nearest(horizontal, Anchors(proposed, false), excluded, tolerance);
        var correction = new PointD(x?.Delta ?? fallbackCorrection.X, y?.Delta ?? fallbackCorrection.Y);
        var final = proposed.Move(correction); var guides = ImmutableArray.CreateBuilder<AlignmentGuide>(2);
        if (x is not null) guides.Add(new(new(x.Line.Position, Math.Min(final.Y, x.Line.Bounds.Y)),
            new(x.Line.Position, Math.Max(final.Bottom, x.Line.Bounds.Bottom)), x.Line.NodeId));
        if (y is not null) guides.Add(new(new(Math.Min(final.X, y.Line.Bounds.X), y.Line.Position),
            new(Math.Max(final.Right, y.Line.Bounds.Right), y.Line.Position), y.Line.NodeId));
        return new(correction, guides.ToImmutable());
    }
    private static Match? Nearest(Line[] lines, double[] anchors, IReadOnlySet<string> excluded, double tolerance)
    {
        Match? best = null;
        for (var a = 0; a < anchors.Length; a++)
        {
            var low = 0; var high = lines.Length;
            while (low < high)
            {
                var middle = low + (high - low) / 2;
                if (lines[middle].Position < anchors[a] - tolerance) low = middle + 1; else high = middle;
            }
            for (var i = low; i < lines.Length && lines[i].Position <= anchors[a] + tolerance; i++)
            {
                var line = lines[i]; if (excluded.Contains(line.NodeId)) continue;
                var match = new Match(line, line.Position - anchors[a], a);
                if (best is null || Compare(match, best) < 0) best = match;
            }
        }
        return best;
    }
    private static int Compare(Match a, Match b)
    {
        var order = Math.Abs(a.Delta).CompareTo(Math.Abs(b.Delta)); if (order != 0) return order;
        order = (a.Anchor == a.Line.Anchor ? 0 : 1).CompareTo(b.Anchor == b.Line.Anchor ? 0 : 1); if (order != 0) return order;
        order = StringComparer.Ordinal.Compare(a.Line.NodeId, b.Line.NodeId); if (order != 0) return order;
        order = a.Anchor.CompareTo(b.Anchor); return order != 0 ? order : a.Line.Anchor.CompareTo(b.Line.Anchor);
    }
}
