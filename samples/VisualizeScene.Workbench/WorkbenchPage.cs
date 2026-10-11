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
    private readonly Grid workspace = new();
    private readonly Border leftRail, rightRail;
    private CancellationTokenSource? layoutCancellation;
    private int frameCount;
    private bool ready, dirty, loaded;
    private string currentName = "Order processing", lastError = "";
    private readonly Dictionary<string, FrameworkElement> testControls = [];
    private DispatcherTimer? diagnosticTimer;

    public WorkbenchPage()
    {
        Background = Brush("#F1F3F5"); graph.Document = document; overview.Target = graph; inspector.Target = graph; search.Target = graph;
        document.Replace(SampleGraphs.Flowchart(), clearHistory: true);
        var root = new Grid(); root.RowDefinitions.Add(new() { Height = GridLength.Auto }); root.RowDefinitions.Add(new() { Height = GridLength.Auto }); root.RowDefinitions.Add(new() { Height = new(1, GridUnitType.Star) }); root.RowDefinitions.Add(new() { Height = new(27) });
        var menu = BuildMenu(); AddSearchMenu(menu); root.Children.Add(menu); var toolbar = BuildToolbar(); Grid.SetRow(toolbar, 1); root.Children.Add(toolbar);
        workspace.ColumnDefinitions.Add(new() { Width = new(210) }); workspace.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) }); workspace.ColumnDefinitions.Add(new() { Width = new(280) });
        leftRail = BuildLeft(); rightRail = BuildRight(); workspace.Children.Add(leftRail); Grid.SetColumn(rightRail, 2); workspace.Children.Add(rightRail);
        var editorArea = new Grid(); editorArea.Children.Add(graph); editorArea.Children.Add(search);
        var editorHost = new Border { BorderBrush = Brush("#B8C2CC"), BorderThickness = new(1), Child = editorArea, Background = Brush("#FFFFFF") }; Grid.SetColumn(editorHost, 1); workspace.Children.Add(editorHost); Grid.SetRow(workspace, 2); root.Children.Add(workspace);
        var footer = new Grid { Background = Brush("#E7ECF2"), Padding = new(10, 0, 10, 0) };
        footer.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) }); footer.ColumnDefinitions.Add(new() { Width = GridLength.Auto }); footer.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        footer.Children.Add(status); modeText.Margin = new(18, 0, 18, 0); Grid.SetColumn(modeText, 1); footer.Children.Add(modeText); Grid.SetColumn(counts, 2); footer.Children.Add(counts); Grid.SetRow(footer, 3); root.Children.Add(footer); Content = root;
        document.Changed += (_, args) => { dirty = true; status.Text = args.Operation; UpdateStatus(); PublishTestState(); };
        graph.SelectionChanged += (_, _) => { UpdateStatus(); PublishTestState(); };
        graph.Error += (_, text) => Report(text); graph.StatusChanged += (_, text) => status.Text = text;
        graph.FrameRendered += (_, _) => { frameCount++; PublishTestState(); };
        Loaded += async (_, _) =>
        {
            if (loaded) return; loaded = true;
            await RunAsync(() => graph.LoadTypefaceAsync(new Uri("ms-appx:///Uno.Fonts.OpenSans/Fonts/OpenSans-Regular.ttf")));
            await Task.Delay(100); graph.Fit(); ready = true; dirty = false; UpdateStatus(); StartDiagnostics(); PublishTestState();
        };
        SizeChanged += (_, args) =>
        {
            var narrow = args.NewSize.Width < 1050; var mobile = args.NewSize.Width < 760;
            workspace.ColumnDefinitions[0].Width = new(mobile ? 0 : 210); leftRail.Visibility = mobile ? Visibility.Collapsed : Visibility.Visible;
            workspace.ColumnDefinitions[2].Width = new(narrow ? 0 : 280); rightRail.Visibility = narrow ? Visibility.Collapsed : Visibility.Visible;
        };
        samples.SelectionChanged += async (_, _) => { if (samples.SelectedItem is string name && name != currentName) { if (await ConfirmDiscard()) LoadExample(name); else samples.SelectedItem = currentName; } };
        Unloaded += (_, _) => { diagnosticTimer?.Stop(); layoutCancellation?.Cancel(); search.Dispose(); inspector.Dispose(); overview.Dispose(); graph.Dispose(); };
        UpdateStatus();
    }
    private void Run(Action action) { try { action(); PublishTestState(); } catch (Exception ex) { Report(ex.Message); } }
    private async Task RunAsync(Func<Task> action)
    {
        try { await action(); PublishTestState(); }
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
