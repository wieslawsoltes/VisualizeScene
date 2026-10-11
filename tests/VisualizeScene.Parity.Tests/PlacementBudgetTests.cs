using System.Collections.Immutable;
using VisualizeScene.Core;
using VisualizeScene.Layout;

static class PlacementBudgetTests
{
    private static void Check(bool value) { if (!value) throw new Exception("Assertion failed"); }
    private static GraphSnapshot EdgeGraph() => new(
        [new("a", new(0, 0, 100, 60), ""), new("b", new(600, 0, 100, 60), "")],
        [new("e", "a", "b", Routing: EdgeRouting.Polyline)], [],
        [new("l", "e", "Caption", new())]);

    public static void Run(Action<string, Action> test)
    {
        test("Placement budgets: maximum edge samples respect the default candidate limit", () =>
        {
            var result = new LabelPlacementEngine().Calculate(EdgeGraph(), new(EdgeSamples: 50), _ => new(50, 20));
            Check(result.MovedLabels == 1 && result.Conflicts.IsEmpty);
        });
        test("Placement budgets: a single built-in candidate is a valid bounded search", () =>
        {
            var result = new LabelPlacementEngine().Calculate(EdgeGraph(), new(CandidateLimit: 1), _ => new(50, 20));
            Check(result.EligibleLabels == 1 && result.Graph.Labels[0].Offset.IsFinite);
        });
        test("Placement budgets: all built-in owner candidates obey the configured bound", () =>
        {
            var graph = EdgeGraph() with { Ports = [new("p", "a", new(1, .5))] };
            var provider = new DefaultLabelCandidateProvider();
            foreach (var owner in new[] { "a", "p", "e" }) foreach (var limit in new[] { 1, 3, 17, 128 })
            {
                var label = new GraphLabel("x", owner, "X", new());
                var candidates = provider.GetCandidates(new(graph.Index(), label, new(50, 20), new(CandidateLimit: limit, EdgeSamples: 50))).ToArray();
                Check(candidates.Length > 0 && candidates.Length <= limit && candidates.All(p => p.IsFinite));
            }
        });
        test("Placement budgets: subdivided routes exhaust the work budget before candidate search", () =>
        {
            var graph = EdgeGraph();
            graph = graph with
            {
                Edges = [graph.Edges[0] with { Bends = Enumerable.Range(0, 200).Select(i => new PointD(110 + i * 2, 30)).ToImmutableArray() }],
                Labels = [graph.Labels[0] with { Offset = new(0, 1000) }]
            };
            var rejected = false;
            try { new LabelPlacementEngine().Calculate(graph, new(EvaluationLimit: 100), _ => new(50, 20), labelIds: new HashSet<string>()); }
            catch (InvalidOperationException ex) { rejected = ex.Message.Contains("evaluation limit"); }
            Check(rejected && graph.Labels[0].Offset == new PointD(0, 1000));
        });
    }
}
