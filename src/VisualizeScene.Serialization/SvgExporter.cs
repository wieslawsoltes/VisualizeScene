using System.Globalization;
using System.Xml.Linq;
using VisualizeScene.Core;

namespace VisualizeScene.Serialization;

public static class SvgExporter
{
    private static string F(double value) => value.ToString("0.###", CultureInfo.InvariantCulture);
    public static string Export(GraphSnapshot graph)
    {
        GraphValidation.Validate(graph); var index = graph.Index(); XNamespace ns = "http://www.w3.org/2000/svg";
        var visible = graph.Nodes.Where(n => index.IsVisible(n.Id)).ToArray();
        var bounds = RectD.Enclose(visible.Select(n => n.Bounds).Concat(graph.Edges.SelectMany(e => index.EdgePath(e)).Select(p => new RectD(p.X, p.Y, 0, 0)))).Inflate(40);
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
                PointD[] points = node.Shape switch
                {
                    NodeShape.Diamond => [new(b.Center.X, b.Y), new(b.Right, b.Center.Y), new(b.Center.X, b.Bottom), new(b.X, b.Center.Y)],
                    NodeShape.Hexagon => [new(b.X + b.Width / 4, b.Y), new(b.Right - b.Width / 4, b.Y), new(b.Right, b.Center.Y), new(b.Right - b.Width / 4, b.Bottom), new(b.X + b.Width / 4, b.Bottom), new(b.X, b.Center.Y)],
                    _ => [new(b.X + b.Width / 5, b.Y), new(b.Right, b.Y), new(b.Right - b.Width / 5, b.Bottom), new(b.X, b.Bottom)]
                };
                element = new(ns + "polygon", new XAttribute("points", string.Join(" ", points.Select(p => $"{F(p.X)},{F(p.Y)}"))));
            }
            else element = new(ns + "rect", new XAttribute("x", F(b.X)), new XAttribute("y", F(b.Y)), new XAttribute("width", F(b.Width)), new XAttribute("height", F(b.Height)), new XAttribute("rx", node.Shape == NodeShape.RoundedRectangle ? "6" : "0"));
            element.Add(new XAttribute("fill", node.Fill), new XAttribute("stroke", node.Stroke), new XAttribute("stroke-width", "1.5"), new XElement(ns + "title", node.Text)); svg.Add(element);
            Text(node.Text, node.IsGroup && !node.Collapsed ? new(b.Center.X, b.Y + 15) : b.Center, 13);
        }
        int Depth(GraphNode n) { var depth = 0; while (n.ParentId is not null) { depth++; n = index.Nodes[n.ParentId]; } return depth; }
        foreach (var n in visible.Where(n => n.IsGroup && !n.Collapsed).OrderBy(Depth)) Node(n);
        foreach (var edge in graph.Edges)
        {
            if (index.VisibleRepresentative(edge.SourceId) == index.VisibleRepresentative(edge.TargetId) && edge.SourceId != edge.TargetId) continue;
            var path = index.EdgePath(edge); var e = new XElement(ns + "polyline", new XAttribute("points", string.Join(" ", path.Select(p => $"{F(p.X)},{F(p.Y)}"))), new XAttribute("fill", "none"), new XAttribute("stroke", edge.Stroke), new XAttribute("stroke-width", "1.5"), new XAttribute("stroke-linejoin", "round"));
            if (edge.Directed) e.Add(new XAttribute("marker-end", "url(#arrow)")); svg.Add(e);
            if (edge.Text.Length > 0) Text(edge.Text, PointD.Lerp(path[(path.Count - 1) / 2], path[path.Count / 2], .5) + new PointD(0, -9));
        }
        foreach (var n in visible.Where(n => !n.IsGroup || n.Collapsed)) Node(n);
        foreach (var label in graph.Labels)
        {
            PointD position;
            if (index.Nodes.TryGetValue(label.OwnerId, out var node)) { if (!index.IsVisible(node.Id)) continue; position = node.Bounds.Center; }
            else if (index.Edges.TryGetValue(label.OwnerId, out var edge)) { var points = index.EdgePath(edge); position = PointD.Lerp(points[0], points[^1], .5); }
            else { if (!index.IsVisible(index.Ports[label.OwnerId].NodeId)) continue; position = index.PortLocation(label.OwnerId); }
            Text(label.Text, position + label.Offset, label.FontSize);
        }
        return new XDocument(svg).ToString();
    }
}
