using VisualizeScene.Core;

namespace VisualizeScene.Rendering.Skia;

public sealed partial class GraphRenderer
{
    private static bool SameEdgeBounds(GraphEdge a, GraphEdge b)
    {
        var x = a.Style ?? EdgeStyle.Default; var y = b.Style ?? EdgeStyle.Default;
        return a.Text == b.Text && (a.Style is null) == (b.Style is null) && x.Width == y.Width &&
            x.ResolveArrow(true, a.Directed) == y.ResolveArrow(true, b.Directed) &&
            x.ResolveArrow(false, a.Directed) == y.ResolveArrow(false, b.Directed);
    }
    private RectD EdgeVisualBounds(EdgePicture picture)
    {
        var edge = picture.Edge;
        var bounds = edge.Style is null ? RectD.Enclose(picture.Points.Select(p => new RectD(p.X, p.Y, 0, 0))) : EdgeGeometry.Bounds(edge, picture.Points);
        if (edge.Text.Length > 0)
        {
            var caption = new GraphLabel("", edge.Id, edge.Text, new(0,-9),11);
            bounds = RectD.Union(bounds, GraphScene.LabelBounds(index!, caption, MeasureLabel(caption)));
        }
        return bounds.Inflate(14);
    }
}
