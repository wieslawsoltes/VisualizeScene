using VisualizeScene.Core;

namespace VisualizeScene.Editing;

public enum GroupFoldOperation { Toggle, Expand, Collapse }
public sealed record GraphGroupFoldResult(GraphSnapshot Graph, int EligibleGroups, int ChangedGroups, int ExaminedNodes);

/// <summary>Changes a captured set of visible group flags once, without moving contents or serially rebuilding the document.</summary>
public static class GraphGroupFolding
{
    public static GraphGroupFoldResult Calculate(GraphSnapshot graph, IEnumerable<string> itemIds,
        GroupFoldOperation operation, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(graph); ArgumentNullException.ThrowIfNull(itemIds);
        cancellationToken.ThrowIfCancellationRequested();
        if (!Enum.IsDefined(operation)) throw new ArgumentOutOfRangeException(nameof(operation));
        var index = graph.Index(); var eligible = new HashSet<string>(StringComparer.Ordinal);
        var changes = new Dictionary<string, GraphNode>(StringComparer.Ordinal);
        foreach (var id in itemIds)
        {
            cancellationToken.ThrowIfCancellationRequested(); ArgumentNullException.ThrowIfNull(id);
            if (!index.Nodes.TryGetValue(id, out var node))
            {
                if (!index.Edges.ContainsKey(id) && !index.Ports.ContainsKey(id) && !index.Labels.ContainsKey(id))
                    throw new ArgumentException("Selected item is absent: " + id, nameof(itemIds));
                continue;
            }
            if (!node.IsGroup || !index.IsVisible(id) || !eligible.Add(id)) continue;
            // Locked is a position/size lock, not a lock on expanding the graph hierarchy.
            var collapsed = operation switch
            {
                GroupFoldOperation.Toggle => !node.Collapsed,
                GroupFoldOperation.Collapse => true,
                GroupFoldOperation.Expand => false,
                _ => throw new ArgumentOutOfRangeException(nameof(operation))
            };
            if (collapsed != node.Collapsed) changes.Add(id, node with { Collapsed = collapsed });
        }
        cancellationToken.ThrowIfCancellationRequested();
        if (changes.Count == 0) return new(graph, eligible.Count, 0, 0);
        var nodes = graph.Nodes.ToBuilder();
        for (var i = 0; i < nodes.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (changes.TryGetValue(nodes[i].Id, out var replacement)) nodes[i] = replacement;
        }
        var result = graph with { Nodes = nodes.ToImmutable() };
        GraphValidation.Validate(result); cancellationToken.ThrowIfCancellationRequested();
        return new(result, eligible.Count, changes.Count, nodes.Count);
    }
}
