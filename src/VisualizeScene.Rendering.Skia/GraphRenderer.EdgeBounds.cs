using VisualizeScene.Core;

namespace VisualizeScene.Rendering.Skia;

public sealed partial class GraphRenderer
{
    private SpatialIndex<string> captionIndex = new([]);
    private readonly List<string> captionCandidates = [];
    private readonly HashSet<string> visibleEdgeSet = new(StringComparer.Ordinal);
    public int CaptionIndexBuilds { get; private set; }
    private static bool SameEdgeBounds(GraphEdge a, GraphEdge b)
    {
        // Legacy markers fit the fixed route margin whether directed or not.
        if (a.Style is null && b.Style is null) return true;
        var x = a.Style ?? EdgeStyle.Default; var y = b.Style ?? EdgeStyle.Default;
        return (a.Style is null) == (b.Style is null) && x.Width == y.Width &&
            x.ResolveArrow(true, a.Directed) == y.ResolveArrow(true, b.Directed) &&
            x.ResolveArrow(false, a.Directed) == y.ResolveArrow(false, b.Directed);
    }
    private RectD EdgeVisualBounds(EdgePicture picture)
    {
        if (picture.Edge.Style is not null) return EdgeGeometry.Bounds(picture.Edge, picture.Points).Inflate(14);
        var path = picture.Points; var bounds = new RectD(path[0].X, path[0].Y, 0, 0);
        for (var i = 1; i < path.Count; i++) bounds = RectD.Union(bounds, new(path[i].X, path[i].Y, 0, 0));
        return bounds.Inflate(14);
    }
    private void PrepareEdgeCaptions(GraphSnapshot snapshot, GraphSnapshot? previous, GraphIndex? previousIndex, bool geometryChanged)
    {
        var changed = previous is null || geometryChanged;
        if (!changed && previous!.Edges != snapshot.Edges)
            foreach (var edge in snapshot.Edges)
                if (!previousIndex!.Edges.TryGetValue(edge.Id, out var old) || old.Text != edge.Text) { changed = true; break; }
        if (!changed) return;
        var items = new List<(RectD Bounds, string Id)>();
        foreach (var edge in snapshot.Edges)
        {
            if (edge.Text.Length == 0 || !edges.TryGetValue(edge.Id, out var picture)) continue;
            var size = MeasureLabel(new("", edge.Id, edge.Text, new(), 11));
            // DrawText wraps at 220 and displays at most three lines in its 50-unit box.
            // Conservative bounds include wrapped lines and background, not only the unwrapped metrics.
            var center = Geometry.PointAtFraction(picture.Points, .5) + new PointD(0, -9);
            var width = Math.Min(228, size.X); var height = Math.Max(size.Y, 50);
            items.Add((new(center.X - width / 2, center.Y - height / 2, width, height), edge.Id));
        }
        captionIndex = new(items); CaptionIndexBuilds++;
    }
    private int QueryEdgeCaptions(RectD view, double zoom, bool overview)
    {
        if (overview || zoom < .35) return 0;
        captionCandidates.Clear(); var stats = captionIndex.Query(view, captionCandidates);
        if (captionCandidates.Count == 0) return stats.ExaminedEntries;
        visibleEdgeSet.Clear();
        foreach (var id in visibleEdgeIds) visibleEdgeSet.Add(id);
        foreach (var id in captionCandidates) if (visibleEdgeSet.Add(id)) visibleEdgeIds.Add(id);
        return stats.ExaminedEntries;
    }
    private void ClearEdgeCaptions()
    { captionIndex = new([]); captionCandidates.Clear(); visibleEdgeSet.Clear(); }
}
