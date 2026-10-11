using SkiaSharp;
using VisualizeScene.Core;
using VisualizeScene.Editing;
using VisualizeScene.Rendering.Skia;

static class PictureCacheCases
{
    static byte[] Draw(GraphRenderer renderer, GraphSnapshot graph, Viewport? view = null, IReadOnlySet<string>? selection = null)
    {
        using var surface = SKSurface.Create(new SKImageInfo(800, 400)) ?? throw new Exception("No raster surface");
        renderer.Render(surface.Canvas, graph, view ?? new(), 800, 400, new(ShowGrid: false, Selection: selection));
        using var image = surface.Snapshot(); using var bitmap = SKBitmap.FromImage(image); return bitmap.Bytes;
    }
    public static void Run(Action<string, Action> test)
    {
        test("Cache: cached and uncached text are pixel-identical at fractional zoom/origins", () => {
            var graph = Cases.Graph() with { Labels = [new("l", "a", "Annotation\nGraphemes: e\u0301 Ω ffi", new(15.25, 100.125), 17.5)] };
            using var cached = new GraphRenderer(); using var direct = new GraphRenderer { PictureCacheCapacity = 0 };
            foreach (var zoom in new[] { .35, 1d, 1.25, 2d }) {
                var view = new Viewport(); view.Restore(new(new(-7.25, -3.5), zoom));
                Cases.Check(Draw(cached, graph, view).SequenceEqual(Draw(direct, graph, view)));
            }
        });
        test("Cache: warmed annotation frames do not reshape or record again", () => {
            var graph = Cases.Graph(); using var renderer = new GraphRenderer(); var first = Draw(renderer, graph); var before = renderer.PictureStatistics;
            for (var i = 0; i < 20; i++) Cases.Check(first.SequenceEqual(Draw(renderer, graph)));
            Cases.Check(renderer.PictureStatistics == before && before.LabelRecordings == 1);
        });
        test("Cache: one shared LRU bounds both node and label pictures", () => {
            var graph = Cases.Many(1000); using var renderer = new GraphRenderer { PictureCacheCapacity = 8 }; var view = new Viewport();
            for (var i = 0; i < 20; i++) { view.Restore(new(new(i * 300, 0), 1)); Draw(renderer, graph, view); Cases.Check(renderer.PictureStatistics.Retained <= 8); }
            Cases.Check(renderer.PictureStatistics.Evictions > 0);
            using var direct = new GraphRenderer { PictureCacheCapacity = 0 };
            Cases.Check(Draw(renderer, graph, view).SequenceEqual(Draw(direct, graph, view)));
        });
        test("Cache: reducing the budget evicts immediately and zero disables retention", () => {
            var graph = Cases.Many(100); using var renderer = new GraphRenderer(); Draw(renderer, graph);
            renderer.PictureCacheCapacity = 2; Cases.Check(renderer.PictureStatistics.Retained <= 2);
            renderer.PictureCacheCapacity = 0; Cases.Check(renderer.PictureStatistics.Retained == 0);
            Draw(renderer, graph); Cases.Check(renderer.PictureStatistics.Retained == 0);
            renderer.PictureCacheCapacity = 20; Draw(renderer, graph); Cases.Check(renderer.PictureStatistics.Retained > 0);
            Cases.Throws<ArgumentOutOfRangeException>(() => renderer.PictureCacheCapacity = -1);
        });
        test("Cache: owner movement and label text/font changes invalidate retained labels", () => {
            var graph = Cases.Graph(); using var renderer = new GraphRenderer(); Draw(renderer, graph);
            var edit = new GraphEdit(graph); edit.Move(["a"], new(20.25, 30.5)); edit.Update(graph.Labels[0] with { Text = "Changed", FontSize = 20 }); var next = edit.Build();
            using var direct = new GraphRenderer { PictureCacheCapacity = 0 }; Cases.Check(Draw(renderer, next).SequenceEqual(Draw(direct, next)));
            Cases.Check(renderer.PictureStatistics.LabelRecordings == 2);
        });
        test("Cache: removing graph contents disposes retained native pictures", () => {
            using var renderer = new GraphRenderer(); Draw(renderer, Cases.Graph()); Cases.Check(renderer.PictureStatistics.Retained > 0);
            Draw(renderer, GraphSnapshot.Empty); Cases.Check(renderer.PictureStatistics.Retained == 0);
            Draw(renderer, Cases.Graph()); renderer.Dispose(); Cases.Check(renderer.PictureStatistics.Retained == 0);
            Cases.Throws<ObjectDisposedException>(() => renderer.PictureCacheCapacity = 8);
        });
        test("Selection rendering: select-all visits only viewport items", () => {
            var graph = Cases.Many(10000); var d = new GraphDocument(); d.Replace(graph); using var editor = new EditorSession(d); editor.SelectAll();
            using var renderer = new GraphRenderer(); Draw(renderer, graph, selection: editor.Selection);
            Cases.Check(renderer.Statistics.SelectionAdorners > 0 && renderer.Statistics.SelectionAdorners < 50 && renderer.Statistics.ExaminedItems < 512);
            var view = new Viewport(); view.ZoomAt(new(), .1); Draw(renderer, graph, view, editor.Selection);
            Cases.Check(renderer.Statistics.VisibleLabels == 0);
        });
        test("Selected handles: warm 10000-item selection is hit-tested through local candidates", () => {
            var graph = Cases.Many(10000); var d = new GraphDocument(); d.Replace(graph); using var editor = new EditorSession(d); editor.SelectAll();
            var first = editor.Hit(new(-2, -2)); Cases.Check(first.Kind == HitKind.ResizeHandle && first.Id == "n0");
            for (var i = 0; i < 100; i++) editor.Hit(new(50, 30));
            var start = GC.GetAllocatedBytesForCurrentThread();
            for (var i = 0; i < 1000; i++) Cases.Check(editor.Hit(new(50, 30)).Id == "n0");
            Cases.Check(GC.GetAllocatedBytesForCurrentThread() - start < 500000 && editor.LastHandleCandidateCount < 10 && editor.LastHitStatistics.ExaminedEntries < 256);
        });
        test("Selected handles: immutable selection order controls overlapping handle priority", () => {
            var graph = Cases.Graph(); graph = graph with { Nodes = graph.Nodes.Add(graph.Nodes[0] with { Id = "top" }) };
            var d = new GraphDocument(); d.Replace(graph); using var editor = new EditorSession(d);
            editor.Select(["top", "a"]); Cases.Check(editor.Hit(new(0, 0)).Id == "top");
            editor.Select(["a"]); Cases.Check(editor.Hit(new(0, 0)).Id == "a");
        });
    }
}
