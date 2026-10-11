using System.Collections.Immutable;
using VisualizeScene.Core;
using VisualizeScene.Editing;

static class ReviewCases
{
    public static void Run(Action<string, Action> test)
    {
        test("Review: equivalent route arrays do not add history", () => {
            var d = new GraphDocument(); d.Replace(Cases.Graph(), clearHistory: true); var revision = d.Revision;
            d.Edit("clear", e => e.Update(e.Edge("ab") with { Bends = [] }));
            Cases.Check(d.Revision == revision && !d.CanUndo);
            d.Edit("bend", e => e.Update(e.Edge("ab") with { Bends = [new(150, 100)] })); revision = d.Revision;
            d.Edit("same bend", e => e.Update(e.Edge("ab") with { Bends = [new(150, 100)] }));
            Cases.Check(d.Revision == revision); d.Undo(); Cases.Check(!d.CanUndo);
        });
        test("Review: same-value updates preserve all collection identities", () => {
            var g = Cases.Graph(); var edit = new GraphEdit(g); edit.Update(g.Nodes[0] with { }); edit.Update(g.Edges[0] with { });
            edit.Update(g.Ports[0] with { }); edit.Update(g.Labels[0] with { }); edit.Move(["a"], new());
            Cases.Check(ReferenceEquals(edit.Build(), g));
        });
        test("Review: bulk removal scans each item once and preserves cascade semantics", () => {
            var g = Cases.Many(10000); var edit = new GraphEdit(g);
            var result = edit.RemoveRange(g.Nodes.Select(n => n.Id));
            var next = edit.Build(); GraphValidation.Validate(next);
            Cases.Check(next.Nodes.IsEmpty && next.Ports.IsEmpty && next.Labels.IsEmpty);
            Cases.Check(result.Nodes == 10000 && result.Ports == 10000 && result.Labels == 10000 && result.ExaminedItems == 30000);
        });
        test("Review: bulk port deletion detaches surviving edges and cascades its labels", () => {
            var g = Cases.Graph(); g = g with { Edges = [g.Edges[0] with { SourcePortId = "ap" }, g.Edges[1]], Labels = g.Labels.Add(new("lp", "ap", "Port label", new())) };
            var edit = new GraphEdit(g); var result = edit.RemoveRange(["ap", "ap", "missing"]); var next = edit.Build();
            GraphValidation.Validate(next); Cases.Check(next.Edges[0].SourcePortId is null && next.Nodes == g.Nodes);
            Cases.Check(result.Ports == 1 && result.Nodes == 0 && result.Edges == 0 && result.Labels == 1);
        });
        test("Review: bulk deletion works on groups added inside the current transaction", () => {
            var edit = new GraphEdit(Cases.Graph()); var id = edit.Group(["a", "b"]); edit.Add(new GraphLabel("lg", id, "Group", new()));
            edit.RemoveRange([id, "a"]); var g = edit.Build(); GraphValidation.Validate(g);
            Cases.Check(g.Nodes.Length == 1 && g.Nodes[0].Id == "c" && g.Edges.IsEmpty && g.Ports.IsEmpty && g.Labels.IsEmpty);
        });
        test("Review: bulk removal materializes aliases and exceptions before mutation", () => {
            var g = Cases.Graph(); var edit = new GraphEdit(g);
            IEnumerable<string> Broken() { yield return "a"; throw new InvalidOperationException(); }
            Cases.Throws<InvalidOperationException>(() => edit.RemoveRange(Broken())); Cases.Check(ReferenceEquals(edit.Build(), g));
            edit.RemoveRange(edit.Nodes.Select(n => n.Id)); Cases.Check(edit.Build().Nodes.IsEmpty);
        });
        test("Review: removing absent IDs is a genuine no-op", () => {
            var g = Cases.Graph(); var edit = new GraphEdit(g); edit.RemoveRange(["unknown"]); Cases.Check(ReferenceEquals(edit.Build(), g));
        });
        test("Review: select-all bulk deletion is one exact undo transaction", () => {
            var d = new GraphDocument(); var g = Cases.Many(1000); d.Replace(g, clearHistory: true); using var s = new EditorSession(d);
            s.SelectAll(); s.DeleteSelection(); Cases.Check(d.Snapshot.Nodes.IsEmpty && d.CanUndo);
            d.Undo(); Cases.Check(ReferenceEquals(d.Snapshot, g) && !d.CanUndo); d.Redo(); Cases.Check(d.Snapshot.Labels.IsEmpty);
        });
        test("Review: group bounds move children and internal bends once", () => {
            var edit = new GraphEdit(Cases.Graph()); edit.Update(edit.Edge("ab") with { Bends = [new(200, 80)] });
            var group = edit.Group(["a", "b"]); var before = edit.Build(); var bounds = edit.Node(group).Bounds;
            edit.SetNodeBounds(group, bounds.Move(new(50, 70)) with { Width = bounds.Width + 100 }); var after = edit.Build(); GraphValidation.Validate(after);
            Cases.Check(after.Index().Nodes["a"].Bounds == before.Index().Nodes["a"].Bounds.Move(new(50, 70)));
            Cases.Check(after.Edges[0].Bends[0] == new PointD(250, 150) && after.Nodes[2] == before.Nodes[2]);
        });
        test("Review: locked group bounds and invalid geometry reject atomically", () => {
            var d = new GraphDocument(); d.Replace(Cases.Graph(), clearHistory: true); d.Edit("lock", e => e.Update(e.Node("a") with { Locked = true })); var before = d.Snapshot;
            Cases.Throws<InvalidOperationException>(() => d.Edit("move", e => e.SetNodeBounds("a", new(50, 0, 100, 60))));
            Cases.Throws<ArgumentOutOfRangeException>(() => d.Edit("invalid", e => e.SetNodeBounds("b", new(0, 0, 0, 60))));
            Cases.Check(ReferenceEquals(d.Snapshot, before));
        });
        test("Review: default connection policy does not allocate a full edge list", () => {
            var nodes = Enumerable.Range(0, 10000).Select(i => new GraphNode("n" + i, new(i * 200, 0, 100, 60))).ToImmutableArray();
            var edges = Enumerable.Range(0, 9999).Select(i => new GraphEdge("e" + i, "n" + i, "n" + (i + 1))).ToImmutableArray();
            var index = new GraphSnapshot(nodes, edges, [], []).Index(); var policy = new ConnectionPolicy(); var request = new ConnectionRequest(new("n0"), new("n1"));
            for (var i = 0; i < 100; i++) Cases.Check(policy.Evaluate(index, request).Allowed);
            var memory = GC.GetAllocatedBytesForCurrentThread();
            for (var i = 0; i < 1000; i++) Cases.Check(policy.Evaluate(index, request).Allowed);
            Cases.Check(GC.GetAllocatedBytesForCurrentThread() - memory < 65536);
        });
        test("Review: port capacity counts both ends of a self-loop exactly once each", () => {
            var g = Cases.Graph(); g = g with { Edges = [new("loop", "a", "a", SourcePortId: "ap", TargetPortId: "ap")] };
            var policy = new ConnectionPolicy { Ports = new Dictionary<string, PortConstraint> { ["ap"] = new(Capacity: 2) } };
            Cases.Check(!policy.Evaluate(g.Index(), new(new("a", "ap"), new("b"))).Allowed);
            Cases.Check(policy.Evaluate(g.Index(), new(new("a", "ap"), new("a", "ap"), "loop")).Allowed);
        });
        test("Review: indexed degree and parallel checks match exhaustive reference", () => {
            var random = new Random(771); var nodes = Enumerable.Range(0, 10).Select(i => new GraphNode("n" + i, new(i * 200, 0, 100, 60))).ToImmutableArray();
            for (var iteration = 0; iteration < 100; iteration++) {
                var edges = Enumerable.Range(0, 30).Select(i => new GraphEdge("e" + i, "n" + random.Next(10), "n" + random.Next(10), Directed: random.Next(2) == 0)).ToImmutableArray();
                var graph = new GraphSnapshot(nodes, edges, [], []).Index(); var from = "n" + random.Next(10); var to = "n" + random.Next(10); var excluded = iteration % 2 == 0 ? edges[0].Id : null;
                var maxOut = random.Next(8); var maxIn = random.Next(8); var parallel = iteration % 3 == 0;
                var available = edges.Where(e => e.Id != excluded).ToArray();
                var expected = (parallel || !available.Any(e => e.SourceId == from && e.TargetId == to || !e.Directed && e.SourceId == to && e.TargetId == from)) &&
                    available.Count(e => e.SourceId == from || !e.Directed && e.TargetId == from) < maxOut &&
                    available.Count(e => e.TargetId == to || !e.Directed && e.SourceId == to) < maxIn;
                var policy = new ConnectionPolicy { AllowParallelEdges = parallel, MaximumOutgoing = maxOut, MaximumIncoming = maxIn };
                Cases.Check(policy.Evaluate(graph, new(new(from), new(to), excluded)).Allowed == expected);
            }
        });
        test("Review: corner minimum size keeps the opposite corner anchored", () => {
            var d = new GraphDocument(); d.Replace(Cases.Graph(), clearHistory: true); using var editor = new EditorSession(d) { SnapToGrid = false, SnapToItems = false };
            editor.Select(["a"]); editor.PointerDown(new(0, 0)); editor.PointerUp(new(150, 100));
            Cases.Check(d.Snapshot.Nodes[0].Bounds == new RectD(80, 40, 20, 20));
            d.Undo(); Cases.Check(!d.CanUndo);
        });
        test("Review: aligning selected ancestors and children moves descendants once", () => {
            var edit = new GraphEdit(Cases.Graph()); var group = edit.Group(["a", "b"]); var g = edit.Build();
            var d = new GraphDocument(); d.Replace(g, clearHistory: true); using var editor = new EditorSession(d);
            var originalDelta = g.Index().Nodes["a"].Bounds.X - g.Index().Nodes[group].Bounds.X;
            editor.Select([group, "a", "c"]); editor.Align(Alignment.Right);
            var index = d.Snapshot.Index(); Cases.Check(index.Nodes["a"].Bounds.X - index.Nodes[group].Bounds.X == originalDelta);
            d.Undo(); Cases.Check(ReferenceEquals(d.Snapshot, g));
        });
        test("Review: malformed connection roles are rejected", () => {
            Cases.Throws<ArgumentOutOfRangeException>(() => new ConnectionPolicy().CanStart(Cases.Graph().Index(), new("a"), (EdgeEnd)99));
        });
    }
}
