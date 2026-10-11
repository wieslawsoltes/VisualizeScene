using System.Collections;
using System.Collections.Immutable;
using VisualizeScene.Core;

namespace VisualizeScene.Editing;

[Flags]
public enum SelectionKinds { None = 0, Nodes = 1, Edges = 2, Ports = 4, Labels = 8, Groups = 16, All = 31 }
public enum AreaSelectionMode { Intersects, Contains, Directional }
public enum SelectionOperation { Replace, Add, Remove, Toggle }
public enum NeighborDirection { Incoming, Outgoing, Both }

public sealed partial class EditorSession
{
    // Never expose the mutable HashSet, even through an IReadOnlySet that callers could downcast.
    private sealed class ReadOnlySelection : IReadOnlySet<string>
    {
        private readonly string[] order;
        private readonly HashSet<string> items;
        public ReadOnlySelection(IEnumerable<string> ids) { order = ids.Distinct(StringComparer.Ordinal).ToArray(); items = new(order, StringComparer.Ordinal); }
        public int Count => items.Count;
        public bool Contains(string item) => items.Contains(item);
        public bool IsProperSubsetOf(IEnumerable<string> other) => items.IsProperSubsetOf(other);
        public bool IsProperSupersetOf(IEnumerable<string> other) => items.IsProperSupersetOf(other);
        public bool IsSubsetOf(IEnumerable<string> other) => items.IsSubsetOf(other);
        public bool IsSupersetOf(IEnumerable<string> other) => items.IsSupersetOf(other);
        public bool Overlaps(IEnumerable<string> other) => items.Overlaps(other);
        public bool SetEquals(IEnumerable<string> other) => items.SetEquals(other);
        public IEnumerator<string> GetEnumerator() => ((IEnumerable<string>)order).GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
    private ReadOnlySelection readSelection = new([]);
    private ReadOnlySelection? marqueeOriginal, marqueePreview;
    private SpatialIndex<string> portIndex = new([]);
    private SelectionOperation marqueeOperation;
    private SelectionKinds marqueeItemKinds = SelectionKinds.Nodes | SelectionKinds.Edges | SelectionKinds.Ports | SelectionKinds.Labels;
    private AreaSelectionMode marqueeMode;
    public IReadOnlySet<string> DisplaySelection => marqueePreview ?? readSelection;
    public bool IsMarqueeContainment { get; private set; }
    public SelectionKinds MarqueeItemKinds
    {
        get => marqueeItemKinds;
        set { ValidateKinds(value); if (marqueeItemKinds == value) return; CancelGesture(); marqueeItemKinds = value; }
    }
    public AreaSelectionMode MarqueeMode
    {
        get => marqueeMode;
        set { if (!Enum.IsDefined(value)) throw new ArgumentOutOfRangeException(nameof(value)); if (marqueeMode == value) return; CancelGesture(); marqueeMode = value; }
    }
    private static void ValidateKinds(SelectionKinds kinds)
    { if ((kinds & ~SelectionKinds.All) != 0) throw new ArgumentOutOfRangeException(nameof(kinds)); }
    private SelectionKinds ItemKind(string id) => index.Nodes.TryGetValue(id, out var node) ? node.IsGroup ? SelectionKinds.Groups : SelectionKinds.Nodes
        : index.Edges.ContainsKey(id) ? SelectionKinds.Edges : index.Ports.ContainsKey(id) ? SelectionKinds.Ports
        : index.Labels.ContainsKey(id) ? SelectionKinds.Labels : SelectionKinds.None;
    private bool IsSelectable(string id)
    {
        if (id is null) return false;
        return index.Labels.TryGetValue(id, out var label) ? GraphScene.IsOwnerVisible(index, label.OwnerId) : GraphScene.IsOwnerVisible(index, id);
    }
    private IEnumerable<string> VisibleItems(SelectionKinds kinds)
    {
        ValidateKinds(kinds);
        return Document.Snapshot.Nodes.Select(n => n.Id).Concat(Document.Snapshot.Edges.Select(e => e.Id))
            .Concat(Document.Snapshot.Ports.Select(p => p.Id)).Concat(Document.Snapshot.Labels.Select(l => l.Id))
            .Where(id => (ItemKind(id) & kinds) != 0 && IsSelectable(id));
    }
    private void ReindexSelection() => portIndex = new(Document.Snapshot.Ports.Where(p => index.IsVisible(p.NodeId))
        .Select(p => { var point = index.PortLocation(p.Id); return (new RectD(point.X, point.Y, 0, 0), p.Id); }));
    private void PublishSelection()
    {
        readSelection = new(selection); SelectionChanged?.Invoke(this, EventArgs.Empty); Invalidate();
    }

    /// <summary>Materializes inputs before mutation. Duplicates toggle only once; unknown and hidden items are ignored.</summary>
    public void ApplySelection(IEnumerable<string> ids, SelectionOperation operation = SelectionOperation.Replace)
    {
        ArgumentNullException.ThrowIfNull(ids);
        if (!Enum.IsDefined(operation)) throw new ArgumentOutOfRangeException(nameof(operation));
        var revision = Document.Revision;
        var incoming = ids.Where(IsSelectable).Distinct(StringComparer.Ordinal).ToArray();
        if (revision != Document.Revision) throw new InvalidOperationException("The graph changed while the selection was enumerated.");
        var next = CombineSelection(readSelection, incoming, operation);
        if (selection.SetEquals(next)) return;
        CancelGesture(false); selection.Clear(); selection.UnionWith(next); PublishSelection();
    }
    private static ReadOnlySelection CombineSelection(IEnumerable<string> original, IEnumerable<string> incoming, SelectionOperation operation)
    {
        var result = new HashSet<string>(original, StringComparer.Ordinal);
        switch (operation)
        {
            case SelectionOperation.Replace: return new(incoming);
            case SelectionOperation.Add: result.UnionWith(incoming); break;
            case SelectionOperation.Remove: result.ExceptWith(incoming); break;
            case SelectionOperation.Toggle: result.SymmetricExceptWith(incoming); break;
            default: throw new ArgumentOutOfRangeException(nameof(operation));
        }
        return new(result);
    }
    public void SelectAll() => SelectAll(SelectionKinds.All);
    public void SelectAll(SelectionKinds kinds) => ApplySelection(VisibleItems(kinds));
    public void ClearSelection() => ApplySelection([]);
    public void InvertSelection() => InvertSelection(SelectionKinds.All);
    public void InvertSelection(SelectionKinds kinds) => ApplySelection(VisibleItems(kinds), SelectionOperation.Toggle);

    /// <summary>Queries painted geometry. Expanded groups match their header/border; labels obey the current zoom LOD.</summary>
    public ImmutableArray<string> QueryArea(RectD rectangle, AreaSelectionMode mode = AreaSelectionMode.Intersects, SelectionKinds kinds = SelectionKinds.All)
    {
        ValidateKinds(kinds);
        if (mode is not (AreaSelectionMode.Intersects or AreaSelectionMode.Contains))
            throw new ArgumentOutOfRangeException(nameof(mode), "Directional mode requires a pointer drag.");
        Geometry.RectangleContainsRectangle(rectangle, rectangle); // Validate before any indexed query.
        var contain = mode == AreaSelectionMode.Contains; var result = new HashSet<string>(StringComparer.Ordinal);
        if ((kinds & (SelectionKinds.Nodes | SelectionKinds.Groups)) != 0)
            foreach (var id in nodeIndex.Query(rectangle))
            {
                if ((ItemKind(id) & kinds) == 0) continue;
                var node = index.Nodes[id]; var b = node.Bounds;
                bool match;
                if (contain) match = Geometry.RectangleContainsRectangle(rectangle, b);
                else if (node.IsGroup && !node.Collapsed)
                    match = rectangle.Intersects(new(b.X, b.Y, b.Width, Math.Min(28, b.Height))) ||
                        rectangle.Intersects(new(b.X, b.Y, Math.Min(6, b.Width), b.Height)) ||
                        rectangle.Intersects(new(Math.Max(b.X, b.Right - 6), b.Y, Math.Min(6, b.Width), b.Height)) ||
                        rectangle.Intersects(new(b.X, Math.Max(b.Y, b.Bottom - 6), b.Width, Math.Min(6, b.Height)));
                else match = Geometry.RectangleIntersectsNode(rectangle, node);
                if (match) result.Add(id);
            }
        if (kinds.HasFlag(SelectionKinds.Edges))
            foreach (var id in edgeIndex.Query(rectangle))
            {
                var path = paths[id];
                if (contain ? path.All(rectangle.Contains) : Enumerable.Range(1, path.Count - 1)
                    .Any(i => Geometry.RectangleIntersectsSegment(rectangle, path[i - 1], path[i]))) result.Add(id);
            }
        if (kinds.HasFlag(SelectionKinds.Ports))
        {
            var radius = 3.5 / Viewport.Zoom;
            foreach (var id in portIndex.Query(rectangle.Inflate(radius)))
            {
                var p = index.PortLocation(id); var bounds = new RectD(p.X - radius, p.Y - radius, radius * 2, radius * 2);
                var nearest = new PointD(Math.Clamp(p.X, rectangle.X, rectangle.Right), Math.Clamp(p.Y, rectangle.Y, rectangle.Bottom));
                if (contain ? Geometry.RectangleContainsRectangle(rectangle, bounds) : PointD.Distance(p, nearest) <= radius) result.Add(id);
            }
        }
        if (kinds.HasFlag(SelectionKinds.Labels) && Viewport.Zoom >= .25)
            foreach (var id in labelIndex.Query(rectangle))
                if (!contain || Geometry.RectangleContainsRectangle(rectangle, GetLabelBounds(index.Labels[id]))) result.Add(id);
        return result.Order(StringComparer.Ordinal).ToImmutableArray();
    }
    public void SelectArea(RectD rectangle, AreaSelectionMode mode = AreaSelectionMode.Intersects,
        SelectionKinds kinds = SelectionKinds.All, SelectionOperation operation = SelectionOperation.Replace)
        => ApplySelection(QueryArea(rectangle, mode, kinds), operation);

    private static SelectionOperation PointerSelectionOperation(InputModifiers keys) => keys.HasFlag(InputModifiers.Control)
        ? keys.HasFlag(InputModifiers.Shift) ? SelectionOperation.Remove : SelectionOperation.Toggle
        : keys.HasFlag(InputModifiers.Shift) ? SelectionOperation.Add : SelectionOperation.Replace;
    private void BeginMarquee(InputModifiers keys) { marqueeOriginal = readSelection; marqueeOperation = PointerSelectionOperation(keys); }
    private void UpdateMarquee(PointD screen)
    {
        Marquee = RectD.FromPoints(Viewport.ToWorld(start), Viewport.ToWorld(screen));
        IsMarqueeContainment = marqueeMode == AreaSelectionMode.Contains || marqueeMode == AreaSelectionMode.Directional && screen.X >= start.X;
        marqueePreview = CombineSelection(marqueeOriginal ?? readSelection, QueryArea(Marquee.Value,
            IsMarqueeContainment ? AreaSelectionMode.Contains : AreaSelectionMode.Intersects, marqueeItemKinds), marqueeOperation);
        Invalidate();
    }
    private void ClearMarquee() { marqueeOriginal = marqueePreview = null; IsMarqueeContainment = false; }

    /// <summary>Expands through visible directed/undirected topology. Folded external edges connect their visible group representatives.</summary>
    public void SelectNeighbors(NeighborDirection direction = NeighborDirection.Both, int depth = 1, bool includeEdges = true)
    {
        if (!Enum.IsDefined(direction) || depth < 1 || depth > 10000) throw new ArgumentOutOfRangeException(nameof(direction));
        var seeds = new HashSet<string>(StringComparer.Ordinal);
        void Seed(string owner)
        {
            if (index.Nodes.ContainsKey(owner)) seeds.Add(index.VisibleRepresentative(owner));
            else if (index.Ports.TryGetValue(owner, out var port)) seeds.Add(index.VisibleRepresentative(port.NodeId));
            else if (index.Edges.TryGetValue(owner, out var edge)) { seeds.Add(index.VisibleRepresentative(edge.SourceId)); seeds.Add(index.VisibleRepresentative(edge.TargetId)); }
        }
        foreach (var id in selection) Seed(index.Labels.TryGetValue(id, out var label) ? label.OwnerId : id);
        if (seeds.Count == 0) return;
        var adjacency = new Dictionary<string, List<(string Node, string Edge)>>(StringComparer.Ordinal);
        void Arc(string from, string to, string edge)
        { if (!adjacency.TryGetValue(from, out var list)) adjacency[from] = list = []; list.Add((to, edge)); }
        foreach (var edge in Document.Snapshot.Edges.Where(e => paths.ContainsKey(e.Id)))
        {
            var from = index.VisibleRepresentative(edge.SourceId); var to = index.VisibleRepresentative(edge.TargetId);
            if (!edge.Directed || direction != NeighborDirection.Incoming) Arc(from, to, edge.Id);
            if (!edge.Directed || direction != NeighborDirection.Outgoing) Arc(to, from, edge.Id);
        }
        var result = new HashSet<string>(selection, StringComparer.Ordinal); result.UnionWith(seeds);
        var seen = new HashSet<string>(seeds, StringComparer.Ordinal);
        var queue = new Queue<(string Id, int Level)>(seeds.Select(id => (id, 0)));
        while (queue.TryDequeue(out var item))
        {
            if (item.Level >= depth || !adjacency.TryGetValue(item.Id, out var links)) continue;
            foreach (var link in links)
            {
                result.Add(link.Node); if (includeEdges) result.Add(link.Edge);
                if (seen.Add(link.Node)) queue.Enqueue((link.Node, item.Level + 1));
            }
        }
        ApplySelection(result);
    }
}
