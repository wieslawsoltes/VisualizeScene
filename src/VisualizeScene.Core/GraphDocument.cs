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
    public void Replace(GraphSnapshot snapshot, string name = "Load graph", bool clearHistory = false, long? expectedRevision = null)
    {
        if (editing) throw new InvalidOperationException("Cannot replace inside an edit.");
        if (expectedRevision.HasValue && Revision != expectedRevision.Value) throw new InvalidOperationException("The graph changed while this replacement was running.");
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
    private static bool Equivalent(GraphSnapshot a, GraphSnapshot b)
    {
        if (ReferenceEquals(a, b)) return true;
        if (a.Nodes != b.Nodes && !a.Nodes.SequenceEqual(b.Nodes) ||
            a.Ports != b.Ports && !a.Ports.SequenceEqual(b.Ports) ||
            a.Labels != b.Labels && !a.Labels.SequenceEqual(b.Labels)) return false;
        if (a.Edges == b.Edges) return true;
        if (a.Edges.Length != b.Edges.Length) return false;
        for (var i = 0; i < a.Edges.Length; i++)
        {
            var left = a.Edges[i]; var right = b.Edges[i];
            if (left == right) continue;
            if (left with { Bends = right.Bends } != right) return false;
            var x = left.Bends.IsDefault ? ImmutableArray<PointD>.Empty : left.Bends;
            var y = right.Bends.IsDefault ? ImmutableArray<PointD>.Empty : right.Bends;
            if (!x.AsSpan().SequenceEqual(y.AsSpan())) return false;
        }
        return true;
    }
}
