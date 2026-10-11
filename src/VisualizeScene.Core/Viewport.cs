namespace VisualizeScene.Core;

public readonly record struct ViewportState(PointD Origin, double Zoom);

/// <summary>Finite, atomic view transforms. Revision changes only when the view actually changes.</summary>
public sealed class Viewport
{
    public const double MinimumZoom = .05, MaximumZoom = 8;
    public PointD Origin { get; private set; }
    public double Zoom { get; private set; } = 1;
    public long Revision { get; private set; }
    public ViewportState State => new(Origin, Zoom);
    public PointD ToWorld(PointD screen) => Origin + screen * (1 / Zoom);
    public PointD ToScreen(PointD world) => (world - Origin) * Zoom;

    public void Restore(ViewportState state)
    {
        if (!state.Origin.IsFinite || !double.IsFinite(state.Zoom) || state.Zoom < MinimumZoom || state.Zoom > MaximumZoom)
            throw new ArgumentOutOfRangeException(nameof(state), "View origin must be finite and zoom must be between 0.05 and 8.");
        if (state == State) return;
        Origin = state.Origin; Zoom = state.Zoom; Revision++;
    }
    public void Pan(PointD screenDelta)
    {
        Point(screenDelta); Restore(new(Origin - screenDelta * (1 / Zoom), Zoom));
    }
    public void Center(PointD point, double width, double height)
    {
        Point(point); Size(width, height, 0);
        Restore(new(point - new PointD(width / (2 * Zoom), height / (2 * Zoom)), Zoom));
    }
    public void ZoomAt(PointD screenAnchor, double factor) => Transform(screenAnchor, screenAnchor, factor);

    /// <summary>Moves a screen anchor and scales around its world position in one atomic operation.</summary>
    public void Transform(PointD fromScreen, PointD toScreen, double factor)
    {
        Point(fromScreen); Point(toScreen);
        if (!double.IsFinite(factor) || factor <= 0) throw new ArgumentOutOfRangeException(nameof(factor));
        var zoom = Math.Clamp(Zoom * factor, MinimumZoom, MaximumZoom);
        Restore(new(ToWorld(fromScreen) - toScreen * (1 / zoom), zoom));
    }
    public void Fit(RectD bounds, double width, double height, double padding = 40)
    {
        Geometry.RectangleContainsRectangle(bounds, bounds); Size(width, height, padding);
        if (width <= 2 * padding || height <= 2 * padding) return;
        var zoom = Math.Clamp(Math.Min((width - 2 * padding) / Math.Max(1, bounds.Width),
            (height - 2 * padding) / Math.Max(1, bounds.Height)), MinimumZoom, 2);
        Restore(new(bounds.Center - new PointD(width / (2 * zoom), height / (2 * zoom)), zoom));
    }

    /// <summary>Minimally pans a target into view, centering oversized targets without changing zoom.</summary>
    public bool EnsureVisible(RectD bounds, double width, double height, double padding = 24)
    {
        Geometry.RectangleContainsRectangle(bounds, bounds); Size(width, height, padding);
        if (width <= 2 * padding || height <= 2 * padding) return false;
        var old = State; var margin = padding / Zoom;
        static double Axis(double origin, double min, double extent, double view, double margin)
        {
            if (extent > view - 2 * margin) return min + extent / 2 - view / 2;
            if (min < origin + margin) return min - margin;
            if (min + extent > origin + view - margin) return min + extent - view + margin;
            return origin;
        }
        Restore(new(new(Axis(Origin.X, bounds.X, bounds.Width, width / Zoom, margin),
            Axis(Origin.Y, bounds.Y, bounds.Height, height / Zoom, margin)), Zoom));
        return old != State;
    }
    private static void Point(PointD point)
    { if (!point.IsFinite) throw new ArgumentOutOfRangeException(nameof(point)); }
    private static void Size(double width, double height, double padding)
    {
        if (!double.IsFinite(width) || !double.IsFinite(height) || !double.IsFinite(padding) || width < 0 || height < 0 || padding < 0)
            throw new ArgumentOutOfRangeException(nameof(width), "View dimensions and padding must be finite and nonnegative.");
    }
}
