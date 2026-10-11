using Microsoft.UI.Xaml;
using VisualizeScene.Controls.Uno;
using VisualizeScene.Editing;

namespace PackageConsumer;

/// <summary>Compile-only coverage of new control APIs through the packaged assembly; no workbench integration.</summary>
public static class WorkspaceApi
{
    public static EditorWorkspace Create(GraphControl graph)
    {
        var structure = new GraphStructureView { Target = graph };
        var palette = new NodePalette { Target = graph, UseTiles = true };
        var pane = new EditorPane { Title = "Graph tools", PaneContent = palette };
        _ = palette.GetTemplateButton(0); _ = structure.VisibleRowCount;
        _ = new GraphStructureIndex(graph.Document.Snapshot).GetRows();
        var properties = new EditorPropertyGrid(); properties.AddGroup("Appearance");
        var color = new EditorColorField { Value = "#FFCC66" }; properties.AddProperty("Fill", color);
        properties.SetGroupExpanded("Appearance", false); properties.Clear();
        color.IsMixed = true;
        using var inspector = new GraphPropertyInspector { Target = graph };
        _ = inspector.BatchItemCount; _ = inspector.BatchExaminedItems;
        _ = graph.IsContextMenuOpen; _ = graph.FindContextCommand("Delete");
        var layout = new EditorWorkspace { LeftContent = pane, RightContent = structure, LeftPaneWidth = 230, RightPaneWidth = 300 };
        _ = layout.LeftResizeGrip; _ = new EditorIcon { Kind = EditorIconKind.Copy };
        layout.ResetPanes();
        layout.Unloaded += (_, _) => structure.Dispose();
        return layout;
    }
}
