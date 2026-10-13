using SkiaSharp;
using VisualizeScene.Core;

namespace VisualizeScene.Rendering.Skia;

public sealed partial class GraphRenderer
{
    private sealed record EdgeCaptionPicture(string Text, PointD Center, IReadOnlyList<PointD> Points, SKPicture Picture);
    private readonly Dictionary<string, EdgeCaptionPicture> edgeCaptionPictures = new(StringComparer.Ordinal);
    private long edgeCaptionRecordings;

    private void DrawEdgeCaptionPicture(SKCanvas canvas, EdgePicture edge)
    {
        if (!PictureRetentionEnabled)
        {
            DrawText(canvas, edge.Edge.Text, Geometry.PointAtFraction(edge.Points, .5) + new PointD(0, -9), 11, 220, 50, true);
            return;
        }
        edgeCaptionPictures.TryGetValue(edge.Edge.Id, out var cached);
        // Unchanged routes reuse the center too: a frame must not walk every stored bend to rediscover it.
        var center = cached is not null && ReferenceEquals(cached.Points, edge.Points)
            ? cached.Center : Geometry.PointAtFraction(edge.Points, .5) + new PointD(0, -9);
        var retained = true;
        if (cached is null || cached.Text != edge.Edge.Text || cached.Center != center)
        {
            var size = MeasureLabel(new("", edge.Edge.Id, edge.Edge.Text, new(), 11));
            var width = Math.Min(228, size.X); var height = Math.Max(size.Y, 50);
            var bounds = new RectD(center.X - width / 2, center.Y - height / 2, width, height);
            using var recorder = new SKPictureRecorder(); var target = recorder.BeginRecording(Rect(bounds.Inflate(2)));
            // Keep the exact world-coordinate commands and original wrapping/background contract.
            DrawText(target, edge.Edge.Text, center, 11, 220, 50, true);
            var next = new EdgeCaptionPicture(edge.Edge.Text, center, edge.Points, recorder.EndRecording());
            DropPicture(PictureKind.EdgeCaption, edge.Edge.Id); cached = next; edgeCaptionRecordings++;
            retained = RetainPicture(PictureKind.EdgeCaption, edge.Edge.Id, cached.Picture);
            if (retained) edgeCaptionPictures.Add(edge.Edge.Id, cached);
        }
        else
        {
            if (!ReferenceEquals(cached.Points, edge.Points))
                edgeCaptionPictures[edge.Edge.Id] = cached = cached with { Points = edge.Points };
            TouchPicture(PictureKind.EdgeCaption, edge.Edge.Id);
        }
        try { canvas.DrawPicture(cached.Picture); }
        finally { if (!retained) cached.Picture.Dispose(); }
    }
    private void PruneEdgeCaptionPictures()
    {
        foreach (var id in edgeCaptionPictures.Keys.ToArray())
            if (!edges.TryGetValue(id, out var edge) || edge.Edge.Text.Length == 0)
                DropPicture(PictureKind.EdgeCaption, id);
    }
}
