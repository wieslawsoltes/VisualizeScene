using VisualizeScene.Core;

namespace VisualizeScene.Layout;

/// <summary>Exterior node candidates, radial port candidates and arc-length edge candidates. No rotation or node movement.</summary>
public sealed class DefaultLabelCandidateProvider : ILabelCandidateProvider
{
    public IEnumerable<PointD> GetCandidates(LabelCandidateContext context)
    {
        var index = context.Index; var label = context.Label; var size = context.Size; var gap = context.Options.Gap;
        if (index.Nodes.TryGetValue(label.OwnerId, out var node))
        {
            var b = node.Bounds;
            foreach (var extra in new[] { 0d, Math.Max(size.Y, gap) + gap })
            {
                var x = b.Width / 2 + size.X / 2 + gap + extra;
                var y = b.Height / 2 + size.Y / 2 + gap + extra;
                foreach (var delta in new PointD[] { new(0, -y), new(x, 0), new(0, y), new(-x, 0), new(x, -y), new(x, y), new(-x, y), new(-x, -y) })
                    yield return b.Center + delta;
            }
        }
        else if (index.Ports.ContainsKey(label.OwnerId))
        {
            var center = index.PortLocation(label.OwnerId);
            var owner = index.Nodes[index.Ports[label.OwnerId].NodeId];
            for (var ring = 0; ring < 3; ring++)
            {
                var x = size.X / 2 + gap + 4 + ring * (owner.Bounds.Width / 2 + gap);
                var y = size.Y / 2 + gap + 4 + ring * (owner.Bounds.Height / 2 + gap);
                foreach (var delta in new PointD[] { new(0, -y), new(x, 0), new(0, y), new(-x, 0), new(x, -y), new(x, y), new(-x, y), new(-x, -y) })
                    yield return center + delta;
            }
        }
        else
        {
            var path = index.EdgePath(index.Edges[label.OwnerId]);
            for (var i = 0; i < context.Options.EdgeSamples; i++)
            {
                var fraction = (i + 1d) / (context.Options.EdgeSamples + 1);
                var center = Geometry.PointAtFraction(path, fraction);
                var before = Geometry.PointAtFraction(path, Math.Max(0, fraction - .0001));
                var after = Geometry.PointAtFraction(path, Math.Min(1, fraction + .0001));
                var tangent = after - before; var length = tangent.Length;
                var normal = length > 1e-10 ? new PointD(-tangent.Y / length, tangent.X / length) : new PointD(0, -1);
                var radius = Math.Abs(normal.X) * size.X / 2 + Math.Abs(normal.Y) * size.Y / 2;
                foreach (var distance in new[] { radius + gap, 2 * radius + 2 * gap })
                {
                    yield return center + normal * distance;
                    yield return center - normal * distance;
                }
            }
        }
    }
}
