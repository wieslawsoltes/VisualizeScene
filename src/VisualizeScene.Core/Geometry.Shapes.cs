namespace VisualizeScene.Core;

public static partial class Geometry
{
    /// <summary>Vertices in the same winding order used by the vector renderers.</summary>
    public static PointD[] Polygon(GraphNode node)
    {
        var b = node.Bounds;
        return node.Shape switch
        {
            NodeShape.Diamond => [new(b.Center.X, b.Y), new(b.Right, b.Center.Y), new(b.Center.X, b.Bottom), new(b.X, b.Center.Y)],
            NodeShape.Hexagon => [new(b.X + b.Width / 4, b.Y), new(b.Right - b.Width / 4, b.Y), new(b.Right, b.Center.Y), new(b.Right - b.Width / 4, b.Bottom), new(b.X + b.Width / 4, b.Bottom), new(b.X, b.Center.Y)],
            NodeShape.Parallelogram => [new(b.X + b.Width / 5, b.Y), new(b.Right, b.Y), new(b.Right - b.Width / 5, b.Bottom), new(b.X, b.Bottom)],
            _ => [new(b.X, b.Y), new(b.Right, b.Y), new(b.Right, b.Bottom), new(b.X, b.Bottom)]
        };
    }

    public static bool HitNode(GraphNode node, PointD point)
    {
        var b = node.Bounds; if (!point.IsFinite || !b.Contains(point)) return false;
        var x = Math.Abs(point.X - b.Center.X) / Math.Max(.001, b.Width / 2);
        var y = Math.Abs(point.Y - b.Center.Y) / Math.Max(.001, b.Height / 2);
        if (node.Shape == NodeShape.Ellipse) return x * x + y * y <= 1 + 1e-12;
        if (node.Shape == NodeShape.Diamond) return x + y <= 1 + 1e-12;
        if (node.Shape == NodeShape.RoundedRectangle)
        {
            var radius = Math.Min(6, Math.Min(b.Width, b.Height) / 2);
            var dx = Math.Max(0, Math.Abs(point.X - b.Center.X) - (b.Width / 2 - radius));
            var dy = Math.Max(0, Math.Abs(point.Y - b.Center.Y) - (b.Height / 2 - radius));
            return dx * dx + dy * dy <= radius * radius + 1e-12;
        }
        if (node.Shape is not (NodeShape.Hexagon or NodeShape.Parallelogram)) return true;
        var vertices = Polygon(node);
        for (var i = 0; i < vertices.Length; i++)
        {
            var a = vertices[i]; var d = vertices[(i + 1) % vertices.Length] - a;
            if (Cross(d, point - a) < -1e-9) return false;
        }
        return true;
    }

    /// <summary>Intersects the ray from the node center with the painted shape, not just its bounding box.</summary>
    public static PointD ClipToNode(GraphNode node, PointD toward)
    {
        if (!toward.IsFinite) throw new ArgumentException("Endpoint coordinates must be finite.", nameof(toward));
        var b = node.Bounds; var center = b.Center; var direction = toward - center;
        if (direction.Length < 1e-9) return center;
        var rx = Math.Max(b.Width / 2, .001); var ry = Math.Max(b.Height / 2, .001);
        if (node.Shape == NodeShape.Ellipse)
            return center + direction * (1 / Math.Sqrt(direction.X * direction.X / (rx * rx) + direction.Y * direction.Y / (ry * ry)));
        if (node.Shape == NodeShape.RoundedRectangle)
        {
            var high = 1 / Math.Max(Math.Abs(direction.X) / rx, Math.Abs(direction.Y) / ry); var low = 0d;
            if (HitNode(node, center + direction * high)) return center + direction * high;
            // Convexity gives one crossing; fixed iterations are bounded even at extreme zoom.
            for (var i = 0; i < 48; i++)
            {
                var middle = (low + high) / 2;
                if (HitNode(node, center + direction * middle)) low = middle; else high = middle;
            }
            return center + direction * low;
        }
        var vertices = Polygon(node); var first = double.PositiveInfinity;
        for (var i = 0; i < vertices.Length; i++)
        {
            var a = vertices[i]; var edge = vertices[(i + 1) % vertices.Length] - a;
            var denominator = Cross(direction, edge); if (Math.Abs(denominator) < 1e-15) continue;
            var t = Cross(a - center, edge) / denominator;
            var u = Cross(a - center, direction) / denominator;
            if (t >= 0 && u >= -1e-12 && u <= 1 + 1e-12) first = Math.Min(first, t);
        }
        return double.IsFinite(first) ? center + direction * first : center;
    }

    /// <summary>Orthogonal loop outside the owner's bounds; explicit interior ports use their nearest exit side.</summary>
    public static IReadOnlyList<PointD> SelfLoop(RectD node, PointD start, PointD end, double clearance = 35)
    {
        if (!node.IsValid || !start.IsFinite || !end.IsFinite || !double.IsFinite(clearance) || clearance <= 0)
            throw new ArgumentException("Invalid self-loop geometry.");
        var outside = node.Inflate(clearance);
        (int Side, PointD Exit) Exit(PointD p)
        {
            double[] distances = [Math.Abs(p.Y - node.Y), Math.Abs(p.X - node.Right), Math.Abs(p.Y - node.Bottom), Math.Abs(p.X - node.X)];
            var side = Enumerable.Range(0, 4).MinBy(i => distances[i]);
            return (side, side switch { 0 => new(p.X, outside.Y), 1 => new(outside.Right, p.Y), 2 => new(p.X, outside.Bottom), _ => new(outside.X, p.Y) });
        }
        var a = Exit(start); var b = Exit(end);
        PointD[] corners = [new(outside.Right, outside.Y), new(outside.Right, outside.Bottom), new(outside.X, outside.Bottom), new(outside.X, outside.Y)];
        List<PointD> Clockwise((int Side, PointD Exit) from, (int Side, PointD Exit) to)
        {
            var result = new List<PointD> { from.Exit };
            var count = (to.Side - from.Side + 4) % 4;
            if (count == 0 && PointD.Distance(start, end) < 1e-9) count = 4;
            for (var i = 0; i < count; i++) result.Add(corners[(from.Side + i) % 4]);
            result.Add(to.Exit); return result;
        }
        static double Length(IReadOnlyList<PointD> path) => Enumerable.Range(1, path.Count - 1).Sum(i => PointD.Distance(path[i - 1], path[i]));
        var clockwise = Clockwise(a, b); var counter = Clockwise(b, a); counter.Reverse();
        return new[] { start }.Concat(Length(clockwise) <= Length(counter) ? clockwise : counter).Append(end).ToArray();
    }

    private static double Cross(PointD a, PointD b) => a.X * b.Y - a.Y * b.X;

    /// <summary>Position at a fraction of arc length, skipping zero-length polyline segments.</summary>
    public static PointD PointAtFraction(IReadOnlyList<PointD> points, double fraction)
    {
        ArgumentNullException.ThrowIfNull(points);
        if (points.Count == 0) throw new ArgumentException("The path is empty.", nameof(points));
        if (!double.IsFinite(fraction)) throw new ArgumentOutOfRangeException(nameof(fraction));
        var length = 0d;
        for (var i = 1; i < points.Count; i++) length += PointD.Distance(points[i - 1], points[i]);
        if (length <= 1e-12) return points[0];
        var remaining = Math.Clamp(fraction, 0, 1) * length;
        for (var i = 1; i < points.Count; i++)
        {
            var segment = PointD.Distance(points[i - 1], points[i]); if (segment <= 1e-12) continue;
            if (remaining <= segment) return PointD.Lerp(points[i - 1], points[i], remaining / segment);
            remaining -= segment;
        }
        return points[^1];
    }
}
