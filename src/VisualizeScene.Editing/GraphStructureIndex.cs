using System.Collections.Immutable;
using VisualizeScene.Core;

namespace VisualizeScene.Editing;

public sealed record GraphStructureItem(string Id, string Text, int Depth, bool IsGroup, bool HasChildren, bool IsVisible);

/// <summary>Immutable preorder graph outline. Expansion is view state and never modifies graph folding or undo history.</summary>
public sealed class GraphStructureIndex
{
    private readonly ImmutableArray<GraphStructureItem> items;
    public GraphSnapshot Snapshot { get; }
    public int Count => items.Length;
    public GraphStructureIndex(GraphSnapshot graph)
    {
        Snapshot = graph ?? throw new ArgumentNullException(nameof(graph)); var index = graph.Index();
        var rows = ImmutableArray.CreateBuilder<GraphStructureItem>(graph.Nodes.Length);
        var stack = new Stack<(GraphNode Node, int Depth)>();
        for (var i = graph.Nodes.Length - 1; i >= 0; i--) if (graph.Nodes[i].ParentId is null) stack.Push((graph.Nodes[i], 0));
        while (stack.TryPop(out var next))
        {
            var children = index.Children(next.Node.Id);
            rows.Add(new(next.Node.Id, next.Node.Text, next.Depth, next.Node.IsGroup, children.Count > 0, index.IsVisible(next.Node.Id)));
            for (var i = children.Count - 1; i >= 0; i--) stack.Push((children[i], next.Depth + 1));
        }
        items = rows.MoveToImmutable();
    }
    public ImmutableArray<GraphStructureItem> GetRows(IEnumerable<string>? collapsed = null)
    {
        if (collapsed is null) return items;
        var hidden = collapsed.ToHashSet(StringComparer.Ordinal); if (hidden.Count == 0) return items;
        var result = ImmutableArray.CreateBuilder<GraphStructureItem>(); var skipDepth = int.MaxValue;
        foreach (var item in items)
        {
            if (item.Depth > skipDepth) continue;
            skipDepth = int.MaxValue; result.Add(item);
            if (item.HasChildren && hidden.Contains(item.Id)) skipDepth = item.Depth;
        }
        return result.ToImmutable();
    }
}
