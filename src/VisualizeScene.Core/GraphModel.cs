using System.Collections.Immutable;

namespace VisualizeScene.Core;

public enum NodeShape { Rectangle, RoundedRectangle, Ellipse, Diamond, Hexagon, Parallelogram }
public enum EdgeRouting { Polyline, Orthogonal }

public sealed record GraphNode(string Id, RectD Bounds, string Text = "Node", NodeShape Shape = NodeShape.RoundedRectangle,
    string Fill = "#DDEBFA", string Stroke = "#4778A5", string? ParentId = null, bool IsGroup = false, bool Collapsed = false, bool Locked = false);
public sealed record GraphPort(string Id, string NodeId, PointD RelativeLocation);
public sealed record GraphLabel(string Id, string OwnerId, string Text, PointD Offset, double FontSize = 12);
public sealed record GraphEdge(string Id, string SourceId, string TargetId, string Text = "", double Weight = 1,
    ImmutableArray<PointD> Bends = default, string Stroke = "#607D99", bool Directed = true,
    EdgeRouting Routing = EdgeRouting.Orthogonal, string? SourcePortId = null, string? TargetPortId = null);

public sealed record GraphSnapshot(ImmutableArray<GraphNode> Nodes, ImmutableArray<GraphEdge> Edges,
    ImmutableArray<GraphPort> Ports, ImmutableArray<GraphLabel> Labels)
{
    public static GraphSnapshot Empty { get; } = new([], [], [], []);
    public RectD Bounds => RectD.Enclose(Nodes.Select(n => n.Bounds));
    public GraphIndex Index() => new(this);
}

/// <summary>Adjacency and identity indexes for one immutable revision. Safe for concurrent readers.</summary>
public sealed class GraphIndex
{
    public GraphSnapshot Graph { get; }
    public IReadOnlyDictionary<string, GraphNode> Nodes { get; }
    public IReadOnlyDictionary<string, GraphEdge> Edges { get; }
    public IReadOnlyDictionary<string, GraphPort> Ports { get; }
    private readonly Dictionary<string, List<GraphEdge>> outgoing = new(StringComparer.Ordinal);
    private readonly Dictionary<string, List<GraphEdge>> incoming = new(StringComparer.Ordinal);
    private readonly Dictionary<string, List<GraphNode>> children = new(StringComparer.Ordinal);
    public GraphIndex(GraphSnapshot graph)
    {
        Graph = graph;
        Nodes = graph.Nodes.ToDictionary(n => n.Id, StringComparer.Ordinal);
        Edges = graph.Edges.ToDictionary(e => e.Id, StringComparer.Ordinal);
        Ports = graph.Ports.ToDictionary(p => p.Id, StringComparer.Ordinal);
        foreach (var n in graph.Nodes) { outgoing[n.Id] = []; incoming[n.Id] = []; children[n.Id] = []; }
        foreach (var e in graph.Edges) { outgoing[e.SourceId].Add(e); incoming[e.TargetId].Add(e); }
        foreach (var n in graph.Nodes) if (n.ParentId is not null) children[n.ParentId].Add(n);
    }
    public IReadOnlyList<GraphEdge> Outgoing(string id) => outgoing[id];
    public IReadOnlyList<GraphEdge> Incoming(string id) => incoming[id];
    public IReadOnlyList<GraphNode> Children(string id) => children[id];
    public IEnumerable<GraphNode> Descendants(string id)
    {
        var stack = new Stack<GraphNode>(children[id]);
        while (stack.TryPop(out var n)) { yield return n; foreach (var child in children[n.Id]) stack.Push(child); }
    }
    public string VisibleRepresentative(string id)
    {
        var result = id;
        for (var parent = Nodes[id].ParentId; parent is not null; parent = Nodes[parent].ParentId)
            if (Nodes[parent].Collapsed) result = parent;
        return result;
    }
    public bool IsVisible(string id) => VisibleRepresentative(id) == id;
    public PointD PortLocation(string id)
    {
        var p = Ports[id]; var b = Nodes[p.NodeId].Bounds;
        return new(b.X + p.RelativeLocation.X * b.Width, b.Y + p.RelativeLocation.Y * b.Height);
    }
    public IReadOnlyList<PointD> EdgePath(GraphEdge edge)
    {
        var source = VisibleRepresentative(edge.SourceId); var target = VisibleRepresentative(edge.TargetId);
        var a = Nodes[source]; var b = Nodes[target];
        if (source == target)
        {
            var r = a.Bounds;
            return [new(r.Right, r.Center.Y), new(r.Right + 35, r.Center.Y), new(r.Right + 35, r.Y - 25), new(r.Center.X, r.Y - 25), new(r.Center.X, r.Y)];
        }
        var bends = edge.Bends.IsDefault ? ImmutableArray<PointD>.Empty : edge.Bends;
        if (source != edge.SourceId || target != edge.TargetId) bends = [];
        var start = edge.SourcePortId is not null && source == edge.SourceId ? PortLocation(edge.SourcePortId) : Geometry.ClipToNode(a, bends.Length > 0 ? bends[0] : b.Bounds.Center);
        var end = edge.TargetPortId is not null && target == edge.TargetId ? PortLocation(edge.TargetPortId) : Geometry.ClipToNode(b, bends.Length > 0 ? bends[^1] : a.Bounds.Center);
        if (bends.Length > 0) return new[] { start }.Concat(bends).Append(end).ToArray();
        if (edge.Routing == EdgeRouting.Polyline) return [start, end];
        if (Math.Abs(b.Bounds.Center.Y - a.Bounds.Center.Y) >= Math.Abs(b.Bounds.Center.X - a.Bounds.Center.X))
        {
            start = edge.SourcePortId is null ? Geometry.ClipToNode(a, new(a.Bounds.Center.X, b.Bounds.Center.Y)) : start;
            end = edge.TargetPortId is null ? Geometry.ClipToNode(b, new(b.Bounds.Center.X, a.Bounds.Center.Y)) : end;
            var y = (start.Y + end.Y) / 2;
            return [start, new(start.X, y), new(end.X, y), end];
        }
        start = edge.SourcePortId is null ? Geometry.ClipToNode(a, new(b.Bounds.Center.X, a.Bounds.Center.Y)) : start;
        end = edge.TargetPortId is null ? Geometry.ClipToNode(b, new(a.Bounds.Center.X, b.Bounds.Center.Y)) : end;
        var x = (start.X + end.X) / 2;
        return [start, new(x, start.Y), new(x, end.Y), end];
    }
}

public static class GraphValidation
{
    public static void Validate(GraphSnapshot graph)
    {
        if (graph.Nodes.IsDefault || graph.Edges.IsDefault || graph.Ports.IsDefault || graph.Labels.IsDefault)
            throw new ArgumentException("Graph collections must be initialized.");
        var ids = new HashSet<string>(StringComparer.Ordinal);
        void Id(string id) { if (string.IsNullOrWhiteSpace(id) || id.Length > 1024 || !ids.Add(id)) throw new ArgumentException($"Invalid or duplicate ID: {id}"); }
        foreach (var n in graph.Nodes)
        {
            Id(n.Id);
            if (!n.Bounds.IsValid || n.Bounds.Width < 1 || n.Bounds.Height < 1 || !Enum.IsDefined(n.Shape)) throw new ArgumentException($"Invalid node geometry: {n.Id}");
            if (n.Collapsed && !n.IsGroup) throw new ArgumentException("Only groups can be collapsed.");
            if (n.Text is null || !ValidColor(n.Fill) || !ValidColor(n.Stroke)) throw new ArgumentException("Invalid node style.");
        }
        var nodes = graph.Nodes.ToDictionary(n => n.Id, StringComparer.Ordinal);
        var done = new HashSet<string>();
        foreach (var node in graph.Nodes)
        {
            var path = new HashSet<string>();
            for (var n = node; !done.Contains(n.Id);)
            {
                if (!path.Add(n.Id)) throw new ArgumentException("Group hierarchy cannot contain a cycle.");
                if (n.ParentId is null) break;
                if (!nodes.TryGetValue(n.ParentId, out var parent) || !parent.IsGroup) throw new ArgumentException("A parent must be an existing group.");
                n = parent;
            }
            done.UnionWith(path);
        }
        foreach (var p in graph.Ports)
        {
            Id(p.Id);
            if (!nodes.ContainsKey(p.NodeId) || !p.RelativeLocation.IsFinite || p.RelativeLocation.X < 0 || p.RelativeLocation.X > 1 || p.RelativeLocation.Y < 0 || p.RelativeLocation.Y > 1) throw new ArgumentException("Invalid port.");
        }
        var ports = graph.Ports.ToDictionary(p => p.Id);
        foreach (var e in graph.Edges)
        {
            Id(e.Id);
            if (!nodes.ContainsKey(e.SourceId) || !nodes.ContainsKey(e.TargetId)) throw new ArgumentException("Dangling edge endpoint.");
            if (!double.IsFinite(e.Weight) || !Enum.IsDefined(e.Routing) || !ValidColor(e.Stroke) || e.Text is null) throw new ArgumentException("Invalid edge data.");
            if (!e.Bends.IsDefault && e.Bends.Any(p => !p.IsFinite)) throw new ArgumentException("Non-finite bend.");
            if (e.SourcePortId is not null && (!ports.TryGetValue(e.SourcePortId, out var sp) || sp.NodeId != e.SourceId)) throw new ArgumentException("Source port owner mismatch.");
            if (e.TargetPortId is not null && (!ports.TryGetValue(e.TargetPortId, out var tp) || tp.NodeId != e.TargetId)) throw new ArgumentException("Target port owner mismatch.");
        }
        var owners = ids.ToHashSet();
        foreach (var label in graph.Labels)
        {
            Id(label.Id);
            if (!owners.Contains(label.OwnerId) || label.Text is null || !label.Offset.IsFinite || !double.IsFinite(label.FontSize) || label.FontSize <= 0 || label.FontSize > 512) throw new ArgumentException("Invalid label.");
        }
    }
    public static bool ValidColor(string? color) => color is { Length: 7 } && color[0] == '#' && color.Skip(1).All(Uri.IsHexDigit);
}
