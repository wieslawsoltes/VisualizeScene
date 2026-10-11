using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using VisualizeScene.Editing;

namespace VisualizeScene.Workbench;

public sealed partial class WorkbenchPage
{
    private void AddSelectionMenu(MenuBar bar)
    {
        var menu = new MenuBarItem { Title = "Selection" }; Register("MenuSelection", menu); bar.Items.Add(menu);
        void Item(string text, string id, Action action)
        {
            var item = new MenuFlyoutItem { Text = text }; Register(id, item);
            item.Click += (_, _) => Run(() => { action(); graph.Focus(FocusState.Programmatic); PublishTestState(); });
            menu.Items.Add(item);
        }
        Item("Select all  Ctrl+A", "SelectAllItems", graph.Session.SelectAll);
        foreach (var kind in new[] { SelectionKinds.Nodes, SelectionKinds.Edges, SelectionKinds.Ports, SelectionKinds.Labels, SelectionKinds.Groups })
            Item("Select " + kind.ToString().ToLowerInvariant(), "Select" + kind, () => graph.Session.SelectAll(kind));
        Item("Invert selection  Ctrl+I", "InvertSelection", graph.Session.InvertSelection);
        Item("Clear selection  Ctrl+Shift+A", "ClearSelection", graph.Session.ClearSelection);
        menu.Items.Add(new MenuFlyoutSeparator());
        foreach (var direction in Enum.GetValues<NeighborDirection>())
            Item("Expand " + direction.ToString().ToLowerInvariant() + " neighbors", "SelectNeighbors" + direction, () => graph.Session.SelectNeighbors(direction));
        menu.Items.Add(new MenuFlyoutSeparator());
        var modes = new Dictionary<AreaSelectionMode, ToggleMenuFlyoutItem>();
        foreach (var mode in Enum.GetValues<AreaSelectionMode>())
        {
            var text = mode switch { AreaSelectionMode.Contains => "Marquee: fully contained", AreaSelectionMode.Directional => "Marquee: directional window / crossing", _ => "Marquee: intersecting" };
            var item = new ToggleMenuFlyoutItem { Text = text, IsChecked = graph.Session.MarqueeMode == mode };
            Register("Marquee" + mode, item); modes[mode] = item; menu.Items.Add(item);
            item.Click += (_, _) =>
            {
                graph.Session.MarqueeMode = mode;
                foreach (var pair in modes) pair.Value.IsChecked = pair.Key == mode;
                graph.Focus(FocusState.Programmatic); PublishTestState();
            };
        }
        menu.Items.Add(new MenuFlyoutSeparator());
        var filters = new Dictionary<SelectionKinds, ToggleMenuFlyoutItem>();
        foreach (var (kinds, text, id) in new[]
        {
            (SelectionKinds.All & ~SelectionKinds.Groups, "Marquee: nodes, edges, ports and labels", "MarqueeAllKinds"),
            (SelectionKinds.Nodes, "Marquee: nodes only", "MarqueeNodesOnly"),
            (SelectionKinds.Labels, "Marquee: independent labels only", "MarqueeLabelsOnly")
        })
        {
            var item = new ToggleMenuFlyoutItem { Text = text, IsChecked = graph.Session.MarqueeItemKinds == kinds };
            Register(id, item); filters[kinds] = item; menu.Items.Add(item);
            item.Click += (_, _) =>
            {
                graph.Session.MarqueeItemKinds = kinds;
                foreach (var pair in filters) pair.Value.IsChecked = pair.Key == kinds;
                graph.Focus(FocusState.Programmatic); PublishTestState();
            };
        }
    }
}
