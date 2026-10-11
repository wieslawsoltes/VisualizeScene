using System.Collections.Immutable;
using VisualizeScene.Core;
using VisualizeScene.Editing;

static class SnappingTests
{
    private static void Check(bool condition) { if (!condition) throw new Exception("Assertion failed"); }
    private static void Near(double a, double b) => Check(Math.Abs(a - b) < 1e-7);
    private static GraphSnapshot Graph() => new([new("a", new(0, 0, 100, 60)), new("b", new(300, 200, 100, 60))], [], [], []);
    private static EditorSession Session(out GraphDocument document)
    { document = new(); document.Replace(Graph(), clearHistory: true); return new(document) { SnapToGrid = false }; }
    public static void Run(Action<string, Action> test)
    {
        test("Snapping: item edges override grid independently per axis", () =>
        { var i = new ItemSnapIndex(Graph()); var r = i.Snap(new(297, 89, 100, 60), new(3, 1), new HashSet<string> { "a" }, 1); Check(r.Correction == new PointD(3, 1) && r.Guides.Length == 1); Check(r.Guides[0].TargetId == "b"); });
        test("Snapping: center alignment works for unequal sizes", () =>
        { var i = new ItemSnapIndex(Graph()); var r = i.Snap(new(323, 90, 50, 20), new(), new HashSet<string> { "a" }, 1); Near(r.Correction.X, 2); });
        test("Snapping: tolerance is measured in screen coordinates", () =>
        { var i = new ItemSnapIndex(Graph()); var excluded = new HashSet<string> { "a" }; var p = new RectD(296, 89, 100, 60); Check(i.Snap(p, new(), excluded, 2).Guides.IsEmpty); Check(i.Snap(p, new(), excluded, .5).Guides.Length == 1); });
        test("Snapping: both axes can align simultaneously", () =>
        { var r = new ItemSnapIndex(Graph()).Snap(new(298, 196, 100, 60), new(), new HashSet<string> { "a" }, 1); Check(r.Correction == new PointD(2, 4) && r.Guides.Length == 2); });
        test("Snapping: excluded moving items never attract themselves", () =>
        { var r = new ItemSnapIndex(Graph()).Snap(new(2, 2, 100, 60), new(), new HashSet<string> { "a", "b" }, 1); Check(r.Correction == new PointD() && r.Guides.IsEmpty); });
        test("Snapping: ties are deterministic under node reordering", () =>
        { var g = Graph() with { Nodes = [new("z", new(300, 200, 100, 60)), new("b", new(300, 400, 100, 60))] }; var a = new ItemSnapIndex(g).Snap(new(298, 90, 100, 60), new(), new HashSet<string>(), 1); var b = new ItemSnapIndex(g with { Nodes = g.Nodes.Reverse().ToImmutableArray() }).Snap(new(298, 90, 100, 60), new(), new HashSet<string>(), 1); Check(a.Correction == b.Correction && a.Guides.SequenceEqual(b.Guides) && a.Guides[0].TargetId == "b"); });
        test("Snapping: point geometry supports resize corner alignment", () =>
        { var r = new ItemSnapIndex(Graph()).Snap(new(397, 259, 0, 0), new(), new HashSet<string> { "a" }, 1); Check(r.Correction == new PointD(3, 1) && r.Guides.Length == 2); });
        test("Snapping: invalid geometry, zoom, correction and tolerance are rejected", () =>
        {
            var index = new ItemSnapIndex(Graph()); var excluded = new HashSet<string>();
            foreach (var action in new Action[]
            {
                () => index.Snap(new(0, 0, 10, 10), new(), excluded, 0),
                () => index.Snap(new(0, 0, 10, 10), new(), excluded, double.NaN),
                () => index.Snap(new(0, 0, 10, 10), new(), excluded, 1, -1),
                () => index.Snap(new(0, 0, 10, 10), new(double.PositiveInfinity, 0), excluded, 1),
                () => index.Snap(new(double.NaN, 0, 10, 10), new(), excluded, 1)
            })
            {
                var rejected = false;
                try { action(); } catch (ArgumentOutOfRangeException) { rejected = true; }
                Check(rejected);
            }
        });
        test("Snapping: empty index preserves supplied grid correction", () =>
        { var r = new ItemSnapIndex(GraphSnapshot.Empty).Snap(new(1, 2, 20, 30), new(4, 5), new HashSet<string>(), 1); Check(r.Correction == new PointD(4, 5) && r.Guides.IsEmpty); });
        test("Snapping: hidden nodes and expanded group frames are excluded", () =>
        { var g = new GraphSnapshot([new("g", new(300, 200, 300, 300), IsGroup: true), new("h", new(800, 0, 100, 100), IsGroup: true, Collapsed: true), new("child", new(300, 200, 100, 60), ParentId: "h")], [], [], []); Check(new ItemSnapIndex(g).Snap(new(297, 196, 100, 60), new(), new HashSet<string>(), 1).Guides.IsEmpty); });
        test("Snapping: preview does not mutate document; release commits once", () =>
        { using var s = Session(out var d); s.PointerDown(new(50, 30)); s.PointerMove(new(347, 118)); Check(!d.CanUndo && !s.AlignmentGuides.IsEmpty); Near(s.DisplayGraph.Nodes[0].Bounds.X, 300); s.PointerUp(new(347, 118)); Near(d.Snapshot.Nodes[0].Bounds.X, 300); Check(s.AlignmentGuides.IsEmpty); d.Undo(); Check(!d.CanUndo && d.Snapshot.Nodes[0].Bounds.X == 0); });
        test("Snapping: cancel clears guides and restores original geometry", () =>
        { using var s = Session(out var d); s.PointerDown(new(50, 30)); s.PointerMove(new(347, 118)); Check(!s.AlignmentGuides.IsEmpty); s.CancelGesture(); s.PointerUp(new(347, 118)); Check(s.AlignmentGuides.IsEmpty && !d.CanUndo); });
        test("Snapping: moving out of range clears preview lines", () =>
        { using var s = Session(out var d); s.PointerDown(new(50, 30)); s.PointerMove(new(347, 118)); Check(!s.AlignmentGuides.IsEmpty); s.PointerMove(new(271, 118)); Check(s.AlignmentGuides.IsEmpty); s.CancelGesture(); });
        test("Snapping: toggling item snapping off preserves free movement", () =>
        { using var s = Session(out var d); s.SnapToItems = false; s.PointerDown(new(50, 30)); s.PointerUp(new(347, 118)); Near(d.Snapshot.Nodes[0].Bounds.X, 297); Check(s.AlignmentGuides.IsEmpty); });
        test("Snapping: document revision changes discard guides and stale preview", () =>
        { using var s = Session(out var d); s.PointerDown(new(50, 30)); s.PointerMove(new(347, 118)); d.Edit("rename", e => e.Update(e.Node("a") with { Text = "changed" })); s.PointerUp(new(347, 118)); Check(s.AlignmentGuides.IsEmpty && d.Snapshot.Nodes[0].Bounds.X == 0); });
        test("Snapping: selected group descendants do not attract their owner", () =>
        { using var s = Session(out var d); string group = ""; d.Edit("group", e => group = e.Group(["a", "b"])); s.Select([group]); var b = d.Snapshot.Index().Nodes[group].Bounds; s.PointerDown(new(b.Center.X, b.Y + 15)); s.PointerMove(new(b.Center.X + 3, b.Y + 18)); Check(s.AlignmentGuides.IsEmpty); s.CancelGesture(); });
        test("Snapping: keyboard nudge remains exact rather than magnetic", () =>
        { using var s = Session(out var d); s.Select(["a"]); s.Nudge(new(297, 89)); Near(d.Snapshot.Nodes[0].Bounds.X, 297); Check(s.AlignmentGuides.IsEmpty); });
        test("Snapping: out-and-back gesture adds no history", () =>
        { using var s = Session(out var d); s.PointerDown(new(50, 30)); s.PointerMove(new(347, 118)); s.PointerUp(new(50, 30)); Check(!d.CanUndo && s.AlignmentGuides.IsEmpty); });
    }
}
