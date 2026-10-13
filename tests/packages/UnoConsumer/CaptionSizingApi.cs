using VisualizeScene.Controls.Uno;

namespace PackageConsumer;

public static class CaptionSizingApi
{
    public static void Verify(GraphControl graph)
    {
        using var tools = new GraphArrangeToolbar { Target = graph };
        _ = tools.FindButton(GraphArrangeCommand.FitNodeText);
        _ = GraphArrangeToolbar.CanExecute(GraphArrangeCommand.FitNodeText, tools.State);
        _ = graph.CaptionMeasurementBuilds; _ = graph.CachedCaptionMeasurements;
        graph.ArrangeNodes(GraphArrangeCommand.FitNodeText);
    }
}
