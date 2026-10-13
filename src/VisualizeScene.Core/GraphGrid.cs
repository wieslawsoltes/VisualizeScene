namespace VisualizeScene.Core;

public enum GridPattern { Dots, Crosses, Lines, HorizontalLines, VerticalLines }
[Flags] public enum GridSnapAxes { None = 0, X = 1, Y = 2, Both = X | Y }
public enum GridNodeAnchor { TopLeft, Center }

/// <summary>Immutable, view-owned grid preferences. They are not graph records or undoable edits.</summary>
public sealed record GraphGridSettings
{
    public static GraphGridSettings Default { get; } = new();
    public double HorizontalSpacing { get; init; } = 10;
    public double VerticalSpacing { get; init; } = 10;
    public PointD Origin { get; init; }
    public GridPattern Pattern { get; init; } = GridPattern.Dots;
    public string Color { get; init; } = "#D3DCE5";
    public double Thickness { get; init; } = 1.3;
    public bool Visible { get; init; } = true;
    public bool SnapEnabled { get; init; } = true;
    public GridSnapAxes SnapAxes { get; init; } = GridSnapAxes.Both;
    // Preserve the existing top-left node behavior unless a caller explicitly selects center snapping.
    public GridNodeAnchor NodeAnchor { get; init; } = GridNodeAnchor.TopLeft;

    public void Validate()
    {
        if (!double.IsFinite(HorizontalSpacing) || HorizontalSpacing <= 0 ||
            !double.IsFinite(VerticalSpacing) || VerticalSpacing <= 0)
            throw new ArgumentOutOfRangeException(nameof(HorizontalSpacing), "Grid spacing must be finite and positive.");
        if (!Origin.IsFinite) throw new ArgumentOutOfRangeException(nameof(Origin));
        if (!Enum.IsDefined(Pattern) || !Enum.IsDefined(NodeAnchor) || (SnapAxes & ~GridSnapAxes.Both) != 0)
            throw new ArgumentOutOfRangeException(nameof(Pattern), "Unknown grid pattern, anchor or snap axes.");
        if (!GraphValidation.ValidColor(Color)) throw new ArgumentException("Grid color must be #RRGGBB.", nameof(Color));
        if (!double.IsFinite(Thickness) || Thickness < .25 || Thickness > 4)
            throw new ArgumentOutOfRangeException(nameof(Thickness), "Grid thickness must be between 0.25 and 4 screen units.");
    }
}

/// <summary>Screen-coordinate marks. Exponent >= 0 only removes lattice points, never inserts fractional ones.</summary>
public readonly record struct GridAxisPlan(double First, double Step, int Count, int DecimationExponent, bool Suppressed = false)
{
    public double Position(int index) => index >= 0 && index < Count ? First + index * Step : throw new ArgumentOutOfRangeException(nameof(index));
}
public readonly record struct GraphGridPlan(GridAxisPlan X, GridAxisPlan Y, GridPattern Pattern)
{
    public int Marks => Pattern switch {
        GridPattern.HorizontalLines => Y.Count, GridPattern.VerticalLines => X.Count,
        GridPattern.Lines => X.Count + Y.Count, _ => X.Count * Y.Count
    };
}

/// <summary>Shared lattice snapping and bounded screen sampling, independent of any UI or rendering backend.</summary>
public static class GraphGrid
{
    public const int MaximumAxisMarks = 256;
    public const double MinimumScreenSpacing = 14;
    private const double Margin = 4;

    public static PointD Snap(PointD point, GraphGridSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings); settings.Validate();
        if (!point.IsFinite) throw new ArgumentOutOfRangeException(nameof(point));
        if (!settings.SnapEnabled) return point;
        static double Axis(double value, double origin, double step)
        {
            var units = (value - origin) / step;
            var result = Math.Round(units, MidpointRounding.ToEven) * step + origin;
            if (!double.IsFinite(result)) throw new OverflowException("The grid snap cannot be represented with finite coordinates.");
            return result;
        }
        return new((settings.SnapAxes & GridSnapAxes.X) != 0 ? Axis(point.X, settings.Origin.X, settings.HorizontalSpacing) : point.X,
            (settings.SnapAxes & GridSnapAxes.Y) != 0 ? Axis(point.Y, settings.Origin.Y, settings.VerticalSpacing) : point.Y);
    }

    public static GraphGridPlan Plan(GraphGridSettings settings, ViewportState viewport, double width, double height)
    {
        ArgumentNullException.ThrowIfNull(settings); settings.Validate();
        if (!viewport.Origin.IsFinite || !double.IsFinite(viewport.Zoom) || viewport.Zoom < Viewport.MinimumZoom || viewport.Zoom > Viewport.MaximumZoom ||
            !double.IsFinite(width) || !double.IsFinite(height) || width < 0 || height < 0)
            throw new ArgumentOutOfRangeException(nameof(viewport), "A finite viewport and nonnegative dimensions are required.");
        if (!settings.Visible || width == 0 || height == 0) return new(default, default, settings.Pattern);
        return new(Axis(settings.Origin.X, settings.HorizontalSpacing, viewport.Origin.X, width, viewport.Zoom),
            Axis(settings.Origin.Y, settings.VerticalSpacing, viewport.Origin.Y, height, viewport.Zoom), settings.Pattern);
    }

    private static GridAxisPlan Axis(double gridOrigin, double spacing, double viewOrigin, double extent, double zoom)
    {
        // Reserve space for both boundary marks and the four-screen-unit clipping margins.
        var required = Math.Max(MinimumScreenSpacing, extent / (MaximumAxisMarks - 4)) / zoom;
        if (!double.IsFinite(required)) return new(0, 0, 0, 0, true);
        var exponent = Math.Max(0, Math.ILogB(required) - Math.ILogB(spacing));
        var step = Math.ScaleB(spacing, exponent);
        if (step < required) step = Math.ScaleB(spacing, ++exponent);
        if (!double.IsFinite(step)) return new(0, 0, 0, exponent, true);
        // Reduced phases avoid overflowing gridOrigin - viewOrigin and subtracting enormous world coordinates.
        var offset = Math.IEEERemainder(gridOrigin, step) - Math.IEEERemainder(viewOrigin, step);
        if (offset < 0) offset += step;
        if (offset >= step) offset -= step;
        var screenStep = step * zoom; var first = offset * zoom;
        if (double.IsFinite(screenStep) && first - screenStep >= -Margin) first -= screenStep;
        if (!double.IsFinite(first) || first > extent + Margin) return new(0, 0, 0, exponent);
        if (!double.IsFinite(screenStep)) return new(first, 0, 1, exponent);
        var count = (int)Math.Clamp(Math.Floor((extent + Margin - first) / screenStep) + 1, 0, MaximumAxisMarks);
        return new(first, screenStep, count, exponent);
    }
}
