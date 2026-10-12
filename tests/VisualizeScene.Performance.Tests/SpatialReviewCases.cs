using System.Collections.Immutable;
using SkiaSharp;
using VisualizeScene.Core;
using VisualizeScene.Editing;
using VisualizeScene.Rendering.Skia;

static class SpatialReviewCases
{
    public static void Run(Action<string, Action> test)
    {
        DrawingOrderRenderingTests.Run(test);
        test("Packed spatial: randomized queries match exhaustive geometry", () => {
            var random = new Random(718); var items = Enumerable.Range(0, 10000).Select(i =>
                (new RectD(random.Next(-1000, 1000), random.Next(-1000, 1000), random.Next(0, 300), random.Next(0, 300)), i)).ToArray();
            var index = new SpatialIndex<int>(items); var destination = new List<int>();
            for (var q = 0; q < 100; q++) {
                var box = new RectD(random.Next(-1100, 1100), random.Next(-1100, 1100), 125, 130); destination.Clear(); index.Query(box, destination);
                Cases.Check(destination.Order().SequenceEqual(items.Where(x => x.Item1.Intersects(box)).Select(x => x.i).Order()));
                Cases.Check(destination.SequenceEqual(index.Query(box)));
            }
        });
        test("Packed spatial: equal centers and sorted adversarial inputs terminate", () => {
            foreach (var reverse in new[] { false, true }) {
                var items = Enumerable.Range(0, 10000).Select(i => (new RectD(i % 2, 0, 0, 0), i));
                var index = new SpatialIndex<int>(reverse ? items.Reverse() : items); Cases.Check(index.Query(new(0, 0, 1, 1)).Count() == 10000);
            }
        });
        test("Packed spatial: constructor rejects overflow and invalid arguments", () => {
            Cases.Throws<ArgumentNullException>(() => new SpatialIndex<int>(null!));
            Cases.Throws<ArgumentException>(() => new SpatialIndex<int>([(new(double.MaxValue, 0, double.MaxValue, 1), 1)]));
            Cases.Throws<ArgumentException>(() => new SpatialIndex<int>([(new(-double.MaxValue, 0, 1, 1), 1), (new(double.MaxValue, 0, 0, 1), 2)]));
            var index = new SpatialIndex<int>([]);
            Cases.Throws<ArgumentOutOfRangeException>(() => index.Query(new(0, 0, -1, 0)));
        });
        test("Packed spatial: constructing ten thousand entries stays within a linear storage budget", () => {
            var items = Enumerable.Range(0, 10000).Select(i => (new RectD(i % 100 * 200, i / 100 * 100, 80, 50), i)).ToArray();
            _ = new SpatialIndex<int>(items); var memory = GC.GetAllocatedBytesForCurrentThread(); var index = new SpatialIndex<int>(items);
            Cases.Check(GC.GetAllocatedBytesForCurrentThread() - memory < 3 * 1024 * 1024);
            Cases.Check(index.Query(new(0, 0, 400, 150)).Count() == 6);
        });
        test("Edge painter order: topmost edge is independent of BVH partitioning", () => {
            var g = new GraphSnapshot([new("a", new(0, 0, 100, 60)), new("b", new(500, 0, 100, 60))],
                [new("red", "a", "b", Stroke: "#FF0000", Directed: false), new("blue", "a", "b", Stroke: "#0000FF", Directed: false)], [], []);
            var d = new GraphDocument(); d.Replace(g); using var editor = new EditorSession(d); using var renderer = new GraphRenderer();
            using var surface = SKSurface.Create(new SKImageInfo(800, 300))!;
            foreach (var graph in new[] { g, g with { Edges = g.Edges.Reverse().ToImmutableArray() } }) {
                d.Replace(graph); renderer.Render(surface.Canvas, graph, new(), 800, 300, new(ShowGrid: false));
                Cases.Check(editor.Hit(new(250, 30), false).Id == graph.Edges[^1].Id);
                using var image = surface.Snapshot(); using var bitmap = SKBitmap.FromImage(image); var pixel = bitmap.GetPixel(250, 30);
                Cases.Check(graph.Edges[^1].Id == "blue" ? pixel.Blue > pixel.Red : pixel.Red > pixel.Blue);
            }
        });
    }
}
