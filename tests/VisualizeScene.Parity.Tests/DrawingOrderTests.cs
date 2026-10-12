using System.Collections.Immutable;
using System.Xml.Linq;
using VisualizeScene.Core;
using VisualizeScene.Editing;
using VisualizeScene.Serialization;

static class DrawingOrderTests
{
    static void Check(bool v) { if (!v) throw new Exception("Assertion failed"); }
    static void Throws<T>(Action action) where T : Exception { try { action(); } catch (T) { return; } throw new Exception("Expected " + typeof(T).Name); }
    static GraphSnapshot Graph() => new(Enumerable.Range(0, 4).Select(i => new GraphNode(((char)('a' + i)).ToString(), new(i * 20, 0, 100, 60), "N" + i)).ToImmutableArray(), [], [], []);
    static string Ids(GraphSnapshot g) => string.Concat(g.Nodes.Select(n => n.Id));
    public static void Run(Action<string, Action> test)
    {
        test("Order: all four operations preserve stable selected and unselected order", () => {
            var g = Graph(); var expected = new[] { "bdac", "acbd", "badc", "acbd" };
            foreach (var op in Enum.GetValues<DrawingOrder>()) Check(Ids(GraphDrawingOrder.Calculate(g, ["a", "c", "a"], op).Graph) == expected[(int)op]);
        });
        test("Order: boundary, all-selected and empty commands are true no-ops", () => {
            var g = Graph();
            Check(ReferenceEquals(g, GraphDrawingOrder.Calculate(g, ["d"], DrawingOrder.Raise).Graph));
            Check(ReferenceEquals(g, GraphDrawingOrder.Calculate(g, ["a"], DrawingOrder.Lower).Graph));
            foreach (var op in Enum.GetValues<DrawingOrder>()) {
                Check(ReferenceEquals(g, GraphDrawingOrder.Calculate(g, [], op).Graph));
                Check(ReferenceEquals(g, GraphDrawingOrder.Calculate(g, ["a", "b", "c", "d"], op).Graph));
            }
        });
        test("Order: session commands retain geometry, records, selection and exact undo", () => {
            var g = Graph(); var d = new GraphDocument(); d.Replace(g, clearHistory: true); using var s = new EditorSession(d);
            s.Select(["a", "c"]); s.ChangeDrawingOrder(DrawingOrder.BringToFront); var after = d.Snapshot;
            Check(Ids(after) == "bdac" && s.Selection.SetEquals(["a", "c"]));
            foreach (var n in after.Nodes) Check(ReferenceEquals(n, g.Index().Nodes[n.Id]));
            d.Undo(); Check(ReferenceEquals(d.Snapshot, g) && !d.CanUndo); d.Redo(); Check(ReferenceEquals(d.Snapshot, after));
        });
        test("Order: regular nodes and expanded groups never cross paint layers", () => {
            var g = new GraphSnapshot([new("g", new(0, 0, 300, 300), IsGroup: true), new("a", new(10, 40, 100, 60)),
                new("j", new(20, 20, 260, 240), IsGroup: true), new("b", new(30, 40, 100, 60))], [], [], []);
            var r = GraphDrawingOrder.Calculate(g, ["g", "a"], DrawingOrder.BringToFront);
            Check(Ids(r.Graph) == "jbga" && r.EligibleItems == 2 && r.ChangedSlots == 4);
        });
        test("Order: folded descendants and ports do not silently reorder", () => {
            var g = new GraphSnapshot([new("g", new(0, 0, 300, 300), IsGroup: true, Collapsed: true),
                new("a", new(10, 40, 100, 60), ParentId: "g"), new("b", new(30, 40, 100, 60), ParentId: "g")], [], [new("p", "g", new(.5, .5))], []);
            var result = GraphDrawingOrder.Calculate(g, ["a", "b", "p"], DrawingOrder.BringToFront);
            Check(result.EligibleItems == 0 && ReferenceEquals(result.Graph, g));
        });
        test("Order: independent edges and labels reorder without changing topology or bends", () => {
            var g = Graph() with { Edges = [new("e1", "a", "b", Bends: [new(20, 90)]), new("e2", "a", "b")],
                Labels = [new("l1", "a", "One", new()), new("l2", "b", "Two", new())] };
            var r = GraphDrawingOrder.Calculate(g, ["e1", "l1"], DrawingOrder.BringToFront).Graph;
            Check(r.Nodes == g.Nodes && r.Ports == g.Ports && ReferenceEquals(r.Edges[^1], g.Edges[0]) && ReferenceEquals(r.Labels[^1], g.Labels[0]));
        });
        test("Order: malformed IDs and cancellation are rejected before publication", () => {
            var g = Graph(); Throws<ArgumentException>(() => GraphDrawingOrder.Calculate(g, ["missing"], DrawingOrder.Raise));
            Throws<ArgumentNullException>(() => GraphDrawingOrder.Calculate(g, [null!], DrawingOrder.Raise));
            Throws<ArgumentOutOfRangeException>(() => GraphDrawingOrder.Calculate(g, [], (DrawingOrder)99));
            using var c = new CancellationTokenSource(); c.Cancel(); Throws<OperationCanceledException>(() => GraphDrawingOrder.Calculate(g, ["a"], DrawingOrder.Raise, c.Token));
        });
        test("Order: randomized partitions retain original relative order", () => {
            var random = new Random(225); var g = new GraphSnapshot(Enumerable.Range(0, 200).Select(i => new GraphNode("n" + i, new(0, 0, 100, 60))).ToImmutableArray(), [], [], []);
            for (var trial = 0; trial < 50; trial++) {
                var selected = g.Nodes.Where(_ => random.Next(2) == 0).Select(n => n.Id).ToHashSet();
                foreach (var op in Enum.GetValues<DrawingOrder>()) {
                    var result = GraphDrawingOrder.Calculate(g, selected, op).Graph;
                    Check(result.Nodes.Where(n => selected.Contains(n.Id)).SequenceEqual(g.Nodes.Where(n => selected.Contains(n.Id))));
                    Check(result.Nodes.Where(n => !selected.Contains(n.Id)).SequenceEqual(g.Nodes.Where(n => !selected.Contains(n.Id))));
                    Check(result.Nodes.Length == g.Nodes.Length);
                }
            }
        });
        test("Order: ten thousand nodes have bounded linear storage and preserve record identities", () => {
            var g = new GraphSnapshot(Enumerable.Range(0, 10000).Select(i => new GraphNode("n" + i, new(0, 0, 100, 60))).ToImmutableArray(), [], [], []);
            var ids = g.Nodes.Take(5000).Select(n => n.Id).ToArray(); _ = GraphDrawingOrder.Calculate(g, ids, DrawingOrder.BringToFront);
            var allocated = GC.GetAllocatedBytesForCurrentThread(); var r = GraphDrawingOrder.Calculate(g, ids, DrawingOrder.BringToFront);
            Check(GC.GetAllocatedBytesForCurrentThread() - allocated < 20 * 1024 * 1024);
            Check(r.ExaminedItems == 10000 && r.EligibleItems == 5000 && r.ChangedSlots == 10000 && r.Graph.Nodes[0].Id == "n5000");
        });
        test("Picking: nested group depth takes precedence over later parent records", () => {
            var g = new GraphSnapshot([new("child", new(0, 0, 180, 180), ParentId: "parent", IsGroup: true),
                new("parent", new(0, 0, 200, 200), IsGroup: true)], [], [], []);
            var d = new GraphDocument(); d.Replace(g); using var s = new EditorSession(d);
            Check(s.Hit(new(50, 10), false).Id == "child");
        });
        test("GraphML: node and port ordering survives non-preorder hierarchy serialization", () => {
            var g = new GraphSnapshot([new("child", new(0, 0, 100, 60), ParentId: "parent"), new("other", new(20, 20, 100, 60)),
                new("parent", new(0, 0, 300, 300), IsGroup: true)], [], [new("p1", "parent", new(.5, .5)), new("p2", "child", new(.5, .5))], []);
            foreach (var op in Enum.GetValues<DrawingOrder>()) {
                var ordered = GraphDrawingOrder.Calculate(g, ["child"], op).Graph;
                var imported = GraphIO.FromGraphML(GraphIO.ToGraphML(ordered));
                Check(imported.Diagnostics.Count == 0 && GraphIO.ToJson(imported.Graph) == GraphIO.ToJson(ordered));
            }
        });
        test("GraphML: native drawing-order metadata rejects injected, missing and duplicate IDs", () => {
            XNamespace ns = "http://graphml.graphdrawing.org/xmlns";
            foreach (var invalid in new[] { "null", "[]", "[\"a\",\"b\",\"c\",\"missing\"]", "[\"a\",\"b\",\"c\",\"c\"]" }) {
                var xml = XDocument.Parse(GraphIO.ToGraphML(Graph())); xml.Descendants(ns + "data").Single(e => (string?)e.Attribute("key") == "o").Value = invalid;
                Throws<FormatException>(() => GraphIO.FromGraphML(xml.ToString()));
            }
        });
        test("GraphML: old native documents without order metadata remain supported", () => {
            XNamespace ns = "http://graphml.graphdrawing.org/xmlns"; var g = Graph(); var xml = XDocument.Parse(GraphIO.ToGraphML(g));
            xml.Descendants(ns + "data").Where(e => (string?)e.Attribute("key") is "o" or "q").Remove();
            Check(GraphIO.ToJson(GraphIO.FromGraphML(xml.ToString()).Graph) == GraphIO.ToJson(g));
        });
        test("SVG: exported shape order follows node drawing order", () => {
            var g = new GraphSnapshot([new("red", new(0, 0, 100, 60), "", NodeShape.Rectangle, "#FF0000"),
                new("blue", new(0, 0, 100, 60), "", NodeShape.Rectangle, "#0000FF")], [], [], []);
            var ordered = GraphDrawingOrder.Calculate(g, ["red"], DrawingOrder.BringToFront).Graph;
            XNamespace ns = "http://www.w3.org/2000/svg"; var xml = XDocument.Parse(SvgExporter.Export(ordered));
            Check((string?)xml.Descendants(ns + "rect").Last().Attribute("fill") == "#FF0000");
        });
    }
}
