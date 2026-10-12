using System.Collections.Immutable;
using VisualizeScene.Core;

namespace VisualizeScene.Editing;

public enum DrawingOrder { BringToFront, SendToBack, Raise, Lower }
public sealed record GraphOrderResult(GraphSnapshot Graph, int ChangedSlots, int EligibleItems, int ExaminedItems);

/// <summary>Stable drawing-order edits within existing paint layers. Does not change hierarchy, geometry or port precedence.</summary>
public static class GraphDrawingOrder
{
    public static GraphOrderResult Calculate(GraphSnapshot graph, IEnumerable<string> selectedIds, DrawingOrder operation,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(graph); ArgumentNullException.ThrowIfNull(selectedIds);
        cancellationToken.ThrowIfCancellationRequested();
        if (!Enum.IsDefined(operation)) throw new ArgumentOutOfRangeException(nameof(operation));
        var index = graph.Index(); var selected = new HashSet<string>(StringComparer.Ordinal);
        foreach (var id in selectedIds)
        {
            cancellationToken.ThrowIfCancellationRequested(); ArgumentNullException.ThrowIfNull(id);
            if (!(index.Nodes.ContainsKey(id) || index.Edges.ContainsKey(id) || index.Labels.ContainsKey(id) || index.Ports.ContainsKey(id)))
                throw new ArgumentException("Unknown drawing-order item: " + id, nameof(selectedIds));
            selected.Add(id);
        }
        if (selected.Count == 0) return new(graph, 0, 0, 0);
        var changed = 0; var eligible = 0; var examined = 0;
        ImmutableArray<T> Reorder<T>(ImmutableArray<T> source, Func<T, string> identity, Func<T, int> layer) where T : class
        {
            var layers = new Dictionary<int, List<int>>();
            for (var i = 0; i < source.Length; i++)
            {
                cancellationToken.ThrowIfCancellationRequested(); examined++;
                var key = layer(source[i]); if (key < 0) continue;
                if (!layers.TryGetValue(key, out var positions)) layers.Add(key, positions = []);
                positions.Add(i); if (selected.Contains(identity(source[i]))) eligible++;
            }
            ImmutableArray<T>.Builder? result = null;
            foreach (var positions in layers.Values)
            {
                if (positions.Count < 2 || !positions.Any(p => selected.Contains(identity(source[p])))) continue;
                var ordered = positions.Select(p => source[p]).ToArray();
                bool Picked(T item) => selected.Contains(identity(item));
                if (operation is DrawingOrder.Raise or DrawingOrder.Lower)
                {
                    if (operation == DrawingOrder.Raise)
                        for (var i = ordered.Length - 2; i >= 0; i--)
                        { cancellationToken.ThrowIfCancellationRequested(); if (Picked(ordered[i]) && !Picked(ordered[i + 1])) (ordered[i], ordered[i + 1]) = (ordered[i + 1], ordered[i]); }
                    else
                        for (var i = 1; i < ordered.Length; i++)
                        { cancellationToken.ThrowIfCancellationRequested(); if (Picked(ordered[i]) && !Picked(ordered[i - 1])) (ordered[i], ordered[i - 1]) = (ordered[i - 1], ordered[i]); }
                }
                else
                {
                    var next = new T[ordered.Length]; var at = 0; var selectedFirst = operation == DrawingOrder.SendToBack;
                    foreach (var item in ordered) { cancellationToken.ThrowIfCancellationRequested(); if (Picked(item) == selectedFirst) next[at++] = item; }
                    foreach (var item in ordered) { cancellationToken.ThrowIfCancellationRequested(); if (Picked(item) != selectedFirst) next[at++] = item; }
                    ordered = next;
                }
                for (var i = 0; i < positions.Count; i++)
                {
                    if (ReferenceEquals(source[positions[i]], ordered[i])) continue;
                    result ??= source.ToBuilder(); result[positions[i]] = ordered[i]; changed++;
                }
            }
            return result?.ToImmutable() ?? source;
        }
        // Expanded groups stay behind edges/nodes and are ordered by hierarchy depth first.
        var nodes = Reorder(graph.Nodes, n => n.Id, n => !index.IsVisible(n.Id) ? -1 : n.IsGroup && !n.Collapsed ? index.Depth(n.Id) + 1 : 0);
        var edges = Reorder(graph.Edges, e => e.Id, e => GraphScene.IsEdgeVisible(index, e) ? 0 : -1);
        var labels = Reorder(graph.Labels, l => l.Id, l => GraphScene.IsOwnerVisible(index, l.OwnerId) ? 0 : -1);
        cancellationToken.ThrowIfCancellationRequested();
        var graphResult = changed == 0 ? graph : graph with { Nodes = nodes, Edges = edges, Labels = labels };
        GraphValidation.Validate(graphResult);
        return new(graphResult, changed, eligible, examined);
    }
}

public sealed partial class EditorSession
{
    public void ChangeDrawingOrder(DrawingOrder operation)
    {
        var revision = Document.Revision;
        CancelGesture();
        if (Document.Revision != revision) throw new InvalidOperationException("The graph changed while preparing drawing order.");
        var result = GraphDrawingOrder.Calculate(Document.Snapshot, Selection, operation);
        Document.Replace(result.Graph, operation switch
        {
            DrawingOrder.BringToFront => "Bring to front", DrawingOrder.SendToBack => "Send to back",
            DrawingOrder.Raise => "Raise selection", _ => "Lower selection"
        }, expectedRevision: revision);
    }
}
