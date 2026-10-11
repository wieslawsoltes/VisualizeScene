using System.Collections.Immutable;
using VisualizeScene.Core;
using VisualizeScene.Algorithms;

static class NumericTests
{
    private static void Check(bool value) { if (!value) throw new Exception("Assertion failed"); }
    private static void Near(double a, double b, double tolerance = 1e-12)
    { if (!double.IsFinite(a) || Math.Abs(a - b) > tolerance) throw new Exception($"Expected {b:R}, got {a:R}"); }
    private static void Throws<T>(Action action) where T : Exception
    { try { action(); } catch (T) { return; } throw new Exception("Expected " + typeof(T).Name); }
    private static GraphSnapshot Graph(int n, IEnumerable<GraphEdge> edges) => new(
        Enumerable.Range(0, n).Select(i => new GraphNode("n" + i, new(i * 200, 0, 100, 60))).ToImmutableArray(), edges.ToImmutableArray(), [], []);
    // Independent dense transition-matrix oracle, intentionally only used for small graphs.
    private static double[] ReferenceRank(GraphSnapshot graph, double damping, int steps)
    {
        var n = graph.Nodes.Length; var matrix = new double[n, n]; var ids = graph.Nodes.Select((x, i) => (x.Id, i)).ToDictionary(x => x.Id, x => x.i);
        foreach (var e in graph.Edges) { matrix[ids[e.SourceId], ids[e.TargetId]]++; if (!e.Directed) matrix[ids[e.TargetId], ids[e.SourceId]]++; }
        for (var row = 0; row < n; row++)
        {
            double sum = 0; for (var col = 0; col < n; col++) sum += matrix[row, col];
            for (var col = 0; col < n; col++) matrix[row, col] = sum == 0 ? 1d / n : matrix[row, col] / sum;
        }
        var rank = Enumerable.Repeat(1d / n, n).ToArray();
        for (var step = 0; step < steps; step++)
        {
            var next = new double[n];
            for (var col = 0; col < n; col++) { next[col] = (1 - damping) / n; for (var row = 0; row < n; row++) next[col] += damping * matrix[row, col] * rank[row]; }
            rank = next;
        }
        return rank;
    }
    public static void Run(Action<string, Action> test)
    {
        test("Numeric: PageRank matches independent dense oracle on mixed multigraphs", () => {
            var random = new Random(829);
            for (var trial = 0; trial < 80; trial++) {
                var count = random.Next(1, 16); var graph = Graph(count, Enumerable.Range(0, random.Next(1, 50)).Select(i =>
                    new GraphEdge("e" + i, "n" + random.Next(count), "n" + random.Next(count), Directed: random.Next(2) == 0)));
                var damping = trial % 2 == 0 ? .85 : .3; var expected = ReferenceRank(graph, damping, 60);
                var actual = GraphAlgorithms.PageRank(graph, damping, 60, double.Epsilon);
                for (var i = 0; i < count; i++) Near(actual["n" + i], expected[i]);
                Near(actual.Values.Sum(), 1);
            }
        });
        test("Numeric: PageRank dangling, singleton and zero-damping limits", () => {
            var emptyEdges = Graph(4, []); var r = GraphAlgorithms.PageRank(emptyEdges); Check(r.Values.All(x => x == .25));
            Check(GraphAlgorithms.PageRank(Graph(1, []))["n0"] == 1);
            r = GraphAlgorithms.PageRank(Graph(2, [new("e", "n0", "n1")]), damping: 0); Check(r.Values.All(x => x == .5));
        });
        test("Numeric: PageRank cancellation applies even to empty graphs", () => {
            using var c = new CancellationTokenSource(); c.Cancel();
            Throws<OperationCanceledException>(() => GraphAlgorithms.PageRank(GraphSnapshot.Empty, cancellationToken: c.Token));
            Throws<ArgumentOutOfRangeException>(() => GraphAlgorithms.PageRank(GraphSnapshot.Empty, iterations: 0));
            Throws<ArgumentOutOfRangeException>(() => GraphAlgorithms.PageRank(GraphSnapshot.Empty, damping: double.NaN));
        });
        test("Numeric: PageRank iterations reuse storage rather than allocate dictionaries", () => {
            var g = Graph(2000, Enumerable.Range(0, 1999).Select(i => new GraphEdge("e" + i, "n" + i, "n" + (i + 1))));
            long Measure(int iterations) {
                var before = GC.GetAllocatedBytesForCurrentThread(); var ranks = GraphAlgorithms.PageRank(g, iterations: iterations, tolerance: double.Epsilon);
                var bytes = GC.GetAllocatedBytesForCurrentThread() - before; GC.KeepAlive(ranks); return bytes;
            }
            _ = Measure(1); _ = Measure(100); var one = Measure(1); var many = Measure(100);
            Check(many <= one + 32768);
        });
        test("Numeric: PageRank results and immutable input are independent", () => {
            var g = Graph(3, [new("e", "n0", "n1")]); var before = g.Nodes; var a = GraphAlgorithms.PageRank(g); var b = GraphAlgorithms.PageRank(g);
            ((IDictionary<string, double>)a)["n0"] = 99; Check(b["n0"] != 99 && g.Nodes == before);
        });
        test("Numeric: small flow through huge-capacity edges is not lost to subtraction", () => {
            var g = Graph(3, [new("large", "n0", "n1", Weight: 1e25), new("small", "n1", "n2", Weight: 1)]);
            var r = GraphAlgorithms.MaximumFlow(g, "n0", "n2"); Check(r.Value == 1 && r.EdgeFlows["large"] == 1 && r.EdgeFlows["small"] == 1);
        });
        test("Numeric: antiparallel edges have independent residual pairs", () => {
            var g = Graph(3, [new("a", "n0", "n1", Weight: 10), new("b", "n1", "n0", Weight: 50), new("c", "n1", "n2", Weight: 3), new("loop", "n1", "n1", Weight: 100)]);
            var r = GraphAlgorithms.MaximumFlow(g, "n0", "n2"); Check(r.Value == 3 && r.EdgeFlows["a"] == 3 && r.EdgeFlows["b"] == 0 && r.EdgeFlows["loop"] == 0);
        });
        test("Numeric: flow agrees with exhaustive min cuts and conservation", () => {
            var random = new Random(170);
            for (var trial = 0; trial < 100; trial++) {
                var n = random.Next(2, 8); var graph = Graph(n, Enumerable.Range(0, 22).Select(i =>
                    new GraphEdge("e" + i, "n" + random.Next(n), "n" + random.Next(n), Weight: random.Next(10))));
                var result = GraphAlgorithms.MaximumFlow(graph, "n0", "n" + (n - 1)); double cut = double.PositiveInfinity;
                for (var mask = 1; mask < (1 << (n - 1)); mask += 2) {
                    var capacity = graph.Edges.Where(e => (mask & (1 << int.Parse(e.SourceId[1..]))) != 0 && (mask & (1 << int.Parse(e.TargetId[1..]))) == 0).Sum(e => e.Weight);
                    cut = Math.Min(cut, capacity);
                }
                Near(result.Value, cut);
                foreach (var e in graph.Edges) Check(result.EdgeFlows[e.Id] >= 0 && result.EdgeFlows[e.Id] <= e.Weight);
                for (var i = 0; i < n; i++) {
                    var id = "n" + i; var balance = graph.Edges.Where(e => e.SourceId == id).Sum(e => result.EdgeFlows[e.Id]) - graph.Edges.Where(e => e.TargetId == id).Sum(e => result.EdgeFlows[e.Id]);
                    Near(balance, i == 0 ? cut : i == n - 1 ? -cut : 0);
                }
                Near(graph.Edges.Where(e => result.SourcePartition.Contains(e.SourceId) && !result.SourcePartition.Contains(e.TargetId)).Sum(e => e.Weight), cut);
            }
        });
        test("Numeric: subnormal, tiny and large representable flows survive", () => {
            foreach (var cap in new[] { double.Epsilon, 1e-200, 1e-15, 1d, 1e200 }) {
                var r = GraphAlgorithms.MaximumFlow(Graph(2, [new("e", "n0", "n1", Weight: cap)]), "n0", "n1"); Check(r.Value == cap && r.EdgeFlows["e"] == cap);
            }
        });
        test("Numeric: flow rejects invalid terminals, cancellation and overflow", () => {
            var g = Graph(2, [new("e", "n0", "n1", Weight: 1e308), new("f", "n0", "n1", Weight: 1e308)]);
            Throws<OverflowException>(() => GraphAlgorithms.MaximumFlow(g, "n0", "n1"));
            Throws<ArgumentException>(() => GraphAlgorithms.MaximumFlow(g, "n0", "absent"));
            Throws<ArgumentNullException>(() => GraphAlgorithms.MaximumFlow(g, null!, "n1"));
            using var c = new CancellationTokenSource(); c.Cancel(); Throws<OperationCanceledException>(() => GraphAlgorithms.MaximumFlow(g, "n0", "n1", c.Token));
        });
    }
}
