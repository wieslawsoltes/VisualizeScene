using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using VisualizeScene.Core;

static class ColdIndexTests
{
    static void Check(bool v) { if (!v) throw new Exception("Assertion failed"); }
    static void Throws<T>(Action a) where T : Exception { try { a(); } catch (T) { return; } throw new Exception("Expected " + typeof(T).Name); }
    static GraphSnapshot Graph() => new([new("a", new(0, 0, 100, 60)), new("b", new(200, 0, 100, 60))],
        [new("ab", "a", "b")], [new("ap", "a", new(.5, .5))], [new("la", "a", "Label", new())]);
    public static void Run(Action<string, Action> test)
    {
        test("Document lease: stale replacement never edits or clears history", () => {
            var document = new GraphDocument(); document.Replace(Graph()); var revision = document.Revision;
            document.Edit("move", edit => edit.Move(["a"], new(10, 0))); var before = document.Snapshot;
            Throws<InvalidOperationException>(() => document.Replace(GraphSnapshot.Empty, clearHistory: true, expectedRevision: revision));
            Check(ReferenceEquals(document.Snapshot, before) && document.CanUndo);
            document.Replace(GraphSnapshot.Empty, expectedRevision: document.Revision); Check(document.Snapshot.Nodes.IsEmpty);
            document.Undo(); Check(ReferenceEquals(document.Snapshot, before));
        });
        test("Document lease: no-op replacements still reject stale revision", () => {
            var document = new GraphDocument(); document.Replace(Graph()); var revision = document.Revision;
            document.Edit("move", edit => edit.Move(["a"], new(10, 0))); document.Undo();
            Throws<InvalidOperationException>(() => document.Replace(document.Snapshot, expectedRevision: revision));
        });
        test("Cold index: validation maps are reused by independently constructed indexes", () => {
            var graph = Graph(); GraphValidation.Validate(graph); var a = new GraphIndex(graph); var b = new GraphIndex(graph);
            Check(ReferenceEquals(a.Nodes, b.Nodes) && ReferenceEquals(a.Edges, b.Edges) && ReferenceEquals(a.Ports, b.Ports) && ReferenceEquals(a.Labels, b.Labels));
        });
        test("Cold index: all public maps and adjacency reject mutation", () => {
            var index = Graph().Index();
            Throws<NotSupportedException>(() => ((IDictionary<string, GraphNode>)index.Nodes).Clear());
            Throws<NotSupportedException>(() => ((IDictionary<string, GraphEdge>)index.Edges).Clear());
            Throws<NotSupportedException>(() => ((IDictionary<string, GraphPort>)index.Ports).Clear());
            Throws<NotSupportedException>(() => ((IDictionary<string, GraphLabel>)index.Labels).Clear());
            Throws<NotSupportedException>(() => ((IList<GraphEdge>)index.Outgoing("a")).Clear());
        });
        test("Cold index: invalid snapshots never publish completed maps", () => {
            var g = Graph() with { Nodes = [null!] };
            for (var i = 0; i < 2; i++) Throws<ArgumentException>(() => g.Index());
            Throws<ArgumentException>(() => (Graph() with { Edges = [null!] }).Index());
            Throws<ArgumentException>(() => (Graph() with { Ports = [null!] }).Index());
            Throws<ArgumentException>(() => (Graph() with { Labels = [null!] }).Index());
        });
        test("Cold index: parent cycles, missing and non-group parents reject", () => {
            Throws<ArgumentException>(() => new GraphSnapshot([new("a", new(0, 0, 100, 60), ParentId: "b", IsGroup: true), new("b", new(0, 0, 100, 60), ParentId: "a", IsGroup: true)], [], [], []).Index());
            Throws<ArgumentException>(() => (Graph() with { Nodes = [new("a", new(0, 0, 100, 60), ParentId: "missing")] }).Index());
            Throws<ArgumentException>(() => (Graph() with { Nodes = [new("a", new(0, 0, 100, 60), ParentId: "b"), new("b", new(0, 0, 100, 60))] }).Index());
        });
        test("Cold index: labels cannot own labels, themselves, or collide with IDs", () => {
            var g = Graph();
            Throws<ArgumentException>(() => (g with { Labels = g.Labels.Add(new("nested", "la", "nested", new())) }).Index());
            Throws<ArgumentException>(() => (g with { Labels = [new("self", "self", "self", new())] }).Index());
            Throws<ArgumentException>(() => (g with { Labels = [new("a", "b", "duplicate", new())] }).Index());
        });
        test("Cold index: shuffled deep groups retain depth and outermost folding", () => {
            var nodes = Enumerable.Range(0, 10000).Select(i => new GraphNode("n" + i, new(0, 0, 100, 60), IsGroup: true,
                ParentId: i == 0 ? null : "n" + (i - 1), Collapsed: i is 10 or 500)).Reverse().ToImmutableArray();
            var index = new GraphSnapshot(nodes, [], [], []).Index(); Check(index.Depth("n9999") == 9999 && index.VisibleRepresentative("n9999") == "n10");
        });
        test("Cold index: concurrent validation shares map identities", () => {
            var graph = Graph(); var maps = new IReadOnlyDictionary<string, GraphNode>[64];
            Parallel.For(0, maps.Length, i => { GraphValidation.Validate(graph); maps[i] = new GraphIndex(graph).Nodes; });
            Check(maps.All(m => ReferenceEquals(m, maps[0])));
        });
        test("Cold index: warm path lookup creates no per-call closures", () => {
            var graph = Graph(); var index = graph.Index(); var edge = graph.Edges[0];
            for (var i = 0; i < 100; i++) _ = index.EdgePath(edge);
            var memory = GC.GetAllocatedBytesForCurrentThread();
            for (var i = 0; i < 1000; i++) _ = index.EdgePath(edge);
            Check(GC.GetAllocatedBytesForCurrentThread() - memory < 1024);
        });
        test("Cold index: validation-only weak cache does not retain snapshots", () => {
            var weak = Retired(); for (var i = 0; i < 5 && weak.IsAlive; i++) { GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect(); }
            Check(!weak.IsAlive);
        });
        test("Cold index: no per-call color enumerator allocation", () => {
            for (var i = 0; i < 100; i++) Check(GraphValidation.ValidColor("#aB019F"));
            var before = GC.GetAllocatedBytesForCurrentThread(); for (var i = 0; i < 1000; i++) Check(GraphValidation.ValidColor("#aB019F"));
            Check(GC.GetAllocatedBytesForCurrentThread() - before < 1024);
            Check(!GraphValidation.ValidColor("#１２３４５６") && !GraphValidation.ValidColor("#00000G") && !GraphValidation.ValidColor(null));
        });
    }
    [MethodImpl(MethodImplOptions.NoInlining)] static WeakReference Retired() { var g = Graph(); GraphValidation.Validate(g); return new(g); }
}
