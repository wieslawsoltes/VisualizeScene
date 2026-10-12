using System.Globalization;
using System.Xml.Linq;
using VisualizeScene.Core;

namespace VisualizeScene.Serialization;

public static class SvgExporter
{
    private static string F(double value) => value.ToString("0.###", CultureInfo.InvariantCulture);
    public static string Export(GraphSnapshot graph, Func<GraphLabel, PointD>? measureLabel = null)
    {
        GraphValidation.Validate(graph); var index = graph.Index(); XNamespace ns = "http://www.w3.org/2000/svg";
        var visible = graph.Nodes.Where(n => index.IsVisible(n.Id)).ToArray();
        var bounds = GraphScene.Bounds(graph, measureLabel).Inflate(40);
        var svg = new XElement(ns + "svg", new XAttribute("viewBox", $"{F(bounds.X)} {F(bounds.Y)} {F(bounds.Width)} {F(bounds.Height)}"), new XAttribute("width", F(bounds.Width)), new XAttribute("height", F(bounds.Height)), new XAttribute("role", "img"), new XElement(ns + "title", "VisualizeScene graph"));
        var marker = new XElement(ns + "marker", new XAttribute("id", "arrow"), new XAttribute("viewBox", "0 0 10 10"), new XAttribute("refX", "9"), new XAttribute("refY", "5"), new XAttribute("markerWidth", "7"), new XAttribute("markerHeight", "7"), new XAttribute("orient", "auto-start-reverse"), new XElement(ns + "path", new XAttribute("d", "M 0 0 L 10 5 L 0 10 z"), new XAttribute("fill", "context-stroke")));
        svg.Add(new XElement(ns + "defs", marker));
        svg.Add(new XElement(ns + "rect", new XAttribute("x", F(bounds.X)), new XAttribute("y", F(bounds.Y)), new XAttribute("width", F(bounds.Width)), new XAttribute("height", F(bounds.Height)), new XAttribute("fill", "white")));
        void Text(string text, PointD center, double fontSize = 12)
        {
            var lines = text.Split('\n');
            for (var i = 0; i < lines.Length; i++) svg.Add(new XElement(ns + "text", new XAttribute("x", F(center.X)), new XAttribute("y", F(center.Y + (i - (lines.Length - 1) / 2d) * fontSize * 1.25)), new XAttribute("text-anchor", "middle"), new XAttribute("dominant-baseline", "central"), new XAttribute("font-family", "Segoe UI,Arial,sans-serif"), new XAttribute("font-size", F(fontSize)), new XAttribute("fill", "#21384D"), lines[i]));
        }
        void Node(GraphNode node)
        {
            var b = node.Bounds;
            XElement element;
            if (node.Shape == NodeShape.Ellipse) element = new(ns + "ellipse", new XAttribute("cx", F(b.Center.X)), new XAttribute("cy", F(b.Center.Y)), new XAttribute("rx", F(b.Width / 2)), new XAttribute("ry", F(b.Height / 2)));
            else if (node.Shape is NodeShape.Diamond or NodeShape.Hexagon or NodeShape.Parallelogram)
            {
                var points = Geometry.Polygon(node);
                element = new(ns + "polygon", new XAttribute("points", string.Join(" ", points.Select(p => $"{F(p.X)},{F(p.Y)}"))));
            }
            else element = new(ns + "rect", new XAttribute("x", F(b.X)), new XAttribute("y", F(b.Y)), new XAttribute("width", F(b.Width)), new XAttribute("height", F(b.Height)), new XAttribute("rx", node.Shape == NodeShape.RoundedRectangle ? "6" : "0"));
            element.Add(new XAttribute("fill", node.Fill), new XAttribute("stroke", node.Stroke), new XAttribute("stroke-width", "1.5"), new XElement(ns + "title", node.Text)); svg.Add(element);
            Text(node.Text, node.IsGroup && !node.Collapsed ? new(b.Center.X, b.Y + 15) : b.Center, 13);
        }
        foreach (var n in visible.Where(n => n.IsGroup && !n.Collapsed).OrderBy(n => index.Depth(n.Id))) Node(n);
        foreach (var edge in graph.Edges)
        {
            if (!GraphScene.IsEdgeVisible(index, edge)) continue;
            var path = index.EdgePath(edge); var e = new XElement(ns + "polyline", new XAttribute("points", string.Join(" ", path.Select(p => $"{F(p.X)},{F(p.Y)}"))), new XAttribute("fill", "none"), new XAttribute("stroke", edge.Stroke), new XAttribute("stroke-width", "1.5"), new XAttribute("stroke-linejoin", "round"));
            if (edge.Directed) e.Add(new XAttribute("marker-end", "url(#arrow)")); svg.Add(e);
            if (edge.Text.Length > 0) Text(edge.Text, Geometry.PointAtFraction(path, .5) + new PointD(0, -9));
        }
        foreach (var n in visible.Where(n => !n.IsGroup || n.Collapsed)) Node(n);
        foreach (var label in graph.Labels)
        {
            if (!GraphScene.IsOwnerVisible(index, label.OwnerId)) continue;
            Text(label.Text, GraphScene.LabelAnchor(index, label.OwnerId) + label.Offset, label.FontSize);
        }
        foreach (var port in graph.Ports.Where(p => index.IsVisible(p.NodeId)))
        {
            var point = index.PortLocation(port.Id);
            svg.Add(new XElement(ns + "circle", new XAttribute("cx", F(point.X)), new XAttribute("cy", F(point.Y)),
                new XAttribute("r", "3.5"), new XAttribute("fill", "white"), new XAttribute("stroke", "#386797")));
        }
        return new XDocument(svg).ToString();
    }
}
