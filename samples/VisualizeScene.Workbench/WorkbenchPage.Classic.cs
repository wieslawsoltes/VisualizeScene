using System.Globalization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using VisualizeScene.Controls.Uno;
using VisualizeScene.Editing;

namespace VisualizeScene.Workbench;

// Classic graph-editor chrome built from reusable Uno controls.
public sealed partial class WorkbenchPage
{
    private readonly TextBlock documentCaption = new() { FontSize = 12, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly ComboBox zoomLevels = new() { ItemsSource = new[] { "25%", "50%", "75%", "100%", "150%", "200%", "400%" }, Width = 88, MinHeight = 26, FontSize = 12 };
    private readonly Dictionary<EditorMode, Button> modeButtons = [];
    private EditorPane? palettePane, overviewPane;
    private EditorMode? paintedMode;
    private string paintedDocument = "";
    private double paintedZoom = -1;
    private bool updatingZoom;
    private Button IconButton(EditorIconKind icon, string id, string label, Action action)
    {
        var button = new Button { Content = new EditorIcon { Kind = icon }, Padding = new(4), MinWidth = 28, MinHeight = 28,
            Background = Brush("#F8F8F8"), BorderThickness = new(1), BorderBrush = Brush("#D9DDE1"), CornerRadius = new(1) };
        AutomationProperties.SetName(button, label); ToolTipService.SetToolTip(button, label); Register(id, button);
        button.Click += (_, _) => Run(action); return button;
    }
    private FrameworkElement BuildToolbar()
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2, Padding = new(5, 3, 5, 3) };
        void Add(EditorIconKind kind, string id, string label, Action action) => row.Children.Add(IconButton(kind, id, label, action));
        Add(EditorIconKind.New, "New", "New graph", () => _ = NewGraph()); Add(EditorIconKind.Open, "Open", "Open graph", () => _ = RunAsync(OpenFile)); Add(EditorIconKind.Save, "Save", "Save graph", () => _ = Save("json"));
        row.Children.Add(Separator()); Add(EditorIconKind.Undo, "Undo", "Undo · Ctrl+Z", document.Undo); Add(EditorIconKind.Redo, "Redo", "Redo · Ctrl+Y", document.Redo);
        row.Children.Add(Separator()); Add(EditorIconKind.Cut, "Cut", "Cut · Ctrl+X", () => { graph.Session.Copy(); graph.Session.DeleteSelection(); });
        Add(EditorIconKind.Copy, "Copy", "Copy · Ctrl+C", () => graph.Session.Copy()); Add(EditorIconKind.Paste, "Paste", "Paste · Ctrl+V", () => graph.Session.Paste()); Add(EditorIconKind.Delete, "Delete", "Delete selection", graph.Session.DeleteSelection);
        row.Children.Add(Separator());
        foreach (var (mode, icon, id, text) in new[] { (EditorMode.Select, EditorIconKind.Select, "ModeSelect", "Select"), (EditorMode.CreateNode, EditorIconKind.Node, "ModeNode", "Create node"), (EditorMode.CreateEdge, EditorIconKind.Edge, "ModeEdge", "Create edge"), (EditorMode.Pan, EditorIconKind.Pan, "ModePan", "Pan") })
        { var button = IconButton(icon, id, text, () => SetMode(mode)); modeButtons.Add(mode, button); row.Children.Add(button); }
        row.Children.Add(Separator()); Add(EditorIconKind.Group, "Group", "Group selection · Ctrl+G", graph.Session.Group); Add(EditorIconKind.Fold, "Fold", "Fold / unfold groups", FoldSelected);
        row.Children.Add(Separator()); Add(EditorIconKind.ZoomOut, "ZoomOut", "Zoom out", () => graph.Zoom(.8)); Add(EditorIconKind.Fit, "Fit", "Fit content · Home", graph.Fit); Add(EditorIconKind.ZoomIn, "ZoomIn", "Zoom in", () => graph.Zoom(1.25));
        Register("ZoomLevels", zoomLevels); row.Children.Add(zoomLevels);
        zoomLevels.SelectionChanged += (_, _) =>
        {
            if (updatingZoom || zoomLevels.SelectedItem is not string value) return;
            graph.Zoom(double.Parse(value.TrimEnd('%'), CultureInfo.InvariantCulture) / 100 / graph.Session.Viewport.Zoom); graph.Focus(FocusState.Programmatic);
        };
        row.Children.Add(Separator()); Add(EditorIconKind.Layout, "QuickLayout", "Apply chosen layout", () => _ = ApplyLayout()); Add(EditorIconKind.Search, "QuickFind", "Find graph text · Ctrl+F", () => search.Open());
        row.Children.Add(Separator()); samples.MinWidth = 180; samples.MinHeight = 26; Register("Samples", samples); row.Children.Add(samples);
        return new ScrollViewer { Content = row, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled, Background = Brush("#F0F1F2") };
    }
    private Border BuildLeft()
    {
        var root = new Grid(); var topRow = new RowDefinition { Height = new(1, GridUnitType.Star) }; root.RowDefinitions.Add(topRow); root.RowDefinitions.Add(new() { Height = GridLength.Auto });
        var body = new Grid(); body.RowDefinitions.Add(new() { Height = new(28) }); body.RowDefinitions.Add(new() { Height = new(1, GridUnitType.Star) });
        var tabs = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 1, Background = Brush("#F0F1F2") };
        var content = new ContentControl { HorizontalContentAlignment = HorizontalAlignment.Stretch, VerticalContentAlignment = VerticalAlignment.Stretch, IsTabStop = false };
        var palette = new NodePalette { Target = graph, UseTiles = true, Margin = new(4) };
        palette.SetItems(palette.Items.Select(item => item with { Fill = "#FFCC66", Stroke = "#A87523" }));
        palette.TemplateSelected += (_, template) => { status.Text = "Click the diagram to place " + template.Name + ". Escape returns to selection."; UpdateChrome(); PublishTestState(); };
        for (var i = 0; i < palette.Items.Count; i++) if (palette.GetTemplateButton(i) is { } button) Register("Template" + i, button);
        var catalog = new StackPanel { Spacing = 6, Padding = new(5) };
        var flowchart = new EditorPane { Title = "Flowchart", PaneContent = palette }; Register("FlowchartCollapse", flowchart.CollapseButton); catalog.Children.Add(flowchart);
        var edgeTools = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 5, Padding = new(7) };
        edgeTools.Children.Add(IconButton(EditorIconKind.Edge, "PaletteEdge", "Draw connection", () => SetMode(EditorMode.CreateEdge)));
        edgeTools.Children.Add(Button("Route edges", "PaletteRoute", () => _ = RouteEdges())); catalog.Children.Add(new EditorPane { Title = "Connections", PaneContent = edgeTools });
        var groupTools = new StackPanel { Spacing = 5, Padding = new(6) };
        groupTools.Children.Add(Button("Group selected nodes", "PaletteGroup", graph.Session.Group)); groupTools.Children.Add(Button("Ungroup", "PaletteUngroup", graph.Session.Ungroup)); catalog.Children.Add(new EditorPane { Title = "Groups", PaneContent = groupTools });
        catalog.Children.Add(new TextBlock { Text = "Choose a shape, then click the diagram.\nDrag nodes to move. Double-click to edit.", FontSize = 10, TextWrapping = TextWrapping.Wrap, Margin = new(5), Foreground = Brush("#536171") });
        var paletteScroll = new ScrollViewer { Content = catalog, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        Button? paletteTab = null, structureTab = null;
        void SelectTab(bool outline)
        {
            content.Content = outline ? structure : paletteScroll;
            if (paletteTab is not null) paletteTab.Background = Brush(outline ? "#E9ECF0" : "#FFFFFF");
            if (structureTab is not null) structureTab.Background = Brush(outline ? "#FFFFFF" : "#E9ECF0"); PublishTestState();
        }
        paletteTab = Button("Palette", "TabPalette", () => SelectTab(false)); structureTab = Button("Structure", "TabStructure", () => SelectTab(true));
        tabs.Children.Add(paletteTab); tabs.Children.Add(structureTab); body.Children.Add(tabs); Grid.SetRow(content, 1); body.Children.Add(content); SelectTab(false);
        palettePane = new EditorPane { Title = "Graph tools", PaneContent = body }; Register("LeftPaneCollapse", palettePane.CollapseButton); root.Children.Add(palettePane);
        palettePane.CollapsedChanged += (_, _) => topRow.Height = palettePane.IsCollapsed ? GridLength.Auto : new(1, GridUnitType.Star);
        overview.Height = 170; Register("Overview", overview); overviewPane = new EditorPane { Title = "Overview", PaneContent = overview, Margin = new(0, 4, 0, 0) };
        Register("OverviewCollapse", overviewPane.CollapseButton); Grid.SetRow(overviewPane, 1); root.Children.Add(overviewPane);
        return new Border { Child = root, Background = Brush("#F2F3F5") };
    }
    private FrameworkElement BuildDocumentHeader()
    {
        var row = new Grid { Height = 27, Background = Brush("#E8ECF1"), Padding = new(7, 0, 7, 0) };
        row.ColumnDefinitions.Add(new() { Width = GridLength.Auto }); row.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
        row.Children.Add(new EditorIcon { Kind = EditorIconKind.Layout, Margin = new(0, 0, 7, 0) }); Grid.SetColumn(documentCaption, 1); row.Children.Add(documentCaption); return row;
    }
    private void AddWorkspaceMenu(MenuBar bar)
    {
        var view = bar.Items.First(item => item.Title == "View"); view.Items.Add(new MenuFlyoutSeparator());
        void Item(string text, string id, Action action) { var item = new MenuFlyoutItem { Text = text }; Register(id, item); item.Click += (_, _) => Run(action); view.Items.Add(item); }
        Item("Show / hide palette pane", "ToggleLeftPane", () => workspace.ShowLeftPane = !workspace.ShowLeftPane);
        Item("Show / hide properties pane", "ToggleRightPane", () => workspace.ShowRightPane = !workspace.ShowRightPane);
        Item("Reset workspace", "ResetWorkspace", () => { workspace.ResetPanes(); if (palettePane is not null) palettePane.IsCollapsed = false; if (overviewPane is not null) overviewPane.IsCollapsed = false; });
    }
    private void UpdateChrome()
    {
        if (paintedMode != graph.Session.Mode)
        {
            paintedMode = graph.Session.Mode;
            foreach (var pair in modeButtons) { pair.Value.Background = Brush(pair.Key == paintedMode ? "#D6E8FB" : "#F8F8F8"); pair.Value.BorderBrush = Brush(pair.Key == paintedMode ? "#6599C9" : "#D9DDE1"); }
        }
        if (testControls.GetValueOrDefault("Undo") is Control undo) undo.IsEnabled = document.CanUndo;
        if (testControls.GetValueOrDefault("Redo") is Control redo) redo.IsEnabled = document.CanRedo;
        foreach (var id in new[] { "Cut", "Copy", "Delete" }) if (testControls.GetValueOrDefault(id) is Control button) button.IsEnabled = graph.Session.Selection.Count > 0;
        var title = currentName + (dirty ? " *" : ""); if (paintedDocument != title) { paintedDocument = title; documentCaption.Text = title; }
        var zoom = graph.Session.Viewport.Zoom;
        if (paintedZoom != zoom)
        {
            paintedZoom = zoom; updatingZoom = true; var value = (zoom * 100).ToString("0", CultureInfo.InvariantCulture) + "%";
            zoomLevels.SelectedItem = Math.Abs(zoom * 100 - Math.Round(zoom * 100)) < .001 && ((string[])zoomLevels.ItemsSource).Contains(value) ? value : null;
            zoomLevels.PlaceholderText = $"{zoom * 100:0.#}%"; updatingZoom = false;
        }
    }
}
