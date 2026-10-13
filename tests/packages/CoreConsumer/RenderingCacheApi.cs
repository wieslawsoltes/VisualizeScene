using SkiaSharp;
using VisualizeScene.Core;
using VisualizeScene.Rendering.Skia;

static class RenderingCacheApi
{
    public static void Verify()
    {
        var graph = new GraphSnapshot([new("a", new(0, 0, 80, 40)), new("b", new(300, 0, 80, 40))],
            [new("ab", "a", "b", "Packaged caption")], [], []);
        using var renderer = new GraphRenderer { PictureCacheCapacity = 32, PictureCacheByteBudget = 4L * 1024 * 1024 };
        using var surface = SKSurface.Create(new SKImageInfo(500, 200))!;
        void Draw() => renderer.Render(surface.Canvas, graph, new(), 500, 200, new(ShowGrid: false));
        Draw(); var before = renderer.PictureStatistics; Draw(); var after = renderer.PictureStatistics;
        if (before.EdgeCaptionRecordings != 1 || after.EdgeCaptionRecordings != 1 || after.RetainedEdgeCaptions != 1 ||
            after.EstimatedRetainedBytes <= 0 || after.EstimatedRetainedBytes > after.ByteBudget || after.Retained > after.Capacity)
            throw new InvalidOperationException("Packaged caption cache did not retain/reuse within both budgets.");
        renderer.PictureCacheByteBudget = 0; Draw();
        if (renderer.PictureStatistics.Retained != 0 || renderer.PictureStatistics.EstimatedRetainedBytes != 0)
            throw new InvalidOperationException("Packaged zero-byte budget did not disable retention.");
        Console.WriteLine("PASS packaged rendering cache budget, caption reuse and disable");
    }
}
