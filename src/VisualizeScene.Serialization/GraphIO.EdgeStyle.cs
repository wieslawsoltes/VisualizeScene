using System.Xml.Linq;
using VisualizeScene.Core;

namespace VisualizeScene.Serialization;

public static partial class GraphIO
{
    private static GraphEdge LegacyEdgeStyle(XElement item, GraphEdge edge, List<string> diagnostics)
    {
        var line = item.Descendants().FirstOrDefault(e => e.Name.LocalName == "LineStyle");
        var arrows = item.Descendants().FirstOrDefault(e => e.Name.LocalName == "Arrows");
        if (line is null && arrows is null) return edge;
        var color = (string?)line?.Attribute("color") ?? edge.Stroke;
        if (!GraphValidation.ValidColor(color)) { diagnostics.Add($"Edge '{edge.Id}' has an unsupported line color; the default color was used."); color = edge.Stroke; }
        var width = Number((string?)line?.Attribute("width"), 1.5);
        var dash = ((string?)line?.Attribute("type"))?.ToLowerInvariant() switch
        {
            null or "line" => EdgeDash.Solid, "dashed" => EdgeDash.Dash, "dotted" => EdgeDash.Dot,
            "dashed_dotted" => EdgeDash.DashDot, "dash_dot_dot" => EdgeDash.DashDotDot, var unknown => UnknownDash(unknown)
        };
        EdgeDash UnknownDash(string value) { diagnostics.Add($"Edge '{edge.Id}' line pattern '{value}' is unsupported; solid was used."); return EdgeDash.Solid; }
        EdgeArrow Arrow(string? value, bool source) => value?.ToLowerInvariant() switch
        {
            null => source ? EdgeArrow.None : EdgeArrow.Automatic, "none" => EdgeArrow.None,
            "standard" or "delta" => EdgeArrow.Triangle, "white_delta" => EdgeArrow.HollowTriangle,
            "plain" => EdgeArrow.Open, "diamond" => EdgeArrow.Diamond, "white_diamond" => EdgeArrow.HollowDiamond,
            "circle" => EdgeArrow.Circle, "transparent_circle" => EdgeArrow.HollowCircle, "t_shape" => EdgeArrow.Tee,
            var unknown => UnknownArrow(unknown)
        };
        EdgeArrow UnknownArrow(string value) { diagnostics.Add($"Edge '{edge.Id}' arrow '{value}' is unsupported; no marker was used."); return EdgeArrow.None; }
        var style = new EdgeStyle(width, dash, Arrow((string?)arrows?.Attribute("source"), true), Arrow((string?)arrows?.Attribute("target"), false));
        style.Validate(); return edge with { Stroke = color, Style = style };
    }
}
