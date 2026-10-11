using SkiaSharp;
using VisualizeScene.Core;

namespace VisualizeScene.Rendering.Skia;

public readonly record struct OverviewStatistics(long SceneRecordings, long Frames, bool HasPicture);

/// <summary>One bounded, pixel-density-aware overview image, independent of the viewport indicator. Single drawing thread.</summary>
public sealed class GraphOverviewRenderer : IDisposable
{
    private GraphRenderer renderer = new();
    private readonly Viewport overview = new();
    private readonly SKPaint marker = new() { IsAntialias = true };
    private GraphSnapshot? snapshot;
    private SKImage? picture;
    private RectD bounds;
    private double width, height, density;
    private long recordings, frames;
    private bool disposed;
    private double scale = 1;
    public OverviewStatistics Statistics => new(recordings, frames, picture is not null);
    public ViewportState OverviewViewport => overview.State;
    public long CachedPixels => picture is null ? 0 : (long)picture.Width * picture.Height;

    public void Render(SKCanvas canvas, GraphSnapshot graph, RectD contentBounds, double width, double height,
        ViewportState targetView, double targetWidth, double targetHeight)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        ArgumentNullException.ThrowIfNull(canvas); ArgumentNullException.ThrowIfNull(graph);
        Geometry.RectangleContainsRectangle(contentBounds, contentBounds);
        if (!double.IsFinite(width) || !double.IsFinite(height) || width < 0 || height < 0 || width > 4096 || height > 4096 ||
            !double.IsFinite(targetWidth) || !double.IsFinite(targetHeight) || targetWidth < 0 || targetHeight < 0 ||
            !targetView.Origin.IsFinite || !double.IsFinite(targetView.Zoom) || targetView.Zoom < Viewport.MinimumZoom || targetView.Zoom > Viewport.MaximumZoom)
            throw new ArgumentOutOfRangeException(nameof(width), "Finite nonnegative dimensions, an overview no larger than 4096 per side and a valid view transform are required.");
        if (width == 0 || height == 0) return;
        var matrix = canvas.TotalMatrix;
        var pixelDensity = Math.Max(Math.Sqrt((double)matrix.ScaleX * matrix.ScaleX + (double)matrix.SkewY * matrix.SkewY),
            Math.Sqrt((double)matrix.ScaleY * matrix.ScaleY + (double)matrix.SkewX * matrix.SkewX));
        if (!double.IsFinite(pixelDensity) || pixelDensity <= 0) throw new ArgumentException("Canvas transform must have finite positive scale.", nameof(canvas));
        pixelDensity = Math.Clamp(pixelDensity, 1, 4);
        var pixelsX = (int)Math.Ceiling(width * pixelDensity); var pixelsY = (int)Math.Ceiling(height * pixelDensity);
        if ((long)pixelsX * pixelsY > 16 * 1024 * 1024) throw new ArgumentOutOfRangeException(nameof(width), "Overview cache is limited to 16 million pixels.");
        if (!ReferenceEquals(snapshot, graph) || picture is null || this.width != width || this.height != height || bounds != contentBounds || density != pixelDensity)
        {
            GraphValidation.Validate(graph);
            // The editor has a 5% zoom floor; the overview must fit much larger scenes too.
            var fit = Math.Min(Math.Max(1, width - 20) / Math.Max(1, contentBounds.Width), Math.Max(1, height - 20) / Math.Max(1, contentBounds.Height));
            var cameraZoom = Math.Clamp(fit, Viewport.MinimumZoom, Viewport.MaximumZoom);
            var extraScale = Math.Min(1, fit / cameraZoom);
            var virtualWidth = width / extraScale; var virtualHeight = height / extraScale;
            if (!double.IsFinite(virtualWidth) || !double.IsFinite(virtualHeight) || virtualWidth > float.MaxValue || virtualHeight > float.MaxValue)
                throw new ArgumentOutOfRangeException(nameof(contentBounds), "Overview scale is outside the supported rendering range.");
            var nextView = new Viewport();
            nextView.Restore(new(contentBounds.Center - new PointD(virtualWidth / (2 * cameraZoom), virtualHeight / (2 * cameraZoom)), cameraZoom));
            using var surface = SKSurface.Create(new SKImageInfo(pixelsX, pixelsY)) ?? throw new InvalidOperationException("Could not allocate overview image.");
            surface.Canvas.Scale((float)(pixelsX / width * extraScale), (float)(pixelsY / height * extraScale));
            renderer.Render(surface.Canvas, graph, nextView, virtualWidth, virtualHeight, new(ShowGrid: false, Overview: true));
            var next = surface.Snapshot(); picture?.Dispose(); picture = next;
            overview.Restore(nextView.State); this.width = width; this.height = height; bounds = contentBounds; snapshot = graph;
            scale = extraScale; density = pixelDensity; recordings++;
        }
        var save = canvas.Save();
        try
        {
            canvas.ClipRect(SKRect.Create((float)width, (float)height)); canvas.Clear(SKColors.White);
            canvas.DrawImage(picture, SKRect.Create((float)width, (float)height));
            var top = ToScreen(targetView.Origin);
            var right = top.X + targetWidth / targetView.Zoom * overview.Zoom * scale;
            var bottom = top.Y + targetHeight / targetView.Zoom * overview.Zoom * scale;
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
    public PointD ToWorld(PointD screen)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (!screen.IsFinite) throw new ArgumentOutOfRangeException(nameof(screen));
        return overview.ToWorld(screen * (1 / scale));
    }
    public PointD ToScreen(PointD world)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (!world.IsFinite) throw new ArgumentOutOfRangeException(nameof(world));
        return overview.ToScreen(world) * scale;
    }
    public void Clear()
    {
        ObjectDisposedException.ThrowIf(disposed, this); picture?.Dispose(); picture = null; snapshot = null;
        renderer.Dispose(); renderer = new();
    }
    public void Dispose()
    {
        if (disposed) return; disposed = true; picture?.Dispose(); picture = null; snapshot = null;
        marker.Dispose(); renderer.Dispose();
    }
}
