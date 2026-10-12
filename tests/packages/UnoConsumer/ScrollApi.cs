using VisualizeScene.Controls.Uno;
using VisualizeScene.Core;

namespace PackageConsumer;

/// <summary>Package-only compile coverage on desktop and browser targets; the browser integration suite supplies execution coverage.</summary>
public static class ScrollApi
{
    public static GraphScrollViewer Create(GraphControl unparentedGraph)
    {
        var viewer = new GraphScrollViewer { Target = unparentedGraph };
        var model = ViewportScrollModel.Calculate(unparentedGraph.ContentBounds, unparentedGraph.Session.Viewport.State, 800, 600);
        _ = model.Horizontal.GetOrigin(.5); _ = viewer.HorizontalBar; _ = viewer.VerticalBar; _ = viewer.FitButton;
        _ = viewer.FindThumb(true); viewer.ScrollBarsVisible = false; viewer.ScrollBarsVisible = true;
        _ = unparentedGraph.FitSelection();
        _ = GraphScene.BoundsForItems(unparentedGraph.Document.Snapshot, unparentedGraph.Session.Selection, unparentedGraph.MeasureLabel);
        return viewer;
    }
}
