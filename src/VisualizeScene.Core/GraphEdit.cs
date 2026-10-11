using System.Collections.Immutable;

namespace VisualizeScene.Core;

public readonly record struct GraphRemovalResult(int Nodes, int Edges, int Ports, int Labels, int ExaminedItems);

/// <summary>An isolated copy-on-write transaction; validation and publication belong to GraphDocument.</summary>
public sealed class GraphEdit
{
    private readonly GraphSnapshot original;
    private Dictionary<string, GraphNode>? nodeEdits;
    private Dictionary<string, GraphEdge>? edgeEdits;
    private Dictionary<string, GraphPort>? portEdits;
    private Dictionary<string, GraphLabel>? labelEdits;
    private Dictionary<string, GraphNode> nodes => nodeEdits ??= original.Nodes.ToDictionary(n => n.Id);
    private Dictionary<string, GraphEdge> edges => edgeEdits ??= original.Edges.ToDictionary(e => e.Id);
    private Dictionary<string, GraphPort> ports => portEdits ??= original.Ports.ToDictionary(p => p.Id);
    private Dictionary<string, GraphLabel> labels => labelEdits ??= original.Labels.ToDictionary(l => l.Id);
    public IEnumerable<GraphNode> Nodes => nodeEdits is null ? original.Nodes : nodeEdits.Values;
    public IEnumerable<GraphEdge> Edges => edgeEdits is null ? original.Edges : edgeEdits.Values;
    private IEnumerable<GraphPort> Ports => portEdits is null ? original.Ports : portEdits.Values;
    private IEnumerable<GraphLabel> Labels => labelEdits is null ? original.Labels : labelEdits.Values;
    public GraphEdit(GraphSnapshot snapshot) => original = snapshot ?? throw new ArgumentNullException(nameof(snapshot));
    public static string NewId(string prefix = "n") => prefix + Guid.NewGuid().ToString("N");
    public void Add(GraphNode node) => nodes.Add(node.Id, node);
    public void Add(GraphEdge edge) => edges.Add(edge.Id, edge);
    public void Add(GraphPort port) => ports.Add(port.Id, port);
    public void Add(GraphLabel label) => labels.Add(label.Id, label);
    public GraphNode Node(string id) => nodeEdits is null ? original.Index().Nodes[id] : nodeEdits[id];
    public GraphEdge Edge(string id) => edgeEdits is null ? original.Index().Edges[id] : edgeEdits[id];
    public void Update(GraphNode node) { if (Node(node.Id) != node) nodes[node.Id] = node; }
    public void Update(GraphEdge edge) { if (Edge(edge.Id) != edge) edges[edge.Id] = edge; }
    public void Update(GraphPort port)
    {
        var old = portEdits is null ? original.Index().Ports[port.Id] : portEdits[port.Id];
        if (old != port) ports[port.Id] = port;
    }
    public void Update(GraphLabel label)
    {
        var old = labelEdits is null ? original.Index().Labels[label.Id] : labelEdits[label.Id];
        if (old != label) labels[label.Id] = label;
    }
    public void Remove(string id) => RemoveRange([id]);

    /// <summary>Removes all requested items and dependent subtrees in one graph pass. Deleting a port detaches surviving edge endpoints.</summary>
    public GraphRemovalResult RemoveRange(IEnumerable<string> ids)
    {
        ArgumentNullException.ThrowIfNull(ids);
        // Materialize before mutation, including lazy enumerations over this transaction.
        var remove = ids.ToHashSet(StringComparer.Ordinal);
        if (remove.Count == 0) return default;
        var examined = 0;
        var childLists = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var node in Nodes)
        {
            examined++;
            if (node.ParentId is not { } parent) continue;
            if (!childLists.TryGetValue(parent, out var list)) childLists[parent] = list = [];
            list.Add(node.Id);
        }
        var queue = new Queue<string>(remove);
        while (queue.TryDequeue(out var id))
            if (childLists.TryGetValue(id, out var children))
                foreach (var child in children) if (remove.Add(child)) queue.Enqueue(child);
        var nodeIds = remove.Where(id => nodeEdits is null ? original.Index().Nodes.ContainsKey(id) : nodeEdits.ContainsKey(id)).ToArray();
        var portIds = new List<string>();
        foreach (var port in Ports)
        {
            examined++;
            if (remove.Contains(port.Id) || remove.Contains(port.NodeId)) { remove.Add(port.Id); portIds.Add(port.Id); }
        }
        var edgeIds = new List<string>(); var detached = new List<GraphEdge>();
        foreach (var edge in Edges)
        {
            examined++;
            if (remove.Contains(edge.Id) || remove.Contains(edge.SourceId) || remove.Contains(edge.TargetId))
            { remove.Add(edge.Id); edgeIds.Add(edge.Id); continue; }
            var source = edge.SourcePortId is not null && remove.Contains(edge.SourcePortId);
            var target = edge.TargetPortId is not null && remove.Contains(edge.TargetPortId);
            if (source || target) detached.Add(edge with { SourcePortId = source ? null : edge.SourcePortId, TargetPortId = target ? null : edge.TargetPortId });
        }
        var labelIds = new List<string>();
        foreach (var label in Labels)
        {
            examined++;
            if (remove.Contains(label.Id) || remove.Contains(label.OwnerId)) labelIds.Add(label.Id);
        }
        foreach (var id in nodeIds) nodes.Remove(id);
        foreach (var id in portIds) ports.Remove(id);
        foreach (var id in edgeIds) edges.Remove(id);
        foreach (var edge in detached) edges[edge.Id] = edge;
        foreach (var id in labelIds) labels.Remove(id);
        return new(nodeIds.Length, edgeIds.Count, portIds.Count, labelIds.Count, examined);
    }
    public void Move(IEnumerable<string> ids, PointD delta)
    {
        ArgumentNullException.ThrowIfNull(ids);
        if (!delta.IsFinite) throw new ArgumentException("Movement must be finite.", nameof(delta));
        if (delta == default) return;
        var selected = ids.Where(id => nodeEdits is null ? original.Index().Nodes.ContainsKey(id) : nodeEdits.ContainsKey(id)).ToHashSet();
        if (selected.Count == 0) return;
        var children = Nodes.Where(n => n.ParentId is not null).ToLookup(n => n.ParentId!);
        var queue = new Queue<string>(selected);
        while (queue.TryDequeue(out var id)) foreach (var child in children[id]) if (selected.Add(child.Id)) queue.Enqueue(child.Id);
        foreach (var id in selected) { var n = Node(id); if (!n.Locked) Update(n with { Bounds = n.Bounds.Move(delta) }); }
        foreach (var e in Edges.ToArray())
            if (selected.Contains(e.SourceId) && selected.Contains(e.TargetId) && !Node(e.SourceId).Locked && !Node(e.TargetId).Locked && !e.Bends.IsDefaultOrEmpty)
                Update(e with { Bends = e.Bends.Select(p => p + delta).ToImmutableArray() });
    }
    /// <summary>Changes a group's frame and translates descendants once; dimensions do not scale child contents.</summary>
    public void SetNodeBounds(string id, RectD bounds)
    {
        Geometry.RectangleContainsRectangle(bounds, bounds);
        if (bounds.Width < 1 || bounds.Height < 1) throw new ArgumentOutOfRangeException(nameof(bounds));
        var node = Node(id);
        if (node.Locked && node.Bounds != bounds) throw new InvalidOperationException("The node position is locked.");
        var delta = new PointD(bounds.X - node.Bounds.X, bounds.Y - node.Bounds.Y);
        if (node.IsGroup && delta != default) Move([id], delta);
        Update(Node(id) with { Bounds = bounds });
    }
    public string Group(IEnumerable<string> ids)
    {
        ArgumentNullException.ThrowIfNull(ids);
        var selected = ids.Distinct().Where(id => nodeEdits is null ? original.Index().Nodes.ContainsKey(id) : nodeEdits.ContainsKey(id)).Select(Node).ToArray();
        if (selected.Length == 0) throw new ArgumentException("Select at least one node.");
        var parent = selected[0].ParentId;
        if (selected.Any(n => n.ParentId != parent)) throw new ArgumentException("Grouped nodes must have the same parent.");
        var id = NewId("g"); var bounds = RectD.Enclose(selected.Select(n => n.Bounds)).Inflate(28);
        bounds = bounds with { Y = bounds.Y - 20, Height = bounds.Height + 20 };
        Add(new GraphNode(id, bounds, "Group", NodeShape.RoundedRectangle, "#F0F5FA", "#94A9BE", parent, true));
        foreach (var node in selected) Update(node with { ParentId = id });
        return id;
    }
    public void Ungroup(string id)
    {
        var group = Node(id); if (!group.IsGroup) throw new ArgumentException("Not a group.");
        foreach (var child in Nodes.Where(n => n.ParentId == id).ToArray()) Update(child with { ParentId = group.ParentId });
        Remove(id);
    }
    public GraphSnapshot Build() => nodeEdits is null && edgeEdits is null && portEdits is null && labelEdits is null ? original :
        new(nodeEdits is null ? original.Nodes : nodeEdits.Values.ToImmutableArray(),
            edgeEdits is null ? original.Edges : edgeEdits.Values.ToImmutableArray(),
            portEdits is null ? original.Ports : portEdits.Values.ToImmutableArray(),
            labelEdits is null ? original.Labels : labelEdits.Values.ToImmutableArray());
}
