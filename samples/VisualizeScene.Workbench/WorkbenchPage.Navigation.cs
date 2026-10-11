using Microsoft.UI.Xaml.Controls;
using VisualizeScene.Controls.Uno;
using VisualizeScene.Editing;

namespace VisualizeScene.Workbench;

public sealed partial class WorkbenchPage
{
    private void AddNavigationMenu(MenuBarItem view)
    {
        var navigation = new MenuFlyoutSubItem { Text = "Keyboard navigation" };
        Register("NavigationMenu", navigation); view.Items.Add(navigation);
        void Item(string title, string id, Action action)
        {
            var item = new MenuFlyoutItem { Text = title }; Register(id, item); navigation.Items.Add(item);
            item.Click += (_, _) => Run(() => { action(); ReturnSelectionFocus(view, item); PublishTestState(); });
        }
        foreach (var direction in Enum.GetValues<NavigationDirection>())
            Item("Navigate " + direction.ToString().ToLowerInvariant() + "  Ctrl+Arrow", "Navigate" + direction, () => graph.Navigate(direction));
        Item("First item  Ctrl+Home", "NavigateFirst", () => graph.NavigateBoundary());
        Item("Last item  Ctrl+End", "NavigateLast", () => graph.NavigateBoundary(true));
        Item("Reveal focused item", "RevealFocused", graph.EnsureFocusedItemVisible);
        var touch = new MenuFlyoutSubItem { Text = "Touch input" }; Register("TouchMenu", touch); view.Items.Add(touch);
        var items = new Dictionary<GraphTouchMode, ToggleMenuFlyoutItem>();
        foreach (var mode in Enum.GetValues<GraphTouchMode>())
        {
            var title = mode switch
            {
                GraphTouchMode.Edit => "One finger edits; two fingers navigate",
                GraphTouchMode.Disabled => "Disable graph touch input",
                _ => "One finger pans; tap selects; pinch zooms"
            };
            var item = new ToggleMenuFlyoutItem { Text = title, IsChecked = graph.TouchMode == mode };
            Register("Touch" + mode, item); items[mode] = item; touch.Items.Add(item);
            item.Click += (_, _) => Run(() =>
            {
                graph.TouchMode = mode;
                foreach (var pair in items) pair.Value.IsChecked = pair.Key == mode;
                ReturnSelectionFocus(view, item); PublishTestState();
            });
        }
    }
}
