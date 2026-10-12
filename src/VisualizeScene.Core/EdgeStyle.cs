namespace VisualizeScene.Core;

public enum EdgeDash { Solid, Dash, Dot, DashDot, DashDotDot }
public enum EdgeArrow { Automatic, None, Triangle, Open, HollowTriangle, Diamond, HollowDiamond, Circle, HollowCircle, Tee }

/// <summary>Immutable, renderer-independent edge appearance. Arrows never change graph direction or algorithm semantics.</summary>
public sealed record EdgeStyle(double Width = 1.5, EdgeDash Dash = EdgeDash.Solid,
    EdgeArrow SourceArrow = EdgeArrow.None, EdgeArrow TargetArrow = EdgeArrow.Automatic)
{
    public static EdgeStyle Default { get; } = new();
    public void Validate()
    {
        if (!double.IsFinite(Width) || Width < .25 || Width > 64)
            throw new ArgumentOutOfRangeException(nameof(Width), "Line width must be finite and between 0.25 and 64.");
        if (!Enum.IsDefined(Dash) || !Enum.IsDefined(SourceArrow) || !Enum.IsDefined(TargetArrow))
            throw new ArgumentException("Unknown line pattern or arrow style.");
    }
    public EdgeArrow ResolveArrow(bool source, bool directed)
    {
        var arrow = source ? SourceArrow : TargetArrow;
        return arrow == EdgeArrow.Automatic ? !source && directed ? EdgeArrow.Triangle : EdgeArrow.None : arrow;
    }
    /// <summary>Dash lengths in stroke-width units. Read-only static data, with no per-call array allocation.</summary>
    public static ReadOnlySpan<double> DashPattern(EdgeDash dash) => dash switch
    {
        EdgeDash.Solid => [], EdgeDash.Dash => [4, 2], EdgeDash.Dot => [1, 2],
        EdgeDash.DashDot => [4, 2, 1, 2], EdgeDash.DashDotDot => [4, 2, 1, 2, 1, 2],
        _ => throw new ArgumentOutOfRangeException(nameof(dash))
    };
}

/// <summary>Endpoint marker in world coordinates, shared by raster/vector output and picking.</summary>
public readonly record struct EdgeMarker(EdgeArrow Kind, PointD Tip, PointD Direction, double Size, double OutlineWidth)
{
    public bool IsCircle => Kind is EdgeArrow.Circle or EdgeArrow.HollowCircle;
    public bool IsOpen => Kind is EdgeArrow.Open or EdgeArrow.Tee;
    public bool IsHollow => Kind is EdgeArrow.HollowTriangle or EdgeArrow.HollowDiamond or EdgeArrow.HollowCircle;
    public PointD CircleCenter => Tip - Direction * (Size * .45);
    public double CircleRadius => Size * .45;
    private PointD At(double back, double side) => Tip - Direction * back + new PointD(-Direction.Y, Direction.X) * side;
    public int CopyVertices(Span<PointD> destination)
    {
        if (destination.Length < 4) throw new ArgumentException("Four vertex slots are required.", nameof(destination));
        switch (Kind)
        {
            case EdgeArrow.Triangle: case EdgeArrow.HollowTriangle:
                destination[0] = Tip; destination[1] = At(Size, Size * .45); destination[2] = At(Size, -Size * .45); return 3;
            case EdgeArrow.Open:
                destination[0] = At(Size, Size * .45); destination[1] = Tip; destination[2] = At(Size, -Size * .45); return 3;
            case EdgeArrow.Diamond: case EdgeArrow.HollowDiamond:
                destination[0] = Tip; destination[1] = At(Size * .5, Size * .45); destination[2] = At(Size, 0); destination[3] = At(Size * .5, -Size * .45); return 4;
            case EdgeArrow.Tee:
                destination[0] = At(0, Size * .45); destination[1] = At(0, -Size * .45); return 2;
            default: return 0;
        }
    }
    public RectD Bounds
    {
        get
        {
            var padding = IsHollow || IsOpen ? OutlineWidth / 2 : 0;
            if (IsCircle) { var c = CircleCenter; var r = CircleRadius + padding; return new(c.X - r, c.Y - r, 2 * r, 2 * r); }
            Span<PointD> points = stackalloc PointD[4]; var count = CopyVertices(points);
            var bounds = new RectD(Tip.X, Tip.Y, 0, 0);
            for (var i = 0; i < count; i++) bounds = RectD.Union(bounds, new(points[i].X, points[i].Y, 0, 0));
            return bounds.Inflate(padding);
        }
    }
    public bool Hit(PointD point, double tolerance = 0)
    {
        if (!Bounds.Inflate(tolerance).Contains(point)) return false;
        if (IsCircle) return PointD.Distance(point, CircleCenter) <= CircleRadius + tolerance + (IsHollow ? OutlineWidth / 2 : 0);
        Span<PointD> points = stackalloc PointD[4]; var count = CopyVertices(points); var sign = 0;
        for (var i = 0; i < (IsOpen ? count - 1 : count); i++)
        {
            var a = points[i]; var b = points[(i + 1) % count];
            if (Geometry.DistanceToSegment(point, a, b) <= tolerance + (IsOpen || IsHollow ? OutlineWidth / 2 : 0)) return true;
        }
        if (IsOpen || count < 3) return false;
        for (var i = 0; i < count; i++)
        {
            var a = points[i]; var b = points[(i + 1) % count]; var d = b - a; var p = point - a;
            var cross = d.X * p.Y - d.Y * p.X; if (Math.Abs(cross) <= 1e-12) continue;
            var next = cross > 0 ? 1 : -1; if (sign != 0 && sign != next) return false; sign = next;
        }
        return true;
    }
    public bool Intersects(RectD rectangle)
    {
        if (!Bounds.Intersects(rectangle)) return false;
        if (IsCircle)
        {
            var c = CircleCenter; return Hit(new(Math.Clamp(c.X, rectangle.X, rectangle.Right), Math.Clamp(c.Y, rectangle.Y, rectangle.Bottom)));
        }
        Span<PointD> points = stackalloc PointD[4]; var count = CopyVertices(points);
        var radius = IsOpen || IsHollow ? OutlineWidth / 2 : 0;
        for (var i = 0; i < (IsOpen ? count - 1 : count); i++)
            if (EdgeGeometry.IntersectsStroke(rectangle, points[i], points[(i + 1) % count], radius)) return true;
        return !IsOpen && (Hit(new(rectangle.X, rectangle.Y)) || Hit(new(rectangle.Right, rectangle.Y)) ||
            Hit(new(rectangle.Right, rectangle.Bottom)) || Hit(new(rectangle.X, rectangle.Bottom)));
    }
}

public static class EdgeGeometry
{
    // Intersection with a round-capped segment, including diagonal corners; inflating the rectangle alone over-selects.
    internal static bool IntersectsStroke(RectD rectangle, PointD a, PointD b, double radius)
    {
        if (Geometry.RectangleIntersectsSegment(rectangle, a, b)) return true;
        if (radius == 0) return false;
        static double Distance(PointD p, RectD r) => PointD.Distance(p, new(Math.Clamp(p.X, r.X, r.Right), Math.Clamp(p.Y, r.Y, r.Bottom)));
        if (Distance(a, rectangle) <= radius || Distance(b, rectangle) <= radius) return true;
        return Geometry.DistanceToSegment(new(rectangle.X, rectangle.Y), a, b) <= radius ||
            Geometry.DistanceToSegment(new(rectangle.Right, rectangle.Y), a, b) <= radius ||
            Geometry.DistanceToSegment(new(rectangle.Right, rectangle.Bottom), a, b) <= radius ||
            Geometry.DistanceToSegment(new(rectangle.X, rectangle.Bottom), a, b) <= radius;
    }
    public static EdgeMarker? Marker(GraphEdge edge, IReadOnlyList<PointD> path, bool source, bool overview = false)
    {
        var style = edge.Style ?? EdgeStyle.Default; var kind = style.ResolveArrow(source, edge.Directed);
        if (kind == EdgeArrow.None || path.Count < 2) return null;
        var tip = source ? path[0] : path[^1];
        for (var j = 1; j < path.Count; j++)
        {
            var other = source ? path[j] : path[path.Count - 1 - j]; var delta = tip - other; var length = delta.Length;
            if (length <= .001) continue;
            if (!double.IsFinite(length)) throw new ArgumentException("Marker tangent exceeds the finite coordinate range.");
            var size = (overview ? 5 : 8) * Math.Max(1, Math.Sqrt(style.Width / 1.5));
            return new(kind, tip, delta * (1 / length), size, Math.Min(style.Width, size / 4));
        }
        return null;
    }
    /// <summary>Includes stroke and endpoint markers, but not the separately measured edge caption.</summary>
    public static RectD Bounds(GraphEdge edge, IReadOnlyList<PointD> path)
    {
        if (path.Count == 0) throw new ArgumentException("Edge paths cannot be empty.", nameof(path));
        var b = new RectD(path[0].X, path[0].Y, 0, 0);
        for (var i = 1; i < path.Count; i++) b = RectD.Union(b, new(path[i].X, path[i].Y, 0, 0));
        b = b.Inflate((edge.Style ?? EdgeStyle.Default).Width / 2);
        if (Marker(edge, path, true) is { } source) b = RectD.Union(b, source.Bounds);
        if (Marker(edge, path, false) is { } target) b = RectD.Union(b, target.Bounds);
        return b;
    }
    public static bool Hit(GraphEdge edge, IReadOnlyList<PointD> path, PointD point, double tolerance)
    {
        var width = edge.Style?.Width / 2 ?? 0; // Preserve legacy centerline picking for unstyled edges.
        for (var i = 1; i < path.Count; i++) if (Geometry.DistanceToSegment(point, path[i - 1], path[i]) <= tolerance + width) return true;
        return edge.Style is not null && (Marker(edge, path, true)?.Hit(point, tolerance) == true || Marker(edge, path, false)?.Hit(point, tolerance) == true);
    }
    public static bool MatchesArea(GraphEdge edge, IReadOnlyList<PointD> path, RectD rectangle, bool contains)
    {
        if (contains)
        {
            if (edge.Style is not null) return Geometry.RectangleContainsRectangle(rectangle, Bounds(edge, path));
            for (var i = 0; i < path.Count; i++) if (!rectangle.Contains(path[i])) return false;
            return true;
        }
        var radius = edge.Style?.Width / 2 ?? 0;
        for (var i = 1; i < path.Count; i++) if (IntersectsStroke(rectangle, path[i - 1], path[i], radius)) return true;
        return edge.Style is not null && (Marker(edge, path, true)?.Intersects(rectangle) == true || Marker(edge, path, false)?.Intersects(rectangle) == true);
    }
}
