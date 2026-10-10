using VisualizeScene.Core;
using VisualizeScene.Editing;

static class ConnectionTests
{
    static void Check(bool condition) { if (!condition) throw new Exception("Assertion failed"); }
    static GraphDocument Document()
    {
        var d = new GraphDocument();
        d.Replace(new([new("a", new(0, 0, 100, 60)), new("b", new(300, 0, 100, 60)), new("c", new(300, 200, 100, 60))],
            [new("ab", "a", "b", Bends: [], Routing: EdgeRouting.Polyline, SourcePortId: "ap", TargetPortId: "bp")],
            [new("ap", "a", new(1, .5)), new("bp", "b", new(0, .5)), new("cp", "c", new(0, .5))], []), clearHistory: true);
        return d;
    }
    public static void Run(Action<string, Action> test)
    {
        test("Selection: click on a selected item collapses a multiple selection", () =>
        {
            var d = Document(); using var s = new EditorSession(d); s.Select(["a", "b", "c", "ab"]);
            s.PointerDown(new(50, 30)); Check(s.Selection.Count == 4); s.PointerUp(new(50, 30));
            Check(s.Selection.Count == 1 && s.Selection.Contains("a"));
        });
        test("Selection: dragging a selected item preserves and moves multiple selection", () =>
        {
            var d = Document(); using var s = new EditorSession(d); s.Select(["a", "b"]);
            s.PointerDown(new(50, 30)); s.PointerMove(new(70, 50)); s.PointerUp(new(70, 50));
            Check(s.Selection.Count == 2 && d.Snapshot.Nodes[0].Bounds.X == 20 && d.Snapshot.Nodes[1].Bounds.X == 320);
        });
        test("Connection: target reconnect preserves source port and supports undo", () =>
        {
            var d = Document(); using var s = new EditorSession(d);
            Check(s.Reconnect("ab", EdgeEnd.Target, new("c", "cp")).Allowed);
            Check(d.Snapshot.Edges[0].TargetPortId == "cp" && d.Snapshot.Edges[0].SourcePortId == "ap");
            d.Undo(); Check(d.Snapshot.Edges[0].TargetPortId == "bp"); d.Redo(); Check(d.Snapshot.Edges[0].TargetId == "c");
        });
        test("Connection: source reconnect clears stale source port", () =>
        {
            var d = Document(); using var s = new EditorSession(d); Check(s.Reconnect("ab", EdgeEnd.Source, new("c")).Allowed);
            Check(d.Snapshot.Edges[0].SourceId == "c" && d.Snapshot.Edges[0].SourcePortId is null);
        });
        test("Connection: wrong-owner port is rejected atomically", () =>
        {
            var d = Document(); using var s = new EditorSession(d); var revision = d.Revision;
            Check(!s.Reconnect("ab", EdgeEnd.Target, new("c", "bp")).Allowed); Check(d.Revision == revision);
        });
        test("Connection: endpoint drag commits once and retains bends", () =>
        {
            var d = Document(); d.Edit("bend", e => e.Update(e.Edge("ab") with { Bends = [new(200, 30)] }));
            using var s = new EditorSession(d); s.Select(["ab"]); var revision = d.Revision;
            Check(s.Hit(new(300, 30)).Kind == HitKind.TargetHandle); s.PointerDown(new(300, 30));
            s.PointerMove(new(250, 160)); Check(d.Revision == revision); s.PointerMove(new(300, 230));
            Check(s.DisplayGraph.Edges[0].TargetId == "c"); Check(s.CandidateDecision.Allowed);
            s.PointerUp(new(300, 230)); Check(d.Revision == revision + 1 && d.Snapshot.Edges[0].TargetPortId == "cp");
            Check(d.Snapshot.Edges[0].Bends[0] == new PointD(200, 30));
        });
        test("Connection: invalid endpoint drop restores original edge", () =>
        {
            var d = Document(); using var s = new EditorSession(d); s.Select(["ab"]); var revision = d.Revision;
            s.PointerDown(new(300, 30)); s.PointerMove(new(300, 230)); s.PointerUp(new(200, 120));
            Check(d.Revision == revision && d.Snapshot.Edges[0].TargetId == "b" && s.EdgePreview is null);
        });
        test("Connection: canceled endpoint drag leaves no history", () =>
        {
            var d = Document(); using var s = new EditorSession(d); s.Select(["ab"]);
            s.PointerDown(new(300, 30)); s.PointerMove(new(300, 230)); s.CancelGesture(); s.PointerUp(new(300, 230));
            Check(!d.CanUndo && d.Snapshot.Edges[0].TargetId == "b" && s.CandidatePosition is null);
        });
        test("Connection: out-and-back drag restores original snapshot", () =>
        {
            var d = Document(); using var s = new EditorSession(d); s.Select(["ab"]);
            s.PointerDown(new(300, 30)); s.PointerMove(new(300, 230)); s.PointerUp(new(300, 30)); Check(!d.CanUndo);
        });
        test("Connection: node out-and-back drag also restores snapshot", () =>
        {
            var d = Document(); using var s = new EditorSession(d);
            s.PointerDown(new(50, 30)); s.PointerMove(new(100, 100)); s.PointerUp(new(50, 30)); Check(!d.CanUndo);
        });
        test("Port: pointer relocation moves attached endpoint with one undo", () =>
        {
            var d = Document(); using var s = new EditorSession(d) { SnapToGrid = false };
            s.PointerDown(new(100, 30)); Check(s.Selection.Contains("ap")); s.PointerMove(new(50, 0));
            Check(!d.CanUndo && s.DisplayGraph.Ports[0].RelativeLocation == new PointD(.5, 0)); s.PointerUp(new(50, 0));
            Check(d.Snapshot.Index().EdgePath(d.Snapshot.Edges[0])[0] == new PointD(50, 0)); d.Undo(); Check(!d.CanUndo);
        });
        test("Port: relocation clamps to owning node bounds", () =>
        {
            var d = Document(); using var s = new EditorSession(d); s.MovePort("ap", new(-100, 400));
            Check(d.Snapshot.Ports[0].RelativeLocation == new PointD(0, 1));
        });
        test("Port: delete detaches endpoint and undo restores reference", () =>
        {
            var d = Document(); using var s = new EditorSession(d); s.Select(["ap"]); s.DeleteSelection();
            Check(d.Snapshot.Ports.Length == 2 && d.Snapshot.Edges[0].SourcePortId is null); d.Undo(); Check(d.Snapshot.Edges[0].SourcePortId == "ap");
        });
        test("Port: add and select through reusable editor command", () =>
        {
            var d = Document(); using var s = new EditorSession(d); var id = s.AddPort("c", new(350, 200));
            Check(s.Selection.Contains(id) && d.Snapshot.Index().Ports[id].RelativeLocation == new PointD(.5, 0));
        });
        test("Port: port-only clipboard retains owner and valid references", () =>
        {
            var d = Document(); var copied = GraphClipboard.Copy(d.Snapshot, ["ap"]); GraphValidation.Validate(copied);
            Check(copied.Nodes.Length == 1 && copied.Ports.Length == 1);
        });
        test("Policy: prevents self loops and parallel edges", () =>
        {
            var i = Document().Snapshot.Index(); var p = new ConnectionPolicy { AllowSelfLoops = false, AllowParallelEdges = false };
            Check(!p.Evaluate(i, new(new("a"), new("a"))).Allowed);
            Check(!p.Evaluate(i, new(new("a"), new("b"))).Allowed);
            Check(p.Evaluate(i, new(new("a", "ap"), new("b", "bp"), "ab")).Allowed);
        });
        test("Policy: cycle check follows reachability and excludes replaced edge", () =>
        {
            var i = Document().Snapshot.Index(); var p = new ConnectionPolicy { AllowCycles = false };
            Check(!p.Evaluate(i, new(new("b"), new("a"))).Allowed);
            Check(p.Evaluate(i, new(new("b"), new("a"), "ab")).Allowed);
            Check(!p.Evaluate(i, new(new("c"), new("c"))).Allowed);
        });
        test("Policy: explicit ports and direction constraints", () =>
        {
            var i = Document().Snapshot.Index(); var p = new ConnectionPolicy { RequirePorts = true,
                Ports = new Dictionary<string, PortConstraint> { ["ap"] = new(PortRoles.Source), ["bp"] = new(PortRoles.Target) } };
            Check(!p.Evaluate(i, new(new("a"), new("c"))).Allowed);
            Check(!p.CanStart(i, new("b", "bp"), EdgeEnd.Source).Allowed);
            Check(p.Evaluate(i, new(new("a", "ap"), new("b", "bp"), "ab")).Allowed);
        });
        test("Policy: port capacity counts endpoint incidences", () =>
        {
            var i = Document().Snapshot.Index(); var p = new ConnectionPolicy { Ports = new Dictionary<string, PortConstraint> { ["ap"] = new(Capacity: 1) } };
            Check(!p.Evaluate(i, new(new("a", "ap"), new("c"))).Allowed);
            Check(p.Evaluate(i, new(new("a", "ap"), new("c"), "ab")).Allowed);
            Check(!p.Evaluate(i, new(new("a", "ap"), new("a", "ap"), "ab")).Allowed);
        });
        test("Policy: degree limits do not double count reconnection", () =>
        {
            var i = Document().Snapshot.Index(); var p = new ConnectionPolicy { MaximumOutgoing = 1, MaximumIncoming = 1 };
            Check(!p.Evaluate(i, new(new("a"), new("c"))).Allowed);
            Check(p.Evaluate(i, new(new("a"), new("c"), "ab")).Allowed);
        });
        test("Policy: immovable ports cannot be relocated by drag", () =>
        {
            var d = Document(); using var s = new EditorSession(d) { ConnectionPolicy = new ConnectionPolicy { Ports = new Dictionary<string, PortConstraint> { ["ap"] = new(Movable: false) } } };
            s.PointerDown(new(100, 30)); s.PointerMove(new(50, 0)); s.PointerUp(new(50, 0)); Check(!d.CanUndo);
        });
        test("Policy: locked and folded endpoints cannot be reconnected", () =>
        {
            var d = Document(); d.Edit("lock", e => e.Update(e.Node("c") with { Locked = true })); using var s = new EditorSession(d);
            Check(!s.Reconnect("ab", EdgeEnd.Target, new("c")).Allowed);
            d.Edit("group", e => { var g = e.Group(["c"]); e.Update(e.Node(g) with { Collapsed = true }); });
            Check(!s.Reconnect("ab", EdgeEnd.Target, new("c", "cp")).Allowed);
        });
        test("Creation uses the same policy and preserves source after rejected target", () =>
        {
            var d = Document(); using var s = new EditorSession(d) { Mode = EditorMode.CreateEdge, ConnectionPolicy = new ConnectionPolicy { AllowParallelEdges = false } };
            s.PointerDown(new(100, 30)); s.PointerUp(new(100, 30)); s.PointerDown(new(300, 30)); s.PointerUp(new(300, 30));
            Check(d.Snapshot.Edges.Length == 1 && !s.CandidateDecision.Allowed);
            s.PointerDown(new(300, 230)); s.PointerUp(new(300, 230));
            Check(d.Snapshot.Edges.Length == 2 && d.Snapshot.Edges[1].TargetPortId == "cp");
        });
    }
}
