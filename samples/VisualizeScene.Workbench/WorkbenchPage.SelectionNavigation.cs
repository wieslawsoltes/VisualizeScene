using Microsoft.UI.Xaml.Controls;

namespace VisualizeScene.Workbench;

public sealed partial class WorkbenchPage
{
    private void AddSelectionNavigation(MenuBar menu)
    {
        var command = new MenuFlyoutItem { Text = "Zoom to selection" };
        Register("ZoomSelection", command);
        void Update() => command.IsEnabled = graph.Session.Selection.Count > 0;
        graph.SelectionChanged += (_, _) => Update();
        command.Click += (_, _) => Run(() => graph.FitSelection());
        menu.Items.First(item => item.Title == "View").Items.Add(command);
        Update();
    }
}
