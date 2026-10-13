namespace VisualizeScene.Core;

/// <summary>Shared geometry for the renderer's plain, centered, 13-unit node caption.</summary>
public static class NodeCaptionLayout
{
    public const double FontSize = 13;
    public const double LineHeight = FontSize * 1.25;
    public const double HeightInset = 10;
    public static double WidthFraction(NodeShape shape) => shape == NodeShape.Diamond ? .6 : .82;

    /// <summary>Conservative whole-world-unit frame enclosing an unwrapped text block inside the shape.</summary>
    /// <remarks>CaptionSize is a measured block, not an estimate from the number of characters.
    /// Independent annotations and group headers are outside this contract.</remarks>
    public static PointD FittingSize(NodeShape shape, PointD captionSize, double horizontalPadding = 8, double verticalPadding = 6)
    {
        if (!Enum.IsDefined(shape)) throw new ArgumentOutOfRangeException(nameof(shape));
        if (!captionSize.IsFinite || captionSize.X < 0 || captionSize.Y < 0)
            throw new ArgumentOutOfRangeException(nameof(captionSize));
        if (!double.IsFinite(horizontalPadding) || horizontalPadding < 0 || !double.IsFinite(verticalPadding) || verticalPadding < 0)
            throw new ArgumentOutOfRangeException(nameof(horizontalPadding));
        var w = captionSize.X + 2 * horizontalPadding;
        var h = captionSize.Y + 2 * verticalPadding;
        var size = shape switch
        {
            NodeShape.Ellipse => new PointD(w * Math.Sqrt(2), h * Math.Sqrt(2)),
            NodeShape.Diamond => new PointD(w * 2, h * 2),
            // The center half/six-tenths stays inside these polygons at every height.
            NodeShape.Hexagon => new PointD(w * 2, h),
            NodeShape.Parallelogram => new PointD(w / .6, h),
            NodeShape.RoundedRectangle => new PointD(w + 12, h + 12),
            _ => new PointD(w, h)
        };
        size = new(Math.Ceiling(Math.Max(size.X, w / WidthFraction(shape))),
            Math.Ceiling(Math.Max(size.Y, captionSize.Y + HeightInset)));
        if (!size.IsFinite) throw new ArgumentException("Caption dimensions exceed the finite coordinate range.", nameof(captionSize));
        return size;
    }
}
