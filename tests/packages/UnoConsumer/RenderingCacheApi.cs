using VisualizeScene.Controls.Uno;
using VisualizeScene.Rendering.Skia;

namespace PackageConsumer;

public static class RenderingCacheApi
{
    public static PictureCacheStatistics Verify(GraphControl graph)
    {
        graph.PictureCacheCapacity = 128;
        graph.PictureCacheByteBudget = 4L * 1024 * 1024;
        var stats = graph.PictureStatistics;
        _ = stats.EstimatedRetainedBytes; _ = stats.ByteBudget; _ = stats.OversizedBypasses;
        _ = stats.EdgeCaptionRecordings; _ = stats.RetainedEdgeCaptions;
        return stats;
    }
}
