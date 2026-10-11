using System.Collections.Immutable;
using VisualizeScene.Core;
using VisualizeScene.Editing;

static class SelectionTests
{
    private static void Check(bool value) { if (!value) throw new Exception("Assertion failed"); }
    private static void Equal(IEnumerable<string> actual, params string[] expected)
    { if (!actual.ToHashSet().SetEquals(expected)) throw new Exception($"Expected [{string.Join(",", expected)}], got [{string.Join(",", actual)}]"); }
    private static void Throws<T>(Action action) where T : Exception
    { try { action(); } catch (T) { return; } throw new Exception("Expected " + typeof(T).Name); }
    private static GraphSnapshot Graph() => new(
        [new("a", new(0, 0, 100, 60)), new("b", new(300, 0, 100, 60)), new("c", new(300, 200, 100, 60))],
        [new("ab", "a", "b", Routing: EdgeRouting.Polyline), new("bc", "b", "c", Routing: EdgeRouting.Polyline)],
        [new("ap", "a", new(1, .5))], [new("la", "a", "Outside annotation", new(0, 110))]);
    private static EditorSession Session(out GraphDocument document, GraphSnapshot? graph = null)
    {
        document = new(); document.Replace(graph ?? Graph(), clearHistory: true);
        return new(document) { LabelSizeProvider = _ => new(80, 20), SnapToGrid = false, SnapToItems = false };
    }
    public static void Run(Action<string, Action> test)
    {
        test("Selection: aliases and lazy enumerations are materialized before replacement", () =>
        { using var s = Session(out _); s.Select(["a", "b"]); s.Select(s.Selection); Equal(s.Selection, "a", "b"); s.Select(s.Selection.Where(id => id == "a")); Equal(s.Selection, "a"); });
        test("Selection: immutable published snapshots cannot mutate the session", () =>
        { using var s = Session(out _); s.Select(["a"]); var old = s.Selection; s.Select(["b"]); Equal(old, "a"); Check(old is not ISet<string> && old is not ICollection<string>); });
        test("Selection: enumeration failure does not partially update selection", () =>
        {
            using var s = Session(out _); s.Select(["a"]);
            IEnumerable<string> Broken() { yield return "b"; throw new InvalidOperationException("Broken enumeration"); }
            Throws<InvalidOperationException>(() => s.Select(Broken())); Equal(s.Selection, "a");
        });
        test("Selection: enumeration against a changed revision is rejected", () =>
        {
            using var s = Session(out var d); s.Select(["a"]);
            IEnumerable<string> Stale() { yield return "b"; d.Edit("Rename", e => e.Update(e.Node("c") with { Text = "changed" })); }
            Throws<InvalidOperationException>(() => s.Select(Stale())); Equal(s.Selection, "a");
        });
        test("Selection: duplicate toggle IDs toggle once and unknown IDs are ignored", () =>
        { using var s = Session(out _); s.Select(["a"]); s.ApplySelection(["a", "a", "missing"], SelectionOperation.Toggle); Check(s.Selection.Count == 0); });
        test("Selection: events see the complete selection and suppress unchanged membership", () =>
        { using var s = Session(out var d); var count = 0; s.SelectionChanged += (_, _) => { count++; Equal(s.Selection, "a", "b"); }; var r = d.Revision; s.Select(["a", "b"]); s.Select(["b", "a"]); Check(count == 1 && d.Revision == r && !d.CanUndo); });
        test("Selection: node-only inversion preserves selected items of other kinds", () =>
        { using var s = Session(out _); s.Select(["a", "la"]); s.InvertSelection(SelectionKinds.Nodes); Equal(s.Selection, "b", "c", "la"); });
        test("Selection: select-all includes visible ports and independent labels", () =>
        { using var s = Session(out _); s.SelectAll(); Equal(s.Selection, "a", "b", "c", "ab", "bc", "ap", "la"); s.ClearSelection(); Check(s.Selection.Count == 0); });
        test("Area: label-only rectangle returns measured label geometry", () =>
        { using var s = Session(out _); Equal(s.QueryArea(new(5, 125, 90, 30), kinds: SelectionKinds.Labels), "la"); });
        test("Area: containment rejects partially intersected labels", () =>
        { using var s = Session(out _); Check(s.QueryArea(new(5, 125, 45, 20), AreaSelectionMode.Contains, SelectionKinds.Labels).IsEmpty); Equal(s.QueryArea(new(5, 125, 90, 30), AreaSelectionMode.Contains, SelectionKinds.Labels), "la"); });
        test("Area: line crossing selects an edge with both endpoints outside", () =>
        { using var s = Session(out _); Equal(s.QueryArea(new(190, 20, 20, 20), kinds: SelectionKinds.Edges), "ab"); Check(s.QueryArea(new(190, 20, 20, 20), AreaSelectionMode.Contains, SelectionKinds.Edges).IsEmpty); });
        test("Area: polyline bounding-box false positives are rejected", () =>
        {
            var g = Graph() with { Edges = [new("ac", "a", "c", Bends: [new(200, 30), new(200, 230)])] };
            using var s = Session(out _, g); Check(s.QueryArea(new(120, 100, 20, 20), kinds: SelectionKinds.Edges).IsEmpty);
            Equal(s.QueryArea(new(195, 100, 10, 20), kinds: SelectionKinds.Edges), "ac");
        });
        test("Area: port circles use screen-space radius", () =>
        { using var s = Session(out _); Equal(s.QueryArea(new(103, 29, 0, 1), kinds: SelectionKinds.Ports), "ap"); s.Viewport.ZoomAt(new(), 2); Check(s.QueryArea(new(103, 29, 0, 1), kinds: SelectionKinds.Ports).IsEmpty); });
        test("Area: port containment requires the entire painted circle", () =>
        { using var s = Session(out _); Check(s.QueryArea(new(99, 29, 2, 2), AreaSelectionMode.Contains, SelectionKinds.Ports).IsEmpty); Equal(s.QueryArea(new(96, 26, 8, 8), AreaSelectionMode.Contains, SelectionKinds.Ports), "ap"); });
        test("Area: low-detail hidden labels cannot be selected with a marquee", () =>
        { using var s = Session(out _); s.Viewport.ZoomAt(new(), .1); Check(s.QueryArea(new(5, 125, 90, 30), kinds: SelectionKinds.Labels).IsEmpty); });
        test("Area: invalid bounds, modes and flags fail before selection changes", () =>
        {
            using var s = Session(out _); s.Select(["a"]);
            Throws<ArgumentOutOfRangeException>(() => s.SelectArea(new(double.NaN, 0, 10, 10)));
            Throws<ArgumentOutOfRangeException>(() => s.QueryArea(new(), AreaSelectionMode.Directional));
            Throws<ArgumentOutOfRangeException>(() => s.QueryArea(new(), kinds: (SelectionKinds)64));
            Throws<ArgumentOutOfRangeException>(() => s.MarqueeItemKinds = (SelectionKinds)(-1));
            Throws<ArgumentOutOfRangeException>(() => s.MarqueeMode = (AreaSelectionMode)99);
            Throws<ArgumentOutOfRangeException>(() => s.ApplySelection(["b"], (SelectionOperation)99)); Equal(s.Selection, "a");
        });
        test("Area: expanded group interiors are not selectable frames", () =>
        {
            using var s = Session(out _, new([new("g", new(0, 0, 500, 400), IsGroup: true)], [], [], []));
            Check(s.QueryArea(new(100, 100, 50, 50), kinds: SelectionKinds.Groups).IsEmpty);
            Equal(s.QueryArea(new(100, 10, 50, 10), kinds: SelectionKinds.Groups), "g");
            Equal(s.QueryArea(new(-1, -1, 502, 402), AreaSelectionMode.Contains, SelectionKinds.Groups), "g");
        });
        test("Area: folded child nodes, labels, ports and internal edges stay hidden", () =>
        {
            var g = Graph(); using var s = Session(out var d, g); string id = "";
            d.Edit("Group", e => { id = e.Group(["a", "b", "c"]); e.Update(e.Node(id) with { Collapsed = true }); });
            s.SelectAll(); Equal(s.Selection, id); s.Select(["a", "ap", "la", "ab"]); Check(s.Selection.Count == 0);
            Equal(s.QueryArea(new(-100, -100, 700, 500)), id);
        });
        test("Selection: external folding prunes stale selections", () =>
        {
            using var s = Session(out var d); string id = ""; d.Edit("Group", e => id = e.Group(["a", "b", "c"])); s.Select(["a", "ap", "la", "ab"]);
            d.Edit("Fold", e => e.Update(e.Node(id) with { Collapsed = true })); Check(s.Selection.Count == 0);
        });
        test("Marquee: preview highlights independently and cancel restores selection", () =>
        { using var s = Session(out var d); s.Select(["b"]); var r = d.Revision; s.PointerDown(new(5, 125)); s.PointerMove(new(95, 155)); Equal(s.Selection, "b"); Equal(s.DisplaySelection, "la"); s.CancelGesture(); Equal(s.DisplaySelection, "b"); Check(s.Marquee is null && d.Revision == r && !d.CanUndo); });
        test("Marquee: release publishes once without graph history", () =>
        { using var s = Session(out var d); s.Select(["b"]); var count = 0; s.SelectionChanged += (_, _) => count++; s.PointerDown(new(5, 125)); s.PointerUp(new(95, 155)); Equal(s.Selection, "la"); Check(count == 1 && !d.CanUndo && s.Marquee is null); });
        test("Marquee: shift adds, control toggles, control-shift subtracts", () =>
        {
            using var s = Session(out _); s.Select(["b"]);
            s.PointerDown(new(5, 125), InputModifiers.Shift); s.PointerUp(new(95, 155)); Equal(s.Selection, "b", "la");
            s.PointerDown(new(5, 125), InputModifiers.Control); s.PointerUp(new(95, 155)); Equal(s.Selection, "b");
            s.Select(["b", "la"]); s.PointerDown(new(5, 125), InputModifiers.Control | InputModifiers.Shift); s.PointerUp(new(95, 155)); Equal(s.Selection, "b");
        });
        test("Marquee: directional window versus crossing semantics", () =>
        {
            using var s = Session(out _); s.MarqueeMode = AreaSelectionMode.Directional;
            s.PointerDown(new(5, 125)); s.PointerMove(new(50, 145)); Check(s.IsMarqueeContainment); s.PointerUp(new(50, 145)); Check(s.Selection.Count == 0);
            s.PointerDown(new(50, 125)); s.PointerMove(new(5, 145)); Check(!s.IsMarqueeContainment); s.PointerUp(new(5, 145)); Equal(s.Selection, "la");
        });
        test("Marquee: out-and-back drag does not clear the existing selection", () =>
        { using var s = Session(out _); s.Select(["b"]); s.PointerDown(new(5, 125)); s.PointerMove(new(95, 155)); s.PointerUp(new(5, 125)); Equal(s.Selection, "b"); });
        test("Marquee: empty single click clears; modified empty click preserves", () =>
        { using var s = Session(out _); s.Select(["b"]); s.PointerDown(new(5, 125), InputModifiers.Control); s.PointerUp(new(5, 125)); Equal(s.Selection, "b"); s.PointerDown(new(5, 125)); s.PointerUp(new(5, 125)); Check(s.Selection.Count == 0); });
        test("Marquee: changing mode cancels the preview", () =>
        { using var s = Session(out _); s.Select(["b"]); s.PointerDown(new(5, 125)); s.PointerMove(new(95, 155)); s.MarqueeMode = AreaSelectionMode.Contains; Check(s.Marquee is null); Equal(s.DisplaySelection, "b"); });
        test("Marquee: revision changes cancel stale selection previews", () =>
        { using var s = Session(out var d); s.Select(["b"]); s.PointerDown(new(5, 125)); s.PointerMove(new(95, 155)); d.Edit("Rename", e => e.Update(e.Node("a") with { Text = "changed" })); s.PointerUp(new(95, 155)); Equal(s.Selection, "b"); Check(s.Marquee is null); });
        test("Marquee: labels-only filter excludes nodes and ports", () =>
        { using var s = Session(out _); s.MarqueeItemKinds = SelectionKinds.Labels; s.PointerDown(new(-10, -10)); s.PointerUp(new(110, 155)); Equal(s.Selection, "la"); });
        test("Selection: deselecting a drag anchor cannot move another selected item", () =>
        { using var s = Session(out var d); s.Select(["a", "b"]); s.PointerDown(new(50, 30), InputModifiers.Control); s.PointerUp(new(80, 50)); Equal(s.Selection, "b"); Check(!d.CanUndo && d.Snapshot.Nodes[1].Bounds.X == 300); });
        test("Selection: shift-click adds without toggling selected items off", () =>
        { using var s = Session(out _); s.Select(["a"]); s.PointerDown(new(50, 30), InputModifiers.Shift); s.PointerUp(new(50, 30)); Equal(s.Selection, "a"); });
        test("Neighbors: outgoing expansion respects depth", () =>
        { using var s = Session(out var d); s.Select(["a"]); s.SelectNeighbors(NeighborDirection.Outgoing); Equal(s.Selection, "a", "b", "ab"); s.Select(["a"]); s.SelectNeighbors(NeighborDirection.Outgoing, 2); Equal(s.Selection, "a", "b", "c", "ab", "bc"); Check(!d.CanUndo); });
        test("Neighbors: incoming expansion does not traverse outgoing links", () =>
        { using var s = Session(out _); s.Select(["c"]); s.SelectNeighbors(NeighborDirection.Incoming); Equal(s.Selection, "b", "c", "bc"); });
        test("Neighbors: undirected edges work in either direction", () =>
        { var g = Graph(); g = g with { Edges = [g.Edges[0] with { Directed = false }, g.Edges[1]] }; using var s = Session(out _, g); s.Select(["b"]); s.SelectNeighbors(NeighborDirection.Outgoing); Equal(s.Selection, "a", "b", "c", "ab", "bc"); });
        test("Neighbors: label and port selections resolve their owners", () =>
        { using var s = Session(out _); s.Select(["la", "ap"]); s.SelectNeighbors(NeighborDirection.Outgoing); Equal(s.Selection, "la", "ap", "a", "b", "ab"); });
        test("Neighbors: folded external edges use visible group representatives", () =>
        { using var s = Session(out var d); string id = ""; d.Edit("Group", e => { id = e.Group(["a"]); e.Update(e.Node(id) with { Collapsed = true }); }); s.Select([id]); s.SelectNeighbors(NeighborDirection.Outgoing); Equal(s.Selection, id, "b", "ab"); });
        test("Neighbors: cycles terminate and edge selection is optional", () =>
        { var g = Graph(); g = g with { Edges = g.Edges.Add(new("ca", "c", "a")) }; using var s = Session(out _, g); s.Select(["a"]); s.SelectNeighbors(NeighborDirection.Both, 10000, false); Equal(s.Selection, "a", "b", "c"); });
        test("Neighbors: malformed direction/depth leaves selection unchanged", () =>
        { using var s = Session(out _); s.Select(["a"]); Throws<ArgumentOutOfRangeException>(() => s.SelectNeighbors(depth: 0)); Throws<ArgumentOutOfRangeException>(() => s.SelectNeighbors((NeighborDirection)42)); Equal(s.Selection, "a"); });
        foreach (var shape in Enum.GetValues<NodeShape>()) test("Area geometry: " + shape + " containment and center crossing", () =>
        { var n = new GraphNode("n", new(0, 0, 100, 60), Shape: shape); Check(Geometry.RectangleIntersectsNode(new(49, -10, 2, 80), n)); Check(Geometry.RectangleIntersectsNode(new(-1, -1, 102, 62), n)); Check(!Geometry.RectangleIntersectsNode(new(200, 200, 1, 1), n)); });
        test("Area geometry: nonrectangular bounding corners are rejected", () =>
        { foreach (var shape in Enum.GetValues<NodeShape>().Where(s => s != NodeShape.Rectangle)) Check(!Geometry.RectangleIntersectsNode(new(0, 0, 1, 1), new("n", new(0, 0, 100, 60), Shape: shape))); });
        test("Area geometry: segment tangency and zero-length points are handled", () =>
        { var r = new RectD(0, 0, 10, 10); Check(Geometry.RectangleIntersectsSegment(r, new(-5, 0), new(15, 0))); Check(Geometry.RectangleIntersectsSegment(r, new(5, 5), new(5, 5))); Check(!Geometry.RectangleIntersectsSegment(r, new(-5, 11), new(15, 11))); });
        test("Area geometry: extreme finite segments avoid subtraction overflow", () =>
        { Check(Geometry.RectangleIntersectsSegment(new(-1, -1, 2, 2), new(-1e308, 0), new(1e308, 0))); });
        test("Area geometry: malformed public arguments are rejected", () =>
        { Throws<ArgumentOutOfRangeException>(() => Geometry.RectangleIntersectsSegment(new(0, 0, -1, 10), new(), new())); Throws<ArgumentException>(() => Geometry.RectangleIntersectsSegment(new(), new(double.NaN, 0), new())); });
        test("Area: ten-thousand-node indexed fixture returns only the local items", () =>
        {
            var nodes = Enumerable.Range(0, 10000).Select(i => new GraphNode("n" + i, new(i % 100 * 200, i / 100 * 200, 100, 60))).ToImmutableArray();
            using var s = Session(out _, new(nodes, [], [], [])); Equal(s.QueryArea(new(-1, -1, 102, 62), kinds: SelectionKinds.Nodes), "n0");
        });
    }
}
