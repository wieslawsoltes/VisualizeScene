namespace VisualizeScene.Core;

public static partial class Geometry
{
    /// <summary>True when the rectangle touches the segment, including endpoints and degenerate segments.</summary>
    public static bool RectangleIntersectsSegment(RectD rectangle, PointD a, PointD b)
    {
        ValidateRectangle(rectangle);
        if (!a.IsFinite || !b.IsFinite) throw new ArgumentException("Segment coordinates must be finite.");
        // Avoid overflow in subtraction for opposite, very large coordinates.
        var scale = Math.Max(Math.Max(Math.Abs(a.X), Math.Abs(a.Y)), Math.Max(Math.Abs(b.X), Math.Abs(b.Y)));
        scale = Math.Max(scale, Math.Max(Math.Max(Math.Abs(rectangle.X), Math.Abs(rectangle.Y)),
            Math.Max(Math.Abs(rectangle.Right), Math.Abs(rectangle.Bottom))));
        if (scale > 1e150)
        {
            a = a * (1 / scale); b = b * (1 / scale);
            rectangle = new(rectangle.X / scale, rectangle.Y / scale, rectangle.Width / scale, rectangle.Height / scale);
        }
        var low = 0d; var high = 1d; var delta = b - a;
        bool Axis(double origin, double direction, double min, double max)
        {
            if (direction == 0) return origin >= min && origin <= max;
            var first = (min - origin) / direction; var last = (max - origin) / direction;
            low = Math.Max(low, Math.Min(first, last)); high = Math.Min(high, Math.Max(first, last));
            return low <= high;
        }
        return Axis(a.X, delta.X, rectangle.X, rectangle.Right) && Axis(a.Y, delta.Y, rectangle.Y, rectangle.Bottom);
    }

    /// <summary>Rectangle intersection against the filled node shape, not merely its bounding box.</summary>
    public static bool RectangleIntersectsNode(RectD rectangle, GraphNode node)
    {
        ArgumentNullException.ThrowIfNull(node); ValidateRectangle(rectangle); ValidateRectangle(node.Bounds);
        if (!rectangle.Intersects(node.Bounds)) return false;
        if (node.Shape == NodeShape.Rectangle) return true;
        if (node.Shape is NodeShape.Ellipse or NodeShape.RoundedRectangle or NodeShape.Diamond)
        {
            // These axis-symmetric convex shapes minimize their implicit distance at this point.
            var center = node.Bounds.Center;
            return HitNode(node, new(Math.Clamp(center.X, rectangle.X, rectangle.Right), Math.Clamp(center.Y, rectangle.Y, rectangle.Bottom)));
        }
        var vertices = Polygon(node);
        if (vertices.Any(rectangle.Contains)) return true;
        PointD[] corners = [new(rectangle.X, rectangle.Y), new(rectangle.Right, rectangle.Y),
            new(rectangle.Right, rectangle.Bottom), new(rectangle.X, rectangle.Bottom)];
        if (corners.Any(p => HitNode(node, p))) return true;
        for (var i = 0; i < vertices.Length; i++)
            if (RectangleIntersectsSegment(rectangle, vertices[i], vertices[(i + 1) % vertices.Length])) return true;
        return false;
    }

    public static bool RectangleContainsRectangle(RectD outer, RectD inner)
    {
        ValidateRectangle(outer); ValidateRectangle(inner);
        return outer.X <= inner.X && outer.Y <= inner.Y && outer.Right >= inner.Right && outer.Bottom >= inner.Bottom;
    }

    private static void ValidateRectangle(RectD rectangle)
    {
        if (!rectangle.IsValid || !double.IsFinite(rectangle.Right) || !double.IsFinite(rectangle.Bottom))
            throw new ArgumentOutOfRangeException(nameof(rectangle), "Rectangle coordinates and extents must be finite and nonnegative.");
    }
}
