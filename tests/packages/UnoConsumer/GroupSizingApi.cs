using VisualizeScene.Controls.Uno;
using VisualizeScene.Editing;

namespace PackageConsumer;

/// <summary>Compile the group command surface from NuGet on both Uno targets. Browser suites execute it through input.</summary>
public static class GroupSizingApi
{
    public static void Verify(GraphControl graph, GraphArrangeToolbar toolbar)
    {
        _ = toolbar.FindButton(GraphArrangeCommand.FitGroups);
        _ = GraphArrangeToolbar.CanExecute(GraphArrangeCommand.FitGroups, toolbar.State);
        _ = new GroupSizingOptions(Padding: 12, HeaderHeight: 28, EnlargeOnly: true);
        graph.ArrangeNodes(GraphArrangeCommand.FitGroups);
    }
}
