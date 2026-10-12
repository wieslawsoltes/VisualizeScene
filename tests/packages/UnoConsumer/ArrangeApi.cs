using VisualizeScene.Controls.Uno;
using VisualizeScene.Editing;

namespace PackageConsumer;

/// <summary>Compile-only reusable arrangement controls in both isolated Uno package consumers.</summary>
public static class ArrangeApi
{
    public static GraphArrangeToolbar Create(GraphControl graph)
    {
        var toolbar = new GraphArrangeToolbar { Target = graph };
        _ = toolbar.FindButton(GraphArrangeCommand.SameSize); _ = toolbar.StateBuilds;
        _ = GraphArrangeToolbar.CanExecute(GraphArrangeCommand.AlignLeft, toolbar.State);
        var state = GraphArrangeState.Calculate(graph.Document.Snapshot, graph.Session.Selection, graph.Session.FocusedItemId);
        _ = GraphNodeSizing.Calculate(graph.Document.Snapshot, graph.Session.Selection, NodeSizeMatch.Width, state.ReferenceId);
        graph.ArrangeNodes(GraphArrangeCommand.SameWidth);
        return toolbar;
    }
}
