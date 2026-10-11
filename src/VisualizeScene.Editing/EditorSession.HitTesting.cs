using VisualizeScene.Core;

namespace VisualizeScene.Editing;

public readonly record struct HitTestStatistics(int ExaminedEntries, int CandidateItems);

public sealed partial class EditorSession
{
    private readonly List<string> hitNodes = [], hitEdges = [], hitPorts = [], hitLabels = [], hitHandles = [];
    private ReadOnlySelection? handleSelection;
    private readonly Dictionary<string, int> handleOrder = new(StringComparer.Ordinal);
    public int LastHandleCandidateCount { get; private set; }
    private void QuerySelectedHandles()
    {
        // The private published selection is immutable; ranks remain valid until its identity changes.
        if (!ReferenceEquals(handleSelection, readSelection))
        {
            handleOrder.Clear(); var rank = 0;
            foreach (var id in readSelection) handleOrder.Add(id, rank++);
            handleSelection = readSelection;
        }
        hitHandles.Clear();
        foreach (var id in hitNodes) if (handleOrder.ContainsKey(id)) hitHandles.Add(id);
        foreach (var id in hitEdges) if (handleOrder.ContainsKey(id)) hitHandles.Add(id);
        hitHandles.Sort((a, b) => handleOrder[a].CompareTo(handleOrder[b]));
        LastHandleCandidateCount = hitHandles.Count;
    }
    private Dictionary<string, int> nodeOrder = [], portOrder = [], labelOrder = [];
    public HitTestStatistics LastHitStatistics { get; private set; }
    private void ReindexHitOrder()
    {
        nodeOrder = Document.Snapshot.Nodes.Select((n, i) => (n.Id, i)).ToDictionary(p => p.Id, p => p.i, StringComparer.Ordinal);
        portOrder = Document.Snapshot.Ports.Select((p, i) => (p.Id, i)).ToDictionary(p => p.Id, p => p.i, StringComparer.Ordinal);
        labelOrder = Document.Snapshot.Labels.Select((l, i) => (l.Id, i)).ToDictionary(p => p.Id, p => p.i, StringComparer.Ordinal);
    }
    private void QueryHitCandidates(PointD world, double tolerance)
    {
        LastHandleCandidateCount = 0;
        hitNodes.Clear(); hitEdges.Clear(); hitPorts.Clear(); hitLabels.Clear();
        var point = new RectD(world.X, world.Y, 0, 0); var expanded = point.Inflate(tolerance);
        var n = nodeIndex.Query(expanded, hitNodes); var e = edgeIndex.Query(expanded, hitEdges);
        var p = portIndex.Query(expanded, hitPorts);
        var l = Viewport.Zoom >= .25 ? labelIndex.Query(point, hitLabels) : default;
        LastHitStatistics = new(n.ExaminedEntries + e.ExaminedEntries + p.ExaminedEntries + l.ExaminedEntries,
            hitNodes.Count + hitEdges.Count + hitPorts.Count + hitLabels.Count);
    }
    private string? HitPort(PointD world, double tolerance)
    {
        string? best = null; var order = int.MaxValue;
        foreach (var id in hitPorts)
            if (portOrder[id] < order && PointD.Distance(world, index.PortLocation(id)) < tolerance)
            { best = id; order = portOrder[id]; }
        return best;
    }
    private string? HitLabel()
    {
        string? best = null; var order = -1;
        foreach (var id in hitLabels) if (labelOrder[id] > order) { best = id; order = labelOrder[id]; }
        return best;
    }
    private GraphNode? HitNode(PointD world, bool groupOnly)
    {
        GraphNode? best = null; var order = -1;
        foreach (var id in hitNodes)
        {
            var node = index.Nodes[id];
            if ((node.IsGroup && !node.Collapsed) != groupOnly || nodeOrder[id] <= order || !Geometry.HitNode(node, world)) continue;
            if (groupOnly && !(world.Y < node.Bounds.Y + 25 || world.X < node.Bounds.X + 6 || world.X > node.Bounds.Right - 6 || world.Y > node.Bounds.Bottom - 6)) continue;
            best = node; order = nodeOrder[id];
        }
        return best;
    }
    public HitResult Hit(PointD world, bool handles = true)
    {
        LastHitStatistics = default;
        var tolerance = 7 / Viewport.Zoom;
        QueryHitCandidates(world, tolerance);
        if (handles)
        {
            QuerySelectedHandles();
            foreach (var id in hitHandles)
            {
                if (index.Nodes.TryGetValue(id, out var node) && !node.Locked && index.IsVisible(id))
                {
                    var b = node.Bounds; PointD[] points = [new(b.X, b.Y), new(b.Right, b.Y), new(b.Right, b.Bottom), new(b.X, b.Bottom)];
                    for (var i = 0; i < points.Length; i++) if (PointD.Distance(world, points[i]) <= tolerance) return new(HitKind.ResizeHandle, id, i);
                }
                if (index.Edges.TryGetValue(id, out var edge) && paths.TryGetValue(id, out var path))
                {
                    if (index.IsVisible(edge.SourceId) && !index.Nodes[edge.SourceId].Locked && PointD.Distance(world, path[0]) <= tolerance) return new(HitKind.SourceHandle, id);
                    if (index.IsVisible(edge.TargetId) && !index.Nodes[edge.TargetId].Locked && PointD.Distance(world, path[^1]) <= tolerance) return new(HitKind.TargetHandle, id);
                    if (!edge.Bends.IsDefault)
                        for (var i = 0; i < edge.Bends.Length; i++) if (PointD.Distance(world, edge.Bends[i]) <= tolerance) return new(HitKind.Bend, id, i);
                }
            }
        }
        var port = HitPort(world, tolerance); if (port is not null) return new(HitKind.Port, port);
        var label = HitLabel(); if (label is not null) return new(HitKind.Label, label);
        var normal = HitNode(world, groupOnly: false); if (normal is not null) return new(HitKind.Node, normal.Id);
        foreach (var id in hitEdges)
        {
            var points = paths[id];
            for (var i = 1; i < points.Count; i++)
                if (Geometry.DistanceToSegment(world, points[i - 1], points[i]) <= tolerance) return new(HitKind.Edge, id);
        }
        var group = HitNode(world, groupOnly: true);
        return group is null ? new(HitKind.None) : new(HitKind.Node, group.Id);
    }
}
