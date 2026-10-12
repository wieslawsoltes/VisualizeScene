using System.Collections.Immutable;
using VisualizeScene.Core;

namespace VisualizeScene.Editing;

public sealed record GraphAlignmentResult(GraphSnapshot Graph, int MovedNodes, int TranslatedEdges, int ExaminedNodes, int ExaminedEdges);

/// <summary>Batch alignment with one hierarchy traversal and one edge pass, rather than Move per selected root.</summary>
public static class GraphAlignment
{
    public static GraphAlignmentResult Calculate(GraphSnapshot graph, IEnumerable<string> selectedIds, Alignment alignment,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(graph); ArgumentNullException.ThrowIfNull(selectedIds);
        cancellationToken.ThrowIfCancellationRequested();
        if (!Enum.IsDefined(alignment)) throw new ArgumentOutOfRangeException(nameof(alignment));
        var index = graph.Index(); var selected = new HashSet<string>(StringComparer.Ordinal); var simpleRoots = true;
        foreach (var id in selectedIds)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (index.Nodes.TryGetValue(id, out var node) && !node.Locked)
            { selected.Add(id); simpleRoots &= !node.IsGroup && node.ParentId is null; }
        }
        if (selected.Count < 2) return new(graph, 0, 0, 0, 0);
        // Flat top-level selections need no ancestor ownership dictionary or traversal stack.
        // Their document order is exactly the original hierarchy preorder.
        Dictionary<string, string>? owners = null;
        var roots = new List<GraphNode>(selected.Count); var examinedNodes = 0;
        if (simpleRoots)
        {
            foreach (var node in graph.Nodes)
            {
                cancellationToken.ThrowIfCancellationRequested(); examinedNodes++;
                if (selected.Contains(node.Id)) roots.Add(node);
            }
        }
        else
        {
            owners = new(StringComparer.Ordinal); var stack = new Stack<(GraphNode Node, string? Root)>();
            for (var i = graph.Nodes.Length - 1; i >= 0; i--) if (graph.Nodes[i].ParentId is null) stack.Push((graph.Nodes[i], null));
            while (stack.TryPop(out var item))
            {
                cancellationToken.ThrowIfCancellationRequested(); examinedNodes++;
                var root = item.Root;
                if (root is null && selected.Contains(item.Node.Id)) { root = item.Node.Id; roots.Add(item.Node); }
                if (root is not null) owners.Add(item.Node.Id, root);
                var children = index.Children(item.Node.Id);
                for (var i = children.Count - 1; i >= 0; i--) stack.Push((children[i], root));
            }
        }
        if (roots.Count < 2) return new(graph, 0, 0, examinedNodes, 0);
        var bounds = RectD.Enclose(roots.Select(n => n.Bounds));
        var vertical = alignment == Alignment.DistributeVertical;
        var distribute = alignment is Alignment.DistributeHorizontal or Alignment.DistributeVertical;
        var gap = distribute ? ((vertical ? bounds.Height : bounds.Width) - roots.Sum(n => vertical ? n.Bounds.Height : n.Bounds.Width)) / (roots.Count - 1) : 0;
        // Alignment does not depend on sorted order; distribution retains the existing ordinal tie break.
        if (distribute) roots.Sort((a, b) =>
        {
            var comparison = (vertical ? a.Bounds.Y : a.Bounds.X).CompareTo(vertical ? b.Bounds.Y : b.Bounds.X);
            return comparison != 0 ? comparison : StringComparer.Ordinal.Compare(a.Id, b.Id);
        });
        var cursor = vertical ? bounds.Y : bounds.X;
        var deltas = new Dictionary<string, PointD>(roots.Count, StringComparer.Ordinal);
        foreach (var node in roots)
        {
            var b = node.Bounds;
            var target = alignment switch
            {
                Alignment.Left => b with { X = bounds.X }, Alignment.HorizontalCenter => b with { X = bounds.Center.X - b.Width / 2 }, Alignment.Right => b with { X = bounds.Right - b.Width },
                Alignment.Top => b with { Y = bounds.Y }, Alignment.VerticalCenter => b with { Y = bounds.Center.Y - b.Height / 2 }, Alignment.Bottom => b with { Y = bounds.Bottom - b.Height },
                Alignment.DistributeHorizontal => b with { X = cursor }, Alignment.DistributeVertical => b with { Y = cursor }, _ => b
            };
            deltas.Add(node.Id, new(target.X - b.X, target.Y - b.Y));
            cursor += (vertical ? b.Height : b.Width) + gap;
        }
        if (deltas.Values.All(delta => delta == default)) return new(graph, 0, 0, examinedNodes, 0);
        var nodes = graph.Nodes.ToBuilder(); var moved = new Dictionary<string, (string Root, PointD Delta)>(StringComparer.Ordinal);
        for (var i = 0; i < nodes.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested(); examinedNodes++;
            var node = nodes[i];
            if (node.Locked) continue;
            var root = node.Id;
            if (owners is not null && !owners.TryGetValue(node.Id, out root)) continue;
            if (!deltas.TryGetValue(root, out var delta) || delta == default) continue; nodes[i] = node with { Bounds = node.Bounds.Move(delta) }; moved.Add(node.Id, (root, delta));
        }
        if (moved.Count == 0) return new(graph, 0, 0, examinedNodes, 0);
        ImmutableArray<GraphEdge>.Builder? edges = null; var translated = 0;
        for (var i = 0; i < graph.Edges.Length; i++)
        {
            cancellationToken.ThrowIfCancellationRequested(); var edge = graph.Edges[i];
            if (edge.Bends.IsDefaultOrEmpty || !moved.TryGetValue(edge.SourceId, out var a) ||
                !moved.TryGetValue(edge.TargetId, out var b) || a.Root != b.Root) continue;
            edges ??= graph.Edges.ToBuilder();
            edges[i] = edge with { Bends = edge.Bends.Select(p => p + a.Delta).ToImmutableArray() }; translated++;
        }
        var result = graph with { Nodes = nodes.ToImmutable(), Edges = edges?.ToImmutable() ?? graph.Edges };
        GraphValidation.Validate(result); cancellationToken.ThrowIfCancellationRequested();
        return new(result, moved.Count, translated, examinedNodes, graph.Edges.Length);
    }
}
