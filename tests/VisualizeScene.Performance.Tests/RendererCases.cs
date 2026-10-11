using System.Collections.Immutable;
using SkiaSharp;
using VisualizeScene.Core;
using VisualizeScene.Rendering.Skia;

static class RendererCases
{
    static byte[] Paint(GraphRenderer renderer, GraphSnapshot graph, Viewport? viewport = null)
    {
        using var surface = SKSurface.Create(new SKImageInfo(800, 400)) ?? throw new Exception("No raster surface");
        renderer.Render(surface.Canvas, graph, viewport ?? new(), 800, 400, new(ShowGrid: false));
        using var image = surface.Snapshot(); using var pixels = SKBitmap.FromImage(image); return pixels.Bytes;
    }
    static void FreshMatches(GraphRenderer cached, GraphSnapshot graph)
    {
        var actual = Paint(cached, graph); using var fresh = new GraphRenderer(); var expected = Paint(fresh, graph);
        Cases.Check(actual.SequenceEqual(expected));
    }
    public static void Run(Action<string, Action> test)
    {
        ReviewCases.Run(test);
        test("Renderer: 10000 labels/ports use a viewport broad phase", () => {
            using var r = new GraphRenderer(); Paint(r, Cases.Many(10000)); var s = r.Statistics;
            Cases.Check(s.VisibleLabels > 0 && s.VisibleLabels < 20 && s.VisiblePorts < 20 && s.ExaminedItems < 512);
        });
        test("Renderer: unchanged frames reuse preparation", () => {
            var g = Cases.Graph(); using var r = new GraphRenderer(); var first = Paint(r, g); var prep = r.PreparationStatistics;
            for (var i = 0; i < 5; i++) Cases.Check(first.SequenceEqual(Paint(r, g)));
            Cases.Check(r.PreparationStatistics == prep);
        });
        test("Renderer: pure panning does not rebuild document indexes", () => {
            var g = Cases.Many(1000); using var r = new GraphRenderer(); var v = new Viewport(); Paint(r, g, v); var before = r.PreparationStatistics;
            v.Pan(new(-300, -250)); Paint(r, g, v); var after = r.PreparationStatistics;
            Cases.Check(before.SceneBuilds == after.SceneBuilds && before.AnnotationIndexBuilds == after.AnnotationIndexBuilds && before.NodeIndexBuilds == after.NodeIndexBuilds);
        });
        test("Renderer: label-only edits avoid node/edge index and path rebuilds", () => {
            var g = Cases.Graph(); using var r = new GraphRenderer(); Paint(r, g); var old = r.PreparationStatistics;
            var edit = new GraphEdit(g); edit.Update(g.Labels[0] with { Offset = new(100, 140) }); var next = edit.Build(); FreshMatches(r, next);
            var now = r.PreparationStatistics;
            Cases.Check(now.CreatedPaths == old.CreatedPaths && now.NodeIndexBuilds == old.NodeIndexBuilds && now.EdgeIndexBuilds == old.EdgeIndexBuilds);
        });
        test("Renderer: edge-style changes reuse paths but update rendered pixels", () => {
            var g = Cases.Graph(); using var r = new GraphRenderer(); var image = Paint(r, g); var old = r.PreparationStatistics;
            var edit = new GraphEdit(g); edit.Update(g.Edges[0] with { Stroke = "#FF0000" }); var next = edit.Build();
            Cases.Check(!image.SequenceEqual(Paint(r, next))); FreshMatches(r, next);
            Cases.Check(r.PreparationStatistics.CreatedPaths == old.CreatedPaths && r.PreparationStatistics.ReusedPaths > old.ReusedPaths);
        });
        test("Renderer: node motion rebuilds attached paths only", () => {
            var g = Cases.Graph(); using var r = new GraphRenderer(); Paint(r, g); var old = r.PreparationStatistics;
            var edit = new GraphEdit(g); edit.Move(["a"], new(0, 80)); FreshMatches(r, edit.Build()); var now = r.PreparationStatistics;
            Cases.Check(now.CreatedPaths == old.CreatedPaths + 1 && now.ReusedPaths > old.ReusedPaths);
        });
        test("Renderer: relocated port invalidates its edge and label geometry", () => {
            var g = Cases.Graph(); g = g with { Edges = [g.Edges[0] with { SourcePortId = "ap" }, g.Edges[1]], Labels = [new("lp", "ap", "Port annotation", new(0, 90))] };
            using var r = new GraphRenderer(); Paint(r, g);
            var edit = new GraphEdit(g); edit.Update(g.Ports[0] with { RelativeLocation = new(.5, 0) }); FreshMatches(r, edit.Build());
        });
        test("Renderer: text and font-size changes invalidate annotation bounds", () => {
            var g = Cases.Graph(); using var r = new GraphRenderer(); Paint(r, g); var old = r.PreparationStatistics;
            var edit = new GraphEdit(g); edit.Update(g.Labels[0] with { Text = "Larger label", FontSize = 24 }); FreshMatches(r, edit.Build());
            Cases.Check(r.PreparationStatistics.AnnotationIndexBuilds == old.AnnotationIndexBuilds + 1);
        });
        test("Renderer: distant label is visible even when its owner is outside viewport", () => {
            var g = new GraphSnapshot([new("a", new(5000, 5000, 100, 60))], [], [], [new("l", "a", "Visible", new(-5000, -5000))]);
            using var r = new GraphRenderer(); Paint(r, g); Cases.Check(r.Statistics.VisibleNodes == 0 && r.Statistics.VisibleLabels == 1);
        });
        test("Renderer: folding removes hidden label and port pictures", () => {
            var edit = new GraphEdit(Cases.Graph()); var group = edit.Group(["a", "b"]); var expanded = edit.Build();
            using var r = new GraphRenderer(); Paint(r, expanded); edit.Update(edit.Node(group) with { Collapsed = true }); FreshMatches(r, edit.Build());
            Cases.Check(r.Statistics.VisibleLabels == 0 && r.Statistics.VisiblePorts == 0);
        });
        test("Renderer: changed collection order preserves label paint order", () => {
            var g = Cases.Graph(); g = g with { Labels = [g.Labels[0], g.Labels[0] with { Id = "top", Text = "TOP" }] };
            using var r = new GraphRenderer(); Paint(r, g); FreshMatches(r, g with { Labels = g.Labels.Reverse().ToImmutableArray() });
        });
        test("Renderer: reordered overlapping edges match a fresh painter order", () => {
            var g = Cases.Graph(); g = g with { Edges = [g.Edges[0] with { Stroke = "#FF0000" }, g.Edges[0] with { Id = "second", Stroke = "#0000FF" }] };
            using var r = new GraphRenderer(); Paint(r, g); FreshMatches(r, g with { Edges = g.Edges.Reverse().ToImmutableArray() });
        });
        test("Renderer: deletion and undo do not reuse disposed pictures", () => {
            var d = new GraphDocument(); d.Replace(Cases.Graph()); using var r = new GraphRenderer(); Paint(r, d.Snapshot);
            d.Edit("delete", e => e.Remove("a")); FreshMatches(r, d.Snapshot); d.Undo(); FreshMatches(r, d.Snapshot);
        });
        test("Renderer: disposed instances release caches and reject drawing", () => {
            var r = new GraphRenderer(); Paint(r, Cases.Graph()); r.Dispose(); r.Dispose();
            Cases.Throws<ObjectDisposedException>(() => Paint(r, Cases.Graph()));
        });
    }
}
