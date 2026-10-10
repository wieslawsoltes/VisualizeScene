using System.Collections.Immutable;
using VisualizeScene.Core;

namespace VisualizeScene.Editing;

public static class GraphClipboard
{
    public static GraphSnapshot Copy(GraphSnapshot graph, IEnumerable<string> selection)
    {
        var index = graph.Index(); var selected = selection.ToHashSet(); var nodes = selected.Where(index.Nodes.ContainsKey).ToHashSet();
        foreach (var port in graph.Ports.Where(p => selected.Contains(p.Id))) nodes.Add(port.NodeId);
        foreach (var id in nodes.ToArray()) nodes.UnionWith(index.Descendants(id).Select(n => n.Id));
        foreach (var e in graph.Edges.Where(e => selected.Contains(e.Id))) { nodes.Add(e.SourceId); nodes.Add(e.TargetId); }
        var edges = graph.Edges.Where(e => nodes.Contains(e.SourceId) && nodes.Contains(e.TargetId)).ToImmutableArray();
        var ports = graph.Ports.Where(p => nodes.Contains(p.NodeId)).ToImmutableArray();
        var owners = nodes.Concat(edges.Select(e => e.Id)).Concat(ports.Select(p => p.Id)).ToHashSet();
        return new(graph.Nodes.Where(n => nodes.Contains(n.Id)).Select(n => n with { ParentId = n.ParentId is not null && nodes.Contains(n.ParentId) ? n.ParentId : null }).ToImmutableArray(), edges, ports, graph.Labels.Where(l => owners.Contains(l.OwnerId)).ToImmutableArray());
    }
    public static IReadOnlyList<string> Paste(GraphEdit edit, GraphSnapshot data, PointD offset)
    {
        GraphValidation.Validate(data);
        var map = data.Nodes.Select(n => n.Id).Concat(data.Edges.Select(e => e.Id)).Concat(data.Ports.Select(p => p.Id)).Concat(data.Labels.Select(l => l.Id)).ToDictionary(id => id, _ => GraphEdit.NewId("copy"));
        foreach (var n in data.Nodes) edit.Add(n with { Id = map[n.Id], Bounds = n.Bounds.Move(offset), ParentId = n.ParentId is null ? null : map[n.ParentId] });
        foreach (var p in data.Ports) edit.Add(p with { Id = map[p.Id], NodeId = map[p.NodeId] });
        foreach (var e in data.Edges) edit.Add(e with { Id = map[e.Id], SourceId = map[e.SourceId], TargetId = map[e.TargetId], SourcePortId = e.SourcePortId is null ? null : map[e.SourcePortId], TargetPortId = e.TargetPortId is null ? null : map[e.TargetPortId], Bends = e.Bends.IsDefault ? [] : e.Bends.Select(p => p + offset).ToImmutableArray() });
        foreach (var l in data.Labels) edit.Add(l with { Id = map[l.Id], OwnerId = map[l.OwnerId] });
        return data.Nodes.Where(n => n.ParentId is null).Select(n => map[n.Id]).ToArray();
    }
}
