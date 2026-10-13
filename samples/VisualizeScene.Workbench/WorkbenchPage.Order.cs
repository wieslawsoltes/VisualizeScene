using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using VisualizeScene.Controls.Uno;
using VisualizeScene.Editing;

namespace VisualizeScene.Workbench;

public sealed partial class WorkbenchPage
{
    private GraphOrderToolbar? orderToolbar;
    private void AddDrawingOrderMenu(MenuBar menu)
    {
        var arrange = menu.Items.First(item => item.Title == "Arrange"); arrange.Items.Add(new MenuFlyoutSeparator());
        foreach (var operation in Enum.GetValues<DrawingOrder>())
        {
            var item = new MenuFlyoutItem { Text = GraphOrderToolbar.Caption(operation) };
            Register("MenuOrder" + operation, item);
            item.Click += (_, _) => Run(() => { graph.ChangeDrawingOrder(operation); ReturnSelectionFocus(arrange, item); }); arrange.Items.Add(item);
        }
    }
    private Border BuildRightRail()
    {
        var rows = new Grid(); rows.RowDefinitions.Add(new() { Height = new(1, GridUnitType.Star) }); rows.RowDefinitions.Add(new() { Height = GridLength.Auto });
        rows.Children.Add(new EditorPane { Title = "Properties and tools", PaneContent = BuildRight() });
        overview.Height = 140; Register("Overview", overview);
        overviewPane = new EditorPane { Title = "Overview", PaneContent = overview, Margin = new(0, 4, 0, 0) };
        Register("OverviewCollapse", overviewPane.CollapseButton); Grid.SetRow(overviewPane, 1); rows.Children.Add(overviewPane);
        return new Border { Child = rows, Background = Brush("#F2F3F5") };
    }
    private GraphOrderToolbar CreateOrderToolbar()
    {
        var toolbar = new GraphOrderToolbar { Target = graph, HorizontalAlignment = HorizontalAlignment.Right };
        toolbar.Error += (_, error) => Report(error);
        foreach (var operation in Enum.GetValues<DrawingOrder>())
            if (toolbar.FindButton(operation) is { } button) Register("Order" + operation, button);
        orderToolbar = toolbar; return toolbar;
    }
}
