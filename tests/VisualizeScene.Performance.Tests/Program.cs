using System.Xml.Linq;
using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using VisualizeScene.Core;
using VisualizeScene.Editing;

var results = new List<(string Name, string? Error)>();
void Test(string name, Action body)
{
    try { body(); results.Add((name, null)); Console.WriteLine("PASS " + name); }
    catch (Exception e) { results.Add((name, e.ToString())); Console.WriteLine("FAIL " + name + "\n" + e); }
}
Test("Index: repeated snapshot lookups return one identity", () => {
    var g = Cases.Graph(); Cases.Check(ReferenceEquals(g.Index(), g.Index()));
    var copy = g with { }; Cases.Check(!ReferenceEquals(g.Index(), copy.Index()));
    Cases.Check(g == copy); // Caches must not participate in record equality.
});
Test("Index: concurrent readers share one index and one frozen route", () => {
    var g = Cases.Graph(); var indexes = new GraphIndex[64]; var paths = new IReadOnlyList<PointD>[64];
    Parallel.For(0, 64, i => { indexes[i] = g.Index(); paths[i] = indexes[i].EdgePath(g.Edges[0]); });
    Cases.Check(indexes.All(i => ReferenceEquals(i, indexes[0])) && paths.All(p => ReferenceEquals(p, paths[0])));
});
Test("Index: dictionaries, adjacency and cached routes reject mutation", () => {
    var g = Cases.Graph(); var i = g.Index();
    Cases.Throws<NotSupportedException>(() => ((IDictionary<string, GraphNode>)i.Nodes).Clear());
    Cases.Throws<NotSupportedException>(() => ((IList<GraphEdge>)i.Outgoing("a"))[0] = g.Edges[0] with { TargetId = "a" });
    Cases.Throws<NotSupportedException>(() => ((IList<PointD>)i.EdgePath(g.Edges[0]))[0] = new(999, 999));
    Cases.Check(i.EdgePath(g.Edges[0])[0].X != 999 && i.Outgoing("a")[0].TargetId == "b");
});
Test("Index: a replacement edge with the same ID bypasses the old route", () => {
    var g = Cases.Graph(); var index = g.Index(); var original = index.EdgePath(g.Edges[0]);
    var edge = g.Edges[0] with { Bends = [new(150, 190)] }; var path = index.EdgePath(edge);
    Cases.Check(path.Contains(new PointD(150, 190)) && !original.Contains(new PointD(150, 190)));
});
Test("Validation: identity stamps never validate a changed with-copy", () => {
    var g = Cases.Graph(); GraphValidation.Validate(g);
    Cases.Throws<ArgumentException>(() => GraphValidation.Validate(g with { Edges = [g.Edges[0] with { TargetId = "missing" }] }));
    Cases.Throws<ArgumentException>(() => GraphValidation.Validate(g with { Nodes = [g.Nodes[0] with { Bounds = new(double.MaxValue, 0, double.MaxValue, 60) }] }));
});
Test("Index: deep hierarchy resolves outermost folding without recursive traversal", () => {
    var nodes = Enumerable.Range(0, 10000).Select(i => new GraphNode("n" + i, new(0, 0, 100, 60),
        ParentId: i == 0 ? null : "n" + (i - 1), IsGroup: true, Collapsed: i is 5 or 500)).Reverse().ToImmutableArray();
    var index = new GraphSnapshot(nodes, [], [], []).Index();
    Cases.Check(index.VisibleRepresentative("n9999") == "n5" && index.Depth("n9999")==9999 && index.IsVisible("n5"));
});
Test("Index: weak revision caches do not retain retired snapshots", () => {
    var weak = Cases.Retired();
    for (var i = 0; i < 5 && weak.IsAlive; i++) { GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect(); }
    Cases.Check(!weak.IsAlive);
});
Test("Index: warmed lookups and validation allocate no per-call graph structures", () => {
    var g = Cases.Graph(); var index = g.Index(); var edge = g.Edges[0];
    for (var i = 0; i < 100; i++) { _ = g.Index(); GraphValidation.Validate(g); }
    var before = GC.GetAllocatedBytesForCurrentThread();
    for (var i = 0; i < 1000; i++) { _ = g.Index(); GraphValidation.Validate(g); }
    Cases.Check(GC.GetAllocatedBytesForCurrentThread() - before < 8192);
});
Test("Copy-on-write: empty edits preserve exact snapshot identity", () => {
    var g = Cases.Graph(); Cases.Check(ReferenceEquals(new GraphEdit(g).Build(), g));
});
Test("Copy-on-write: label edit retains every untouched collection", () => {
    var g = Cases.Graph(); var edit = new GraphEdit(g); edit.Update(g.Labels[0] with { Text = "changed" }); var next = edit.Build();
    Cases.Check(next.Nodes == g.Nodes && next.Edges == g.Edges && next.Ports == g.Ports && next.Labels != g.Labels);
});
Test("Copy-on-write: earlier built snapshots remain immutable after further edits", () => {
    var g = Cases.Graph(); var edit = new GraphEdit(g); edit.Update(g.Nodes[0] with { Text = "first" }); var first = edit.Build();
    edit.Update(g.Nodes[0] with { Text = "second" }); var second = edit.Build();
    Cases.Check(first.Nodes[0].Text == "first" && second.Nodes[0].Text == "second" && g.Nodes[0].Text == "A");
});
Test("Copy-on-write: node-only move retains ports, labels and bendless edges", () => {
    var g = Cases.Graph(); var edit = new GraphEdit(g); edit.Move(["a"], new(20, 10)); var next = edit.Build();
    Cases.Check(next.Ports == g.Ports && next.Labels == g.Labels && next.Edges == g.Edges && next.Nodes[0].Bounds.X == 20);
});
Test("Copy-on-write: mixed owner/label movement stays exact and undoable", () => {
    var d = new GraphDocument(); var g = Cases.Graph(); d.Replace(g, clearHistory: true); using var s = new EditorSession(d);
    s.Select(["a", "la"]); s.Nudge(new(10, 20)); Cases.Check(d.Snapshot.Labels[0].Offset == g.Labels[0].Offset);
    d.Undo(); Cases.Check(ReferenceEquals(d.Snapshot, g));
});
Test("Spatial query: caller buffer appends and preserves iterator order", () => {
    var items = Enumerable.Range(0, 1000).Select(i => (new RectD(i % 30 * 40, i / 30 * 40, 20, 20), i));
    var index = new SpatialIndex<int>(items); var random = new Random(42); var buffer = new List<int>();
    for (var i = 0; i < 100; i++) {
        var rect = new RectD(random.Next(1200), random.Next(1200), 200, 150); buffer.Clear(); buffer.Add(-1);
        var stats = index.Query(rect, buffer); Cases.Check(buffer.Skip(1).SequenceEqual(index.Query(rect)) && stats.Results == buffer.Count - 1);
    }
});
Test("Spatial query: warmed traversal has bounded work and allocation", () => {
    var index = new SpatialIndex<int>(Enumerable.Range(0, 10000).Select(i => (new RectD(i % 100 * 200, i / 100 * 200, 100, 60), i)));
    var buffer = new List<int>(100); var rect = new RectD(0, 0, 400, 300); index.Query(rect, buffer);
    var before = GC.GetAllocatedBytesForCurrentThread(); SpatialQueryStatistics stats = default;
    for (var i = 0; i < 1000; i++) { buffer.Clear(); stats = index.Query(rect, buffer); }
    Cases.Check(GC.GetAllocatedBytesForCurrentThread() - before < 8192 && stats.ExaminedEntries < 128 && stats.Results == 6);
});
Test("Spatial query: invalid rectangle or destination is rejected", () => {
    var index = new SpatialIndex<int>([]);
    Cases.Throws<ArgumentOutOfRangeException>(() => index.Query(new(0, 0, -1, 0), []));
    Cases.Throws<ArgumentNullException>(() => index.Query(new(), null!));
});
Test("Hit testing: topmost nodes and labels keep painter order", () => {
    var g = Cases.Graph(); g = g with { Nodes = g.Nodes.Add(g.Nodes[0] with { Id = "top" }), Labels = [g.Labels[0], g.Labels[0] with { Id = "last" }] };
    var d = new GraphDocument(); d.Replace(g); using var s = new EditorSession(d) { LabelSizeProvider = _ => new(80, 20) };
    Cases.Check(s.Hit(new(50, 30), false).Id == "top" && s.Hit(new(50, 130), false).Id == "last");
});
Test("Hit testing: same-position ports preserve original pick priority", () => {
    var g = Cases.Graph(); g = g with { Ports = [g.Ports[0], g.Ports[0] with { Id = "second" }] };
    var d = new GraphDocument(); d.Replace(g); using var s = new EditorSession(d);
    Cases.Check(s.Hit(new(100, 30), false).Id == "ap" && s.FindConnectionCandidate(new(100, 30))!.PortId == "ap");
});
Test("Hit testing: 10000 annotated nodes inspect only nearby candidates", () => {
    var g = Cases.Many(10000); var d = new GraphDocument(); d.Replace(g); using var s = new EditorSession(d) { LabelSizeProvider = _ => new(80, 20) };
    Cases.Check(s.Hit(new(50, 30), false).Id == "n0");
    Cases.Check(s.LastHitStatistics.CandidateItems < 10 && s.LastHitStatistics.ExaminedEntries < 256);
    Cases.Check(s.FindConnectionCandidate(new(100, 30))!.PortId == "p0" && s.LastHitStatistics.ExaminedEntries < 256);
});
RendererCases.Run(Test);
EdgeRenderingTests.Run(Test);
CaptionMeasurementTests.Run(Test);
PictureMemoryCases.Run(Test);
EdgeCaptionCacheCases.Run(Test);
Directory.CreateDirectory("artifacts/validation");
File.WriteAllText("artifacts/validation/performance.xml", new XElement("testsuite", new XAttribute("name", "Performance contracts"),
    new XAttribute("tests", results.Count), new XAttribute("failures", results.Count(r => r.Error is not null)), results.Select(r =>
        new XElement("testcase", new XAttribute("name", r.Name), r.Error is null ? null : new XElement("failure", r.Error)))).ToString());
Console.WriteLine($"{results.Count(r => r.Error is null)}/{results.Count} passed");
return results.Any(r => r.Error is not null) ? 1 : 0;

static class Cases
{
    public static void Check(bool value) { if (!value) throw new Exception("Assertion failed"); }
    public static void Throws<T>(Action body) where T : Exception { try { body(); } catch (T) { return; } throw new Exception("Expected " + typeof(T).Name); }
    public static GraphSnapshot Graph() => new([new("a", new(0, 0, 100, 60), "A"), new("b", new(300, 0, 100, 60), "B"), new("c", new(600, 0, 100, 60), "C")],
        [new("ab", "a", "b"), new("bc", "b", "c")], [new("ap", "a", new(1, .5))], [new("la", "a", "Annotation", new(0, 100))]);
    public static GraphSnapshot Many(int count)
    {
        var nodes = Enumerable.Range(0, count).Select(i => new GraphNode("n" + i, new(i % 100 * 300, i / 100 * 250, 100, 60), "Node")).ToImmutableArray();
        return new(nodes, [], Enumerable.Range(0, count).Select(i => new GraphPort("p" + i, "n" + i, new(1, .5))).ToImmutableArray(),
            Enumerable.Range(0, count).Select(i => new GraphLabel("l" + i, "n" + i, "Label", new(0, 80))).ToImmutableArray());
    }
    [MethodImpl(MethodImplOptions.NoInlining)] public static WeakReference Retired()
    { var graph = Graph(); var index = graph.Index(); _ = index.EdgePath(graph.Edges[0]); return new(graph); }
}
