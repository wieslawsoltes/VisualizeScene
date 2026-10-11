using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using VisualizeScene.Controls.Uno;
using VisualizeScene.Core;
using VisualizeScene.Editing;
using VisualizeScene.Layout;

namespace VisualizeScene.Workbench;

public sealed partial class WorkbenchPage : Page
{
    private readonly GraphDocument document = new();
    private readonly GraphControl graph = new();
    private readonly GraphOverview overview = new();
    private readonly GraphSearchControl search = new();
    private readonly GraphPropertyInspector inspector = new();
    private readonly TextBlock status = new() { Text = "Ready", FontSize = 11, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly TextBlock counts = new() { FontSize = 11, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock modeText = new() { Text = "Select", FontSize = 11, VerticalAlignment = VerticalAlignment.Center };
    private readonly ComboBox samples = new() { ItemsSource = SampleGraphs.Names, SelectedIndex = 0, MinWidth = 190, FontSize = 12 };
    private readonly ComboBox layoutKind = new() { ItemsSource = Enum.GetValues<LayoutKind>(), SelectedItem = LayoutKind.Hierarchical, HorizontalAlignment = HorizontalAlignment.Stretch, FontSize = 12 };
    private readonly ComboBox direction = new() { ItemsSource = Enum.GetValues<LayoutDirection>(), SelectedItem = LayoutDirection.TopToBottom, HorizontalAlignment = HorizontalAlignment.Stretch, FontSize = 12 };
    private readonly NumberBox spacing = new() { Header = "Node spacing", Value = 45, Minimum = 5, Maximum = 500, SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact };
    private readonly NumberBox layers = new() { Header = "Layer spacing", Value = 75, Minimum = 5, Maximum = 1000, SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact };
    private readonly TextBlock analysisOutput = new() { Text = "Choose an analysis to inspect this graph.", TextWrapping = TextWrapping.Wrap, FontSize = 12 };
    private readonly EditorWorkspace workspace = new();
    private readonly GraphStructureView structure = new();
    private readonly Border leftRail, rightRail;
    private CancellationTokenSource? layoutCancellation;
    private int frameCount;
    private bool ready, dirty, loaded;
    private string currentName = "Order processing", lastError = "";
    private readonly Dictionary<string, FrameworkElement> testControls = [];
    private DispatcherTimer? diagnosticTimer;

    public WorkbenchPage()
    {
        RequestedTheme = ElementTheme.Light; Background = Brush("#F1F3F5"); graph.Document = document; overview.Target = graph; inspector.Target = graph; search.Target = graph; structure.Target = graph;
        document.Replace(SampleGraphs.Flowchart(), clearHistory: true);
        var root = new Grid(); root.RowDefinitions.Add(new() { Height = GridLength.Auto }); root.RowDefinitions.Add(new() { Height = GridLength.Auto }); root.RowDefinitions.Add(new() { Height = new(1, GridUnitType.Star) }); root.RowDefinitions.Add(new() { Height = new(27) });
        var menu = BuildMenu(); AddSearchMenu(menu); AddWorkspaceMenu(menu); root.Children.Add(menu); var toolbar = BuildToolbar(); Grid.SetRow(toolbar, 1); root.Children.Add(toolbar);
        leftRail = BuildLeft(); rightRail = BuildRight(); workspace.LeftContent = leftRail; workspace.RightContent = rightRail;
        Register("StructureItems", structure.ItemsControl); Register("StructureToggle", structure.ToggleButton);
        Register("ResizeLeftPane", workspace.LeftResizeGrip); Register("ResizeRightPane", workspace.RightResizeGrip);
        var editorArea = new Grid(); editorArea.RowDefinitions.Add(new() { Height = GridLength.Auto }); editorArea.RowDefinitions.Add(new() { Height = new(1, GridUnitType.Star) }); editorArea.Children.Add(BuildDocumentHeader()); Grid.SetRow(graph, 1); Grid.SetRow(search, 1); editorArea.Children.Add(graph); editorArea.Children.Add(search);
        var editorHost = new Border { BorderBrush = Brush("#B8C2CC"), BorderThickness = new(1), Child = editorArea, Background = Brush("#FFFFFF") }; workspace.MainContent = editorHost; Grid.SetRow(workspace, 2); root.Children.Add(workspace);
        var footer = new Grid { Background = Brush("#E7ECF2"), Padding = new(10, 0, 10, 0) };
        footer.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) }); footer.ColumnDefinitions.Add(new() { Width = GridLength.Auto }); footer.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        footer.Children.Add(status); modeText.Margin = new(18, 0, 18, 0); Grid.SetColumn(modeText, 1); footer.Children.Add(modeText); Grid.SetColumn(counts, 2); footer.Children.Add(counts); Grid.SetRow(footer, 3); root.Children.Add(footer); Content = root;
        document.Changed += (_, args) => { dirty = true; status.Text = args.Operation; UpdateStatus(); PublishTestState(); };
        graph.SelectionChanged += (_, _) => { UpdateStatus(); PublishTestState(); };
        graph.Error += (_, text) => Report(text); graph.StatusChanged += (_, text) => status.Text = text;
        graph.FrameRendered += (_, _) => { frameCount++; UpdateChrome(); PublishTestState(); };
        Loaded += async (_, _) =>
        {
            if (loaded) return; loaded = true;
            await RunAsync(() => graph.LoadTypefaceAsync(new Uri("ms-appx:///Uno.Fonts.OpenSans/Fonts/OpenSans-Regular.ttf")));
            await Task.Delay(100); graph.Fit(); ready = true; dirty = false; UpdateStatus(); StartDiagnostics(); if (diagnosticTimer is not null) diagnosticTimer.Tick += (_, _) => PublishWorkspaceState(); PublishTestState(); PublishWorkspaceState();
        };
        samples.SelectionChanged += async (_, _) => { if (samples.SelectedItem is string name && name != currentName) { if (await ConfirmDiscard()) LoadExample(name); else samples.SelectedItem = currentName; } };
        Unloaded += (_, _) => { diagnosticTimer?.Stop(); layoutCancellation?.Cancel(); search.Dispose(); structure.Dispose(); inspector.Dispose(); overview.Dispose(); graph.Dispose(); };
        UpdateStatus();
    }
    private void Run(Action action) { try { action(); UpdateChrome(); PublishTestState(); } catch (Exception ex) { Report(ex.Message); } }
    private async Task RunAsync(Func<Task> action)
    {
        try { await action(); UpdateChrome(); PublishTestState(); }
        catch (OperationCanceledException) { status.Text = "Operation canceled."; }
        catch (Exception ex) { Report(ex.Message); }
    }
    private void Report(string text) { lastError = text; status.Text = "Error: " + text; PublishTestState(); }
    private void SetMode(EditorMode mode)
    {
        graph.SetMode(mode); modeText.Text = mode.ToString(); status.Text = mode switch { EditorMode.CreateEdge => "Click a source node or port, then a target. Escape cancels.", EditorMode.CreateNode => "Click to add a node. Escape returns to selection.", EditorMode.Pan => "Drag to pan. Mouse wheel zooms at the pointer.", _ => "Drag nodes to move; drag empty space to select; double-click to edit." };
    }
    private void UpdateStatus() => counts.Text = $"{document.Snapshot.Nodes.Length:N0} nodes   {document.Snapshot.Edges.Length:N0} edges   {graph.Session.Selection.Count} selected";
    private void FoldSelected()
    {
        foreach (var id in graph.Session.Selection.ToArray()) if (document.Snapshot.Index().Nodes.TryGetValue(id, out var n) && n.IsGroup) graph.Session.ToggleGroup(id);
    }
}
