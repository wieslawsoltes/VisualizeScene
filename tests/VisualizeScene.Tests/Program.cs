using System.Collections.Immutable;
using System.Diagnostics;
using System.Xml.Linq;
using VisualizeScene.Core;
using VisualizeScene.Algorithms;
using VisualizeScene.Layout;
using VisualizeScene.Serialization;
using VisualizeScene.Editing;

var results = new List<(string Name, string? Error, double Milliseconds)>();
void Check(bool condition, string message = "Assertion failed") { if (!condition) throw new Exception(message); }
void Near(double actual, double expected, double tolerance = 1e-6) => Check(Math.Abs(actual - expected) <= tolerance, $"Expected {expected}, got {actual}");
void Throws<T>(Action action) where T : Exception { try { action(); } catch (T) { return; } throw new Exception("Expected " + typeof(T).Name); }
void Test(string name, Action action)
{
    var timer = Stopwatch.StartNew(); string? error = null;
    try { action(); Console.WriteLine("PASS " + name); } catch (Exception ex) { error = ex.ToString(); Console.WriteLine("FAIL " + name + "\n" + error); }
    results.Add((name, error, timer.Elapsed.TotalMilliseconds));
}
GraphSnapshot Chain(int count = 4)
{
    var nodes = Enumerable.Range(0, count).Select(i => new GraphNode("n" + i, new(i * 170, 0, 100 + i % 3 * 20, 50), "Node " + i)).ToImmutableArray();
    var edges = Enumerable.Range(1, Math.Max(0, count - 1)).Select(i => new GraphEdge("e" + i, "n" + (i - 1), "n" + i, Weight: i, Bends: [])).ToImmutableArray();
    return new(nodes, edges, [], []);
}
GraphDocument Document(GraphSnapshot? graph = null)
{
    var d = new GraphDocument(); d.Replace(graph ?? Chain(), clearHistory: true); return d;
}
Test("Geometry: rectangle intersection and union", () => { var a = new RectD(-5, -3, 10, 8); Check(a.Contains(new(0, 0))); Check(a.Intersects(new(5, 5, 1, 1))); var u = RectD.Union(a, new(20, 30, 2, 2)); Near(u.Right, 22); Near(u.Bottom, 32); });
Test("Geometry: segment distance handles zero length", () => { Near(Geometry.DistanceToSegment(new(3, 4), new(), new()), 5); Near(Geometry.DistanceToSegment(new(5, 3), new(0, 0), new(10, 0)), 3); });
Test("Geometry: shape-aware hit testing", () => { var n = new GraphNode("a", new(0, 0, 100, 100), Shape: NodeShape.Ellipse); Check(!Geometry.HitNode(n, new(0, 0))); Check(Geometry.HitNode(n, new(50, 50))); Check(!Geometry.HitNode(n with { Shape = NodeShape.Diamond }, new(10, 10))); });
Test("Viewport: zoom anchor remains stationary", () => { var v = new Viewport(); v.Pan(new(40, 10)); var screen = new PointD(120, 200); var before = v.ToWorld(screen); v.ZoomAt(screen, 2); Near(PointD.Distance(before, v.ToWorld(screen)), 0); });
Test("Viewport: fit handles degenerate content", () => { var v = new Viewport(); v.Fit(new(0, 0, 0, 0), 900, 600); Check(double.IsFinite(v.Zoom)); Near(v.ToScreen(new(0, 0)).X, 450); });
Test("Core: atomic edit rollback", () => { var d = Document(); var original = d.Snapshot; Throws<ArgumentException>(() => d.Edit("bad", e => e.Add(new GraphEdge("bad", "missing", "n0")))); Check(ReferenceEquals(original, d.Snapshot)); Check(!d.CanUndo); });
Test("Core: action exception rolls back", () => { var d = Document(); Throws<InvalidOperationException>(() => d.Edit("bad", e => { e.Remove("n0"); throw new InvalidOperationException(); })); Check(d.Snapshot.Nodes.Length == 4); });
Test("Core: undo and redo are exact snapshots", () => { var d = Document(); var before = d.Snapshot; d.Edit("move", e => e.Move(["n0"], new(10, 20))); var after = d.Snapshot; d.Undo(); Check(ReferenceEquals(before, d.Snapshot)); d.Redo(); Check(ReferenceEquals(after, d.Snapshot)); });
Test("Core: edits invalidate redo branch", () => { var d = Document(); d.Edit("a", e => e.Move(["n0"], new(10, 0))); d.Undo(); d.Edit("b", e => e.Move(["n1"], new(20, 0))); Check(!d.CanRedo); });
Test("Core: bounded history", () => { var d = new GraphDocument(2); d.Replace(Chain(), clearHistory: true); for (var i = 0; i < 3; i++) d.Edit("move", e => e.Move(["n0"], new(10, 0))); d.Undo(); d.Undo(); Check(!d.CanUndo); Near(d.Snapshot.Nodes[0].Bounds.X, 10); });
Test("Core: revision lease rejects stale layouts", () => { var d = Document(); var revision = d.Revision; d.Edit("move", e => e.Move(["n0"], new(1, 0))); Throws<InvalidOperationException>(() => d.Edit("stale", _ => { }, revision)); });
Test("Core: nested transactions rejected", () => { var d = Document(); Throws<InvalidOperationException>(() => d.Edit("outer", _ => d.Edit("inner", _ => { }))); Check(!d.CanUndo); });
Test("Core: invalid geometry rejected", () => { var d = Document(); Throws<ArgumentException>(() => d.Edit("nan", e => e.Update(e.Node("n0") with { Bounds = new(double.NaN, 0, 10, 10) }))); Throws<ArgumentException>(() => d.Edit("size", e => e.Update(e.Node("n0") with { Bounds = new(0, 0, 0, 10) }))); });
Test("Core: global ID uniqueness", () => { var d = Document(); Throws<ArgumentException>(() => d.Edit("id", e => e.Add(new GraphEdge("n0", "n0", "n1")))); });
Test("Core: style validation", () => { var d = Document(); Throws<ArgumentException>(() => d.Edit("color", e => e.Update(e.Node("n0") with { Fill = "javascript:alert(1)" }))); });
Test("Core: groups, descendants and atomic subtree removal", () => { var d = Document(); string g = ""; d.Edit("group", e => g = e.Group(["n0", "n1"])); Check(d.Snapshot.Index().Descendants(g).Count() == 2); d.Edit("remove", e => e.Remove(g)); Check(d.Snapshot.Nodes.Length == 2); Check(d.Snapshot.Edges.Length == 1); d.Undo(); Check(d.Snapshot.Nodes.Length == 5); });
Test("Core: group cycles rejected", () => { var d = Document(); string a = "", b = ""; d.Edit("groups", e => { a = e.Group(["n0"]); b = e.Group(["n1"]); }); Throws<ArgumentException>(() => d.Edit("cycle", e => { e.Update(e.Node(a) with { ParentId = b }); e.Update(e.Node(b) with { ParentId = a }); })); });
Test("Core: group move does not double-translate selected child", () => { var d = Document(); string id = ""; d.Edit("group", e => id = e.Group(["n0", "n1"])); var old = d.Snapshot.Index().Nodes["n0"].Bounds; d.Edit("move", e => e.Move([id, "n0"], new(50, 20))); Near(d.Snapshot.Index().Nodes["n0"].Bounds.X - old.X, 50); });
Test("Core: locked nodes do not move", () => { var d = Document(); d.Edit("lock", e => e.Update(e.Node("n0") with { Locked = true })); d.Edit("move", e => e.Move(["n0"], new(100, 100))); Near(d.Snapshot.Nodes[0].Bounds.X, 0); });
Test("Core: folding promotes edge endpoints", () => { var d = Document(); string id = ""; d.Edit("fold", e => { id = e.Group(["n0", "n1"]); e.Update(e.Node(id) with { Collapsed = true }); }); var index = d.Snapshot.Index(); Check(!index.IsVisible("n0")); Check(index.VisibleRepresentative("n1") == id); Check(index.EdgePath(index.Edges["e2"]).Count >= 2); });
Test("Core: ports and labels cascade", () => { var d = Document(); d.Edit("port", e => { e.Add(new GraphPort("p", "n0", new(1, .5))); e.Update(e.Edge("e1") with { SourcePortId = "p" }); e.Add(new GraphLabel("l", "p", "port", new(0, -10))); }); d.Edit("remove port", e => e.Remove("p")); Check(d.Snapshot.Ports.Length == 0 && d.Snapshot.Labels.Length == 0 && d.Snapshot.Edges[0].SourcePortId is null); });
Test("Core: port ownership validated", () => { var d = Document(); Throws<ArgumentException>(() => d.Edit("port", e => { e.Add(new GraphPort("p", "n2", new(1, .5))); e.Update(e.Edge("e1") with { SourcePortId = "p" }); })); });
Test("Spatial: BVH matches brute force", () => { var r = new Random(9); var boxes = Enumerable.Range(0, 2000).Select(i => (Bounds: new RectD(r.NextDouble() * 1000, r.NextDouble() * 1000, r.Next(1, 100), r.Next(1, 100)), Item: i)).ToArray(); var index = new SpatialIndex<int>(boxes); for (var i = 0; i < 100; i++) { var q = new RectD(r.Next(1000), r.Next(1000), 70, 90); Check(index.Query(q).Order().SequenceEqual(boxes.Where(b => b.Bounds.Intersects(q)).Select(b => b.Item).Order())); } });
Test("Algorithms: BFS directed and reverse reachability", () => { var g = Chain(); Check(GraphAlgorithms.BreadthFirst(g, "n0").Count == 4); Check(GraphAlgorithms.BreadthFirst(g, "n3").Count == 1); Check(GraphAlgorithms.BreadthFirst(g, "n3", true).Count == 4); });
Test("Algorithms: weak components", () => { var d = Document(); d.Edit("island", e => e.Add(new GraphNode("island", new(0, 200, 50, 50)))); Check(GraphAlgorithms.ConnectedComponents(d.Snapshot).Count == 2); });
Test("Algorithms: strongly connected cycle", () => { var d = Document(); d.Edit("cycle", e => e.Add(new GraphEdge("cycle", "n3", "n0"))); Check(GraphAlgorithms.StronglyConnectedComponents(d.Snapshot).Count == 1); });
Test("Algorithms: SCC does not recurse on a long chain", () => Check(GraphAlgorithms.StronglyConnectedComponents(Chain(10000)).Count == 10000));
Test("Algorithms: topological order and cycle diagnostic", () => { Check(GraphAlgorithms.TopologicalOrder(Chain()).SequenceEqual(new[] { "n0", "n1", "n2", "n3" })); var d = Document(); d.Edit("cycle", e => e.Add(new GraphEdge("cycle", "n3", "n0"))); Throws<InvalidOperationException>(() => GraphAlgorithms.TopologicalOrder(d.Snapshot)); });
Test("Algorithms: Dijkstra and unreachable target", () => { var g = Chain(); var p = GraphAlgorithms.ShortestPath(g, "n0", "n3"); Near(p.Cost, 6); Check(p.Edges.Count == 3); Check(!GraphAlgorithms.ShortestPath(g, "n3", "n0").Found); });
Test("Algorithms: negative-weight guard and Bellman-Ford", () => { var d = Document(); d.Edit("negative", e => e.Update(e.Edge("e2") with { Weight = -2 })); Throws<ArgumentException>(() => GraphAlgorithms.ShortestPath(d.Snapshot, "n0", "n3")); Near(GraphAlgorithms.BellmanFord(d.Snapshot, "n0", "n3").Cost, 2); });
Test("Algorithms: negative cycles diagnosed", () => { var d = Document(); d.Edit("cycle", e => e.Add(new GraphEdge("cycle", "n3", "n0", Weight: -10))); Throws<InvalidOperationException>(() => GraphAlgorithms.BellmanFord(d.Snapshot, "n0", "n3")); });
Test("Algorithms: minimum spanning forest", () => { var d = Document(); d.Edit("extra", e => e.Add(new GraphEdge("expensive", "n0", "n3", Weight: 100))); var tree = GraphAlgorithms.MinimumSpanningForest(d.Snapshot); Check(tree.Count == 3 && !tree.Contains("expensive")); });
Test("Algorithms: PageRank conserves mass with dangling nodes", () => { var ranks = GraphAlgorithms.PageRank(Chain()); Near(ranks.Values.Sum(), 1, 1e-9); Check(ranks["n3"] > ranks["n0"]); });
Test("Algorithms: betweenness chain", () => { var scores = GraphAlgorithms.Betweenness(Chain()); Near(scores["n0"], 0); Near(scores["n1"], 2); Near(scores["n2"], 2); });
Test("Algorithms: DSATUR proper coloring", () => { var g = Chain(); var colors = GraphAlgorithms.Coloring(g); Check(colors.Values.Max() == 1); Check(g.Edges.All(e => colors[e.SourceId] != colors[e.TargetId])); });
Test("Algorithms: maximum flow and min cut", () => { var d = Document(); d.Edit("capacities", e => { e.Update(e.Edge("e1") with { Weight = 3 }); e.Update(e.Edge("e2") with { Weight = 3 }); e.Update(e.Edge("e3") with { Weight = 3 }); e.Add(new GraphEdge("direct", "n0", "n3", Weight: 2)); }); var f = GraphAlgorithms.MaximumFlow(d.Snapshot, "n0", "n3"); Near(f.Value, 5); Near(f.EdgeFlows["direct"], 2); Check(f.SourcePartition.Contains("n0") && !f.SourcePartition.Contains("n3")); });
foreach (var kind in Enum.GetValues<LayoutKind>())
{
    Test("Layout: " + kind + " finite positions and preserved topology", () => { var g = Chain(30); var result = new LayoutEngine().Calculate(g, new(kind, Iterations: 50)); GraphValidation.Validate(result.Graph); Check(result.Graph.Nodes.Select(n => n.Id).SequenceEqual(g.Nodes.Select(n => n.Id))); Check(result.Graph.Edges.Length == g.Edges.Length); Check(result.Graph.Nodes.Select(n => n.Bounds.Center).Distinct().Count() == 30); });
    Test("Layout: " + kind + " empty and singleton", () => { var engine = new LayoutEngine(); Check(engine.Calculate(GraphSnapshot.Empty, new(kind)).Graph.Nodes.Length == 0); Check(engine.Calculate(Chain(1), new(kind)).Graph.Nodes.Length == 1); });
}
Test("Layout: hierarchy respects directed rank", () => { var g = new LayoutEngine().Calculate(Chain(), new()).Graph; var i = g.Index(); Check(g.Edges.All(e => i.Nodes[e.SourceId].Bounds.Bottom < i.Nodes[e.TargetId].Bounds.Y)); });
Test("Layout: hierarchy handles cycles", () => { var d = Document(); d.Edit("cycle", e => e.Add(new GraphEdge("cycle", "n3", "n0"))); GraphValidation.Validate(new LayoutEngine().Calculate(d.Snapshot, new()).Graph); });
Test("Layout: left-to-right direction", () => { var g = new LayoutEngine().Calculate(Chain(), new(Direction: LayoutDirection.LeftToRight)).Graph; var i = g.Index(); Check(g.Edges.All(e => i.Nodes[e.SourceId].Bounds.Right < i.Nodes[e.TargetId].Bounds.X)); });
Test("Layout: compound groups contain descendants", () => { var d = Document(); string id = ""; d.Edit("group", e => id = e.Group(["n0", "n1"])); var g = new LayoutEngine().Calculate(d.Snapshot, new()).Graph; var i = g.Index(); Check(i.Children(id).All(n => i.Nodes[id].Bounds.Contains(n.Bounds.Center))); });
Test("Layout: organic deterministic seed", () => { var e = new LayoutEngine(); var g = Chain(12); Check(GraphIO.ToJson(e.Calculate(g, new(LayoutKind.Organic, Iterations: 30)).Graph) == GraphIO.ToJson(e.Calculate(g, new(LayoutKind.Organic, Iterations: 30)).Graph)); });
Test("Layout: locked positions retained", () => { var d = Document(); d.Edit("lock", e => e.Update(e.Node("n1") with { Locked = true })); var b = d.Snapshot.Nodes[1].Bounds; var g = new LayoutEngine().Calculate(d.Snapshot, new()).Graph; Check(g.Nodes[1].Bounds == b); });
Test("Layout: cancellation", () => { using var source = new CancellationTokenSource(); source.Cancel(); Throws<OperationCanceledException>(() => new LayoutEngine().Calculate(Chain(), new(), source.Token)); });
Test("Router: avoids blocking rectangle", () => { var g = new GraphSnapshot([new("a", new(0, 0, 80, 40)), new("b", new(300, 0, 80, 40)), new("block", new(140, -30, 80, 100))], [new("e", "a", "b")], [], []); var result = new OrthogonalRouter().Route(g); Check(result.Diagnostics.Count == 0); var path = result.Graph.Index().EdgePath(result.Graph.Edges[0]); Check(path.Count >= 4); for (var i = 1; i < path.Count; i++) { Check(Math.Abs(path[i].X - path[i - 1].X) < 1e-6 || Math.Abs(path[i].Y - path[i - 1].Y) < 1e-6); var mid = PointD.Lerp(path[i], path[i - 1], .5); Check(!new RectD(140, -30, 80, 100).Contains(mid)); } });
Test("Editing: drag is one undo entry", () => { var d = Document(); using var s = new EditorSession(d); s.PointerDown(new(50, 25)); s.PointerMove(new(70, 45)); s.PointerMove(new(80, 55)); Check(!d.CanUndo); s.PointerUp(new(80, 55)); Near(d.Snapshot.Nodes[0].Bounds.X, 30); d.Undo(); Near(d.Snapshot.Nodes[0].Bounds.X, 0); Check(!d.CanUndo); });
Test("Editing: canceled drag leaves no edits", () => { var d = Document(); using var s = new EditorSession(d); s.PointerDown(new(50, 25)); s.PointerMove(new(100, 50)); s.CancelGesture(); Check(!d.CanUndo); Near(d.Snapshot.Nodes[0].Bounds.X, 0); });
Test("Editing: create edge from two clicks", () => { var d = Document(Chain(2) with { Edges = [] }); using var s = new EditorSession(d) { Mode = EditorMode.CreateEdge }; s.PointerDown(new(50, 25)); s.PointerUp(new(50, 25)); s.PointerDown(new(220, 25)); s.PointerUp(new(220, 25)); Check(d.Snapshot.Edges.Length == 1); Check(d.Snapshot.Edges[0].SourceId == "n0" && d.Snapshot.Edges[0].TargetId == "n1"); });
Test("Editing: marquee selection", () => { var d = Document(); using var s = new EditorSession(d); s.PointerDown(new(-20, -20)); s.PointerMove(new(310, 70)); s.PointerUp(new(310, 70)); Check(s.Selection.Contains("n0") && s.Selection.Contains("n1") && !s.Selection.Contains("n2")); });
Test("Editing: duplicate remaps topology", () => { var d = Document(); using var s = new EditorSession(d); s.Select(["n0", "n1"]); s.Duplicate(); Check(d.Snapshot.Nodes.Length == 6); Check(d.Snapshot.Edges.Length == 4); var ids = s.Selection; Check(d.Snapshot.Edges.Any(e => ids.Contains(e.SourceId) && ids.Contains(e.TargetId))); GraphValidation.Validate(d.Snapshot); });
Test("Editing: group clipboard remaps parents and ports", () => { var d = Document(); string group = ""; d.Edit("group", e => { group = e.Group(["n0", "n1"]); e.Add(new GraphPort("port", "n0", new(1, .5))); e.Add(new GraphLabel("label", "port", "Hello", new(0, 0))); }); var data = GraphClipboard.Copy(d.Snapshot, [group]); d.Edit("paste", e => GraphClipboard.Paste(e, data, new(20, 20))); Check(d.Snapshot.Nodes.Length == 8); Check(d.Snapshot.Ports.Length == 2 && d.Snapshot.Labels.Length == 2); GraphValidation.Validate(d.Snapshot); });
Test("Editing: alignment and undo", () => { var d = Document(); using var s = new EditorSession(d); s.Select(["n0", "n1"]); s.Align(Alignment.Left); Near(d.Snapshot.Nodes[0].Bounds.X, d.Snapshot.Nodes[1].Bounds.X); d.Undo(); Near(d.Snapshot.Nodes[1].Bounds.X, 170); });
Test("Serialization: JSON round trip", () => { var d = Document(); d.Edit("data", e => { e.Add(new GraphPort("p", "n0", new(1, .5))); e.Add(new GraphLabel("l", "e1", "ą漢字 & <xml>", new(4, -12))); e.Update(e.Edge("e1") with { SourcePortId = "p", Bends = [new(130, 80), new(140, 80)] }); }); var json = GraphIO.ToJson(d.Snapshot); Check(GraphIO.ToJson(GraphIO.FromJson(json)) == json); });
Test("Serialization: default bend arrays normalize", () => { var d = Document(); d.Edit("edge", e => e.Add(new GraphEdge("loop", "n0", "n0"))); Check(GraphIO.FromJson(GraphIO.ToJson(d.Snapshot)).Edges.Length == 4); });
Test("Serialization: native GraphML round trip groups and ports", () => { var d = Document(); d.Edit("group", e => { e.Group(["n0", "n1"]); e.Add(new GraphPort("p", "n0", new(1, .5))); e.Update(e.Edge("e1") with { SourcePortId = "p" }); }); var text = GraphIO.ToGraphML(d.Snapshot); var read = GraphIO.FromGraphML(text); GraphValidation.Validate(read.Graph); Check(read.Graph.Nodes.Length == 5 && read.Graph.Ports.Length == 1 && read.Graph.Edges[0].SourcePortId == "p"); Check(read.Diagnostics.Count == 0); });
Test("Serialization: plain GraphML import", () => { var data = "<graphml xmlns='http://graphml.graphdrawing.org/xmlns'><graph edgedefault='undirected'><node id='a'/><node id='b'/><edge source='a' target='b'/></graph></graphml>"; var g = GraphIO.FromGraphML(data).Graph; Check(g.Nodes.Length == 2 && g.Edges.Length == 1 && !g.Edges[0].Directed); });
Test("Serialization: external entities prohibited", () => Throws<System.Xml.XmlException>(() => GraphIO.FromGraphML("<!DOCTYPE graphml [<!ENTITY x SYSTEM 'file:///etc/passwd'>]><graphml>&x;</graphml>")));
Test("Serialization: unknown schema version rejected", () => Throws<NotSupportedException>(() => GraphIO.FromJson("{\"Version\":99,\"Graph\":null}")));
Test("Serialization: SVG escapes labels", () => { var g = Chain(1); g = g with { Nodes = [g.Nodes[0] with { Text = "<script>alert(1)</script> & test" }] }; var svg = SvgExporter.Export(g); var xml = XDocument.Parse(svg); Check(!xml.Descendants().Any(e => e.Name.LocalName == "script")); Check(svg.Contains("&lt;script&gt;")); });

Directory.CreateDirectory("artifacts/validation");
var suite = new XElement("testsuite", new XAttribute("name", "VisualizeScene conformance"), new XAttribute("tests", results.Count), new XAttribute("failures", results.Count(r => r.Error is not null)), results.Select(r => new XElement("testcase", new XAttribute("name", r.Name), new XAttribute("time", r.Milliseconds / 1000), r.Error is null ? null : new XElement("failure", r.Error))));
File.WriteAllText("artifacts/validation/conformance.xml", new XDocument(suite).ToString());
Console.WriteLine($"\n{results.Count(r => r.Error is null)}/{results.Count} tests passed in {results.Sum(r => r.Milliseconds):F1} ms");
return results.Any(r => r.Error is not null) ? 1 : 0;
