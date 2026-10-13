using SkiaSharp;
using VisualizeScene.Core;
using VisualizeScene.Rendering.Skia;

static class PictureMemoryCases
{
    static byte[] Draw(GraphRenderer renderer, GraphSnapshot graph, Viewport? view = null)
    {
        using var surface = SKSurface.Create(new SKImageInfo(800, 400))!;
        renderer.Render(surface.Canvas, graph, view ?? new(), 800, 400, new(ShowGrid: false));
        using var image = surface.Snapshot(); using var bitmap = SKBitmap.FromImage(image); return bitmap.Bytes;
    }
    static GraphSnapshot Single() => new([new("a", new(10.25, 20.5, 120, 70), "Measured cache")], [], [], []);
    public static void Run(Action<string, Action> test)
    {
        test("Picture memory: exact estimated budget admits a picture and reducing it evicts immediately", () => {
            var g = Single(); using var r = new GraphRenderer(); var expected = Draw(r, g); var cost = r.PictureStatistics.EstimatedRetainedBytes;
            Cases.Check(cost > 1 && r.PictureStatistics.Retained == 1);
            r.PictureCacheByteBudget = cost; Cases.Check(Draw(r, g).SequenceEqual(expected) && r.PictureStatistics.Retained == 1);
            r.PictureCacheByteBudget = cost - 1; Cases.Check(r.PictureStatistics.EstimatedRetainedBytes == 0 && r.PictureStatistics.Retained == 0);
            Cases.Check(Draw(r, g).SequenceEqual(expected));
            Cases.Check(r.PictureStatistics.Retained == 0 && r.PictureStatistics.OversizedBypasses == 1);
        });
        test("Picture memory: oversized node and annotation pictures replay safely without entering the LRU", () => {
            var g = Cases.Graph(); using var r = new GraphRenderer { PictureCacheByteBudget = 1 };
            using var direct = new GraphRenderer { PictureCacheCapacity = 0 };
            for (var i = 0; i < 3; i++) Cases.Check(Draw(r, g).SequenceEqual(Draw(direct, g)));
            Cases.Check(r.PictureStatistics.Retained == 0 && r.PictureStatistics.EstimatedRetainedBytes == 0 && r.PictureStatistics.OversizedBypasses >= 12);
        });
        test("Picture memory: zero byte budget disables retention while preserving fractional raster output", () => {
            var g = Cases.Graph(); using var r = new GraphRenderer(); Draw(r, g); r.PictureCacheByteBudget = 0;
            using var direct = new GraphRenderer { PictureCacheCapacity = 0 };
            foreach (var zoom in new[] { .2, .35, 1d, 1.25, 2d }) {
                var v = new Viewport(); v.Restore(new(new(-5.25, -8.5), zoom));
                Cases.Check(Draw(r, g, v).SequenceEqual(Draw(direct, g, v)));
                Cases.Check(r.PictureStatistics.Retained == 0 && r.PictureStatistics.EstimatedRetainedBytes == 0);
            }
            r.PictureCacheByteBudget = 16L * 1024 * 1024; Draw(r, g); Cases.Check(r.PictureStatistics.EstimatedRetainedBytes > 0);
        });
        test("Picture memory: cost accounting follows replacements rather than accumulating old pictures", () => {
            var g = Cases.Graph(); using var r = new GraphRenderer(); Draw(r, g);
            for (var i = 0; i < 8; i++) {
                var next = g with { Nodes = g.Nodes.SetItem(0, g.Nodes[0] with { Text = "Changed " + i }),
                    Labels = [g.Labels[0] with { Text = "Annotation " + i, FontSize = 13 + i }] };
                var actual = Draw(r, next); using var fresh = new GraphRenderer(); Cases.Check(actual.SequenceEqual(Draw(fresh, next)));
                Cases.Check(r.PictureStatistics.EstimatedRetainedBytes == fresh.PictureStatistics.EstimatedRetainedBytes);
            }
            Draw(r, GraphSnapshot.Empty); Cases.Check(r.PictureStatistics.Retained == 0 && r.PictureStatistics.EstimatedRetainedBytes == 0);
        });
        test("Picture memory: admission evicts older entries before registering the incoming picture", () => {
            var single = Single(); using var probe = new GraphRenderer(); Draw(probe, single); var cost = probe.PictureStatistics.EstimatedRetainedBytes;
            var g = single with { Nodes = single.Nodes.Add(single.Nodes[0] with { Id = "b", Bounds = single.Nodes[0].Bounds.Move(new(2000, 0)) }) };
            using var r = new GraphRenderer { PictureCacheByteBudget = cost }; var far = new Viewport(); far.Restore(new(new(2000, 0), 1));
            Draw(r, g); Draw(r, g, far); var recordings = r.PictureStatistics.NodeRecordings;
            Cases.Check(r.PictureStatistics.Retained == 1 && r.PictureStatistics.Evictions == 1);
            Draw(r, g, far); Cases.Check(r.PictureStatistics.NodeRecordings == recordings);
            Draw(r, g); Cases.Check(r.PictureStatistics.NodeRecordings == recordings + 1 && r.PictureStatistics.EstimatedRetainedBytes <= cost);
        });
        test("Picture memory: count and byte limits both apply to a shared node and annotation cache", () => {
            var g = Cases.Many(100); using var r = new GraphRenderer { PictureCacheCapacity = 3, PictureCacheByteBudget = 6000 };
            var v = new Viewport();
            for (var i = 0; i < 12; i++) {
                v.Restore(new(new(i * 300, 0), 1)); Draw(r, g, v);
                Cases.Check(r.PictureStatistics.Retained <= 3 && r.PictureStatistics.EstimatedRetainedBytes <= 6000);
            }
            r.PictureCacheCapacity = 0; Cases.Check(r.PictureStatistics.Retained == 0 && r.PictureStatistics.EstimatedRetainedBytes == 0);
        });
        test("Picture memory: invalid budgets retain configuration and disposed renderers reject changes", () => {
            var r = new GraphRenderer(); var budget = r.PictureCacheByteBudget;
            Cases.Throws<ArgumentOutOfRangeException>(() => r.PictureCacheByteBudget = -1); Cases.Check(r.PictureCacheByteBudget == budget);
            r.PictureCacheByteBudget = long.MaxValue; Draw(r, Single()); r.Dispose();
            Cases.Check(r.PictureStatistics.EstimatedRetainedBytes == 0 && r.PictureStatistics.Retained == 0);
            Cases.Throws<ObjectDisposedException>(() => r.PictureCacheByteBudget = 0);
        });
    }
}
