using VisualizeScene.Core;

namespace VisualizeScene.Rendering.Skia;

public sealed partial class GraphRenderer
{
    private static bool NodeGeometryChanged(GraphIndex previous, GraphSnapshot next)
    {
        if (previous.Nodes.Count != next.Nodes.Length) return true;
        foreach (var node in next.Nodes)
        {
            if (!previous.Nodes.TryGetValue(node.Id, out var old)) return true;
            if (ReferenceEquals(old, node)) continue;
            if (old.Bounds != node.Bounds || old.Shape != node.Shape || old.ParentId != node.ParentId ||
                old.IsGroup != node.IsGroup || old.Collapsed != node.Collapsed) return true;
        }
        return false;
    }
    private static bool EdgeGeometryChanged(GraphIndex previous, GraphSnapshot next)
    {
        if (previous.Edges.Count != next.Edges.Length) return true;
        foreach (var edge in next.Edges)
            if (!previous.Edges.TryGetValue(edge.Id, out var old) || !SameEdgeGeometry(old, edge)) return true;
        return false;
    }
    private static bool SameEdgeGeometry(GraphEdge a, GraphEdge b)
        => ReferenceEquals(a, b) || a.SourceId == b.SourceId && a.TargetId == b.TargetId &&
            a.SourcePortId == b.SourcePortId && a.TargetPortId == b.TargetPortId && a.Routing == b.Routing &&
            a.Bends.AsSpan().SequenceEqual(b.Bends.AsSpan());
}
