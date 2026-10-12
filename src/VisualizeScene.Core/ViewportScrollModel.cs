namespace VisualizeScene.Core;

/// <summary>One normalized scrollbar and its captured world-coordinate range. Immutable and allocation-free.</summary>
public readonly record struct ViewportScrollAxis
{
    public double MinimumOrigin { get; }
    public double MaximumOrigin { get; }
    public double Origin { get; }
    public double VisibleExtent { get; }
    public double SmallChange { get; }
    public double LargeChange { get; }
    public bool CanScroll => MaximumOrigin > MinimumOrigin;
    public double Value => CanScroll ? Math.Clamp((Origin - MinimumOrigin) / (MaximumOrigin - MinimumOrigin), 0, 1) : 0;
    // WinUI uses viewport / (range + viewport) for its thumb proportion. Its numeric range here is 0..1.
    public double ViewportSize => CanScroll ? Math.Min(1e6, VisibleExtent / (MaximumOrigin - MinimumOrigin)) : 1;
    internal ViewportScrollAxis(double minimum, double maximum, double origin, double visible, double step)
    {
        MinimumOrigin = minimum; MaximumOrigin = maximum; Origin = origin; VisibleExtent = visible;
        var span = maximum - minimum;
        SmallChange = span > 0 ? Math.Min(1, step / span) : 0;
        LargeChange = span > 0 ? Math.Min(1, visible * .85 / span) : 0;
    }
    public double GetOrigin(double value)
    {
        if (!double.IsFinite(value)) throw new ArgumentOutOfRangeException(nameof(value));
        if (!CanScroll || value <= 0) return MinimumOrigin;
        if (value >= 1) return MaximumOrigin;
        return Math.Clamp(MinimumOrigin + (MaximumOrigin - MinimumOrigin) * value, MinimumOrigin, MaximumOrigin);
    }
}

/// <summary>Converts existing scene bounds and a viewport to two scrollbars. Does not traverse or mutate a graph.</summary>
public readonly record struct ViewportScrollModel(ViewportScrollAxis Horizontal, ViewportScrollAxis Vertical)
{
    public static ViewportScrollModel Calculate(RectD content, ViewportState view, double width, double height,
        double padding = 40, double lineStep = 24)
    {
        Geometry.RectangleContainsRectangle(content, content);
        if (!view.Origin.IsFinite || !double.IsFinite(view.Zoom) || view.Zoom < Viewport.MinimumZoom || view.Zoom > Viewport.MaximumZoom ||
            !double.IsFinite(width) || !double.IsFinite(height) || width < 0 || height < 0 ||
            !double.IsFinite(padding) || padding < 0 || !double.IsFinite(lineStep) || lineStep <= 0)
            throw new ArgumentOutOfRangeException(nameof(view), "Finite viewport dimensions, padding and a positive line step are required.");
        var margin = padding / view.Zoom; var step = lineStep / view.Zoom;
        return new(Axis(content.X, content.Right, view.Origin.X, width / view.Zoom, margin, step),
            Axis(content.Y, content.Bottom, view.Origin.Y, height / view.Zoom, margin, step));
    }
    private static ViewportScrollAxis Axis(double start, double end, double origin, double visible, double margin, double step)
    {
        if (!double.IsFinite(visible) || !double.IsFinite(margin) || !double.IsFinite(step))
            throw new ArgumentOutOfRangeException(nameof(visible), "Scroll geometry exceeds the finite coordinate range.");
        if (visible == 0) return new(origin, origin, origin, 0, 0);
        var paddedStart = start - margin; var paddedEnd = end + margin;
        var minimum = Math.Min(origin, paddedStart); var maximum = Math.Max(origin, paddedEnd - visible);
        if (!double.IsFinite(paddedStart) || !double.IsFinite(paddedEnd) || !double.IsFinite(origin + visible) ||
            !double.IsFinite(minimum) || !double.IsFinite(maximum) || !double.IsFinite(maximum - minimum))
            throw new ArgumentOutOfRangeException(nameof(origin), "Scroll geometry exceeds the finite coordinate range.");
        return new(minimum, maximum, origin, visible, step);
    }
}
