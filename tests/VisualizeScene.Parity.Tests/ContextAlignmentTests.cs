using System.Collections.Immutable;
using VisualizeScene.Core;
using VisualizeScene.Editing;

static class ContextAlignmentTests
{
    static void Check(bool value) { if (!value) throw new Exception("Assertion failed"); }
    static void Throws<T>(Action action) where T : Exception { try { action(); } catch (T) { return; } throw new Exception("Expected " + typeof(T).Name); }
    static GraphSnapshot Graph() => new([new("a", new(0, 0, 100, 60)), new("b", new(300, 160, 150, 90)), new("c", new(600, 40, 100, 60))],
        [new("ab", "a", "b", Bends: [new(200, 80)]), new("bc", "b", "c")], [], []);
    static GraphDocument Document(GraphSnapshot? g = null) { var d = new GraphDocument(); d.Replace(g ?? Graph(), clearHistory: true); return d; }
    public static void Run(Action<string, Action> test)
    {
        test("Context: unselected right-click target replaces the old selection", () => {
            var d = Document(); using var s = new EditorSession(d); s.Select(["a"]); var g = d.Snapshot;
            Check(s.PrepareContext(new(350, 190)).Id == "b" && s.Selection.SetEquals(["b"])); Check(ReferenceEquals(g, d.Snapshot) && !d.CanUndo);
        });
        test("Context: selected target retains the whole multiselection and keyboard retains it", () => {
            using var s = new EditorSession(Document()); s.Select(["a", "b"]); var selection = s.Selection;
            s.PrepareContext(new(50, 30)); Check(ReferenceEquals(selection, s.Selection)); s.PrepareContext(); Check(ReferenceEquals(selection, s.Selection));
        });
        test("Context: blank canvas clears selection and cancels movement preview", () => {
            var d = Document(); using var s = new EditorSession(d); s.Select(["a"]); s.PointerDown(new(50, 30)); s.PointerMove(new(100, 80));
            s.PrepareContext(new(500, 500)); Check(s.Selection.Count == 0 && ReferenceEquals(s.DisplayGraph, d.Snapshot)); s.PointerUp(new(100, 80)); Check(!d.CanUndo);
        });
        test("Context: invalid coordinates preserve selection; reentrant edits reject the request", () => {
            var d = Document(); using var s = new EditorSession(d); s.Select(["a"]);
            Throws<ArgumentOutOfRangeException>(() => s.PrepareContext(new(double.NaN, 0))); Check(s.Selection.Contains("a"));
            s.SelectionChanged += (_, _) => d.Edit("Change during selection", e => e.Update(e.Node("c") with { Text = "changed" }));
            Throws<InvalidOperationException>(() => s.PrepareContext(new(350, 190)));
        });
        test("Alignment: all eight modes preserve single-step undo and relative port/label storage", () => {
            foreach (var mode in Enum.GetValues<Alignment>()) {
                var g = Graph(); var d = Document(g); using var s = new EditorSession(d); s.Select(["a", "b", "c"]); s.Align(mode);
                Check(d.Snapshot.Ports == g.Ports && d.Snapshot.Labels == g.Labels); d.Undo(); Check(ReferenceEquals(g, d.Snapshot) && !d.CanUndo);
            }
        });
        test("Alignment: group ownership translates descendants and internal bends exactly once", () => {
            var e = new GraphEdit(Graph()); var group = e.Group(["a", "b"]); var g = e.Build();
            var r = GraphAlignment.Calculate(g, [group, "a", "c"], Alignment.Right); var index = r.Graph.Index();
            var delta = index.Nodes[group].Bounds.X - g.Index().Nodes[group].Bounds.X;
            Check(index.Nodes["a"].Bounds.X == g.Index().Nodes["a"].Bounds.X + delta && index.Nodes["b"].Bounds.X == g.Index().Nodes["b"].Bounds.X + delta);
            Check(r.Graph.Edges[0].Bends[0] == g.Edges[0].Bends[0] + new PointD(delta, 0) && r.TranslatedEdges == 1);
        });
        test("Alignment: locked descendants and cross-root bends keep their positions", () => {
            var e = new GraphEdit(Graph()); var group = e.Group(["a", "b"]); e.Update(e.Node("b") with { Locked = true }); var g = e.Build();
            var r = GraphAlignment.Calculate(g, [group, "c"], Alignment.Right);
            Check(r.Graph.Index().Nodes["b"] == g.Index().Nodes["b"] && r.Graph.Edges == g.Edges);
        });
        test("Alignment: one ancestor root and no-op placement preserve snapshot identity", () => {
            var e = new GraphEdit(Graph()); var group = e.Group(["a", "b"]); var g = e.Build();
            Check(ReferenceEquals(GraphAlignment.Calculate(g, [group, "a", "b"], Alignment.Left).Graph, g));
            g = GraphAlignment.Calculate(Graph(), ["a", "b", "c"], Alignment.Left).Graph;
            Check(ReferenceEquals(GraphAlignment.Calculate(g, ["a", "b", "c"], Alignment.Left).Graph, g));
        });
        test("Alignment: 10000 roots use two node visits and at most one edge pass", () => {
            var g = new GraphSnapshot(Enumerable.Range(0, 10000).Select(i => new GraphNode("n" + i, new(i * 120, i, 80, 50))).ToImmutableArray(), [], [], []);
            var r = GraphAlignment.Calculate(g, g.Nodes.Select(n => n.Id), Alignment.Top);
            Check(r.ExaminedNodes == 20000 && r.ExaminedEdges == 0 && r.MovedNodes == 9999 && r.Graph.Nodes.All(n => n.Bounds.Y == 0));
        });
        test("Alignment: malformed alignment and pre-cancellation reject without publication", () => {
            var g = Graph(); Throws<ArgumentOutOfRangeException>(() => GraphAlignment.Calculate(g, [], (Alignment)999));
            using var c = new CancellationTokenSource(); c.Cancel(); Throws<OperationCanceledException>(() => GraphAlignment.Calculate(g, ["a"], Alignment.Top, c.Token));
        });
    }
}
