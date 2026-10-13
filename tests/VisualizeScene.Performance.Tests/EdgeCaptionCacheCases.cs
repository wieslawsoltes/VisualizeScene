using SkiaSharp;
using VisualizeScene.Core;
using VisualizeScene.Rendering.Skia;

static class EdgeCaptionCacheCases
{
    static GraphSnapshot Graph() => Cases.Graph() with { Edges = [
        new("ab", "a", "b", "First caption\nGraphemes: e\u0301 Ω ffi", Routing: EdgeRouting.Polyline),
        new("bc", "b", "c", "Long text wrapping across several words and more words again", Routing: EdgeRouting.Polyline)] };
    static byte[] Draw(GraphRenderer renderer, GraphSnapshot graph, Viewport? view = null, RenderOptions? options = null)
    {
        using var surface = SKSurface.Create(new SKImageInfo(800, 400))!;
        renderer.Render(surface.Canvas, graph, view ?? new(), 800, 400, options ?? new(ShowGrid: false));
        using var image = surface.Snapshot(); using var bitmap = SKBitmap.FromImage(image); return bitmap.Bytes;
    }
    public static void Run(Action<string, Action> test)
    {
        test("Edge caption cache: replay equals the original direct text path at fractional zoom and origins", () => {
            var g = Graph(); using var cached = new GraphRenderer(); using var direct = new GraphRenderer { PictureCacheCapacity = 0 };
            foreach (var zoom in new[] { .34, .35, 1d, 1.25, 2d, 4d }) {
                var v = new Viewport(); v.Restore(new(new(-7.25, -4.5), zoom));
                Cases.Check(Draw(cached, g, v).SequenceEqual(Draw(direct, g, v)));
            }
        });
        test("Edge caption cache: warmed frames, selection and camera changes reuse recordings", () => {
            var g = Graph(); using var r = new GraphRenderer(); var first = Draw(r, g); var before = r.PictureStatistics;
            Cases.Check(before.EdgeCaptionRecordings == 2 && before.RetainedEdgeCaptions == 2);
            for (var i = 0; i < 20; i++) Cases.Check(first.SequenceEqual(Draw(r, g)));
            var v = new Viewport(); v.Restore(new(new(-5.25, -8.5), 1.25));
            Draw(r, g, v, new(ShowGrid: false, Selection: new HashSet<string> { "ab", "bc" }));
            Cases.Check(r.PictureStatistics.EdgeCaptionRecordings == before.EdgeCaptionRecordings);
        });
        test("Edge caption cache: changing stroke, dash, markers, direction and weight preserves text pictures", () => {
            var g = Graph(); using var r = new GraphRenderer(); Draw(r, g); var before = r.PictureStatistics.EdgeCaptionRecordings;
            var next = g with { Edges = g.Edges.SetItem(0, g.Edges[0] with { Stroke = "#CC2244", Directed = false, Weight = 7, Style = new(3, EdgeDash.Dot) }) };
            using var direct = new GraphRenderer { PictureCacheCapacity = 0 }; Cases.Check(Draw(r, next).SequenceEqual(Draw(direct, next)));
            Cases.Check(r.PictureStatistics.EdgeCaptionRecordings == before);
        });
        test("Edge caption cache: text, route-center and undo changes invalidate exactly the affected picture", () => {
            var g = Graph(); using var r = new GraphRenderer(); Draw(r, g); var before = r.PictureStatistics.EdgeCaptionRecordings;
            using var direct = new GraphRenderer { PictureCacheCapacity = 0 };
            var text = g with { Edges = g.Edges.SetItem(0, g.Edges[0] with { Text = "Replacement" }) };
            Cases.Check(Draw(r, text).SequenceEqual(Draw(direct, text)) && r.PictureStatistics.EdgeCaptionRecordings == before + 1);
            Cases.Check(Draw(r, g).SequenceEqual(Draw(direct, g)) && r.PictureStatistics.EdgeCaptionRecordings == before + 2);
            var bent = g with { Edges = g.Edges.SetItem(0, g.Edges[0] with { Bends = [new(180, 170)] }) };
            Cases.Check(Draw(r, bent).SequenceEqual(Draw(direct, bent)) && r.PictureStatistics.EdgeCaptionRecordings == before + 3);
        });
        test("Edge caption cache: equivalent route arrays do not require another recording", () => {
            var g = Graph(); using var r = new GraphRenderer(); Draw(r, g); var before = r.PictureStatistics.EdgeCaptionRecordings;
            var next = g with { Nodes = g.Nodes.SetItem(2, g.Nodes[2] with { Bounds = g.Nodes[2].Bounds.Move(new(0, 20)) }) };
            Draw(r, next); Cases.Check(r.PictureStatistics.EdgeCaptionRecordings == before + 1);
            // The a-b caption has an equivalent path after this unrelated node-geometry revision.
            using var direct = new GraphRenderer { PictureCacheCapacity = 0 }; Cases.Check(Draw(r, next).SequenceEqual(Draw(direct, next)));
        });
        test("Edge caption cache: empty text and edge deletion promptly remove retained captions", () => {
            var g = Graph(); using var r = new GraphRenderer(); Draw(r, g);
            var empty = g with { Edges = g.Edges.SetItem(0, g.Edges[0] with { Text = "" }) }; Draw(r, empty);
            Cases.Check(r.PictureStatistics.RetainedEdgeCaptions == 1);
            Draw(r, empty with { Edges = [] }); Cases.Check(r.PictureStatistics.RetainedEdgeCaptions == 0);
            Draw(r, g); Draw(r, GraphSnapshot.Empty); Cases.Check(r.PictureStatistics.Retained == 0 && r.PictureStatistics.EstimatedRetainedBytes == 0);
        });
        test("Edge caption cache: group folding removes hidden internal-edge pictures and expansion restores output", () => {
            var g = new GraphSnapshot([new("g", new(-10, -10, 500, 150), IsGroup: true),
                new("a", new(20, 30, 80, 40), ParentId: "g"), new("b", new(300, 30, 80, 40), ParentId: "g")],
                [new("ab", "a", "b", "Internal")], [], []);
            using var r = new GraphRenderer(); var first = Draw(r, g); Cases.Check(r.PictureStatistics.RetainedEdgeCaptions == 1);
            Draw(r, g with { Nodes = g.Nodes.SetItem(0, g.Nodes[0] with { Collapsed = true }) }); Cases.Check(r.PictureStatistics.RetainedEdgeCaptions == 0);
            Cases.Check(first.SequenceEqual(Draw(r, g)) && r.PictureStatistics.EdgeCaptionRecordings == 2);
        });
        test("Edge caption cache: low-detail and overview paths do not record captions", () => {
            var g = Graph(); using var r = new GraphRenderer(); var v = new Viewport(); v.Restore(new(new(), .34));
            Draw(r, g, v); Draw(r, g, options: new(ShowGrid: false, Overview: true));
            Cases.Check(r.PictureStatistics.EdgeCaptionRecordings == 0 && r.PictureStatistics.RetainedEdgeCaptions == 0);
        });
        test("Edge caption cache: oversized text pictures are replayed without unsafe retention or accounting leaks", () => {
            var g = Graph(); using var r = new GraphRenderer { PictureCacheByteBudget = 1 };
            using var direct = new GraphRenderer { PictureCacheCapacity = 0 };
            for (var i = 0; i < 3; i++) Cases.Check(Draw(r, g).SequenceEqual(Draw(direct, g)));
            Cases.Check(r.PictureStatistics.EdgeCaptionRecordings == 6 && r.PictureStatistics.Retained == 0 && r.PictureStatistics.EstimatedRetainedBytes == 0);
        });
        test("Edge caption cache: nodes, labels and captions share both admission limits", () => {
            var g = Graph(); using var r = new GraphRenderer { PictureCacheCapacity = 2, PictureCacheByteBudget = 8000 };
            using var direct = new GraphRenderer { PictureCacheCapacity = 0 };
            Cases.Check(Draw(r, g).SequenceEqual(Draw(direct, g)));
            Cases.Check(r.PictureStatistics.Retained <= 2 && r.PictureStatistics.EstimatedRetainedBytes <= 8000 && r.PictureStatistics.Evictions > 0);
            r.PictureCacheByteBudget = 0; Cases.Check(r.PictureStatistics.RetainedEdgeCaptions == 0 && r.PictureStatistics.EstimatedRetainedBytes == 0);
        });
        test("Edge caption cache: offscreen routes still paint the visible wrapped caption", () => {
            var g = new GraphSnapshot([new("a", new(-85, -20, 20, 20)), new("b", new(-85, 300, 20, 20))],
                [new("ab", "a", "b", "Visible words wrapping from outside the viewport", Routing: EdgeRouting.Polyline)], [], []);
            using var r = new GraphRenderer(); using var direct = new GraphRenderer { PictureCacheCapacity = 0 };
            Cases.Check(Draw(r, g).SequenceEqual(Draw(direct, g)) && r.PictureStatistics.RetainedEdgeCaptions == 1);
        });
        test("Edge caption cache: PNG export is identical with and without retained pictures", () => {
            var g = Graph(); using var r = new GraphRenderer(); using var direct = new GraphRenderer { PictureCacheCapacity = 0 };
            using var first = SKBitmap.Decode(r.ExportPng(g, 1.25)); using var second = SKBitmap.Decode(direct.ExportPng(g, 1.25));
            Cases.Check(first.Bytes.SequenceEqual(second.Bytes)); r.Dispose();
            Cases.Check(r.PictureStatistics.RetainedEdgeCaptions == 0 && r.PictureStatistics.EstimatedRetainedBytes == 0);
        });
    }
}
