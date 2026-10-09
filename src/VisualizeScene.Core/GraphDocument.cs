using System.Collections.Immutable;

namespace VisualizeScene.Core;

public sealed record GraphChangedEventArgs(long Revision, string Operation, GraphSnapshot Snapshot);

/// <summary>Single-writer document. Readers may retain immutable snapshots on any thread.</summary>
public sealed class GraphDocument
{
    private sealed record Entry(string Name, GraphSnapshot Before, GraphSnapshot After);
    private readonly List<Entry> history = [];
    private int cursor;
    private bool editing;
    public GraphSnapshot Snapshot { get; private set; } = GraphSnapshot.Empty;
    public long Revision { get; private set; }
    public int HistoryLimit { get; }
    public bool CanUndo => cursor > 0;
    public bool CanRedo => cursor < history.Count;
    public string? UndoName => CanUndo ? history[cursor - 1].Name : null;
    public string? RedoName => CanRedo ? history[cursor].Name : null;
    public event EventHandler<GraphChangedEventArgs>? Changed;
    public GraphDocument(int historyLimit = 100)
    {
        if (historyLimit < 1) throw new ArgumentOutOfRangeException(nameof(historyLimit));
        HistoryLimit = historyLimit;
    }
    public void Edit(string name, Action<GraphEdit> action, long? expectedRevision = null)
    {
        ArgumentNullException.ThrowIfNull(action);
        if (editing) throw new InvalidOperationException("Nested document edits are not supported. Use the existing edit transaction.");
        if (expectedRevision.HasValue && Revision != expectedRevision.Value) throw new InvalidOperationException("The graph changed while this operation was running.");
        GraphSnapshot next;
        editing = true;
        try { var edit = new GraphEdit(Snapshot); action(edit); next = edit.Build(); GraphValidation.Validate(next); }
        finally { editing = false; }
        if (Equivalent(Snapshot, next)) return;
        Commit(name, next);
    }
    public void Replace(GraphSnapshot snapshot, string name = "Load graph", bool clearHistory = false)
    {
        if (editing) throw new InvalidOperationException("Cannot replace inside an edit.");
        GraphValidation.Validate(snapshot);
        if (clearHistory)
        {
            Snapshot = snapshot; history.Clear(); cursor = 0; Notify(name); return;
        }
        if (!Equivalent(Snapshot, snapshot)) Commit(name, snapshot);
    }
    private void Commit(string name, GraphSnapshot next)
    {
        if (cursor < history.Count) history.RemoveRange(cursor, history.Count - cursor);
        history.Add(new(name, Snapshot, next));
        if (history.Count > HistoryLimit) history.RemoveAt(0);
        cursor = history.Count; Snapshot = next; Notify(name);
    }
    public void Undo()
    {
        if (editing) throw new InvalidOperationException("Cannot undo inside an edit.");
        if (!CanUndo) return;
        var e = history[--cursor]; Snapshot = e.Before; Notify("Undo " + e.Name);
    }
    public void Redo()
    {
        if (editing) throw new InvalidOperationException("Cannot redo inside an edit.");
        if (!CanRedo) return;
        var e = history[cursor++]; Snapshot = e.After; Notify("Redo " + e.Name);
    }
    private void Notify(string operation) { Revision++; Changed?.Invoke(this, new(Revision, operation, Snapshot)); }
    private static bool Equivalent(GraphSnapshot a, GraphSnapshot b) => a.Nodes.SequenceEqual(b.Nodes) && a.Edges.SequenceEqual(b.Edges) && a.Ports.SequenceEqual(b.Ports) && a.Labels.SequenceEqual(b.Labels);
}

/// <summary>An isolated mutable transaction; validation and publication happen atomically in GraphDocument.</summary>
public sealed class GraphEdit
{
    private readonly Dictionary<string, GraphNode> nodes;
    private readonly Dictionary<string, GraphEdge> edges;
    private readonly Dictionary<string, GraphPort> ports;
    private readonly Dictionary<string, GraphLabel> labels;
    public IEnumerable<GraphNode> Nodes => nodes.Values;
    public IEnumerable<GraphEdge> Edges => edges.Values;
    public GraphEdit(GraphSnapshot snapshot)
    {
        nodes = snapshot.Nodes.ToDictionary(n => n.Id); edges = snapshot.Edges.ToDictionary(e => e.Id);
        ports = snapshot.Ports.ToDictionary(p => p.Id); labels = snapshot.Labels.ToDictionary(l => l.Id);
    }
    public static string NewId(string prefix = "n") => prefix + Guid.NewGuid().ToString("N");
    public void Add(GraphNode node) => nodes.Add(node.Id, node);
    public void Add(GraphEdge edge) => edges.Add(edge.Id, edge);
    public void Add(GraphPort port) => ports.Add(port.Id, port);
    public void Add(GraphLabel label) => labels.Add(label.Id, label);
    public GraphNode Node(string id) => nodes[id];
    public GraphEdge Edge(string id) => edges[id];
    public void Update(GraphNode node) { if (!nodes.ContainsKey(node.Id)) throw new KeyNotFoundException(node.Id); nodes[node.Id] = node; }
    public void Update(GraphEdge edge) { if (!edges.ContainsKey(edge.Id)) throw new KeyNotFoundException(edge.Id); edges[edge.Id] = edge; }
    public void Update(GraphPort port) { if (!ports.ContainsKey(port.Id)) throw new KeyNotFoundException(port.Id); ports[port.Id] = port; }
    public void Update(GraphLabel label) { if (!labels.ContainsKey(label.Id)) throw new KeyNotFoundException(label.Id); labels[label.Id] = label; }
    public void Remove(string id)
    {
        var remove = new HashSet<string> { id };
        var children = nodes.Values.Where(n => n.ParentId is not null).ToLookup(n => n.ParentId!);
        var queue = new Queue<string>(); queue.Enqueue(id);
        while (queue.TryDequeue(out var current)) foreach (var child in children[current]) if (remove.Add(child.Id)) queue.Enqueue(child.Id);
        foreach (var e in edges.Values) if (remove.Contains(e.SourceId) || remove.Contains(e.TargetId)) remove.Add(e.Id);
        foreach (var p in ports.Values) if (remove.Contains(p.NodeId)) remove.Add(p.Id);
        foreach (var l in labels.Values) if (remove.Contains(l.OwnerId)) remove.Add(l.Id);
        foreach (var value in remove) { nodes.Remove(value); edges.Remove(value); ports.Remove(value); labels.Remove(value); }
        // Explicit port deletion detaches endpoints instead of leaving dangling references.
        foreach (var e in edges.Values.ToArray())
            if ((e.SourcePortId is not null && remove.Contains(e.SourcePortId)) || (e.TargetPortId is not null && remove.Contains(e.TargetPortId)))
                edges[e.Id] = e with { SourcePortId = e.SourcePortId is not null && remove.Contains(e.SourcePortId) ? null : e.SourcePortId, TargetPortId = e.TargetPortId is not null && remove.Contains(e.TargetPortId) ? null : e.TargetPortId };
    }
    public void Move(IEnumerable<string> ids, PointD delta)
    {
        var selected = ids.Where(nodes.ContainsKey).ToHashSet();
        var children = nodes.Values.Where(n => n.ParentId is not null).ToLookup(n => n.ParentId!);
        var queue = new Queue<string>(selected);
        while (queue.TryDequeue(out var id)) foreach (var child in children[id]) if (selected.Add(child.Id)) queue.Enqueue(child.Id);
        foreach (var id in selected) { var n = nodes[id]; if (!n.Locked) nodes[id] = n with { Bounds = n.Bounds.Move(delta) }; }
        foreach (var e in edges.Values.ToArray())
            if (selected.Contains(e.SourceId) && selected.Contains(e.TargetId) && !nodes[e.SourceId].Locked && !nodes[e.TargetId].Locked && !e.Bends.IsDefaultOrEmpty)
                edges[e.Id] = e with { Bends = e.Bends.Select(p => p + delta).ToImmutableArray() };
    }
    public string Group(IEnumerable<string> ids)
    {
        var selection = ids.Where(nodes.ContainsKey).Select(id => nodes[id]).ToArray();
        if (selection.Length == 0) throw new ArgumentException("Select at least one node.");
        var parent = selection[0].ParentId;
        if (selection.Any(n => n.ParentId != parent)) throw new ArgumentException("Grouped nodes must have the same parent.");
        var id = NewId("g"); var bounds = RectD.Enclose(selection.Select(n => n.Bounds)).Inflate(28);
        bounds = bounds with { Y = bounds.Y - 20, Height = bounds.Height + 20 };
        Add(new GraphNode(id, bounds, "Group", NodeShape.RoundedRectangle, "#F0F5FA", "#94A9BE", parent, true));
        foreach (var n in selection) nodes[n.Id] = n with { ParentId = id };
        return id;
    }
    public void Ungroup(string id)
    {
        var group = nodes[id]; if (!group.IsGroup) throw new ArgumentException("Not a group.");
        foreach (var child in nodes.Values.Where(n => n.ParentId == id).ToArray()) nodes[child.Id] = child with { ParentId = group.ParentId };
        Remove(id);
    }
    public GraphSnapshot Build() => new(nodes.Values.ToImmutableArray(), edges.Values.ToImmutableArray(), ports.Values.ToImmutableArray(), labels.Values.ToImmutableArray());
}
