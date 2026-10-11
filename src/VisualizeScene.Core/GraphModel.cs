using System.Collections.Immutable;
using System.Collections.Concurrent;
using System.Runtime.CompilerServices;

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
    public GraphIndex Index() => GraphIndex.ForSnapshot(this);
}

/// <summary>Adjacency and identity indexes for one immutable revision. Safe for concurrent readers.</summary>
public sealed class GraphIndex
{
    public GraphSnapshot Graph { get; }
    public IReadOnlyDictionary<string, GraphNode> Nodes { get; }
    public IReadOnlyDictionary<string, GraphEdge> Edges { get; }
    public IReadOnlyDictionary<string, GraphPort> Ports { get; }
    public IReadOnlyDictionary<string, GraphLabel> Labels { get; }
    private static readonly ConditionalWeakTable<GraphSnapshot, Lazy<GraphIndex>> indexes = new();
    private readonly Dictionary<string, IReadOnlyList<GraphEdge>> outgoing, incoming;
    private readonly Dictionary<string, IReadOnlyList<GraphNode>> children;
    private readonly Dictionary<string, string> representatives;
    private readonly Dictionary<string, int> depths;
    private readonly ConcurrentDictionary<string, IReadOnlyList<PointD>> edgePaths = new(StringComparer.Ordinal);
    // Weak, identity-keyed entries do not attach mutable state to record equality or keep old revisions alive.
    internal static GraphIndex ForSnapshot(GraphSnapshot graph) => indexes.GetValue(graph,
        static key => new Lazy<GraphIndex>(() => new GraphIndex(key), LazyThreadSafetyMode.ExecutionAndPublication)).Value;
    public GraphIndex(GraphSnapshot graph)
    {
        var identity = GraphValidation.Identity(graph); Graph = graph;
        Nodes = identity.Nodes; Edges = identity.Edges; Ports = identity.Ports; Labels = identity.Labels;
        outgoing = Group(graph.Edges, e => e.SourceId); incoming = Group(graph.Edges, e => e.TargetId);
        children = Group(graph.Nodes.Where(n => n.ParentId is not null), n => n.ParentId!);
        representatives = new(graph.Nodes.Length, StringComparer.Ordinal);
        depths = new(graph.Nodes.Length, StringComparer.Ordinal);
        var queue = new Queue<(GraphNode Node, int Depth, string? Collapsed)>();
        foreach (var node in graph.Nodes) if (node.ParentId is null) queue.Enqueue((node, 0, null));
        while (queue.TryDequeue(out var entry))
        {
            var node = entry.Node; depths.Add(node.Id, entry.Depth); representatives.Add(node.Id, entry.Collapsed ?? node.Id);
            var collapsed = entry.Collapsed ?? (node.Collapsed ? node.Id : null);
            foreach (var child in Children(node.Id)) queue.Enqueue((child, entry.Depth + 1, collapsed));
        }
    }
    private static Dictionary<string, IReadOnlyList<T>> Group<T>(IEnumerable<T> values, Func<T, string> key)
    {
        var buckets = new Dictionary<string, List<T>>(StringComparer.Ordinal);
        foreach (var item in values)
        {
            var id = key(item);
            if (!buckets.TryGetValue(id, out var list)) buckets.Add(id, list = new(1));
            list.Add(item);
        }
        var result = new Dictionary<string, IReadOnlyList<T>>(buckets.Count, StringComparer.Ordinal);
        foreach (var pair in buckets) result.Add(pair.Key, pair.Value.AsReadOnly());
        return result;
    }
    private IReadOnlyList<T> Lookup<T>(Dictionary<string, IReadOnlyList<T>> values, string id)
    {
        if (!Nodes.ContainsKey(id)) throw new KeyNotFoundException(id);
        return values.TryGetValue(id, out var result) ? result : Array.Empty<T>();
    }
    public IReadOnlyList<GraphEdge> Outgoing(string id) => Lookup(outgoing, id);
    public IReadOnlyList<GraphEdge> Incoming(string id) => Lookup(incoming, id);
    public IReadOnlyList<GraphNode> Children(string id) => Lookup(children, id);
    public IEnumerable<GraphNode> Descendants(string id)
    {
        var stack = new Stack<GraphNode>(Children(id));
        while (stack.TryPop(out var n)) { yield return n; foreach (var child in Children(n.Id)) stack.Push(child); }
    }
    public int Depth(string id) => depths[id];
    public string VisibleRepresentative(string id) => representatives[id];
    public bool IsVisible(string id) => representatives[id] == id;
    public PointD PortLocation(string id)
    {
        var p = Ports[id]; var b = Nodes[p.NodeId].Bounds;
        return new(b.X + p.RelativeLocation.X * b.Width, b.Y + p.RelativeLocation.Y * b.Height);
    }
    public IReadOnlyList<PointD> EdgePath(GraphEdge edge)
    {
        ArgumentNullException.ThrowIfNull(edge);
        // Preview edges can share an ID with this revision; never return the stored route for a replacement record.
        if (Edges.TryGetValue(edge.Id, out var stored) && ReferenceEquals(stored, edge))
        {
            if (edgePaths.TryGetValue(edge.Id, out var cached)) return cached;
            return edgePaths.GetOrAdd(edge.Id, FreezePath(edge));
        }
        return FreezePath(edge);
    }
    private IReadOnlyList<PointD> FreezePath(GraphEdge edge)
    {
        var path = BuildEdgePath(edge);
        return Array.AsReadOnly(path as PointD[] ?? path.ToArray());
    }
    private IReadOnlyList<PointD> BuildEdgePath(GraphEdge edge)
    {
        var source = VisibleRepresentative(edge.SourceId); var target = VisibleRepresentative(edge.TargetId);
        var a = Nodes[source]; var b = Nodes[target];
        var bends = edge.Bends.IsDefault ? ImmutableArray<PointD>.Empty : edge.Bends;
        if (source != edge.SourceId || target != edge.TargetId) bends = [];
        var sourcePort = edge.SourcePortId is not null && source == edge.SourceId ? edge.SourcePortId : null;
        var targetPort = edge.TargetPortId is not null && target == edge.TargetId ? edge.TargetPortId : null;
        if (source == target && bends.Length == 0)
        {
            var r = a.Bounds;
            var loopStart = sourcePort is not null ? PortLocation(sourcePort) : Geometry.ClipToNode(a, new(r.Right + 35, r.Center.Y));
            var loopEnd = targetPort is not null ? PortLocation(targetPort) : Geometry.ClipToNode(a, new(r.Center.X, r.Y - 35));
            return Geometry.SelfLoop(r, loopStart, loopEnd);
        }
        var start = sourcePort is not null ? PortLocation(sourcePort) : Geometry.ClipToNode(a, bends.Length > 0 ? bends[0] : b.Bounds.Center);
        var end = targetPort is not null ? PortLocation(targetPort) : Geometry.ClipToNode(b, bends.Length > 0 ? bends[^1] : a.Bounds.Center);
        if (bends.Length > 0) return new[] { start }.Concat(bends).Append(end).ToArray();
        if (edge.Routing == EdgeRouting.Polyline) return [start, end];
        if (Math.Abs(b.Bounds.Center.Y - a.Bounds.Center.Y) >= Math.Abs(b.Bounds.Center.X - a.Bounds.Center.X))
        {
            start = sourcePort is null ? Geometry.ClipToNode(a, new(a.Bounds.Center.X, b.Bounds.Center.Y)) : start;
            end = targetPort is null ? Geometry.ClipToNode(b, new(b.Bounds.Center.X, a.Bounds.Center.Y)) : end;
            var y = (start.Y + end.Y) / 2;
            return [start, new(start.X, y), new(end.X, y), end];
        }
        start = sourcePort is null ? Geometry.ClipToNode(a, new(b.Bounds.Center.X, a.Bounds.Center.Y)) : start;
        end = targetPort is null ? Geometry.ClipToNode(b, new(a.Bounds.Center.X, b.Bounds.Center.Y)) : end;
        var x = (start.X + end.X) / 2;
        return [start, new(x, start.Y), new(x, end.Y), end];
    }
}
