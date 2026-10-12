using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using VisualizeScene.Controls.Uno;

namespace VisualizeScene.Workbench;

public sealed partial class WorkbenchPage
{
    private GraphArrangeToolbar? arrangeToolbar;
    private readonly Dictionary<GraphArrangeCommand, MenuFlyoutItem> arrangeMenuItems = [];
    private FrameworkElement CreateArrangementTools()
    {
        arrangeToolbar = new GraphArrangeToolbar { Target = graph };
        Unloaded += (_, _) => arrangeToolbar?.Dispose();
        arrangeToolbar.Error += (_, error) => Report(error);
        arrangeToolbar.StateChanged += (_, _) => UpdateArrangeMenu();
        foreach (var command in Enum.GetValues<GraphArrangeCommand>())
            if (arrangeToolbar.FindButton(command) is { } button) Register("Arrange" + command, button);
        UpdateArrangeMenu();
        var tools = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 5 };
        tools.Children.Add(arrangeToolbar); tools.Children.Add(CreateOrderToolbar());
        var scroll = new ScrollViewer { Content = tools, Height = 25, HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled };
        return scroll;
    }
    private void AddArrangeCommands(MenuBarItem menu)
    {
        foreach (var command in Enum.GetValues<GraphArrangeCommand>())
        {
            if (command is GraphArrangeCommand.DistributeHorizontal or GraphArrangeCommand.SameWidth or GraphArrangeCommand.FitGroups) menu.Items.Add(new MenuFlyoutSeparator());
            var item = new MenuFlyoutItem { Text = GraphArrangeToolbar.Caption(command), IsEnabled = false };
            Register("MenuArrange" + command, item);
            item.Click += (_, _) => Run(() => { graph.ArrangeNodes(command); ReturnSelectionFocus(menu, item); });
            arrangeMenuItems.Add(command, item); menu.Items.Add(item);
        }
        UpdateArrangeMenu();
    }
    private void PublishArrangementState()
    {
#if __WASM__
        if (!DiagnosticsEnabled || arrangeToolbar is null) return;
        // Read-only native dropdown state lets tests wait for actual open/close rather than guessing animation delays.
        Uno.Foundation.WebAssemblyRuntime.InvokeJS($"globalThis.visualizeSceneLayout={{kind:{layoutKind.SelectedIndex},open:{(layoutKind.IsDropDownOpen ? "true" : "false")},direction:{direction.SelectedIndex},directionOpen:{(direction.IsDropDownOpen ? "true" : "false")}}};");
        var state = arrangeToolbar.State;
        var reference = state.ReferenceId is { } id ? "\"" + System.Text.Json.JsonEncodedText.Encode(id).ToString() + "\"" : "null";
        Uno.Foundation.WebAssemblyRuntime.InvokeJS($"globalThis.visualizeSceneArrange={{builds:{arrangeToolbar.StateBuilds},roots:{state.MovableRoots},reference:{reference}}};");
#endif
    }
    private void UpdateArrangeMenu()
    {
        foreach (var (command, item) in arrangeMenuItems)
            item.IsEnabled = arrangeToolbar is not null && GraphArrangeToolbar.CanExecute(command, arrangeToolbar.State);
    }
}
