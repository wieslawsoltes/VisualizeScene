using System.Collections.Immutable;
using VisualizeScene.Core;

namespace VisualizeScene.Editing;

public static class GraphClipboard
{
    public static GraphSnapshot Copy(GraphSnapshot graph, IEnumerable<string> selection) => Copy(graph, selection, default);

    /// <summary>Copies selected owners and their dependent subgraph, preserving original drawing order.</summary>
    /// <remarks>Unknown IDs are ignored for compatibility. Each included node's children are visited once,
    /// even when selected groups are nested. Selection is enumerated once; cancellation never changes the source.</remarks>
    public static GraphSnapshot Copy(GraphSnapshot graph, IEnumerable<string> selection, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(graph); ArgumentNullException.ThrowIfNull(selection);
        cancellationToken.ThrowIfCancellationRequested(); GraphValidation.Validate(graph);
        var index = graph.Index(); var nodes = new HashSet<string>(StringComparer.Ordinal); var pending = new Queue<string>();
        void AddNode(string id) { if (nodes.Add(id)) pending.Enqueue(id); }
        foreach (var selected in selection)
        {
            cancellationToken.ThrowIfCancellationRequested(); ArgumentNullException.ThrowIfNull(selected);
            var id = index.Labels.TryGetValue(selected, out var label) ? label.OwnerId : selected;
            if (index.Nodes.ContainsKey(id)) AddNode(id);
            else if (index.Ports.TryGetValue(id, out var port)) AddNode(port.NodeId);
            else if (index.Edges.TryGetValue(id, out var edge)) { AddNode(edge.SourceId); AddNode(edge.TargetId); }
        }
        while (pending.TryDequeue(out var id))
        {
            cancellationToken.ThrowIfCancellationRequested();
            foreach (var child in index.Children(id)) { cancellationToken.ThrowIfCancellationRequested(); AddNode(child.Id); }
        }
        if (nodes.Count == 0) return GraphSnapshot.Empty;
        var copiedNodes = ImmutableArray.CreateBuilder<GraphNode>();
        var edges = ImmutableArray.CreateBuilder<GraphEdge>(); var ports = ImmutableArray.CreateBuilder<GraphPort>();
        var labels = ImmutableArray.CreateBuilder<GraphLabel>(); var owners = new HashSet<string>(nodes, StringComparer.Ordinal);
        foreach (var node in graph.Nodes)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (nodes.Contains(node.Id)) copiedNodes.Add(node.ParentId is { } parent && !nodes.Contains(parent) ? node with { ParentId = null } : node);
        }
        foreach (var edge in graph.Edges)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (nodes.Contains(edge.SourceId) && nodes.Contains(edge.TargetId)) { edges.Add(edge); owners.Add(edge.Id); }
        }
        foreach (var port in graph.Ports)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (nodes.Contains(port.NodeId)) { ports.Add(port); owners.Add(port.Id); }
        }
        foreach (var label in graph.Labels)
        {
            cancellationToken.ThrowIfCancellationRequested(); if (owners.Contains(label.OwnerId)) labels.Add(label);
        }
        cancellationToken.ThrowIfCancellationRequested();
        return new(copiedNodes.ToImmutable(), edges.ToImmutable(), ports.ToImmutable(), labels.ToImmutable());
    }
    public static IReadOnlyList<string> Paste(GraphEdit edit, GraphSnapshot data, PointD offset)
    {
        ArgumentNullException.ThrowIfNull(edit); ArgumentNullException.ThrowIfNull(data); GraphValidation.Validate(data);
        if (!offset.IsFinite) throw new ArgumentOutOfRangeException(nameof(offset), "Paste offset must be finite.");
        // Preflight all translated coordinates before mutating the caller-owned edit.
        foreach (var node in data.Nodes)
        {
            var bounds = node.Bounds.Move(offset);
            if (!bounds.IsValid || !double.IsFinite(bounds.Right) || !double.IsFinite(bounds.Bottom))
                throw new OverflowException("Pasted node geometry exceeds the finite coordinate range.");
        }
        foreach (var edge in data.Edges) if (!edge.Bends.IsDefault)
            foreach (var bend in edge.Bends) if (!(bend + offset).IsFinite)
                throw new OverflowException("Pasted bend geometry exceeds the finite coordinate range.");
        var map = data.Nodes.Select(n => n.Id).Concat(data.Edges.Select(e => e.Id)).Concat(data.Ports.Select(p => p.Id)).Concat(data.Labels.Select(l => l.Id)).ToDictionary(id => id, _ => GraphEdit.NewId("copy"));
        foreach (var n in data.Nodes) edit.Add(n with { Id = map[n.Id], Bounds = n.Bounds.Move(offset), ParentId = n.ParentId is null ? null : map[n.ParentId] });
        foreach (var p in data.Ports) edit.Add(p with { Id = map[p.Id], NodeId = map[p.NodeId] });
        foreach (var e in data.Edges) edit.Add(e with { Id = map[e.Id], SourceId = map[e.SourceId], TargetId = map[e.TargetId], SourcePortId = e.SourcePortId is null ? null : map[e.SourcePortId], TargetPortId = e.TargetPortId is null ? null : map[e.TargetPortId], Bends = e.Bends.IsDefault ? [] : e.Bends.Select(p => p + offset).ToImmutableArray() });
        foreach (var l in data.Labels) edit.Add(l with { Id = map[l.Id], OwnerId = map[l.OwnerId] });
        return data.Nodes.Where(n => n.ParentId is null).Select(n => map[n.Id]).ToArray();
    }
}
