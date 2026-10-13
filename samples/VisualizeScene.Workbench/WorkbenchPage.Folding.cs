using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using VisualizeScene.Editing;

namespace VisualizeScene.Workbench;

public sealed partial class WorkbenchPage
{
    private void AddGroupFoldingMenu(MenuBarItem menu)
    {
        foreach (var operation in new[] { GroupFoldOperation.Expand, GroupFoldOperation.Collapse })
        {
            var id = operation == GroupFoldOperation.Expand ? "ExpandSelectedGroups" : "CollapseSelectedGroups";
            var item = new MenuFlyoutItem { Text = operation + " selected groups", IsEnabled = false };
            Register(id, item);
            item.Click += (_, _) => Run(() => { graph.FoldGroups(operation); ReturnSelectionFocus(menu, item); });
            menu.Items.Add(item);
        }
    }
    private void UpdateGroupFoldingAvailability()
    {
        var state = arrangeToolbar?.State ?? default;
        if (testControls.GetValueOrDefault("ExpandSelectedGroups") is Control expand) expand.IsEnabled = state.ExpandableGroups > 0;
        if (testControls.GetValueOrDefault("CollapseSelectedGroups") is Control collapse) collapse.IsEnabled = state.CollapsibleGroups > 0;
        if (testControls.GetValueOrDefault("Fold") is Control toggle) toggle.IsEnabled = state.ExpandableGroups + state.CollapsibleGroups > 0;
    }
}
