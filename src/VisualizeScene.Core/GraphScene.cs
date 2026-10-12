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

    private struct BoundsAccumulator
    {
        public bool HasValue;
        public RectD Value;
        public void Add(RectD bounds)
        {
            if (!bounds.IsValid || !double.IsFinite(bounds.Right) || !double.IsFinite(bounds.Bottom))
                throw new ArgumentException("Scene bounds exceed the finite coordinate range.");
            // Preserve the established sequential-union arithmetic and collection order.
            var next = HasValue ? RectD.Union(Value, bounds) : bounds;
            if (!next.IsValid || !double.IsFinite(next.Right) || !double.IsFinite(next.Bottom))
                throw new ArgumentException("Scene extent exceeds the finite coordinate range.");
            Value = next; HasValue = true;
        }
    }
    private static void AddEdge(ref BoundsAccumulator bounds, GraphIndex index, GraphEdge edge, Func<GraphLabel, PointD> measure)
    {
        var path = index.EdgePath(edge);
        for (var i = 0; i < path.Count; i++) { var p = path[i]; bounds.Add(new(p.X, p.Y, 0, 0)); }
        if (edge.Style is not null) bounds.Add(EdgeGeometry.Bounds(edge, path));
        if (edge.Text.Length == 0) return;
        var caption = new GraphLabel("", edge.Id, edge.Text, new(0, -9), 11);
        bounds.Add(LabelBounds(index, caption, measure(caption)));
    }

    /// <summary>Streams visible scene geometry without retaining a rectangle list. Empty graphs keep the 1x1 default extent.</summary>
    public static RectD Bounds(GraphSnapshot graph, Func<GraphLabel, PointD>? measure = null)
    {
        ArgumentNullException.ThrowIfNull(graph);
        measure ??= EstimateLabelSize; var index = graph.Index(); var bounds = new BoundsAccumulator();
        foreach (var node in graph.Nodes) if (index.IsVisible(node.Id)) bounds.Add(node.Bounds);
        foreach (var edge in graph.Edges) if (IsEdgeVisible(index, edge)) AddEdge(ref bounds, index, edge, measure);
        foreach (var label in graph.Labels) if (IsOwnerVisible(index, label.OwnerId))
            bounds.Add(LabelBounds(index, label, measure(label)));
        return bounds.HasValue ? bounds.Value : new(0, 0, 1, 1);
    }

    /// <summary>Bounds of explicitly selected visible items, not their unselected dependents. Null means no visible items.</summary>
    /// <remarks>Expanded groups contribute their full rectangle; edges include paths and captions; ports contribute their exact position.
    /// Independent labels use the supplied metrics. Unknown IDs reject. Duplicate IDs are harmless repeated contributions.
    /// Traversal follows selection order and does not scan unselected graph items after the shared index is prepared.</remarks>
    public static RectD? BoundsForItems(GraphSnapshot graph, IEnumerable<string> itemIds,
        Func<GraphLabel, PointD>? measure = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(graph); ArgumentNullException.ThrowIfNull(itemIds);
        cancellationToken.ThrowIfCancellationRequested();
        measure ??= EstimateLabelSize; var index = graph.Index(); var bounds = new BoundsAccumulator();
        foreach (var id in itemIds)
        {
            cancellationToken.ThrowIfCancellationRequested(); ArgumentNullException.ThrowIfNull(id);
            if (index.Nodes.TryGetValue(id, out var node))
            { if (index.IsVisible(id)) bounds.Add(node.Bounds); }
            else if (index.Edges.TryGetValue(id, out var edge))
            { if (IsEdgeVisible(index, edge)) AddEdge(ref bounds, index, edge, measure); }
            else if (index.Ports.TryGetValue(id, out var port))
            { if (index.IsVisible(port.NodeId)) { var p = index.PortLocation(id); bounds.Add(new(p.X, p.Y, 0, 0)); } }
            else if (index.Labels.TryGetValue(id, out var label))
            { if (IsOwnerVisible(index, label.OwnerId)) bounds.Add(LabelBounds(index, label, measure(label))); }
            else throw new ArgumentException("Selected item is absent: " + id, nameof(itemIds));
        }
        cancellationToken.ThrowIfCancellationRequested();
        return bounds.HasValue ? bounds.Value : null;
    }
}
