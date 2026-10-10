using System.Collections.Immutable;
using VisualizeScene.Core;

namespace VisualizeScene.Editing;

public enum EditorMode { Select, CreateNode, CreateEdge, Pan }
public enum Alignment { Left, HorizontalCenter, Right, Top, VerticalCenter, Bottom, DistributeHorizontal, DistributeVertical }
[Flags] public enum InputModifiers { None = 0, Shift = 1, Control = 2, Alt = 4 }
public enum HitKind { None, Node, Edge, Port, ResizeHandle, Bend, SourceHandle, TargetHandle }
public sealed record HitResult(HitKind Kind, string? Id = null, int Part = -1);

/// <summary>Input/command state shared by desktop, browser and headless tests. A gesture commits one history entry.</summary>
public sealed partial class EditorSession : IDisposable
{
    private readonly HashSet<string> selection = [];
    private GraphIndex index;
    private SpatialIndex<string> nodeIndex;
    private SpatialIndex<string> edgeIndex;
    private Dictionary<string, IReadOnlyList<PointD>> paths = [];
    private GraphSnapshot? preview;
    private GraphSnapshot? clipboard;
    private PointD start, last;
    private HitResult? drag;
    private InputModifiers modifiers;
    private bool panning, collapseSelectionOnClick, gestureHasMoved;
    private long startRevision;
    private string? sourceNode, sourcePort;
    public GraphDocument Document { get; }
    public GraphSnapshot DisplayGraph => preview ?? Document.Snapshot;
    public Viewport Viewport { get; } = new();
    public IReadOnlySet<string> Selection => selection;
    private EditorMode mode;
    public EditorMode Mode
    {
        get => mode;
        set { if (!Enum.IsDefined(value)) throw new ArgumentOutOfRangeException(nameof(value)); if (mode == value) return; CancelGesture(false); mode = value; Invalidate(); }
    }
    public NodeShape DefaultShape { get; set; } = NodeShape.RoundedRectangle;
    public string DefaultFill { get; set; } = "#DDEBFA";
    public string DefaultStroke { get; set; } = "#4778A5";
    public string DefaultText { get; set; } = "Node";
    public bool SnapToGrid { get; set; } = true;
    public double GridSize { get; set; } = 10;
    public bool ShowGrid { get; set; } = true;
    public RectD? Marquee { get; private set; }
    public (PointD Start, PointD End)? EdgePreview { get; private set; }
    public event EventHandler? Invalidated;
    public event EventHandler? SelectionChanged;
    public event EventHandler<string>? StatusChanged;
    public EditorSession(GraphDocument document)
    {
        Document = document; index = document.Snapshot.Index(); nodeIndex = new([]); edgeIndex = new([]);
        Reindex(); Document.Changed += DocumentChanged;
    }
    public void Dispose() => Document.Changed -= DocumentChanged;
    private void DocumentChanged(object? sender, GraphChangedEventArgs args)
    {
        CancelGesture(false); Reindex();
        var changed = selection.RemoveWhere(id => !index.Nodes.ContainsKey(id) && !index.Edges.ContainsKey(id) && !index.Ports.ContainsKey(id)) > 0;
        if (changed) SelectionChanged?.Invoke(this, EventArgs.Empty);
        StatusChanged?.Invoke(this, args.Operation); Invalidate();
    }
    private void Reindex()
    {
        index = Document.Snapshot.Index();
        nodeIndex = new(Document.Snapshot.Nodes.Where(n => index.IsVisible(n.Id)).Select(n => (n.Bounds, n.Id)));
        paths = Document.Snapshot.Edges.Where(e => index.VisibleRepresentative(e.SourceId) != index.VisibleRepresentative(e.TargetId) || e.SourceId == e.TargetId).ToDictionary(e => e.Id, index.EdgePath);
        edgeIndex = new(paths.Select(p => (RectD.Enclose(p.Value.Select(v => new RectD(v.X, v.Y, 0, 0))).Inflate(1), p.Key)));
    }
    public void Invalidate() => Invalidated?.Invoke(this, EventArgs.Empty);
    public void Select(IEnumerable<string> ids, bool additive = false)
    {
        if (!additive) selection.Clear();
        foreach (var id in ids) if (index.Nodes.ContainsKey(id) || index.Edges.ContainsKey(id) || index.Ports.ContainsKey(id)) selection.Add(id);
        SelectionChanged?.Invoke(this, EventArgs.Empty); Invalidate();
    }
    public void SelectAll() => Select(Document.Snapshot.Nodes.Where(n => index.IsVisible(n.Id)).Select(n => n.Id).Concat(paths.Keys));
    public PointD Snap(PointD point)
    {
        if (!SnapToGrid) return point;
        if (!double.IsFinite(GridSize) || GridSize <= 0) throw new InvalidOperationException("Grid size must be finite and positive.");
        return new(Math.Round(point.X / GridSize) * GridSize, Math.Round(point.Y / GridSize) * GridSize);
    }
    public HitResult Hit(PointD world, bool handles = true)
    {
        var tolerance = 7 / Viewport.Zoom;
        if (handles)
        {
            foreach (var id in selection)
            {
                if (index.Nodes.TryGetValue(id, out var node) && !node.Locked && index.IsVisible(id))
                {
                    var b = node.Bounds; PointD[] points = [new(b.X, b.Y), new(b.Right, b.Y), new(b.Right, b.Bottom), new(b.X, b.Bottom)];
                    for (var i = 0; i < points.Length; i++) if (PointD.Distance(world, points[i]) <= tolerance) return new(HitKind.ResizeHandle, id, i);
                }
                if (index.Edges.TryGetValue(id, out var edge) && paths.TryGetValue(id, out var path))
                {
                    if (index.IsVisible(edge.SourceId) && !index.Nodes[edge.SourceId].Locked && PointD.Distance(world, path[0]) <= tolerance) return new(HitKind.SourceHandle, id);
                    if (index.IsVisible(edge.TargetId) && !index.Nodes[edge.TargetId].Locked && PointD.Distance(world, path[^1]) <= tolerance) return new(HitKind.TargetHandle, id);
                    if (!edge.Bends.IsDefault)
                        for (var i = 0; i < edge.Bends.Length; i++) if (PointD.Distance(world, edge.Bends[i]) <= tolerance) return new(HitKind.Bend, id, i);
                }
            }
        }
        foreach (var p in Document.Snapshot.Ports) if (index.IsVisible(p.NodeId) && PointD.Distance(world, index.PortLocation(p.Id)) < tolerance) return new(HitKind.Port, p.Id);
        var candidates = nodeIndex.Query(new(world.X, world.Y, 0, 0)).ToHashSet();
        var hitNodes = Document.Snapshot.Nodes.Where(n => candidates.Contains(n.Id) && Geometry.HitNode(n, world)).Reverse().ToArray();
        var normal = hitNodes.FirstOrDefault(n => !n.IsGroup || n.Collapsed);
        if (normal is not null) return new(HitKind.Node, normal.Id);
        foreach (var id in edgeIndex.Query(new RectD(world.X, world.Y, 0, 0).Inflate(tolerance)))
        {
            var points = paths[id];
            for (var i = 1; i < points.Count; i++) if (Geometry.DistanceToSegment(world, points[i - 1], points[i]) <= tolerance) return new(HitKind.Edge, id);
        }
        var group = hitNodes.FirstOrDefault(n => world.Y < n.Bounds.Y + 25 || world.X < n.Bounds.X + 6 || world.X > n.Bounds.Right - 6 || world.Y > n.Bounds.Bottom - 6);
        return group is null ? new(HitKind.None) : new(HitKind.Node, group.Id);
    }
    public string AddNode(PointD center)
    {
        var position = Snap(center - new PointD(65, 28)); var id = GraphEdit.NewId();
        Document.Edit("Create node", e => e.Add(new GraphNode(id, new(position.X, position.Y, 130, 56), DefaultText, DefaultShape, DefaultFill, DefaultStroke)));
        Select([id]); return id;
    }
    public void PointerDown(PointD screen, InputModifiers keys = InputModifiers.None, bool middleButton = false)
    {
        start = last = screen; modifiers = keys; startRevision = Document.Revision; preview = null;
        collapseSelectionOnClick = gestureHasMoved = false; drag = null;
        panning = middleButton || Mode == EditorMode.Pan || keys.HasFlag(InputModifiers.Alt);
        if (panning) return;
        var world = Viewport.ToWorld(screen); var hit = Hit(world);
        if (Mode == EditorMode.CreateNode) { AddNode(world); return; }
        if (Mode == EditorMode.CreateEdge) { CreateEdgeContact(world); return; }
        if (hit.Kind is HitKind.Node or HitKind.Edge or HitKind.Port)
        {
            if (keys.HasFlag(InputModifiers.Control) || keys.HasFlag(InputModifiers.Shift))
            {
                if (!selection.Remove(hit.Id!)) selection.Add(hit.Id!); SelectionChanged?.Invoke(this, EventArgs.Empty); Invalidate();
            }
            else if (!selection.Contains(hit.Id!)) Select([hit.Id!]);
            else collapseSelectionOnClick = selection.Count > 1;
        }
        else if (hit.Kind == HitKind.None && !keys.HasFlag(InputModifiers.Shift) && !keys.HasFlag(InputModifiers.Control)) Select([]);
        drag = hit;
    }
    public void PointerMove(PointD screen)
    {
        if (panning) { Viewport.Pan(screen - last); last = screen; Invalidate(); return; }
        var world = Viewport.ToWorld(screen);
        if (sourceNode is not null) { PreviewConnection(world); return; }
        if (drag is null) return;
        if (Document.Revision != startRevision) { CancelGesture(); return; }
        if (PointD.Distance(start, screen) < 2)
        {
            preview = null; Marquee = null; EdgePreview = null; ConnectionCandidate = null; CandidatePosition = null;
            CandidateDecision = ConnectionDecision.Accept; Invalidate(); return;
        }
        gestureHasMoved = true;
        if (drag.Kind == HitKind.None) { Marquee = RectD.FromPoints(Viewport.ToWorld(start), world); Invalidate(); return; }
        if (drag.Kind is HitKind.SourceHandle or HitKind.TargetHandle)
        {
            PreviewConnection(world, index.Edges[drag.Id!], drag.Kind == HitKind.SourceHandle ? EdgeEnd.Source : EdgeEnd.Target); return;
        }
        var edit = new GraphEdit(Document.Snapshot);
        if (drag.Kind == HitKind.Port)
        {
            var port = index.Ports[drag.Id!]; if (!CanMovePort(port)) return;
            edit.Update(port with { RelativeLocation = RelativePortLocation(index.Nodes[port.NodeId], Snap(world)) });
        }
        else if (drag.Kind == HitKind.Node)
        {
            var original = index.Nodes[drag.Id!]; if (original.Locked) return;
            var delta = (screen - start) * (1 / Viewport.Zoom);
            if (SnapToGrid) delta = Snap(new PointD(original.Bounds.X, original.Bounds.Y) + delta) - new PointD(original.Bounds.X, original.Bounds.Y);
            edit.Move(selection, delta);
        }
        else if (drag.Kind == HitKind.ResizeHandle)
        {
            var n = index.Nodes[drag.Id!]; var p = Snap(world); var b = n.Bounds;
            var opposite = drag.Part switch { 0 => new PointD(b.Right, b.Bottom), 1 => new(b.X, b.Bottom), 2 => new(b.X, b.Y), _ => new(b.Right, b.Y) };
            var rect = RectD.FromPoints(opposite, p); rect = rect with { Width = Math.Max(20, rect.Width), Height = Math.Max(20, rect.Height) };
            edit.Update(n with { Bounds = rect });
        }
        else if (drag.Kind == HitKind.Bend)
        {
            var edge = index.Edges[drag.Id!]; edit.Update(edge with { Bends = edge.Bends.SetItem(drag.Part, Snap(world)) });
        }
        else return;
        preview = edit.Build(); Invalidate();
    }
    public void PointerUp(PointD screen)
    {
        if (panning) { panning = false; return; }
        if (drag is null) return;
        PointerMove(screen);
        if (drag is null) return; // A reentrant document change may have canceled the gesture.
        var clicked = collapseSelectionOnClick && !gestureHasMoved ? drag.Id : null;
        var next = preview;
        var operation = drag.Kind switch
        {
            HitKind.ResizeHandle => "Resize node", HitKind.Bend => "Move bend", HitKind.Port => "Move port",
            HitKind.SourceHandle => "Reconnect source", HitKind.TargetHandle => "Reconnect target", _ => "Move nodes"
        };
        var rectangle = Marquee; var additive = modifiers.HasFlag(InputModifiers.Control) || modifiers.HasFlag(InputModifiers.Shift);
        preview = null; drag = null; Marquee = null; EdgePreview = null; ConnectionCandidate = null; CandidatePosition = null; CandidateDecision = ConnectionDecision.Accept;
        if (next is not null && startRevision == Document.Revision) Document.Replace(next, operation);
        if (clicked is not null) Select([clicked]);
        if (rectangle.HasValue) Select(nodeIndex.Query(rectangle.Value).Where(id => !index.Nodes[id].IsGroup), additive);
        Invalidate();
    }
    public void CancelGesture(bool invalidate = true)
    {
        preview = null; drag = null; panning = collapseSelectionOnClick = gestureHasMoved = false; Marquee = null; sourceNode = sourcePort = null; EdgePreview = null; ConnectionCandidate = null; CandidatePosition = null; CandidateDecision = ConnectionDecision.Accept;
        if (invalidate) Invalidate();
    }
    public void DeleteSelection()
    {
        var ids = selection.ToArray(); Document.Edit("Delete selection", edit => { foreach (var id in ids) edit.Remove(id); }); Select([]);
    }
    public GraphSnapshot Copy()
    {
        clipboard = GraphClipboard.Copy(Document.Snapshot, selection); return clipboard;
    }
    public void Paste(GraphSnapshot? data = null)
    {
        data ??= clipboard; if (data is null) return;
        IReadOnlyList<string> added = [];
        Document.Edit("Paste", edit => added = GraphClipboard.Paste(edit, data, new(30, 30))); Select(added);
    }
    public void Duplicate() => Paste(GraphClipboard.Copy(Document.Snapshot, selection));
    public void Group()
    {
        string id = ""; Document.Edit("Group nodes", edit => id = edit.Group(selection)); Select([id]);
    }
    public void Ungroup()
    {
        var groups = selection.Where(id => index.Nodes.TryGetValue(id, out var n) && n.IsGroup).ToArray();
        Document.Edit("Ungroup", edit => { foreach (var id in groups) edit.Ungroup(id); });
    }
    public void ToggleGroup(string id)
    {
        var n = index.Nodes[id]; if (!n.IsGroup) return;
        Document.Edit(n.Collapsed ? "Expand group" : "Collapse group", edit => edit.Update(n with { Collapsed = !n.Collapsed }));
        Select([id]);
    }
    public void Rename(string id, string text)
    {
        Document.Edit("Edit label", edit => { if (index.Nodes.TryGetValue(id, out var n)) edit.Update(n with { Text = text }); else if (index.Edges.TryGetValue(id, out var e)) edit.Update(e with { Text = text }); });
    }
    public void Nudge(PointD delta) => Document.Edit("Move selection", edit => edit.Move(selection, delta));
    public void AddBend(string edgeId, PointD world)
    {
        var edge = index.Edges[edgeId]; var path = paths[edgeId]; var nearest = Enumerable.Range(1, path.Count - 1).MinBy(i => Geometry.DistanceToSegment(world, path[i - 1], path[i]));
        var bends = path.Skip(1).Take(path.Count - 2).ToList(); bends.Insert(nearest - 1, Snap(world));
        Document.Edit("Insert bend", edit => edit.Update(edge with { Bends = bends.ToImmutableArray() })); Select([edgeId]);
    }
    public void Align(Alignment alignment)
    {
        var nodes = selection.Where(index.Nodes.ContainsKey).Select(id => index.Nodes[id]).Where(n => !n.Locked).ToArray(); if (nodes.Length < 2) return;
        var bounds = RectD.Enclose(nodes.Select(n => n.Bounds)); var ordered = nodes.OrderBy(n => alignment == Alignment.DistributeVertical ? n.Bounds.Y : n.Bounds.X).ToArray();
        var gap = alignment == Alignment.DistributeVertical ? (bounds.Height - nodes.Sum(n => n.Bounds.Height)) / (nodes.Length - 1) : (bounds.Width - nodes.Sum(n => n.Bounds.Width)) / (nodes.Length - 1);
        var cursor = alignment == Alignment.DistributeVertical ? bounds.Y : bounds.X;
        Document.Edit("Align nodes", edit =>
        {
            foreach (var node in ordered)
            {
                var b = node.Bounds;
                var target = alignment switch
                {
                    Alignment.Left => b with { X = bounds.X }, Alignment.HorizontalCenter => b with { X = bounds.Center.X - b.Width / 2 }, Alignment.Right => b with { X = bounds.Right - b.Width },
                    Alignment.Top => b with { Y = bounds.Y }, Alignment.VerticalCenter => b with { Y = bounds.Center.Y - b.Height / 2 }, Alignment.Bottom => b with { Y = bounds.Bottom - b.Height },
                    Alignment.DistributeHorizontal => b with { X = cursor }, Alignment.DistributeVertical => b with { Y = cursor }, _ => b
                };
                edit.Move([node.Id], new(target.X - b.X, target.Y - b.Y)); cursor += (alignment == Alignment.DistributeVertical ? b.Height : b.Width) + gap;
            }
        });
    }
}
