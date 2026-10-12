using System.Collections.Immutable;
using VisualizeScene.Core;
using VisualizeScene.Editing;
using VisualizeScene.Serialization;

static class ArrangementTests
{
    static void Check(bool value) { if (!value) throw new Exception("Assertion failed"); }
    static void Throws<T>(Action action) where T : Exception { try { action(); } catch (T) { return; } throw new Exception("Expected " + typeof(T).Name); }
    static GraphSnapshot Graph() => new(
        [new("a", new(0, 0, 100, 60)), new("b", new(300, 160, 150, 90)), new("c", new(700, 50, 80, 40), Locked: true)],
        [new("e", "a", "b", Bends: [new(200, 80)])], [new("p", "b", new(.123456789, .8))], [new("l", "b", "Note", new(0, 90))]);
    public static void Run(Action<string, Action> test)
    {
        test("Arrange: size commands preserve position, untouched dimension, styles and dependent collections", () => {
            foreach (var mode in Enum.GetValues<NodeSizeMatch>()) {
                var g = Graph(); var r = GraphNodeSizing.Calculate(g, ["a", "b", "c", "e", "p", "l"], mode, "a");
                var b = r.Graph.Nodes[1]; Check(r.ResizedNodes == 1 && r.ReferenceId == "a");
                Check(b.Bounds.X == 300 && b.Bounds.Y == 160);
                Check(b.Bounds.Width == (mode == NodeSizeMatch.Height ? 150 : 100));
                Check(b.Bounds.Height == (mode == NodeSizeMatch.Width ? 90 : 60));
                Check(r.Graph.Nodes[0] == g.Nodes[0] && r.Graph.Nodes[2] == g.Nodes[2]);
                Check(r.Graph.Edges == g.Edges && r.Graph.Labels == g.Labels && r.Graph.Ports == g.Ports);
            }
        });
        test("Arrange: a locked selected reference supplies dimensions but cannot be resized", () => {
            var g = Graph(); var state = GraphArrangeState.Calculate(g, ["a", "c"], "c");
            Check(state.SelectedNodes == 2 && state.ResizableTargets == 1 && state.ReferenceId == "c" && state.MovableRoots == 1);
            var r = GraphNodeSizing.Calculate(g, ["a", "c"], NodeSizeMatch.Both, "c");
            Check(r.Graph.Nodes[0].Bounds == new RectD(0, 0, 80, 40) && r.Graph.Nodes[2] == g.Nodes[2]);
        });
        test("Arrange: selected groups resize in place without scaling children or edge bends", () => {
            var e = new GraphEdit(Graph()); var group = e.Group(["b"]); var g = e.Build();
            var r = GraphNodeSizing.Calculate(g, ["a", group], NodeSizeMatch.Both, "a");
            Check(r.Graph.Index().Nodes["b"] == g.Index().Nodes["b"] && r.Graph.Edges == g.Edges);
            Check(r.Graph.Index().Nodes[group].Bounds.Width == 100 && r.ResizedNodes == 1);
        });
        test("Arrange: hidden nodes and non-node selections never participate in sizing", () => {
            var e = new GraphEdit(Graph()); var group = e.Group(["b"]); e.Update(e.Node(group) with { Collapsed = true }); var g = e.Build();
            var state = GraphArrangeState.Calculate(g, ["b", "e", "p", "l", "a", "a"], "b");
            Check(state.SelectedNodes == 1 && state.ReferenceId == "a" && state.ResizableTargets == 0);
            Check(ReferenceEquals(GraphNodeSizing.Calculate(g, ["b", "a", "e"], NodeSizeMatch.Width).Graph, g));
            Throws<ArgumentException>(() => GraphNodeSizing.Calculate(g, ["b", "a"], NodeSizeMatch.Width, "b"));
        });
        test("Arrange: empty, duplicate and identical-size requests retain snapshot identity", () => {
            var g = Graph(); Check(GraphNodeSizing.Calculate(g, [], NodeSizeMatch.Width).ReferenceId is null);
            Check(ReferenceEquals(GraphNodeSizing.Calculate(g, ["a", "a"], NodeSizeMatch.Both).Graph, g));
            var same = GraphNodeSizing.Calculate(g, ["a", "b"], NodeSizeMatch.Both).Graph;
            Check(ReferenceEquals(GraphNodeSizing.Calculate(same, ["a", "b"], NodeSizeMatch.Both).Graph, same));
        });
        test("Arrange: invalid operations, missing references and cancellation are diagnosed", () => {
            var g = Graph(); Throws<ArgumentOutOfRangeException>(() => GraphNodeSizing.Calculate(g, [], (NodeSizeMatch)99));
            Throws<ArgumentException>(() => GraphNodeSizing.Calculate(g, ["a", "b"], NodeSizeMatch.Width, "absent"));
            using var c = new CancellationTokenSource(); c.Cancel();
            Throws<OperationCanceledException>(() => GraphNodeSizing.Calculate(g, [], NodeSizeMatch.Width, cancellationToken: c.Token));
            Throws<OperationCanceledException>(() => GraphArrangeState.Calculate(g, [], cancellationToken: c.Token));
        });
        test("Arrange: overflow cannot publish a partial size update", () => {
            var g = new GraphSnapshot([new("a", new(0, 0, 1e308, 60)), new("b", new(0, 100, 80, 60)), new("c", new(1e308, 200, 80, 60))], [], [], []);
            var d = new GraphDocument(); d.Replace(g, clearHistory: true); using var s = new EditorSession(d);
            s.Select(["a", "b", "c"]); Throws<ArgumentException>(() => s.MatchNodeSize(NodeSizeMatch.Width, "a"));
            Check(ReferenceEquals(g, d.Snapshot) && !d.CanUndo);
        });
        test("Arrange: session uses selected focus and commits one exact undo and redo entry", () => {
            var g = Graph(); var d = new GraphDocument(); d.Replace(g, clearHistory: true); using var s = new EditorSession(d);
            s.Select(["a", "b"]); s.FocusItem("b"); var selected = s.Selection;
            s.MatchNodeSize(NodeSizeMatch.Width); Check(d.Snapshot.Nodes[0].Bounds.Width == 150 && s.LastNodeSizing!.ReferenceId == "b");
            Check(ReferenceEquals(s.Selection, selected)); d.Undo(); Check(ReferenceEquals(g, d.Snapshot) && !d.CanUndo);
            d.Redo(); Check(d.Snapshot.Nodes[0].Bounds.Width == 150);
        });
        test("Arrange: selection state counts roots once and does not let locked ancestors own movement", () => {
            var e = new GraphEdit(Graph()); var group = e.Group(["a", "b"]); var g = e.Build();
            Check(GraphArrangeState.Calculate(g, [group, "a", "b"]).MovableRoots == 1);
            e.Update(e.Node(group) with { Locked = true }); g = e.Build();
            Check(GraphArrangeState.Calculate(g, [group, "a", "b"]).MovableRoots == 2);
        });
        test("Arrange: deep hierarchy state uses iterative ancestor traversal", () => {
            var nodes = Enumerable.Range(0, 5000).Select(i => new GraphNode("n" + i, new(i, i, 100, 60),
                IsGroup: true, ParentId: i == 0 ? null : "n" + (i - 1))).ToImmutableArray();
            var g = new GraphSnapshot(nodes, [], [], []);
            var state = GraphArrangeState.Calculate(g, nodes.Select(n => n.Id));
            Check(state.SelectedNodes == 5000 && state.MovableRoots == 1 && state.ResizableTargets == 4999);
        });
        test("Arrange: alignment fast path and hierarchy path exactly match the preceding implementation", () => {
            var random = new Random(5021);
            for (var trial = 0; trial < 80; trial++) {
                var nodes = Enumerable.Range(0, 8).Select(i => new GraphNode("n" + i,
                    new(random.NextDouble() * 1500 - 750, random.NextDouble() * 900 - 450, 30 + random.NextDouble() * 100, 25 + random.NextDouble() * 80), Locked: i == 7)).ToImmutableArray();
                var g = new GraphSnapshot(nodes, [new("loop", "n0", "n0", Bends: [new(200, 100)]), new("e", "n0", "n1", Bends: [new(0, 150)])], [], []);
                var ids = nodes.Select(n => n.Id).ToList();
                if (trial % 2 == 0) { var e = new GraphEdit(g); var group = e.Group(["n0", "n1"]); g = e.Build(); if (trial % 4 == 0) ids.Add(group); }
                foreach (var mode in Enum.GetValues<Alignment>())
                    Check(GraphIO.ToJson(GraphAlignment.Calculate(g, ids, mode).Graph) == GraphIO.ToJson(AlignmentReference.Calculate(g, ids, mode).Graph));
            }
        });
        test("Arrange: no-op alignment avoids the old graph-sized temporary ownership and node copy", () => {
            var g = new GraphSnapshot(Enumerable.Range(0, 2000).Select(i => new GraphNode("n" + i, new(0, i * 100, 100, 60))).ToImmutableArray(), [], [], []);
            var ids = g.Nodes.Select(n => n.Id).ToArray();
            long Measure(bool baseline) { var start = GC.GetAllocatedBytesForCurrentThread(); var result = baseline
                ? AlignmentReference.Calculate(g, ids, Alignment.Left) : GraphAlignment.Calculate(g, ids, Alignment.Left);
                var bytes = GC.GetAllocatedBytesForCurrentThread() - start; Check(ReferenceEquals(result.Graph, g)); return bytes; }
            _ = Measure(true); _ = Measure(false); var old = Measure(true); var current = Measure(false);
            Check(current < old);
        });
    }
}
