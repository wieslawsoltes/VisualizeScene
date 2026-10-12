using System.Xml.Linq;
using VisualizeScene.Core;

namespace VisualizeScene.Serialization;

public static partial class SvgExporter
{
    private static void StyledEdge(XElement svg, GraphEdge edge, IReadOnlyList<PointD> points)
    {
        var ns = svg.Name.Namespace; var style = edge.Style!;
        var line = new XElement(ns + "polyline", new XAttribute("data-edge-id", edge.Id),
            new XAttribute("points", string.Join(" ", points.Select(p => $"{F(p.X)},{F(p.Y)}"))),
            new XAttribute("fill", "none"), new XAttribute("stroke", edge.Stroke), new XAttribute("stroke-width", F(style.Width)),
            new XAttribute("stroke-linejoin", "round"), new XAttribute("stroke-linecap", style.Dash == EdgeDash.Solid ? "round" : "butt"));
        var pattern = EdgeStyle.DashPattern(style.Dash);
        if (!pattern.IsEmpty)
        {
            var intervals = new string[pattern.Length]; for (var i = 0; i < intervals.Length; i++) intervals[i] = F(pattern[i] * style.Width);
            line.Add(new XAttribute("stroke-dasharray", string.Join(" ", intervals)));
        }
        svg.Add(line);
        void Marker(bool source)
        {
            if (EdgeGeometry.Marker(edge, points, source) is not { } marker) return;
            XElement shape;
            if (marker.IsCircle) shape = new(ns + "circle", new XAttribute("cx",F(marker.CircleCenter.X)),new XAttribute("cy",F(marker.CircleCenter.Y)),new XAttribute("r",F(marker.CircleRadius)));
            else
            {
                Span<PointD> vertices = stackalloc PointD[4]; var count = marker.CopyVertices(vertices); var text = new string[count];
                for (var i=0;i<count;i++) text[i] = $"{F(vertices[i].X)},{F(vertices[i].Y)}";
                shape = new(ns + (marker.IsOpen ? "polyline" : "polygon"), new XAttribute("points",string.Join(" ",text)));
            }
            shape.Add(new XAttribute("data-edge-marker",source ? "source" : "target"),
                new XAttribute("fill", marker.IsOpen ? "none" : marker.IsHollow ? "white" : edge.Stroke));
            if (marker.IsOpen || marker.IsHollow) shape.Add(new XAttribute("stroke",edge.Stroke),new XAttribute("stroke-width",F(marker.OutlineWidth)),new XAttribute("stroke-linejoin","round"),new XAttribute("stroke-linecap","round"));
            svg.Add(shape);
        }
        Marker(true); Marker(false);
    }
}
