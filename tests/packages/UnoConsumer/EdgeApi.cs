using VisualizeScene.Controls.Uno;
using VisualizeScene.Core;
using VisualizeScene.Editing;
using VisualizeScene.Rendering.Skia;

namespace PackageConsumer;

/// <summary>Compile the palette and portable style contracts from NuGet, on both Uno targets.</summary>
public static class EdgeApi
{
    public static EdgePalette Create(GraphControl graph)
    {
        var options = new EdgeCreationOptions(EdgeRouting.Polyline, "#112233", new(3, EdgeDash.DashDot, EdgeArrow.HollowDiamond, EdgeArrow.Open));
        options.Validate(); graph.Session.EdgeDefaults = options;
        var palette = new EdgePalette { Target = graph };
        palette.SetItems([new EdgeTemplate("Dependency", options)]);
        _ = palette.GetTemplateButton(0);
        var summary = new GraphPropertySelection(graph.Document.Snapshot, graph.Session.Selection);
        _ = summary.LineWidth; _ = summary.LinePattern; _ = summary.SourceArrow; _ = summary.TargetArrow;
        _ = new GraphPropertyPatch(LineWidth: 2, TargetArrow: EdgeArrow.None);
        using var painter = new EdgeStyleRenderer(); _ = painter.CachedDashEffects;
        return palette;
    }
}
