using VisualizeScene.Core;

namespace VisualizeScene.Editing;

public readonly record struct HitTestStatistics(int ExaminedEntries, int CandidateItems);

public sealed partial class EditorSession
{
    private readonly List<string> hitNodes = [], hitEdges = [], hitPorts = [], hitLabels = [];
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
        hitNodes.Clear(); hitEdges.Clear(); hitPorts.Clear(); hitLabels.Clear();
        var point = new RectD(world.X, world.Y, 0, 0); var expanded = point.Inflate(tolerance);
        var n = nodeIndex.Query(point, hitNodes); var e = edgeIndex.Query(expanded, hitEdges);
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
}
