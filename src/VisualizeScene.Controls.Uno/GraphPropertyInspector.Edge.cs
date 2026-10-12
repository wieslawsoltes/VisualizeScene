using VisualizeScene.Core;

namespace VisualizeScene.Controls.Uno;

public sealed partial class GraphPropertyInspector
{
    private Func<EdgeStyle?> EdgeAppearance(GraphEdge edge)
    {
        var original = edge.Style ?? EdgeStyle.Default;
        fields.AddGroup("Line style");
        var width = Text("LineWidth", "Line width", F(original.Width));
        var dash = Choice("LinePattern", original.Dash);
        var source = Choice("SourceArrow", original.SourceArrow);
        var targetArrow = Choice("TargetArrow", original.TargetArrow);
        // Returning unchanged values preserves legacy null-style defaults and creates no spurious history.
        return () =>
        {
            var next = new EdgeStyle(N(width), (EdgeDash)dash.SelectedItem, (EdgeArrow)source.SelectedItem, (EdgeArrow)targetArrow.SelectedItem);
            next.Validate(); return next == original ? edge.Style : next;
        };
    }
}
