using VisualizeScene.Core;
using VisualizeScene.Editing;

static class LabelTests
{
    static void Check(bool condition) { if (!condition) throw new Exception("Assertion failed"); }
    static void Near(double actual, double expected) => Check(Math.Abs(actual - expected) < 1e-7);
    static GraphDocument Document()
    {
        var d = new GraphDocument(); d.Replace(new([new("a", new(0, 0, 100, 60)), new("b", new(300, 0, 100, 60))],
            [new("ab", "a", "b", Routing: EdgeRouting.Polyline, Bends: [new(150, 120), new(250, 120)])],
            [new("ap", "a", new(1, .5))], [new("la", "a", "Owner label", new(0, 100)), new("le", "ab", "Edge label", new(0, 50))]), clearHistory: true);
        return d;
    }
    public static void Run(Action<string, Action> test)
    {
        test("Labels: measured bounds participate in hit testing", () =>
        {
            using var s = new EditorSession(Document()) { LabelSizeProvider = _ => new(80, 20) };
            Check(s.GetLabelBounds(s.Document.Snapshot.Labels[0]) == new RectD(10, 120, 80, 20));
            Check(s.Hit(new(50, 130)).Kind == HitKind.Label);
        });
        test("Labels: pointer drag commits one offset change and undo", () =>
        {
            var d = Document(); using var s = new EditorSession(d) { SnapToGrid = false };
            s.PointerDown(new(50, 130)); s.PointerMove(new(90, 160)); Check(!d.CanUndo);
            Check(s.DisplayGraph.Labels[0].Offset == new PointD(40, 130)); s.PointerUp(new(90, 160));
            Check(d.Snapshot.Labels[0].Offset == new PointD(40, 130)); d.Undo(); Check(!d.CanUndo);
        });
        test("Labels: canceled and out-and-back drags leave no history", () =>
        {
            var d = Document(); using var s = new EditorSession(d);
            s.PointerDown(new(50, 130)); s.PointerMove(new(120, 190)); s.CancelGesture(); s.PointerUp(new(120, 190)); Check(!d.CanUndo);
            s.PointerDown(new(50, 130)); s.PointerMove(new(120, 190)); s.PointerUp(new(50, 130)); Check(!d.CanUndo);
        });
        test("Labels: multi-select owner plus label moves only once", () =>
        {
            var d = Document(); using var s = new EditorSession(d); s.Select(["a", "la"]); s.Nudge(new(20, 10));
            Check(d.Snapshot.Labels[0].Offset == new PointD(0, 100));
            Check(GraphScene.LabelAnchor(d.Snapshot.Index(), "a") + d.Snapshot.Labels[0].Offset == new PointD(70, 140));
        });
        test("Labels: selected edge label compensates for changing path anchor", () =>
        {
            var d = Document(); using var s = new EditorSession(d); var before = GraphScene.LabelAnchor(d.Snapshot.Index(), "ab") + d.Snapshot.Labels[1].Offset;
            s.Select(["a", "le"]); s.Nudge(new(20, 10)); var after = GraphScene.LabelAnchor(d.Snapshot.Index(), "ab") + d.Snapshot.Labels[1].Offset;
            Near(after.X - before.X, 20); Near(after.Y - before.Y, 10);
        });
        test("Labels: add, rename and delete are independently undoable", () =>
        {
            var d = Document(); using var s = new EditorSession(d); var id = s.AddLabel("ap", "Port label"); s.Rename(id, "Renamed");
            Check(d.Snapshot.Index().Labels[id].Text == "Renamed"); s.DeleteSelection(); Check(!d.Snapshot.Index().Labels.ContainsKey(id));
            d.Undo(); Check(d.Snapshot.Index().Labels[id].Text == "Renamed"); d.Undo(); Check(d.Snapshot.Index().Labels[id].Text == "Port label");
        });
        test("Labels: duplicate copies only labels, not their owner nodes", () =>
        {
            var d = Document(); using var s = new EditorSession(d); s.Select(["la"]); s.Duplicate();
            Check(d.Snapshot.Nodes.Length == 2 && d.Snapshot.Labels.Length == 3);
            Check(d.Snapshot.Labels[^1].OwnerId == "a" && d.Snapshot.Labels[^1].Offset == new PointD(30, 130));
        });
        test("Labels: copy and paste retargets the selected owner", () =>
        {
            var d = Document(); using var s = new EditorSession(d); s.Select(["la"]); var payload = s.Copy(); GraphValidation.Validate(payload);
            s.Select(["b"]); s.Paste(); Check(d.Snapshot.Nodes.Length == 2 && d.Snapshot.Labels[^1].OwnerId == "b");
        });
        test("Labels: cut and paste preserves owner without duplicating nodes", () =>
        {
            var d = Document(); using var s = new EditorSession(d); s.Select(["la"]); s.Copy(); s.DeleteSelection(); s.Paste();
            Check(d.Snapshot.Nodes.Length == 2 && d.Snapshot.Labels.Length == 2 && d.Snapshot.Labels[^1].OwnerId == "a");
        });
        test("Labels: locked owner prevents geometry changes", () =>
        {
            var d = Document(); d.Edit("Lock", e => e.Update(e.Node("a") with { Locked = true })); var revision = d.Revision;
            using var s = new EditorSession(d); s.Select(["la"]); s.Nudge(new(30, 20)); Check(d.Revision == revision);
        });
        test("Labels: folded owners, internal edges and child self-loops are invisible", () =>
        {
            var d = Document(); string group = ""; d.Edit("Group", e =>
            {
                e.Add(new GraphEdge("loop", "a", "a")); e.Add(new GraphLabel("ll", "loop", "Hidden loop label", new(0, 300)));
                group = e.Group(["a", "b"]); e.Update(e.Node(group) with { Collapsed = true });
            });
            var i = d.Snapshot.Index(); Check(!GraphScene.IsOwnerVisible(i, "a") && !GraphScene.IsOwnerVisible(i, "ab") && !GraphScene.IsOwnerVisible(i, "loop"));
            using var s = new EditorSession(d); s.SelectAll(); Check(s.Selection.SetEquals([group]));
            Check(GraphScene.Bounds(d.Snapshot) == i.Nodes[group].Bounds);
        });
        test("Scene bounds: include remote bends and label extents", () =>
        {
            var d = Document(); d.Edit("Remote geometry", e => { e.Update(e.Edge("ab") with { Bends = [new(800, 600)] }); e.Add(new GraphLabel("far", "b", "far", new(0, 1000))); });
            var bounds = GraphScene.Bounds(d.Snapshot, _ => new(80, 20)); Check(bounds.Right >= 800 && bounds.Bottom >= 1040);
        });
        test("Arc length: skips degenerate segments and does not use chord midpoint", () =>
        {
            PointD[] path = [new(0, 0), new(0, 0), new(100, 0), new(100, 300)];
            Check(Geometry.PointAtFraction(path, .5) == new PointD(100, 100)); Check(Geometry.PointAtFraction(path, 0) == path[0]); Check(Geometry.PointAtFraction(path, 1) == path[^1]);
        });
        test("Shape hit testing: polygon and rounded corners are not rectangular", () =>
        {
            foreach (var shape in new[] { NodeShape.Hexagon, NodeShape.Parallelogram, NodeShape.RoundedRectangle })
                Check(!Geometry.HitNode(new("n", new(0, 0, 100, 60), Shape: shape), new(0, 0)));
        });
        foreach (var shape in Enum.GetValues<NodeShape>()) test("Shape clipping: " + shape + " boundary agrees with hit testing", () =>
        {
            var node = new GraphNode("n", new(-10, 20, 120, 80), Shape: shape);
            for (var k = 0; k < 32; k++)
            {
                var direction = new PointD(Math.Cos(k * Math.PI / 16), Math.Sin(k * Math.PI / 16));
                var point = Geometry.ClipToNode(node, node.Bounds.Center + direction * 500);
                Check(Geometry.HitNode(node, point - direction * .0001)); Check(!Geometry.HitNode(node, point + direction * .001));
            }
        });
        test("Self-loops: explicit ports remain exact endpoints outside owner", () =>
        {
            var g = new GraphSnapshot([new("a", new(0, 0, 100, 60))], [new("loop", "a", "a", SourcePortId: "left", TargetPortId: "bottom")],
                [new("left", "a", new(0, .5)), new("bottom", "a", new(.5, 1))], []);
            var path = g.Index().EdgePath(g.Edges[0]); Check(path[0] == new PointD(0, 30) && path[^1] == new PointD(50, 60));
            for (var k = 1; k < path.Count; k++) Check(!new RectD(.001, .001, 99.998, 59.998).Contains(PointD.Lerp(path[k - 1], path[k], .5)));
        });
        test("Self-loops: manual bends are not replaced by default routing", () =>
        {
            var g = new GraphSnapshot([new("a", new(0, 0, 100, 60))], [new("loop", "a", "a", Bends: [new(-100, 30), new(-100, 150)])], [], []);
            var path = g.Index().EdgePath(g.Edges[0]); Check(path.Count == 4 && path[1] == new PointD(-100, 30));
        });
        test("Self-loops: same-port loop remains non-degenerate", () =>
        {
            var path = Geometry.SelfLoop(new(0, 0, 100, 60), new(100, 30), new(100, 30));
            Check(path.Distinct().Count() >= 5 && path[0] == path[^1]);
        });
        test("Double click: independent labels can activate editing", () =>
        {
            var r = new DoubleClickRecognizer(); var hit = new HitResult(HitKind.Label, "label");
            r.Press(hit, EditorMode.Select, InputModifiers.None, new(), 1, 0); Check(r.Release(new(), 1, 10) is null);
            r.Press(hit, EditorMode.Select, InputModifiers.None, new(), 1, 100); Check(r.Release(new(), 1, 110) == hit);
        });
    }
}
