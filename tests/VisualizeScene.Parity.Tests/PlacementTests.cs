using System.Collections.Immutable;
using VisualizeScene.Core;
using VisualizeScene.Layout;
using VisualizeScene.Serialization;

static class PlacementTests
{
    private sealed class Provider(Func<LabelCandidateContext, IEnumerable<PointD>> get) : ILabelCandidateProvider
    { public IEnumerable<PointD> GetCandidates(LabelCandidateContext context) => get(context); }
    private static void Check(bool condition) { if (!condition) throw new Exception("Assertion failed"); }
    private static void Throws<T>(Action action) where T : Exception
    { try { action(); } catch (T) { return; } throw new Exception("Expected " + typeof(T).Name); }
    private static PointD Size(GraphLabel _) => new(80, 20);
    private static GraphSnapshot Graph(int count = 1) => new([new("n", new(0, 0, 100, 60), "")], [], [],
        Enumerable.Range(0, count).Select(i => new GraphLabel("l" + i, "n", "Annotation", new())).ToImmutableArray());
    private static LabelPlacementResult Place(GraphSnapshot graph) => new LabelPlacementEngine().Calculate(graph, measure: Size);
    public static void Run(Action<string, Action> test)
    {
        test("Placement: empty graph remains the same snapshot", () =>
        { var r = Place(GraphSnapshot.Empty); Check(ReferenceEquals(r.Graph, GraphSnapshot.Empty) && r.MovedLabels == 0 && r.Conflicts.IsEmpty); });
        test("Placement: four overlapping labels move outside the owner without conflicts", () =>
        { var r = Place(Graph(4)); Check(r.MovedLabels == 4 && r.EligibleLabels == 4 && r.Conflicts.IsEmpty); });
        test("Placement: input immutable arrays and label records are unchanged", () =>
        { var g = Graph(4); var json = GraphIO.ToJson(g); var r = Place(g); Check(json == GraphIO.ToJson(g)); Check(r.Graph.Nodes == g.Nodes && r.Graph.Edges == g.Edges && r.Graph.Ports == g.Ports); });
        test("Placement: valid current positions are stable and repeatable", () =>
        { var a = Place(Graph(4)); var b = Place(a.Graph); Check(b.MovedLabels == 0 && ReferenceEquals(b.Graph, a.Graph)); Check(GraphIO.ToJson(Place(Graph(4)).Graph) == GraphIO.ToJson(a.Graph)); });
        test("Placement: labels are processed in stable ID order", () =>
        { var g = Graph(4); var a = Place(g).Graph.Index(); var b = Place(g with { Labels = g.Labels.Reverse().ToImmutableArray() }).Graph.Index(); Check(a.Labels.All(p => p.Value == b.Labels[p.Key])); });
        test("Placement: selected labels only; other labels remain obstacles", () =>
        { var g = Graph(2); var r = new LabelPlacementEngine().Calculate(g, measure: Size, labelIds: new HashSet<string> { "l0" }); Check(r.MovedLabels == 1 && r.Graph.Labels[1] == g.Labels[1]); Check(!r.Conflicts.Any(c => c.LabelId == "l0")); });
        test("Placement: empty selection reports conflicts without moving", () =>
        { var g = Graph(2); var r = new LabelPlacementEngine().Calculate(g, measure: Size, labelIds: new HashSet<string>()); Check(ReferenceEquals(g, r.Graph) && r.EligibleLabels == 0 && r.Conflicts.Length > 0); });
        test("Placement: unknown selected IDs are rejected", () =>
        { Throws<ArgumentException>(() => new LabelPlacementEngine().Calculate(Graph(), labelIds: new HashSet<string> { "missing" })); });
        test("Placement: locked owners preserve their label geometry", () =>
        { var g = Graph(); g = g with { Nodes = [g.Nodes[0] with { Locked = true }] }; var r = Place(g); Check(ReferenceEquals(r.Graph, g) && r.EligibleLabels == 0 && r.Conflicts.Length > 0); });
        test("Placement: locked ancestors preserve child labels", () =>
        { var g = Graph(); g = g with { Nodes = [new("group", new(-100, -100, 500, 400), IsGroup: true, Locked: true), g.Nodes[0] with { ParentId = "group" }] }; Check(Place(g).EligibleLabels == 0); });
        test("Placement: folded child labels are neither moved nor reported", () =>
        { var g = Graph(); g = g with { Nodes = [new("group", new(-100, -100, 500, 400), IsGroup: true, Collapsed: true), g.Nodes[0] with { ParentId = "group" }] }; var r = Place(g); Check(r.EligibleLabels == 0 && r.Conflicts.IsEmpty); });
        test("Placement: expanded group interior is usable", () =>
        { var g = new GraphSnapshot([new("group", new(0, 0, 600, 400), IsGroup: true)], [], [], [new("l", "group", "Inside", new())]); var r = Place(g); Check(r.MovedLabels == 0 && r.Conflicts.IsEmpty); });
        test("Placement: custom candidate providers use world coordinates", () =>
        { var r = new LabelPlacementEngine().Calculate(Graph(), measure: Size, candidates: new Provider(_ => [new(300, 200)])); Check(r.Graph.Labels[0].Offset == new PointD(250, 170) && r.Conflicts.IsEmpty); });
        test("Placement: impossible candidate sets produce explicit conflicts", () =>
        { var r = new LabelPlacementEngine().Calculate(Graph(), measure: Size, candidates: new Provider(_ => [])); Check(r.MovedLabels == 0 && r.Conflicts.Any(c => c.Kind == LabelConflictKind.Node)); });
        test("Placement: candidate enumeration is bounded", () =>
        { Throws<InvalidOperationException>(() => new LabelPlacementEngine().Calculate(Graph(), new(CandidateLimit: 2), Size, candidates: new Provider(_ => [new(), new(), new()]))); });
        test("Placement: nonfinite candidate positions are rejected", () =>
        { Throws<ArgumentException>(() => new LabelPlacementEngine().Calculate(Graph(), measure: Size, candidates: new Provider(_ => [new(double.NaN, 0)]))); });
        test("Placement: invalid measurements are rejected", () =>
        { foreach (var size in new[] { new PointD(0, 20), new PointD(20, -1), new PointD(double.NaN, 20), new PointD(20, double.PositiveInfinity) }) Throws<ArgumentException>(() => new LabelPlacementEngine().Calculate(Graph(), measure: _ => size)); });
        test("Placement: invalid options are rejected", () =>
        { foreach (var options in new[] { new LabelPlacementOptions(Gap: -1), new(Gap: double.NaN), new(Passes: 0), new(CandidateLimit: 0), new(EdgeSamples: 0), new(EvaluationLimit: 0) }) Throws<ArgumentOutOfRangeException>(() => new LabelPlacementEngine().Calculate(Graph(), options)); });
        test("Placement: evaluation budget failure leaves original graph unchanged", () =>
        { var g = Graph(4); var json = GraphIO.ToJson(g); Throws<InvalidOperationException>(() => new LabelPlacementEngine().Calculate(g, new(EvaluationLimit: 3), Size)); Check(json == GraphIO.ToJson(g)); });
        test("Placement: pre-canceled operation does no work", () =>
        { using var c = new CancellationTokenSource(); c.Cancel(); Throws<OperationCanceledException>(() => new LabelPlacementEngine().Calculate(Graph(), cancellationToken: c.Token)); });
        test("Placement: cancellation while generating candidates propagates", () =>
        { using var c = new CancellationTokenSource(); Throws<OperationCanceledException>(() => new LabelPlacementEngine().Calculate(Graph(), measure: Size, candidates: new Provider(_ => { c.Cancel(); return [new(500, 100)]; }), cancellationToken: c.Token)); });
        test("Placement: edge labels clear their route without moving bends", () =>
        {
            var g = new GraphSnapshot([new("a", new(0, 0, 100, 60), ""), new("b", new(400, 0, 100, 60), "")],
                [new("e", "a", "b", Routing: EdgeRouting.Polyline)], [], [new("l", "e", "Edge", new())]);
            var r = Place(g); Check(r.MovedLabels == 1 && r.Conflicts.IsEmpty && r.Graph.Edges == g.Edges);
        });
        test("Placement: port labels clear owner geometry and ports", () =>
        {
            var g = new GraphSnapshot([new("n", new(0, 0, 100, 60), "")], [], [new("p", "n", new(1, .5))], [new("l", "p", "Port", new())]);
            var r = Place(g); Check(r.MovedLabels == 1 && r.Conflicts.IsEmpty && r.Graph.Ports == g.Ports);
        });
        test("Placement: label conflicts are reported once per pair", () =>
        { var g = Graph(3) with { Labels = Graph(3).Labels.Select(l => l with { Offset = new(1000, 1000) }).ToImmutableArray() }; var r = new LabelPlacementEngine().Calculate(g, measure: Size, labelIds: new HashSet<string>()); Check(r.Conflicts.Count(c => c.Kind == LabelConflictKind.Label) == 3); });
        test("Placement: giant labels use bounded overflow broad phase", () =>
        { var r = new LabelPlacementEngine().Calculate(Graph(2), measure: _ => new(100000, 100000)); Check(r.MovedLabels == 2 && r.Conflicts.IsEmpty); });
        test("Placement: applying a result is one undoable transaction", () =>
        { var d = new GraphDocument(); var g = Graph(3); d.Replace(g, clearHistory: true); var r = Place(g); d.Replace(r.Graph, "Place labels"); d.Undo(); Check(ReferenceEquals(g, d.Snapshot) && !d.CanUndo); d.Redo(); Check(ReferenceEquals(r.Graph, d.Snapshot)); });
        test("Placement: offsets survive JSON and GraphML round trips", () =>
        { var g = Place(Graph(4)).Graph; var json = GraphIO.ToJson(g); Check(GraphIO.ToJson(GraphIO.FromJson(json)) == json); Check(GraphIO.ToJson(GraphIO.FromGraphML(GraphIO.ToGraphML(g)).Graph) == json); });
        test("Placement: a thousand separated labels remain stable within the work budget", () =>
        {
            var nodes = Enumerable.Range(0, 1000).Select(i => new GraphNode("n" + i, new(i % 40 * 300, i / 40 * 300, 100, 60), "")).ToImmutableArray();
            var g = new GraphSnapshot(nodes, [], [], nodes.Select(n => new GraphLabel("l" + n.Id, n.Id, "Label", new(0, 70))).ToImmutableArray());
            var r = Place(g); Check(r.MovedLabels == 0 && r.EligibleLabels == 1000 && r.Conflicts.IsEmpty && r.Evaluations < 5_000_000);
        });
    }
}
