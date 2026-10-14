using SkiaSharp;
using VisualizeScene.Core;

namespace VisualizeScene.Rendering.Skia;

public readonly record struct GridRenderStatistics(GraphGridPlan Plan, long GeometryBuilds, long DrawCalls);

/// <summary>Reusable bounded screen-space grid. Keeps one native path, not pictures per grid point.</summary>
public sealed class GraphGridRenderer : IDisposable
{
    private readonly SKPath path = new();
    private readonly SKPaint paint = new() { IsAntialias = true, StrokeCap = SKStrokeCap.Butt };
    private GraphGridPlan? previous;
    private double previousWidth, previousHeight, previousThickness;
    private long builds, draws;
    private bool disposed;
    public GridRenderStatistics Statistics { get; private set; }

    /// <summary>Draws over the caller's background in local screen coordinates, preserving its matrix and clip.</summary>
    public void Render(SKCanvas canvas, GraphGridSettings settings, ViewportState viewport, double width, double height)
    {
        ObjectDisposedException.ThrowIf(disposed, this); ArgumentNullException.ThrowIfNull(canvas);
        var plan = GraphGrid.Plan(settings, viewport, width, height);
        if (width > float.MaxValue || height > float.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(width), "Native grid dimensions must fit finite single-precision screen coordinates.");
        if (plan.Marks == 0) { Statistics = new(plan, builds, draws); return; }
        if (previous != plan || previousWidth != width || previousHeight != height ||
            plan.Pattern == GridPattern.Dots && previousThickness != settings.Thickness)
        {
            // Rewind retains bounded scratch storage. Selection-only repaints reuse this path unchanged.
            path.Rewind();
            if (plan.Pattern is GridPattern.Dots or GridPattern.Crosses)
            {
                for (var ix = 0; ix < plan.X.Count; ix++) for (var iy = 0; iy < plan.Y.Count; iy++)
                {
                    var x = (float)plan.X.Position(ix); var y = (float)plan.Y.Position(iy);
                    if (plan.Pattern == GridPattern.Dots) path.AddCircle(x, y, (float)(settings.Thickness / 2));
                    else { path.MoveTo(x - 3, y); path.LineTo(x + 3, y); path.MoveTo(x, y - 3); path.LineTo(x, y + 3); }
                }
            }
            else
            {
                if (plan.Pattern != GridPattern.HorizontalLines)
                    for (var ix = 0; ix < plan.X.Count; ix++) { var x = (float)plan.X.Position(ix); path.MoveTo(x, 0); path.LineTo(x, (float)height); }
                if (plan.Pattern != GridPattern.VerticalLines)
                    for (var iy = 0; iy < plan.Y.Count; iy++) { var y = (float)plan.Y.Position(iy); path.MoveTo(0, y); path.LineTo((float)width, y); }
            }
            previous = plan; previousWidth = width; previousHeight = height; previousThickness = settings.Thickness; builds++;
        }
        paint.Color = SKColor.Parse(settings.Color); paint.StrokeWidth = (float)settings.Thickness;
        paint.Style = plan.Pattern == GridPattern.Dots ? SKPaintStyle.Fill : SKPaintStyle.Stroke;
        var save = canvas.Save();
        try { canvas.ClipRect(SKRect.Create((float)width, (float)height)); canvas.DrawPath(path, paint); draws++; }
        finally { canvas.RestoreToCount(save); }
        Statistics = new(plan, builds, draws);
    }
    public void Hide()
    { ObjectDisposedException.ThrowIf(disposed, this); Statistics = new(default, builds, draws); }
    public void Dispose()
    { if (disposed) return; disposed = true; path.Dispose(); paint.Dispose(); previous = null; Statistics = new(default, builds, draws); }
}
