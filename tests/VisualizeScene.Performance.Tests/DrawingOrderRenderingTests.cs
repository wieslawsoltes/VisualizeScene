using System.Collections.Immutable;
using SkiaSharp;
using VisualizeScene.Core;
using VisualizeScene.Editing;
using VisualizeScene.Rendering.Skia;

static class DrawingOrderRenderingTests
{
    static byte[] Paint(GraphRenderer renderer, GraphSnapshot graph)
    {
        using var surface = SKSurface.Create(new SKImageInfo(800, 400))!;
        var camera = new Viewport(); camera.ZoomAt(new(), .875); camera.Pan(new(3.125, 2.875));
        renderer.Render(surface.Canvas, graph, camera, 800, 400, new(ShowGrid: false));
        using var image = surface.Snapshot(); using var bitmap = SKBitmap.FromImage(image); return bitmap.Bytes;
    }
    static void MatchesFresh(GraphRenderer cached, GraphSnapshot graph)
    { var pixels = Paint(cached, graph); using var fresh = new GraphRenderer(); Cases.Check(pixels.SequenceEqual(Paint(fresh, graph))); }
    static void SameGeometry(RenderPreparationStatistics a, RenderPreparationStatistics b)
    { Cases.Check(a.NodeIndexBuilds == b.NodeIndexBuilds && a.EdgeIndexBuilds == b.EdgeIndexBuilds && a.AnnotationIndexBuilds == b.AnnotationIndexBuilds && a.CreatedPaths == b.CreatedPaths); }
    public static void Run(Action<string, Action> test)
    {
        test("Order rendering: node reordering reuses geometry but changes overlap pixels", () => {
            var g = new GraphSnapshot([new("a", new(20, 20, 150, 100), "", Fill: "#FF0000"), new("b", new(60, 30, 150, 100), "", Fill: "#0000FF")], [], [], []);
            using var renderer = new GraphRenderer(); var before = Paint(renderer, g); var stats = renderer.PreparationStatistics;
            var next = GraphDrawingOrder.Calculate(g, ["a"], DrawingOrder.BringToFront).Graph;
            Cases.Check(!before.SequenceEqual(Paint(renderer, next))); SameGeometry(stats, renderer.PreparationStatistics); MatchesFresh(renderer, next); MatchesFresh(renderer, g);
        });
        test("Order rendering: overlapping edges reuse native paths and their spatial index", () => {
            var g = Cases.Graph(); g = g with { Edges = [g.Edges[0] with { Stroke = "#FF0000" }, g.Edges[0] with { Id = "top", Stroke = "#0000FF" }] };
            using var renderer = new GraphRenderer(); var before = Paint(renderer, g); var stats = renderer.PreparationStatistics;
            var next = GraphDrawingOrder.Calculate(g, ["ab"], DrawingOrder.BringToFront).Graph;
            Cases.Check(!before.SequenceEqual(Paint(renderer, next))); SameGeometry(stats, renderer.PreparationStatistics); MatchesFresh(renderer, next);
        });
        test("Order rendering: captions, colors and position locks do not rebuild geometry", () => {
            var g = Cases.Graph(); using var renderer = new GraphRenderer(); Paint(renderer, g); var stats = renderer.PreparationStatistics;
            var next = g with { Nodes = g.Nodes.SetItem(0, g.Nodes[0] with { Text = "Changed caption", Fill = "#FFCC66", Stroke = "#CC0000", Locked = true }) };
            MatchesFresh(renderer, next); SameGeometry(stats, renderer.PreparationStatistics);
        });
        test("Order rendering: directed markers and edge captions refresh without rerouting", () => {
            var g = Cases.Graph(); using var renderer = new GraphRenderer(); Paint(renderer, g); var stats = renderer.PreparationStatistics;
            var next = g with { Edges = g.Edges.SetItem(0, g.Edges[0] with { Directed = false, Text = "Updated edge", Weight = 27, Stroke = "#BB0022" }) };
            MatchesFresh(renderer, next); SameGeometry(stats, renderer.PreparationStatistics);
        });
        test("Order rendering: shape and position changes still invalidate clipping and annotations", () => {
            var g = Cases.Graph(); using var renderer = new GraphRenderer(); Paint(renderer, g); var stats = renderer.PreparationStatistics;
            var next = g with { Nodes = g.Nodes.SetItem(0, g.Nodes[0] with { Shape = NodeShape.Diamond, Bounds = new(10, 50, 150, 100) }) };
            MatchesFresh(renderer, next); var after = renderer.PreparationStatistics;
            Cases.Check(after.NodeIndexBuilds > stats.NodeIndexBuilds && after.CreatedPaths > stats.CreatedPaths && after.AnnotationIndexBuilds > stats.AnnotationIndexBuilds);
        });
        test("Order rendering: changed bends cannot inherit an old style-only route", () => {
            var g = Cases.Graph(); using var renderer = new GraphRenderer(); Paint(renderer, g); var stats = renderer.PreparationStatistics;
            MatchesFresh(renderer, g with { Edges = g.Edges.SetItem(0, g.Edges[0] with { Bends = [new(170, 120)] }) });
            Cases.Check(renderer.PreparationStatistics.CreatedPaths == stats.CreatedPaths + 1 && renderer.PreparationStatistics.AnnotationIndexBuilds > stats.AnnotationIndexBuilds);
        });
        test("Order rendering: labels reorder in their own annotation layer", () => {
            var g = Cases.Graph(); g = g with { Labels = [new("l1", "a", "Large", new(0, 120), 24), new("l2", "a", "Small", new(0, 120), 10)] };
            using var renderer = new GraphRenderer(); Paint(renderer, g);
            MatchesFresh(renderer, GraphDrawingOrder.Calculate(g, ["l1"], DrawingOrder.BringToFront).Graph);
        });
        test("Order rendering: reparenting and folding invalidate hierarchy-dependent caches", () => {
            var edit = new GraphEdit(Cases.Graph()); var parent = edit.Group(["a", "b"]); var g = edit.Build();
            using var renderer = new GraphRenderer(); Paint(renderer, g); var stats = renderer.PreparationStatistics;
            edit.Update(edit.Node(parent) with { Collapsed = true }); MatchesFresh(renderer, edit.Build());
            Cases.Check(renderer.PreparationStatistics.AnnotationIndexBuilds > stats.AnnotationIndexBuilds);
            MatchesFresh(renderer, g);
        });
        test("Order rendering: large style revisions reuse every spatial index and native path", () => {
            var g = Cases.Many(10000); using var renderer = new GraphRenderer(); Paint(renderer, g); var stats = renderer.PreparationStatistics;
            Paint(renderer, g with { Nodes = g.Nodes.Select(n => n with { Fill = "#FFCC66" }).ToImmutableArray() });
            SameGeometry(stats, renderer.PreparationStatistics);
            Cases.Check(renderer.Statistics.ExaminedItems < 512);
        });
        test("Order rendering: alternating style, order and geometry revisions match uncached output", () => {
            var g = Cases.Graph(); using var renderer = new GraphRenderer(); var random = new Random(22);
            for (var i = 0; i < 30; i++) {
                g = (i % 3) switch {
                    0 => g with { Nodes = g.Nodes.Reverse().ToImmutableArray() },
                    1 => g with { Nodes = g.Nodes.SetItem(0, g.Nodes[0] with { Fill = i % 2 == 0 ? "#FFCC66" : "#DDEFE3", Text = "Title " + i }) },
                    _ => g with { Nodes = g.Nodes.SetItem(0, g.Nodes[0] with { Bounds = g.Nodes[0].Bounds.Move(new(random.Next(15), random.Next(15))) }) }
                };
                MatchesFresh(renderer, g);
            }
        });
    }
}
