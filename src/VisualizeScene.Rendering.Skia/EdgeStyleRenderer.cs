using SkiaSharp;
using VisualizeScene.Core;

namespace VisualizeScene.Rendering.Skia;

/// <summary>Reusable caller-canvas edge painter. Owns scratch paths and a bounded FIFO cache of native dash effects.</summary>
/// <remarks>Single-render-thread object. It does not own the supplied canvas, route or retained path. Hollow markers use white fill.</remarks>
public sealed class EdgeStyleRenderer : IDisposable
{
    public const int MaximumDashEffects = 32;
    private readonly SKPaint paint = new() { IsAntialias = true, StrokeJoin = SKStrokeJoin.Round };
    private readonly SKPath line = new(), markerPath = new();
    private readonly Dictionary<(EdgeDash Dash, double Width), SKPathEffect> effects = [];
    private readonly Queue<(EdgeDash Dash, double Width)> insertionOrder = new();
    private bool disposed;
    public int CachedDashEffects => effects.Count;
    public long DashEffectBuilds { get; private set; }
    private static SKPoint Point(PointD p) => new((float)p.X, (float)p.Y);
    private SKPathEffect? Effect(EdgeStyle style)
    {
        if (style.Dash == EdgeDash.Solid) return null;
        var key = (style.Dash, style.Width);
        if (effects.TryGetValue(key, out var existing)) return existing;
        var pattern = EdgeStyle.DashPattern(style.Dash); var intervals = new float[pattern.Length];
        for (var i = 0; i < intervals.Length; i++) intervals[i] = (float)(pattern[i] * style.Width);
        var effect = SKPathEffect.CreateDash(intervals, 0) ?? throw new InvalidOperationException("Could not create a native dash effect.");
        if (effects.Count == MaximumDashEffects) { var first = insertionOrder.Dequeue(); effects[first].Dispose(); effects.Remove(first); }
        effects.Add(key, effect); insertionOrder.Enqueue(key); DashEffectBuilds++; return effect;
    }
    public void Draw(SKCanvas canvas, GraphEdge edge, IReadOnlyList<PointD> points, SKPath? retainedPath = null,
        double zoom = 1, bool selected = false, bool overview = false)
    {
        ObjectDisposedException.ThrowIf(disposed, this); ArgumentNullException.ThrowIfNull(canvas);
        ArgumentNullException.ThrowIfNull(edge); ArgumentNullException.ThrowIfNull(points);
        if (!double.IsFinite(zoom) || zoom <= 0) throw new ArgumentOutOfRangeException(nameof(zoom));
        var style = edge.Style ?? EdgeStyle.Default; style.Validate();
        if (points.Count < 2) return;
        if (retainedPath is null)
        {
            line.Reset(); line.MoveTo(Point(points[0]));
            for (var i = 1; i < points.Count; i++) line.LineTo(Point(points[i]));
            retainedPath = line;
        }
        var color = selected ? new SKColor(0, 112, 215) : SKColor.Parse(edge.Stroke);
        paint.Color = color; paint.Style = SKPaintStyle.Stroke;
        paint.StrokeWidth = (float)(selected ? edge.Style is null ? 2.5 / zoom : Math.Max(style.Width, 2.5 / zoom) : style.Width);
        paint.StrokeCap = style.Dash == EdgeDash.Solid ? SKStrokeCap.Round : SKStrokeCap.Butt;
        try { paint.PathEffect = Effect(style); canvas.DrawPath(retainedPath, paint); }
        finally { paint.PathEffect = null; }
        if (EdgeGeometry.Marker(edge, points, true, overview) is { } source) DrawMarker(canvas, source, color);
        if (EdgeGeometry.Marker(edge, points, false, overview) is { } target) DrawMarker(canvas, target, color);
    }
    private void DrawMarker(SKCanvas canvas, EdgeMarker marker, SKColor color)
    {
        markerPath.Reset();
        if (marker.IsCircle) markerPath.AddCircle((float)marker.CircleCenter.X, (float)marker.CircleCenter.Y, (float)marker.CircleRadius);
        else
        {
            Span<PointD> vertices = stackalloc PointD[4]; var count = marker.CopyVertices(vertices);
            if (count == 0) return;
            markerPath.MoveTo(Point(vertices[0])); for (var i = 1; i < count; i++) markerPath.LineTo(Point(vertices[i]));
            if (!marker.IsOpen) markerPath.Close();
        }
        if (!marker.IsOpen)
        {
            paint.Color = marker.IsHollow ? SKColors.White : color; paint.Style = SKPaintStyle.Fill;
            canvas.DrawPath(markerPath, paint);
        }
        if (marker.IsOpen || marker.IsHollow)
        {
            paint.Color = color; paint.Style = SKPaintStyle.Stroke; paint.StrokeCap = SKStrokeCap.Round;
            paint.StrokeWidth = (float)marker.OutlineWidth; canvas.DrawPath(markerPath, paint);
        }
    }
    public void Dispose()
    {
        if (disposed) return; disposed = true; paint.PathEffect = null;
        foreach (var effect in effects.Values) effect.Dispose(); effects.Clear(); insertionOrder.Clear();
        paint.Dispose(); line.Dispose(); markerPath.Dispose();
    }
}
