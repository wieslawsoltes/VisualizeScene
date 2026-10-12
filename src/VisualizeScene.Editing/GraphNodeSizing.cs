using System.Collections.Immutable;
using VisualizeScene.Core;

namespace VisualizeScene.Editing;

public enum NodeSizeMatch { Width, Height, Both }
public readonly record struct GraphArrangeState(int SelectedNodes, int MovableRoots, int ResizableTargets, string? ReferenceId)
{
    /// <summary>Examines selected nodes and their ancestors, not unrelated scene geometry. A selected locked node may be a size reference.</summary>
    public static GraphArrangeState Calculate(GraphSnapshot graph, IEnumerable<string> itemIds, string? preferredReference = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(graph); ArgumentNullException.ThrowIfNull(itemIds);
        cancellationToken.ThrowIfCancellationRequested(); var index = graph.Index();
        var selected = new HashSet<string>(StringComparer.Ordinal); var movable = new HashSet<string>(StringComparer.Ordinal);
        string? reference = null;
        foreach (var id in itemIds)
        {
            cancellationToken.ThrowIfCancellationRequested(); ArgumentNullException.ThrowIfNull(id);
            if (!index.Nodes.TryGetValue(id, out var node) || !index.IsVisible(id) || !selected.Add(id)) continue;
            reference ??= id; if (!node.Locked) movable.Add(id);
        }
        if (preferredReference is not null && selected.Contains(preferredReference)) reference = preferredReference;
        var roots = 0; var memo = new Dictionary<string, bool>(StringComparer.Ordinal); var path = new List<string>();
        foreach (var id in movable)
        {
            var parent = index.Nodes[id].ParentId; var owned = false; path.Clear();
            while (parent is not null)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (movable.Contains(parent)) { owned = true; break; }
                if (memo.TryGetValue(parent, out owned)) break;
                path.Add(parent); parent = index.Nodes[parent].ParentId;
            }
            foreach (var ancestor in path) memo[ancestor] = owned;
            if (!owned) roots++;
        }
        return new(selected.Count, roots, movable.Count - (reference is not null && movable.Contains(reference) ? 1 : 0), reference);
    }
}

public sealed record GraphNodeSizeResult(GraphSnapshot Graph, int ResizedNodes, string? ReferenceId);

/// <summary>Matches dimensions to an explicitly selected reference node. Positions and unselected descendants are never scaled or translated.</summary>
public static class GraphNodeSizing
{
    public static GraphNodeSizeResult Calculate(GraphSnapshot graph, IEnumerable<string> itemIds, NodeSizeMatch match,
        string? referenceId = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(graph); ArgumentNullException.ThrowIfNull(itemIds);
        cancellationToken.ThrowIfCancellationRequested();
        if (!Enum.IsDefined(match)) throw new ArgumentOutOfRangeException(nameof(match));
        var index = graph.Index(); var selected = new HashSet<string>(StringComparer.Ordinal); string? first = null;
        foreach (var id in itemIds)
        {
            cancellationToken.ThrowIfCancellationRequested(); ArgumentNullException.ThrowIfNull(id);
            if (!index.Nodes.ContainsKey(id) || !index.IsVisible(id) || !selected.Add(id)) continue;
            first ??= id;
        }
        if (referenceId is not null && !selected.Contains(referenceId))
            throw new ArgumentException("The size reference must be a selected visible node.", nameof(referenceId));
        referenceId ??= first;
        if (referenceId is null || selected.Count < 2) return new(graph, 0, referenceId);
        var reference = index.Nodes[referenceId].Bounds;
        var replacements = new Dictionary<string, GraphNode>(StringComparer.Ordinal);
        foreach (var id in selected)
        {
            cancellationToken.ThrowIfCancellationRequested(); var node = index.Nodes[id];
            if (id == referenceId || node.Locked) continue;
            var bounds = node.Bounds with
            {
                Width = match is NodeSizeMatch.Width or NodeSizeMatch.Both ? reference.Width : node.Bounds.Width,
                Height = match is NodeSizeMatch.Height or NodeSizeMatch.Both ? reference.Height : node.Bounds.Height
            };
            if (bounds != node.Bounds) replacements.Add(id, node with { Bounds = bounds });
        }
        if (replacements.Count == 0) return new(graph, 0, referenceId);
        var nodes = graph.Nodes.ToBuilder();
        for (var i = 0; i < nodes.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (replacements.TryGetValue(nodes[i].Id, out var replacement)) nodes[i] = replacement;
        }
        var result = graph with { Nodes = nodes.ToImmutable() };
        GraphValidation.Validate(result); cancellationToken.ThrowIfCancellationRequested();
        return new(result, replacements.Count, referenceId);
    }
}
