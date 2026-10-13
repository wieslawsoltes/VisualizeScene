using VisualizeScene.Controls.Uno;
using VisualizeScene.Editing;

namespace PackageConsumer;

public static class GroupFoldingApi
{
    public static void Verify(GraphControl graph)
    {
        graph.FoldGroups(GroupFoldOperation.Expand);
        graph.FoldGroups(GroupFoldOperation.Collapse);
        _ = graph.Session.LastGroupFolding;
    }
}
