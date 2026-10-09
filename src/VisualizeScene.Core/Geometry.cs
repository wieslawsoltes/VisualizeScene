namespace VisualizeScene.Core;

public readonly record struct PointD(double X, double Y)
{
    public static PointD operator +(PointD a, PointD b) => new(a.X + b.X, a.Y + b.Y);
    public static PointD operator -(PointD a, PointD b) => new(a.X - b.X, a.Y - b.Y);
    public static PointD operator *(PointD p, double value) => new(p.X * value, p.Y * value);
    public double Length => Math.Sqrt(X * X + Y * Y);
    public bool IsFinite => double.IsFinite(X) && double.IsFinite(Y);
    public static double Distance(PointD a, PointD b) => (a - b).Length;
    public static PointD Lerp(PointD a, PointD b, double t) => a + (b - a) * t;
}

public readonly record struct RectD(double X, double Y, double Width, double Height)
{
    public double Right => X + Width;
    public double Bottom => Y + Height;
    public PointD Center => new(X + Width / 2, Y + Height / 2);
    public bool IsValid => double.IsFinite(X) && double.IsFinite(Y) && double.IsFinite(Width) && double.IsFinite(Height) && Width >= 0 && Height >= 0;
    public bool Contains(PointD p) => p.X >= X && p.X <= Right && p.Y >= Y && p.Y <= Bottom;
    public bool Intersects(RectD b) => X <= b.Right && Right >= b.X && Y <= b.Bottom && Bottom >= b.Y;
    public RectD Inflate(double amount) => new(X - amount, Y - amount, Width + 2 * amount, Height + 2 * amount);
    public RectD Move(PointD delta) => this with { X = X + delta.X, Y = Y + delta.Y };
    public static RectD FromPoints(PointD a, PointD b) => new(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Abs(a.X - b.X), Math.Abs(a.Y - b.Y));
    public static RectD Union(RectD a, RectD b) => new(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Max(a.Right, b.Right) - Math.Min(a.X, b.X), Math.Max(a.Bottom, b.Bottom) - Math.Min(a.Y, b.Y));
    public static RectD Enclose(IEnumerable<RectD> values)
    {
        using var it = values.GetEnumerator();
        if (!it.MoveNext()) return new RectD(0, 0, 1, 1);
        var result = it.Current;
        while (it.MoveNext()) result = Union(result, it.Current);
        return result;
    }
}

public static class Geometry
{
    public static double DistanceToSegment(PointD p, PointD a, PointD b)
    {
        var d = b - a;
        var lengthSquared = d.X * d.X + d.Y * d.Y;
        if (lengthSquared < 1e-15) return PointD.Distance(p, a);
        var t = Math.Clamp(((p.X - a.X) * d.X + (p.Y - a.Y) * d.Y) / lengthSquared, 0, 1);
        return PointD.Distance(p, PointD.Lerp(a, b, t));
    }

    public static PointD ClipToNode(GraphNode node, PointD toward)
    {
        var b = node.Bounds;
        var c = b.Center;
        var d = toward - c;
        if (d.Length < 1e-9) return c;
        var rx = Math.Max(b.Width / 2, .001);
        var ry = Math.Max(b.Height / 2, .001);
        var t = node.Shape switch
        {
            NodeShape.Ellipse => 1 / Math.Sqrt(d.X * d.X / (rx * rx) + d.Y * d.Y / (ry * ry)),
            NodeShape.Diamond => 1 / (Math.Abs(d.X) / rx + Math.Abs(d.Y) / ry),
            _ => 1 / Math.Max(Math.Abs(d.X) / rx, Math.Abs(d.Y) / ry)
        };
        return c + d * t;
    }

    public static bool HitNode(GraphNode node, PointD p)
    {
        if (!node.Bounds.Contains(p)) return false;
        var b = node.Bounds;
        var x = Math.Abs(p.X - b.Center.X) / Math.Max(.001, b.Width / 2);
        var y = Math.Abs(p.Y - b.Center.Y) / Math.Max(.001, b.Height / 2);
        return node.Shape switch { NodeShape.Ellipse => x * x + y * y <= 1, NodeShape.Diamond => x + y <= 1, _ => true };
    }
}

public sealed class Viewport
{
    public PointD Origin { get; private set; }
    public double Zoom { get; private set; } = 1;
    public PointD ToWorld(PointD screen) => Origin + screen * (1 / Zoom);
    public PointD ToScreen(PointD world) => (world - Origin) * Zoom;
    public void Pan(PointD screenDelta) => Origin -= screenDelta * (1 / Zoom);
    public void Center(PointD point, double width, double height) => Origin = point - new PointD(width / (2 * Zoom), height / (2 * Zoom));
    public void ZoomAt(PointD screenAnchor, double factor)
    {
        if (!double.IsFinite(factor) || factor <= 0) throw new ArgumentOutOfRangeException(nameof(factor));
        var anchor = ToWorld(screenAnchor);
        Zoom = Math.Clamp(Zoom * factor, .05, 8);
        Origin = anchor - screenAnchor * (1 / Zoom);
    }
    public void Fit(RectD bounds, double width, double height, double padding = 40)
    {
        if (width <= 2 * padding || height <= 2 * padding) return;
        Zoom = Math.Clamp(Math.Min((width - 2 * padding) / Math.Max(1, bounds.Width), (height - 2 * padding) / Math.Max(1, bounds.Height)), .05, 2);
        Center(bounds.Center, width, height);
    }
}
