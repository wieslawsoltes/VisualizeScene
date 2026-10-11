using System.Collections.ObjectModel;
using System.Runtime.CompilerServices;

namespace VisualizeScene.Core;

public static class GraphValidation
{
    // Values contain only completed, immutable maps. Lazy avoids duplicate validation under contention.
    internal sealed record ValidatedGraph(IReadOnlyDictionary<string, GraphNode> Nodes,
        IReadOnlyDictionary<string, GraphEdge> Edges, IReadOnlyDictionary<string, GraphPort> Ports,
        IReadOnlyDictionary<string, GraphLabel> Labels);
    private static readonly ConditionalWeakTable<GraphSnapshot, Lazy<ValidatedGraph>> validated = new();
    internal static ValidatedGraph Identity(GraphSnapshot graph)
    {
        ArgumentNullException.ThrowIfNull(graph);
        return validated.GetValue(graph, static snapshot => new(() => ValidateCore(snapshot),
            LazyThreadSafetyMode.ExecutionAndPublication)).Value;
    }
    public static void Validate(GraphSnapshot graph) => _ = Identity(graph);
    private static ValidatedGraph ValidateCore(GraphSnapshot graph)
    {
        if (graph.Nodes.IsDefault || graph.Edges.IsDefault || graph.Ports.IsDefault || graph.Labels.IsDefault)
            throw new ArgumentException("Graph collections must be initialized.");
        var ids = new HashSet<string>(StringComparer.Ordinal);
        void Id(string id)
        {
            if (string.IsNullOrWhiteSpace(id) || id.Length > 1024 || !ids.Add(id))
                throw new ArgumentException($"Invalid or duplicate ID: {id}");
        }
        var nodes = new Dictionary<string, GraphNode>(graph.Nodes.Length, StringComparer.Ordinal);
        foreach (var n in graph.Nodes)
        {
            if (n is null) throw new ArgumentException("A node cannot be null.");
            Id(n.Id);
            if (!n.Bounds.IsValid || !double.IsFinite(n.Bounds.Right) || !double.IsFinite(n.Bounds.Bottom) ||
                n.Bounds.Width < 1 || n.Bounds.Height < 1 || !Enum.IsDefined(n.Shape)) throw new ArgumentException($"Invalid node geometry: {n.Id}");
            if (n.Collapsed && !n.IsGroup) throw new ArgumentException("Only groups can be collapsed.");
            if (n.Text is null || !ValidColor(n.Fill) || !ValidColor(n.Stroke)) throw new ArgumentException("Invalid node style.");
            nodes.Add(n.Id, n);
        }
        // White/gray/black traversal uses one reusable path instead of a new HashSet for every node.
        var states = new Dictionary<string, byte>(nodes.Count, StringComparer.Ordinal);
        var path = new List<string>();
        foreach (var node in graph.Nodes)
        {
            if (states.GetValueOrDefault(node.Id) == 2) continue;
            path.Clear();
            for (var current = node; ;)
            {
                var state = states.GetValueOrDefault(current.Id);
                if (state == 2) break;
                if (state == 1) throw new ArgumentException("Group hierarchy cannot contain a cycle.");
                states[current.Id] = 1; path.Add(current.Id);
                if (current.ParentId is null) break;
                if (!nodes.TryGetValue(current.ParentId, out var parent) || !parent.IsGroup)
                    throw new ArgumentException("A parent must be an existing group.");
                current = parent;
            }
            foreach (var id in path) states[id] = 2;
        }
        var ports = new Dictionary<string, GraphPort>(graph.Ports.Length, StringComparer.Ordinal);
        foreach (var p in graph.Ports)
        {
            if (p is null) throw new ArgumentException("A port cannot be null.");
            Id(p.Id);
            if (p.NodeId is null || !nodes.ContainsKey(p.NodeId) || !p.RelativeLocation.IsFinite ||
                p.RelativeLocation.X < 0 || p.RelativeLocation.X > 1 || p.RelativeLocation.Y < 0 || p.RelativeLocation.Y > 1)
                throw new ArgumentException("Invalid port.");
            ports.Add(p.Id, p);
        }
        var edges = new Dictionary<string, GraphEdge>(graph.Edges.Length, StringComparer.Ordinal);
        foreach (var e in graph.Edges)
        {
            if (e is null) throw new ArgumentException("An edge cannot be null.");
            Id(e.Id);
            if (e.SourceId is null || e.TargetId is null || !nodes.ContainsKey(e.SourceId) || !nodes.ContainsKey(e.TargetId))
                throw new ArgumentException("Dangling edge endpoint.");
            if (!double.IsFinite(e.Weight) || !Enum.IsDefined(e.Routing) || !ValidColor(e.Stroke) || e.Text is null)
                throw new ArgumentException("Invalid edge data.");
            if (!e.Bends.IsDefault) foreach (var p in e.Bends) if (!p.IsFinite) throw new ArgumentException("Non-finite bend.");
            if (e.SourcePortId is not null && (!ports.TryGetValue(e.SourcePortId, out var sp) || sp.NodeId != e.SourceId))
                throw new ArgumentException("Source port owner mismatch.");
            if (e.TargetPortId is not null && (!ports.TryGetValue(e.TargetPortId, out var tp) || tp.NodeId != e.TargetId))
                throw new ArgumentException("Target port owner mismatch.");
            edges.Add(e.Id, e);
        }
        var labels = new Dictionary<string, GraphLabel>(graph.Labels.Length, StringComparer.Ordinal);
        foreach (var label in graph.Labels)
        {
            if (label is null) throw new ArgumentException("A label cannot be null.");
            Id(label.Id);
            // Labels cannot own labels. Do not use the growing global-ID set as an owner registry.
            if (label.OwnerId is null || !(nodes.ContainsKey(label.OwnerId) || ports.ContainsKey(label.OwnerId) || edges.ContainsKey(label.OwnerId)) ||
                label.Text is null || !label.Offset.IsFinite || !double.IsFinite(label.FontSize) || label.FontSize <= 0 || label.FontSize > 512)
                throw new ArgumentException("Invalid label.");
            labels.Add(label.Id, label);
        }
        return new(new ReadOnlyDictionary<string, GraphNode>(nodes), new ReadOnlyDictionary<string, GraphEdge>(edges),
            new ReadOnlyDictionary<string, GraphPort>(ports), new ReadOnlyDictionary<string, GraphLabel>(labels));
    }
    public static bool ValidColor(string? color)
    {
        if (color is not { Length: 7 } || color[0] != '#') return false;
        for (var i = 1; i < color.Length; i++) if (!Uri.IsHexDigit(color[i])) return false;
        return true;
    }
}
