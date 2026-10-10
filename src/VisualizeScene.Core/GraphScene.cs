using System.Globalization;

namespace VisualizeScene.Core;

/// <summary>Shared visibility, label anchors and content bounds used by editing, rendering and export.</summary>
public static class GraphScene
{
    public static bool IsEdgeVisible(GraphIndex index, GraphEdge edge) => edge.SourceId == edge.TargetId
        ? index.IsVisible(edge.SourceId)
        : index.VisibleRepresentative(edge.SourceId) != index.VisibleRepresentative(edge.TargetId);

    public static bool IsOwnerVisible(GraphIndex index, string ownerId)
    {
        if (index.Nodes.ContainsKey(ownerId)) return index.IsVisible(ownerId);
        if (index.Ports.TryGetValue(ownerId, out var port)) return index.IsVisible(port.NodeId);
        return index.Edges.TryGetValue(ownerId, out var edge) && IsEdgeVisible(index, edge);
    }

    public static PointD LabelAnchor(GraphIndex index, string ownerId)
    {
        if (index.Nodes.TryGetValue(ownerId, out var node)) return node.Bounds.Center;
        if (index.Ports.ContainsKey(ownerId)) return index.PortLocation(ownerId);
        if (index.Edges.TryGetValue(ownerId, out var edge)) return Geometry.PointAtFraction(index.EdgePath(edge), .5);
        throw new KeyNotFoundException("A label must belong to an existing node, edge or port.");
    }

    /// <summary>Font-independent estimate for headless clients. UI hosts should supply their actual text measurement.</summary>
    public static PointD EstimateLabelSize(GraphLabel label)
    {
        var lines = label.Text.Replace("\r", "").Split('\n');
        var width = lines.Max(line => StringInfo.ParseCombiningCharacters(line).Length) * label.FontSize;
        return new(Math.Max(8, width + 8), Math.Max(4, lines.Length * label.FontSize * 1.25 + 4));
    }

    public static RectD LabelBounds(GraphIndex index, GraphLabel label, PointD size)
    {
        if (!size.IsFinite || size.X < 0 || size.Y < 0) throw new ArgumentException("Invalid measured label size.", nameof(size));
        var center = LabelAnchor(index, label.OwnerId) + label.Offset;
        return new(center.X - size.X / 2, center.Y - size.Y / 2, size.X, size.Y);
    }

    public static RectD Bounds(GraphSnapshot graph, Func<GraphLabel, PointD>? measure = null)
    {
        measure ??= EstimateLabelSize; var index = graph.Index(); var bounds = new List<RectD>();
        bounds.AddRange(graph.Nodes.Where(n => index.IsVisible(n.Id)).Select(n => n.Bounds));
        foreach (var edge in graph.Edges.Where(e => IsEdgeVisible(index, e)))
        {
            bounds.AddRange(index.EdgePath(edge).Select(p => new RectD(p.X, p.Y, 0, 0)));
            if (edge.Text.Length > 0)
            {
                var caption = new GraphLabel("", edge.Id, edge.Text, new(0, -9), 11);
                bounds.Add(LabelBounds(index, caption, measure(caption)));
            }
        }
        foreach (var label in graph.Labels.Where(l => IsOwnerVisible(index, l.OwnerId)))
            bounds.Add(LabelBounds(index, label, measure(label)));
        return RectD.Enclose(bounds);
    }
}
