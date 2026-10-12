using System.Collections.Immutable;
using VisualizeScene.Core;
using VisualizeScene.Editing;

static class GroupSizingScopeTests
{
    static void Check(bool condition) { if (!condition) throw new Exception("Scoped group sizing assertion failed"); }
    static PointD Measure(GraphLabel _) => new(20, 10);
    static GraphSnapshot Graph(int unrelated)
    {
        var nodes = ImmutableArray.CreateBuilder<GraphNode>();
        var labels = ImmutableArray.CreateBuilder<GraphLabel>();
        nodes.Add(new("g", new(-28, -48, 156, 136), IsGroup: true));
        nodes.Add(new("a", new(0, 0, 100, 60), ParentId: "g"));
        labels.Add(new("note", "a", "Inside", new()));
        for (var i = 0; i < unrelated; i++)
        {
            nodes.Add(new("outside" + i, new(1000 + i * 150, 500, 100, 60)));
            labels.Add(new("label" + i, "outside" + i, "Unrelated", new()));
        }
        return new(nodes.ToImmutable(), [], [], labels.ToImmutable());
    }
    public static void Run(Action<string, Action> test)
    {
        test("Group scope: unrelated annotations do not allocate one list per owner", () => {
            var small = Graph(0); var large = Graph(10000); string[] ids = ["g"];
            Func<GraphLabel, PointD> measure = Measure;
            long Run(GraphSnapshot graph)
            {
                var start = GC.GetAllocatedBytesForCurrentThread();
                var result = GraphGroupSizing.Calculate(graph, ids, measure: measure);
                var allocated = GC.GetAllocatedBytesForCurrentThread() - start;
                Check(ReferenceEquals(graph, result.Graph) && result.ExaminedNodes == 2 && result.MeasuredLabels == 1);
                Check(result.ExaminedLabels == graph.Labels.Length); // A full scan remains, not a sublinear-query claim.
                return allocated;
            }
            for (var i = 0; i < 4; i++) { _ = Run(small); _ = Run(large); }
            var a = Run(small); var b = Run(large);
            Check(b <= a + 4096);
        });
        test("Group scope: unrelated nested labels and fitted-root labels are never measured", () => {
            var graph = Graph(0);
            graph = graph with
            {
                Nodes = graph.Nodes.Add(new("other", new(1000, 1000, 500, 500), IsGroup: true))
                    .Add(new("b", new(1100, 1100, 100, 60), ParentId: "other")),
                Labels = graph.Labels.Add(new("own", "g", "Root", new(10000, 10000)))
                    .Add(new("remote", "b", "Other group", new()))
            };
            var measured = new List<string>();
            var result = GraphGroupSizing.Calculate(graph, ["g"], measure: label => { measured.Add(label.Id); return Measure(label); });
            Check(ReferenceEquals(result.Graph, graph) && measured.SequenceEqual(["note"]));
        });
        test("Group scope: a selected nested group's own annotation still expands its selected ancestor", () => {
            var graph = new GraphSnapshot([
                new("g", new(-100, -100, 800, 800), IsGroup: true),
                new("h", new(0, 0, 500, 500), IsGroup: true, ParentId: "g"),
                new("a", new(100, 100, 100, 60), ParentId: "h")], [], [],
                [new("note", "h", "Nested group", new(500, 0))]);
            var result = GraphGroupSizing.Calculate(graph, ["h", "g"], measure: Measure);
            var index = result.Graph.Index();
            Check(result.MeasuredLabels == 1 && result.ExaminedNodes == 3);
            Check(Geometry.RectangleContainsRectangle(index.Nodes["g"].Bounds,
                GraphScene.LabelBounds(index, graph.Labels[0], Measure(graph.Labels[0]))));
        });
        test("Group scope: enlarge-only retains existing space and repeated operation is inert", () => {
            var graph = new GraphSnapshot([new("g", new(-100, -100, 500, 400), IsGroup: true),
                new("a", new(450, 320, 100, 60), ParentId: "g")], [], [], []);
            var options = new GroupSizingOptions(EnlargeOnly: true, IncludeLabels: false);
            var result = GraphGroupSizing.Calculate(graph, ["g"], options);
            Check(result.Graph.Nodes[0].Bounds == new RectD(-100, -100, 678, 508));
            Check(result.Graph.Nodes[1] == graph.Nodes[1]);
            Check(ReferenceEquals(GraphGroupSizing.Calculate(result.Graph, ["g"], options).Graph, result.Graph));
        });
    }
}
