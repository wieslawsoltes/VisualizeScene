using SkiaSharp;
using VisualizeScene.Core;

namespace VisualizeScene.Rendering.Skia;

public readonly record struct OverviewStatistics(long SceneRecordings, long Frames, bool HasPicture);

/// <summary>One retained overview scene, independent of the moving viewport indicator. Single drawing thread; caller owns its canvas.</summary>
public sealed class GraphOverviewRenderer : IDisposable
{
    private readonly GraphRenderer renderer = new();
    private readonly Viewport overview = new();
    private readonly SKPaint marker = new() { IsAntialias = true };
    private GraphSnapshot? snapshot;
    private SKPicture? picture;
    private RectD bounds;
    private double width, height;
    private long recordings, frames;
    private bool disposed;
    public OverviewStatistics Statistics => new(recordings, frames, picture is not null);
    public ViewportState OverviewViewport => overview.State;

    public void Render(SKCanvas canvas, GraphSnapshot graph, RectD contentBounds, double width, double height,
        ViewportState targetView, double targetWidth, double targetHeight)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        ArgumentNullException.ThrowIfNull(canvas); ArgumentNullException.ThrowIfNull(graph);
        Geometry.RectangleContainsRectangle(contentBounds, contentBounds);
        if (!double.IsFinite(width) || !double.IsFinite(height) || width < 0 || height < 0 || width > 32768 || height > 32768 ||
            !double.IsFinite(targetWidth) || !double.IsFinite(targetHeight) || targetWidth < 0 || targetHeight < 0 ||
            !targetView.Origin.IsFinite || !double.IsFinite(targetView.Zoom) || targetView.Zoom < Viewport.MinimumZoom || targetView.Zoom > Viewport.MaximumZoom)
            throw new ArgumentOutOfRangeException(nameof(width), "Finite nonnegative dimensions and a valid view transform are required.");
        if (width == 0 || height == 0) return;
        if (!ReferenceEquals(snapshot, graph) || picture is null || this.width != width || this.height != height || bounds != contentBounds)
        {
            GraphValidation.Validate(graph);
            // The general editor has a 5% zoom floor. Overview must fit even graphs wider than 20 viewports.
            var availableWidth = Math.Max(1, width - 20); var availableHeight = Math.Max(1, height - 20);
            var fit = Math.Min(availableWidth / Math.Max(1, contentBounds.Width), availableHeight / Math.Max(1, contentBounds.Height));
            var cameraZoom = Math.Clamp(fit, Viewport.MinimumZoom, Viewport.MaximumZoom);
            var extraScale = Math.Min(1, fit / cameraZoom);
            var virtualWidth = width / extraScale; var virtualHeight = height / extraScale;
            if (!double.IsFinite(virtualWidth) || !double.IsFinite(virtualHeight) || virtualWidth > float.MaxValue || virtualHeight > float.MaxValue)
                throw new ArgumentOutOfRangeException(nameof(contentBounds), "Overview scale is outside the supported rendering range.");
            overview.Restore(new(contentBounds.Center - new PointD(virtualWidth / (2 * cameraZoom), virtualHeight / (2 * cameraZoom)), cameraZoom));
            using var recorder = new SKPictureRecorder();
            var drawing = recorder.BeginRecording(SKRect.Create((float)width, (float)height));
            drawing.Scale((float)extraScale);
            renderer.Render(drawing, graph, overview, virtualWidth, virtualHeight, new(ShowGrid: false, Overview: true));
            var next = recorder.EndRecording(); picture?.Dispose(); picture = next;
            this.width = width; this.height = height; bounds = contentBounds; snapshot = graph;
            scale = extraScale; recordings++;
        }
        var save = canvas.Save();
        try
        {
            canvas.ClipRect(SKRect.Create((float)width, (float)height)); canvas.Clear(SKColors.White); canvas.DrawPicture(picture);
            var top = ToScreen(targetView.Origin);
            var right = top.X + targetWidth / targetView.Zoom * overview.Zoom * scale;
            var bottom = top.Y + targetHeight / targetView.Zoom * overview.Zoom * scale;
            // Clip in double precision before conversion to float. Camera indicators may be far offscreen.
            if (right >= 0 && bottom >= 0 && top.X <= width && top.Y <= height)
            {
                var rect = new SKRect((float)Math.Clamp(top.X, -2, width + 2), (float)Math.Clamp(top.Y, -2, height + 2),
                    (float)Math.Clamp(right, -2, width + 2), (float)Math.Clamp(bottom, -2, height + 2));
                marker.Color = new SKColor(0, 112, 215, 28); marker.Style = SKPaintStyle.Fill; canvas.DrawRect(rect, marker);
                marker.Color = new SKColor(0, 112, 215); marker.Style = SKPaintStyle.Stroke; marker.StrokeWidth = 1.5f; canvas.DrawRect(rect, marker);
            }
            frames++;
        }
        finally { canvas.RestoreToCount(save); }
    }
    private double scale = 1;
    public PointD ToWorld(PointD screen)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (!screen.IsFinite) throw new ArgumentOutOfRangeException(nameof(screen));
        return overview.ToWorld(screen * (1 / scale));
    }
    public PointD ToScreen(PointD world) => overview.ToScreen(world) * scale;
    public void Clear()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        picture?.Dispose(); picture = null; snapshot = null;
    }
    public void Dispose()
    {
        if (disposed) return; disposed = true; picture?.Dispose(); picture = null; snapshot = null;
        marker.Dispose(); renderer.Dispose();
    }
}
