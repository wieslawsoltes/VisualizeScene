using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using VisualizeScene.Controls.Uno;
using VisualizeScene.Editing;
using VisualizeScene.Layout;

namespace VisualizeScene.Workbench;

public sealed partial class WorkbenchPage
{
    private static SolidColorBrush Brush(string hex) => new(Windows.UI.Color.FromArgb(255, Convert.ToByte(hex.Substring(1, 2), 16), Convert.ToByte(hex.Substring(3, 2), 16), Convert.ToByte(hex.Substring(5, 2), 16)));
    private void Register(string id, FrameworkElement element) { AutomationProperties.SetAutomationId(element, id); testControls[id] = element; RegisterMenuDiagnostics(id, element); }
    private Button Button(string label, string id, Action action, string? tooltip = null)
    {
        var button = new Button { Content = label, FontSize = 12, Padding = new(7, 3, 7, 3), MinHeight = 27 };
        Register(id, button); if (tooltip is not null) ToolTipService.SetToolTip(button, tooltip); button.Click += (_, _) => Run(action); return button;
    }
    private MenuBar BuildMenu()
    {
        var bar = new MenuBar { Background = Brush("#F6F7F9"), Padding = new(2, 0, 2, 0) };
        MenuBarItem Menu(string text) { var menu = new MenuBarItem { Title = text }; Register("Menu" + text, menu); bar.Items.Add(menu); return menu; }
        void Item(MenuBarItem menu, string text, Action action, string? id = null)
        {
            var item = new MenuFlyoutItem { Text = text }; if (id is not null) Register(id, item); item.Click += (_, _) => Run(action); menu.Items.Add(item);
        }
        var file = Menu("File"); Item(file, "New graph", () => _ = NewGraph()); Item(file, "Open JSON / GraphML…", () => _ = RunAsync(OpenFile)); Item(file, "Import from text…", () => _ = TextExchange(true), "FileImportText");
        file.Items.Add(new MenuFlyoutSeparator()); Item(file, "Save JSON…", () => _ = Save("json"), "SaveJson"); Item(file, "Save GraphML…", () => _ = Save("graphml"), "SaveGraphML"); Item(file, "Export SVG…", () => _ = Save("svg"), "ExportSvg"); Item(file, "Export PNG…", () => _ = Save("png"), "ExportPng"); Item(file, "View document source…", () => _ = TextExchange(false), "ViewSource");
        var edit = Menu("Edit"); Item(edit, "Undo  Ctrl+Z", document.Undo); Item(edit, "Redo  Ctrl+Y", document.Redo); edit.Items.Add(new MenuFlyoutSeparator());
        Item(edit, "Copy  Ctrl+C", () => graph.Session.Copy()); Item(edit, "Paste  Ctrl+V", () => graph.Session.Paste()); Item(edit, "Duplicate  Ctrl+D", graph.Session.Duplicate); Item(edit, "Delete  Del", graph.Session.DeleteSelection); Item(edit, "Select all  Ctrl+A", graph.Session.SelectAll); Item(edit, "Edit label  F2", () => _ = graph.EditLabelAsync());
        Item(edit, "Add independent label", graph.AddLabelToSelection, "AddLabel");
        Item(edit, "Add port to selected node", () =>
        {
            var index = document.Snapshot.Index(); var id = graph.Session.Selection.FirstOrDefault(index.Nodes.ContainsKey);
            if (id is null) { status.Text = "Select a node first."; return; }
            var node = index.Nodes[id]; graph.Session.AddPort(id, new(node.Bounds.Right, node.Bounds.Center.Y));
        }, "AddPort");
        var view = Menu("View"); Item(view, "Fit graph  Home", graph.Fit); Item(view, "Zoom in", () => graph.Zoom(1.25)); Item(view, "Zoom out", () => graph.Zoom(.8)); Item(view, "Toggle grid", () => { graph.Session.ShowGrid = !graph.Session.ShowGrid; graph.Invalidate(); }); Item(view, "Toggle grid snapping", () => graph.Session.SnapToGrid = !graph.Session.SnapToGrid, "ToggleGridSnap");
        var snapItems = new ToggleMenuFlyoutItem { Text = "Alignment guides", IsChecked = graph.Session.SnapToItems };
        Register("ToggleItemSnap", snapItems); snapItems.Click += (_, _) => graph.Session.SnapToItems = snapItems.IsChecked; view.Items.Add(snapItems);
        AddNavigationMenu(view);
        var arrange = Menu("Arrange"); Item(arrange, "Group  Ctrl+G", graph.Session.Group); Item(arrange, "Ungroup  Ctrl+Shift+G", graph.Session.Ungroup); Item(arrange, "Fold / unfold selected groups", FoldSelected);
        arrange.Items.Add(new MenuFlyoutSeparator()); AddArrangeCommands(arrange);
        var layout = Menu("Layout"); foreach (var kind in Enum.GetValues<LayoutKind>()) Item(layout, kind.ToString(), () => { layoutKind.SelectedItem = kind; _ = ApplyLayout(); }, "Layout" + kind); Item(layout, "Route edges around obstacles", () => _ = RouteEdges()); Item(layout, "Place independent labels", () => _ = PlaceLabels(), "MenuPlaceLabels");
        AddSelectionMenu(bar);
        var help = Menu("Help"); Item(help, "Editing guide", () => _ = ShowHelp()); Item(help, "Implementation status", () => _ = ShowStatus()); return bar;
    }
    private static Border Separator() => new() { Width = 1, Height = 25, Margin = new(4, 0, 4, 0), Background = Brush("#C8D0D9"), VerticalAlignment = VerticalAlignment.Center };
    private static TextBlock Heading(string text) => new() { Text = text, FontSize = 12, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Margin = new(0, 0, 0, 8) };
    private Border BuildRight()
    {
        var root = new Grid(); root.RowDefinitions.Add(new() { Height = new(30) }); root.RowDefinitions.Add(new() { Height = new(1, GridUnitType.Star) });
        var header = new Grid { Padding = new(5, 4, 5, 3), Background = Brush("#ECF0F4") };
        for (var i = 0; i < 3; i++) header.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
        var content = new ContentControl { HorizontalContentAlignment = HorizontalAlignment.Stretch, VerticalContentAlignment = VerticalAlignment.Stretch, Padding = new(7) }; Grid.SetRow(content, 1); root.Children.Add(header); root.Children.Add(content);
        var layoutPanel = new StackPanel { Spacing = 6 };
        // Separate labels from the dropdown's hit region so its center cannot target a header boundary.
        layoutKind.Header = direction.Header = null;
        layoutPanel.Children.Add(new TextBlock { Text = "Layout algorithm", FontSize = 12 });
        AutomationProperties.SetName(layoutKind, "Layout algorithm"); Register("LayoutKind", layoutKind); layoutPanel.Children.Add(layoutKind);
        layoutPanel.Children.Add(new TextBlock { Text = "Direction", FontSize = 12 });
        AutomationProperties.SetName(direction, "Layout direction"); Register("LayoutDirection", direction); layoutPanel.Children.Add(direction);
        layoutPanel.Children.Add(spacing); layoutPanel.Children.Add(layers);
        layoutPanel.Children.Add(Button("Apply layout", "ApplyLayout", () => _ = ApplyLayout())); layoutPanel.Children.Add(Button("Route edges", "RouteEdges", () => _ = RouteEdges())); layoutPanel.Children.Add(Button("Cancel operation", "CancelLayout", () => layoutCancellation?.Cancel()));
        layoutPanel.Children.Add(new TextBlock { Text = "Layouts use an immutable snapshot, commit once and can be undone. Stale results are rejected.", TextWrapping = TextWrapping.Wrap, FontSize = 11, Foreground = Brush("#657688") });
        AddLabelPlacementControls(layoutPanel);
        var analysis = new StackPanel { Spacing = 8 };
        foreach (var name in new[] { "Connected components", "Strong components", "Shortest path", "Minimum spanning forest", "PageRank", "Betweenness", "Color graph", "Maximum flow" }) analysis.Children.Add(Button(name, "Analyze" + name.Replace(" ", ""), () => Analyze(name)));
        analysis.Children.Add(new Border { Height = 1, Background = Brush("#CDD5DE"), Margin = new(0, 8, 0, 8) }); analysis.Children.Add(analysisOutput);
        UIElement[] panels = [inspector, new ScrollViewer { Content = layoutPanel, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled }, new ScrollViewer { Content = analysis, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled }];
        var buttons = new List<Button>(); string[] labels = ["Properties", "Layout", "Analysis"];
        void Select(int index)
        {
            content.Content = panels[index];
            for (var i = 0; i < buttons.Count; i++) { buttons[i].Background = Brush(i == index ? "#DDE9F7" : "#F6F8FA"); buttons[i].BorderBrush = Brush(i == index ? "#85AED9" : "#D2D9E1"); }
            PublishTestState();
        }
        for (var i = 0; i < labels.Length; i++)
        {
            var index = i; var tab = Button(labels[i], "Tab" + labels[i], () => Select(index)); tab.HorizontalAlignment = HorizontalAlignment.Stretch; tab.Padding = new(4, 3, 4, 3); tab.Margin = new(1, 0, 1, 0); Grid.SetColumn(tab, i); header.Children.Add(tab); buttons.Add(tab);
        }
        Select(0); return new Border { Child = root, Background = Brush("#F7F8FA") };
    }
}
