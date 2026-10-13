using VisualizeScene.Core;

namespace VisualizeScene.Editing;

public sealed record NodeCaptionSizingOptions(double HorizontalPadding = 8, double VerticalPadding = 6,
    double MinimumWidth = 30, double MinimumHeight = 24, bool EnlargeOnly = false)
{
    public void Validate()
    {
        if (!double.IsFinite(HorizontalPadding) || HorizontalPadding < 0 || !double.IsFinite(VerticalPadding) || VerticalPadding < 0 ||
            !double.IsFinite(MinimumWidth) || MinimumWidth < 1 || !double.IsFinite(MinimumHeight) || MinimumHeight < 1)
            throw new ArgumentOutOfRangeException(nameof(HorizontalPadding), "Caption padding must be finite and nonnegative; minimum sizes must be at least one.");
    }
}
public sealed record GraphNodeCaptionSizeResult(GraphSnapshot Graph, int EligibleNodes, int ResizedNodes, int MeasuredNodes, int ExaminedNodes);

/// <summary>Fits visible, unlocked, non-group nodes to their captions in one immutable operation, without moving top-left positions.</summary>
public static class GraphNodeCaptionSizing
{
    public static bool CanFit(GraphIndex index, GraphNode node) => !node.IsGroup && !node.Locked && index.IsVisible(node.Id);

    public static GraphNodeCaptionSizeResult Calculate(GraphSnapshot graph, IEnumerable<string> ids,
        Func<GraphNode, PointD> measure, NodeCaptionSizingOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(graph); ArgumentNullException.ThrowIfNull(ids); ArgumentNullException.ThrowIfNull(measure);
        cancellationToken.ThrowIfCancellationRequested(); options ??= new(); options.Validate();
        var index = graph.Index(); var seen = new HashSet<string>(StringComparer.Ordinal); var eligible = new List<GraphNode>();
        // Materialize/validate the selection before invoking application-owned measurement callbacks.
        foreach (var id in ids)
        {
            cancellationToken.ThrowIfCancellationRequested(); ArgumentNullException.ThrowIfNull(id);
            if (!seen.Add(id)) continue;
            if (index.Nodes.TryGetValue(id, out var node)) { if (CanFit(index, node)) eligible.Add(node); }
            else if (!index.Edges.ContainsKey(id) && !index.Labels.ContainsKey(id) && !index.Ports.ContainsKey(id))
                throw new ArgumentException("Selected item is absent: " + id, nameof(ids));
        }
        var changes = new Dictionary<string, GraphNode>(StringComparer.Ordinal);
        foreach (var node in eligible)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var size = NodeCaptionLayout.FittingSize(node.Shape, measure(node), options.HorizontalPadding, options.VerticalPadding);
            var width = Math.Max(size.X, options.MinimumWidth); var height = Math.Max(size.Y, options.MinimumHeight);
            if (options.EnlargeOnly) { width = Math.Max(width, node.Bounds.Width); height = Math.Max(height, node.Bounds.Height); }
            var bounds = node.Bounds with { Width = width, Height = height };
            if (!bounds.IsValid || !double.IsFinite(bounds.Right) || !double.IsFinite(bounds.Bottom))
                throw new ArgumentException("Fitted node exceeds the finite coordinate range.", nameof(measure));
            if (bounds != node.Bounds) changes.Add(node.Id, node with { Bounds = bounds });
        }
        cancellationToken.ThrowIfCancellationRequested();
        if (changes.Count == 0) return new(graph, eligible.Count, 0, eligible.Count, 0);
        var nodes = graph.Nodes.ToBuilder();
        for (var i = 0; i < nodes.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (changes.TryGetValue(nodes[i].Id, out var updated)) nodes[i] = updated;
        }
        var result = graph with { Nodes = nodes.ToImmutable() };
        GraphValidation.Validate(result); cancellationToken.ThrowIfCancellationRequested();
        return new(result, eligible.Count, changes.Count, eligible.Count, nodes.Count);
    }
}
