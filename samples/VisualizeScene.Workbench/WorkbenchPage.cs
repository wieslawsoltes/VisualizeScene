using System.Collections.Immutable;
using System.Globalization;
using System.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using VisualizeScene.Algorithms;
using VisualizeScene.Controls.Uno;
using VisualizeScene.Core;
using VisualizeScene.Editing;
using VisualizeScene.Layout;
using VisualizeScene.Serialization;
using Windows.Storage;
using Windows.Storage.Pickers;

namespace VisualizeScene.Workbench;

public sealed class WorkbenchPage : Page
{
    private readonly GraphDocument document = new();
    private readonly GraphControl graph = new();
    private readonly GraphOverview overview = new();
    private readonly GraphPropertyInspector inspector = new();
    private readonly TextBlock status = new() { Text = "Ready", FontSize = 11, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly TextBlock counts = new() { FontSize = 11, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock modeText = new() { Text = "Select", FontSize = 11, VerticalAlignment = VerticalAlignment.Center };
    private readonly ComboBox samples = new() { ItemsSource = SampleGraphs.Names, SelectedIndex = 0, MinWidth = 190, FontSize = 12 };
    private readonly ComboBox layoutKind = new() { ItemsSource = Enum.GetValues<LayoutKind>(), SelectedItem = LayoutKind.Hierarchical, HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly ComboBox direction = new() { ItemsSource = Enum.GetValues<LayoutDirection>(), SelectedItem = LayoutDirection.TopToBottom, HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly NumberBox spacing = new() { Header = "Node spacing", Value = 45, Minimum = 5, Maximum = 500, SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact };
    private readonly NumberBox layers = new() { Header = "Layer spacing", Value = 75, Minimum = 5, Maximum = 1000, SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact };
    private readonly TextBlock analysisOutput = new() { Text = "Choose an analysis to inspect this graph.", TextWrapping = TextWrapping.Wrap, FontSize = 12 };
    private readonly Grid workspace = new();
    private readonly Border leftRail, rightRail;
    private CancellationTokenSource? layoutCancellation;
    private int frameCount;
    private bool ready, dirty;
    private string currentName = "Order processing";
    private string lastError = "";
    private readonly Dictionary<string, FrameworkElement> testControls = [];

    public WorkbenchPage()
    {
        Background = Brush("#F1F3F5"); graph.Document = document; overview.Target = graph; inspector.Target = graph;
        document.Replace(SampleGraphs.Flowchart(), clearHistory: true);
        var root = new Grid(); root.RowDefinitions.Add(new() { Height = GridLength.Auto }); root.RowDefinitions.Add(new() { Height = GridLength.Auto }); root.RowDefinitions.Add(new() { Height = new(1, GridUnitType.Star) }); root.RowDefinitions.Add(new() { Height = new(27) });
        var menu = BuildMenu(); root.Children.Add(menu);
        var toolbar = BuildToolbar(); Grid.SetRow(toolbar, 1); root.Children.Add(toolbar);
        workspace.ColumnDefinitions.Add(new() { Width = new(210) }); workspace.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) }); workspace.ColumnDefinitions.Add(new() { Width = new(280) });
        leftRail = BuildLeft(); rightRail = BuildRight(); workspace.Children.Add(leftRail); Grid.SetColumn(rightRail, 2); workspace.Children.Add(rightRail);
        var editorHost = new Border { BorderBrush = Brush("#B8C2CC"), BorderThickness = new(1), Child = graph, Background = Brush("#FFFFFF") }; Grid.SetColumn(editorHost, 1); workspace.Children.Add(editorHost);
        Grid.SetRow(workspace, 2); root.Children.Add(workspace);
        var footer = new Grid { Background = Brush("#E7ECF2"), Padding = new(10, 0, 10, 0) };
        footer.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) }); footer.ColumnDefinitions.Add(new() { Width = GridLength.Auto }); footer.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        footer.Children.Add(status); modeText.Margin = new(18, 0, 18, 0); Grid.SetColumn(modeText, 1); footer.Children.Add(modeText); Grid.SetColumn(counts, 2); footer.Children.Add(counts); Grid.SetRow(footer, 3); root.Children.Add(footer);
        Content = root;
        document.Changed += (_, args) => { dirty = true; status.Text = args.Operation; UpdateStatus(); PublishTestState(); };
        graph.SelectionChanged += (_, _) => { UpdateStatus(); PublishTestState(); };
        graph.Error += (_, text) => Report(text);
        graph.StatusChanged += (_, text) => status.Text = text;
        graph.FrameRendered += (_, _) => { frameCount++; if (frameCount <= 3 || frameCount % 10 == 0) PublishTestState(); };
        Loaded += (_, _) =>
        {
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
            timer.Tick += (_, _) => { timer.Stop(); graph.Fit(); ready = true; dirty = false; UpdateStatus(); PublishTestState(); }; timer.Start();
        };
        SizeChanged += (_, args) =>
        {
            var narrow = args.NewSize.Width < 1050; var mobile = args.NewSize.Width < 760;
            workspace.ColumnDefinitions[0].Width = new(mobile ? 0 : 210); leftRail.Visibility = mobile ? Visibility.Collapsed : Visibility.Visible;
            workspace.ColumnDefinitions[2].Width = new(narrow ? 0 : 280); rightRail.Visibility = narrow ? Visibility.Collapsed : Visibility.Visible;
        };
        samples.SelectionChanged += async (_, _) => { if (samples.SelectedItem is string name && name != currentName) { if (await ConfirmDiscard()) LoadExample(name); else samples.SelectedItem = currentName; } };
        UpdateStatus();
    }
    private static SolidColorBrush Brush(string hex) => new(Windows.UI.Color.FromArgb(255, Convert.ToByte(hex.Substring(1, 2), 16), Convert.ToByte(hex.Substring(3, 2), 16), Convert.ToByte(hex.Substring(5, 2), 16)));
    private void Register(string id, FrameworkElement element)
    {
        AutomationProperties.SetAutomationId(element, id); testControls[id] = element;
    }
    private Button Button(string label, string id, Action action, string? tooltip = null)
    {
        var button = new Button { Content = label, FontSize = 12, Padding = new(10, 6, 10, 6), MinHeight = 30 };
        Register(id, button); if (tooltip is not null) ToolTipService.SetToolTip(button, tooltip);
        button.Click += (_, _) => Run(action); return button;
    }
    private void Run(Action action) { try { action(); PublishTestState(); } catch (Exception ex) { Report(ex.Message); } }
    private async Task RunAsync(Func<Task> action) { try { await action(); PublishTestState(); } catch (Exception ex) { Report(ex.Message); } }
    private void Report(string text) { lastError = text; status.Text = "Error: " + text; PublishTestState(); }
    private MenuBar BuildMenu()
    {
        var bar = new MenuBar { Background = Brush("#F6F7F9"), Padding = new(2, 0, 2, 0) };
        MenuBarItem Menu(string text) { var menu = new MenuBarItem { Title = text }; bar.Items.Add(menu); return menu; }
        void Item(MenuBarItem menu, string text, Action action) { var item = new MenuFlyoutItem { Text = text }; item.Click += (_, _) => Run(action); menu.Items.Add(item); }
        var file = Menu("File"); Item(file, "New graph", () => _ = NewGraph()); Item(file, "Open JSON / GraphML…", () => _ = RunAsync(OpenFile)); Item(file, "Import from text…", () => _ = TextExchange(true));
        file.Items.Add(new MenuFlyoutSeparator()); Item(file, "Save JSON…", () => _ = Save("json")); Item(file, "Save GraphML…", () => _ = Save("graphml")); Item(file, "Export SVG…", () => _ = Save("svg")); Item(file, "Export PNG…", () => _ = Save("png")); Item(file, "View document source…", () => _ = TextExchange(false));
        var edit = Menu("Edit"); Item(edit, "Undo  Ctrl+Z", document.Undo); Item(edit, "Redo  Ctrl+Y", document.Redo); edit.Items.Add(new MenuFlyoutSeparator());
        Item(edit, "Copy  Ctrl+C", () => graph.Session.Copy()); Item(edit, "Paste  Ctrl+V", () => graph.Session.Paste()); Item(edit, "Duplicate  Ctrl+D", graph.Session.Duplicate); Item(edit, "Delete  Del", graph.Session.DeleteSelection); Item(edit, "Select all  Ctrl+A", graph.Session.SelectAll); Item(edit, "Edit label  F2", () => _ = graph.EditLabelAsync());
        var view = Menu("View"); Item(view, "Fit graph  Home", graph.Fit); Item(view, "Zoom in", () => graph.Zoom(1.25)); Item(view, "Zoom out", () => graph.Zoom(.8)); Item(view, "Toggle grid", () => { graph.Session.ShowGrid = !graph.Session.ShowGrid; graph.Invalidate(); }); Item(view, "Toggle snapping", () => graph.Session.SnapToGrid = !graph.Session.SnapToGrid);
        var arrange = Menu("Arrange"); Item(arrange, "Group  Ctrl+G", graph.Session.Group); Item(arrange, "Ungroup  Ctrl+Shift+G", graph.Session.Ungroup); Item(arrange, "Fold / unfold selected groups", FoldSelected);
        arrange.Items.Add(new MenuFlyoutSeparator()); foreach (var alignment in Enum.GetValues<Alignment>()) Item(arrange, alignment.ToString(), () => graph.Session.Align(alignment));
        var layout = Menu("Layout"); foreach (var kind in Enum.GetValues<LayoutKind>()) Item(layout, kind.ToString(), () => { layoutKind.SelectedItem = kind; _ = ApplyLayout(); }); Item(layout, "Route edges around obstacles", () => _ = RouteEdges());
        var help = Menu("Help"); Item(help, "Editing guide", () => _ = ShowHelp()); Item(help, "Implementation status", () => _ = ShowStatus()); return bar;
    }
    private FrameworkElement BuildToolbar()
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 5, Padding = new(9, 7, 9, 7) };
        row.Children.Add(Button("New", "New", () => _ = NewGraph())); row.Children.Add(Button("Open", "Open", () => _ = RunAsync(OpenFile))); row.Children.Add(Button("Save", "Save", () => _ = Save("json")));
        row.Children.Add(Separator()); row.Children.Add(Button("↶", "Undo", document.Undo, "Undo · Ctrl+Z")); row.Children.Add(Button("↷", "Redo", document.Redo, "Redo · Ctrl+Y")); row.Children.Add(Separator());
        row.Children.Add(Button("Select", "ModeSelect", () => SetMode(EditorMode.Select))); row.Children.Add(Button("Node", "ModeNode", () => SetMode(EditorMode.CreateNode))); row.Children.Add(Button("Edge", "ModeEdge", () => SetMode(EditorMode.CreateEdge))); row.Children.Add(Button("Pan", "ModePan", () => SetMode(EditorMode.Pan)));
        row.Children.Add(Separator()); row.Children.Add(Button("−", "ZoomOut", () => graph.Zoom(.8))); row.Children.Add(Button("Fit", "Fit", graph.Fit)); row.Children.Add(Button("+", "ZoomIn", () => graph.Zoom(1.25)));
        row.Children.Add(Separator()); Register("Samples", samples); row.Children.Add(samples);
        return new ScrollViewer { Content = row, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled, Background = Brush("#ECF0F4") };
    }
    private static Border Separator() => new() { Width = 1, Height = 25, Margin = new(4, 0, 4, 0), Background = Brush("#C8D0D9"), VerticalAlignment = VerticalAlignment.Center };
    private static TextBlock Heading(string text) => new() { Text = text, FontSize = 12, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Margin = new(0, 0, 0, 8) };
    private Border BuildLeft()
    {
        var panel = new Grid { Padding = new(12) }; panel.RowDefinitions.Add(new() { Height = GridLength.Auto }); panel.RowDefinitions.Add(new() { Height = new(1, GridUnitType.Star) }); panel.RowDefinitions.Add(new() { Height = GridLength.Auto });
        var palette = new NodePalette { Target = graph }; palette.TemplateSelected += (_, template) => { modeText.Text = "Create " + template.Name; status.Text = "Click in the canvas to place a " + template.Name.ToLowerInvariant() + ". Escape returns to selection."; PublishTestState(); };
        var top = new StackPanel { Spacing = 5 }; top.Children.Add(Heading("NODE PALETTE")); top.Children.Add(palette);
        top.Children.Add(new TextBlock { Text = "Choose a shape, then click in the diagram.", FontSize = 11, TextWrapping = TextWrapping.Wrap, Margin = new(0, 8, 0, 10), Foreground = Brush("#5F6E7D") });
        top.Children.Add(Button("Group selection", "Group", graph.Session.Group)); top.Children.Add(Button("Fold / unfold", "Fold", FoldSelected)); panel.Children.Add(top);
        var bottom = new StackPanel { Spacing = 5 }; bottom.Children.Add(Heading("OVERVIEW")); overview.Height = 160; bottom.Children.Add(new Border { Child = overview, BorderBrush = Brush("#C5CED7"), BorderThickness = new(1) });
        bottom.Children.Add(new TextBlock { Text = "Click the overview to navigate.", FontSize = 10, Foreground = Brush("#647687") }); Grid.SetRow(bottom, 2); panel.Children.Add(bottom);
        return new Border { Child = panel, Background = Brush("#F4F6F8") };
    }
    private Border BuildRight()
    {
        var tabs = new Pivot { Margin = new(0), HorizontalAlignment = HorizontalAlignment.Stretch };
        var properties = new PivotItem { Header = "Properties", Content = new Border { Padding = new(10), Child = inspector } }; tabs.Items.Add(properties);
        var layoutPanel = new StackPanel { Spacing = 12, Padding = new(10) };
        layoutKind.Header = "Layout algorithm"; direction.Header = "Direction"; Register("LayoutKind", layoutKind); layoutPanel.Children.Add(layoutKind); layoutPanel.Children.Add(direction); layoutPanel.Children.Add(spacing); layoutPanel.Children.Add(layers);
        layoutPanel.Children.Add(Button("Apply layout", "ApplyLayout", () => _ = ApplyLayout())); layoutPanel.Children.Add(Button("Route edges", "RouteEdges", () => _ = RouteEdges()));
        layoutPanel.Children.Add(Button("Cancel operation", "CancelLayout", () => layoutCancellation?.Cancel()));
        layoutPanel.Children.Add(new TextBlock { Text = "Layouts work on an immutable snapshot. The result is committed once and can be undone. Stale results are rejected.", TextWrapping = TextWrapping.Wrap, FontSize = 11, Foreground = Brush("#657688") });
        tabs.Items.Add(new PivotItem { Header = "Layout", Content = new ScrollViewer { Content = layoutPanel } });
        var analysis = new StackPanel { Spacing = 8, Padding = new(10) };
        foreach (var name in new[] { "Connected components", "Strong components", "Shortest path", "Minimum spanning forest", "PageRank", "Betweenness", "Color graph", "Maximum flow" }) analysis.Children.Add(Button(name, "Analyze" + name.Replace(" ", ""), () => Analyze(name)));
        analysis.Children.Add(new Border { Height = 1, Background = Brush("#CDD5DE"), Margin = new(0, 8, 0, 8) }); analysis.Children.Add(analysisOutput); tabs.Items.Add(new PivotItem { Header = "Analysis", Content = new ScrollViewer { Content = analysis } });
        return new Border { Child = tabs, Background = Brush("#F7F8FA") };
    }
    private void SetMode(EditorMode mode) { graph.SetMode(mode); modeText.Text = mode.ToString(); status.Text = mode switch { EditorMode.CreateEdge => "Click a source node or port, then a target. Escape cancels.", EditorMode.CreateNode => "Click to add a node. Escape returns to selection.", EditorMode.Pan => "Drag to pan. Mouse wheel zooms at the pointer.", _ => "Drag nodes to move; drag empty space to select; double-click to edit." }; }
    private void UpdateStatus() => counts.Text = $"{document.Snapshot.Nodes.Length:N0} nodes   {document.Snapshot.Edges.Length:N0} edges   {graph.Session.Selection.Count} selected";
    private void FoldSelected()
    {
        foreach (var id in graph.Session.Selection.ToArray()) if (document.Snapshot.Index().Nodes.TryGetValue(id, out var n) && n.IsGroup) graph.Session.ToggleGroup(id);
    }
    private async Task<bool> ConfirmDiscard()
    {
        if (!dirty) return true;
        var dialog = new ContentDialog { XamlRoot = XamlRoot, Title = "Replace the current graph?", Content = "The current graph has unsaved edits. Save it before replacing it to keep those changes.", PrimaryButtonText = "Replace", CloseButtonText = "Cancel" };
        return await dialog.ShowAsync() == ContentDialogResult.Primary;
    }
    private async Task NewGraph()
    {
        await RunAsync(async () => { if (!await ConfirmDiscard()) return; document.Replace(GraphSnapshot.Empty, "New graph", true); graph.Session.Select([]); graph.SetMode(EditorMode.Select); currentName = "Untitled"; dirty = false; graph.Fit(); });
    }
    private void LoadExample(string name)
    {
        Run(() => { layoutCancellation?.Cancel(); document.Replace(SampleGraphs.Create(name), "Load " + name, true); currentName = name; dirty = false; graph.Session.Select([]); graph.SetMode(EditorMode.Select); graph.Fit(); lastError = ""; });
    }
    private async Task ApplyLayout()
    {
        await RunAsync(async () =>
        {
            layoutCancellation?.Cancel(); layoutCancellation?.Dispose(); layoutCancellation = new(); var token = layoutCancellation.Token;
            var revision = document.Revision; var snapshot = document.Snapshot;
            var options = new LayoutOptions((LayoutKind)layoutKind.SelectedItem, (LayoutDirection)direction.SelectedItem, spacing.Value, layers.Value);
            status.Text = "Calculating " + options.Kind + " layout…";
            await Task.Delay(30, token);
            var result = await Task.Run(() => new LayoutEngine().Calculate(snapshot, options, token), token);
            token.ThrowIfCancellationRequested(); if (revision != document.Revision) throw new InvalidOperationException("Graph changed during layout; result discarded.");
            document.Replace(result.Graph, options.Kind + " layout"); graph.Fit(); status.Text = result.Diagnostics.Count == 0 ? options.Kind + " layout complete." : string.Join(" ", result.Diagnostics);
        });
    }
    private async Task RouteEdges()
    {
        await RunAsync(async () =>
        {
            var revision = document.Revision; var snapshot = document.Snapshot; status.Text = "Routing edges…"; await Task.Delay(30);
            var result = await Task.Run(() => new OrthogonalRouter().Route(snapshot));
            if (revision != document.Revision) throw new InvalidOperationException("Graph changed during routing; result discarded.");
            document.Replace(result.Graph, "Route edges"); status.Text = result.Diagnostics.Count == 0 ? "All edges routed." : $"{result.Diagnostics.Count} routing diagnostics: " + result.Diagnostics[0].Message;
        });
    }
    private void Analyze(string name)
    {
        var snapshot = document.Snapshot; var nodes = graph.Session.Selection.Where(id => snapshot.Index().Nodes.ContainsKey(id)).ToArray();
        switch (name)
        {
            case "Connected components":
                var components = GraphAlgorithms.ConnectedComponents(snapshot); analysisOutput.Text = $"{components.Count} connected components\n" + string.Join("\n", components.Select((c, i) => $"{i + 1}: {c.Count} nodes")); break;
            case "Strong components":
                var strong = GraphAlgorithms.StronglyConnectedComponents(snapshot); analysisOutput.Text = $"{strong.Count} strongly connected components\n" + string.Join("\n", strong.Take(15).Select(c => string.Join(", ", c))); break;
            case "Shortest path":
                if (nodes.Length != 2) throw new InvalidOperationException("Select two nodes, source first, using Ctrl+click.");
                var path = snapshot.Edges.Any(e => e.Weight < 0) ? GraphAlgorithms.BellmanFord(snapshot, nodes[0], nodes[1]) : GraphAlgorithms.ShortestPath(snapshot, nodes[0], nodes[1]);
                analysisOutput.Text = path.Found ? $"Cost: {path.Cost:G6}\n" + string.Join(" → ", path.Nodes) : "No directed path exists."; if (path.Found) graph.Session.Select(path.Nodes.Concat(path.Edges)); break;
            case "Minimum spanning forest":
                var forest = GraphAlgorithms.MinimumSpanningForest(snapshot); graph.Session.Select(forest); analysisOutput.Text = $"{forest.Count} edges in the minimum spanning forest.\nTotal weight: {snapshot.Edges.Where(e => forest.Contains(e.Id)).Sum(e => e.Weight):G6}"; break;
            case "PageRank": Scores(GraphAlgorithms.PageRank(snapshot)); break;
            case "Betweenness": Scores(GraphAlgorithms.Betweenness(snapshot)); break;
            case "Color graph":
                var colors = GraphAlgorithms.Coloring(snapshot); string[] palette = ["#DDEBFA", "#DDEFE3", "#FFF1CC", "#E9E2F3", "#F9E5DB", "#D8EFF0", "#F0DDE8", "#DFE1F5"];
                document.Edit("Color graph", edit => { foreach (var n in snapshot.Nodes) edit.Update(n with { Fill = palette[colors[n.Id] % palette.Length] }); }); analysisOutput.Text = $"DSATUR used {(colors.Count == 0 ? 0 : colors.Values.Max() + 1)} colors.\nThis is a heuristic, not an exact chromatic-number computation."; break;
            case "Maximum flow":
                if (nodes.Length != 2) throw new InvalidOperationException("Select source and sink nodes, in that order. Edge weights are capacities.");
                var flow = GraphAlgorithms.MaximumFlow(snapshot, nodes[0], nodes[1]); graph.Session.Select(flow.EdgeFlows.Where(p => p.Value > 0).Select(p => p.Key)); analysisOutput.Text = $"Maximum flow: {flow.Value:G6}\n" + string.Join("\n", flow.EdgeFlows.Where(p => p.Value > 0).Select(p => $"{p.Key}: {p.Value:G4}")); break;
        }
        void Scores(IReadOnlyDictionary<string, double> values) => analysisOutput.Text = name + "\n" + string.Join("\n", values.OrderByDescending(p => p.Value).Take(20).Select(p => $"{snapshot.Index().Nodes[p.Key].Text.Replace('\n', ' ')}: {p.Value:G4}"));
    }
    private async Task OpenFile()
    {
        var picker = new FileOpenPicker(); picker.FileTypeFilter.Add(".json"); picker.FileTypeFilter.Add(".graphml"); picker.FileTypeFilter.Add(".xml");
        var file = await picker.PickSingleFileAsync(); if (file is null) return;
        var text = await FileIO.ReadTextAsync(file); if (!await ConfirmDiscard()) return;
        Import(text); currentName = file.Name; dirty = false;
    }
    private void Import(string text)
    {
        var import = text.TrimStart().StartsWith('<') ? GraphIO.FromGraphML(text) : new GraphImportResult(GraphIO.FromJson(text), []);
        document.Replace(import.Graph, "Import graph"); graph.Fit(); status.Text = import.Diagnostics.Count == 0 ? "Graph imported." : string.Join(" ", import.Diagnostics);
    }
    private async Task Save(string extension)
    {
        await RunAsync(async () =>
        {
            var bytes = extension switch { "png" => graph.ExportPng(), "svg" => Encoding.UTF8.GetBytes(SvgExporter.Export(document.Snapshot)), "graphml" => Encoding.UTF8.GetBytes(GraphIO.ToGraphML(document.Snapshot)), _ => Encoding.UTF8.GetBytes(GraphIO.ToJson(document.Snapshot)) };
            var name = "visualizescene." + extension;
#if __WASM__
            var mime = extension switch { "png" => "image/png", "svg" => "image/svg+xml", "graphml" => "application/graphml+xml", _ => "application/json" };
            var script = "(()=>{const raw=atob('" + Convert.ToBase64String(bytes) + "');const data=Uint8Array.from(raw,c=>c.charCodeAt(0));const url=URL.createObjectURL(new Blob([data],{type:'" + mime + "'}));const a=document.createElement('a');a.href=url;a.download='" + name + "';a.click();setTimeout(()=>URL.revokeObjectURL(url),1000);})()";
            Uno.Foundation.WebAssemblyRuntime.InvokeJS(script);
            await Task.CompletedTask;
#else
            var picker = new FileSavePicker { SuggestedFileName = "visualizescene" }; picker.FileTypeChoices.Add(extension.ToUpperInvariant(), ["." + extension]);
            var file = await picker.PickSaveFileAsync(); if (file is null) return; await FileIO.WriteBytesAsync(file, bytes);
#endif
            if (extension is "json" or "graphml") dirty = false; status.Text = "Saved " + name;
        });
    }
    private async Task TextExchange(bool import)
    {
        await RunAsync(async () =>
        {
            var text = new TextBox { Text = import ? "" : GraphIO.ToJson(document.Snapshot), AcceptsReturn = true, TextWrapping = TextWrapping.NoWrap, FontFamily = new FontFamily("Consolas"), FontSize = 11, Width = 600, Height = 400, IsReadOnly = !import };
            Register("GraphSource", text);
            var dialog = new ContentDialog { XamlRoot = XamlRoot, Title = import ? "Import JSON or GraphML" : "Graph document · JSON", Content = text, PrimaryButtonText = import ? "Import" : "", CloseButtonText = "Close" };
            if (await dialog.ShowAsync() == ContentDialogResult.Primary && import) Import(text.Text);
        });
    }
    private Task ShowHelp() => ShowMessage("Editing guide", "Select: drag a node to move it; drag empty canvas for a selection rectangle. Ctrl+click adds or removes items.\n\nDouble-click a node or edge to edit its label. Double-click empty canvas to create a node. Ctrl+double-click an edge inserts a bend. Drag selected corners or bends to reshape.\n\nNode mode: click to create nodes. Edge mode: click a source and then a target; ports are supported. Pan mode or middle-button drag moves the viewport. The wheel zooms around the pointer.\n\nCtrl+Z/Y: undo/redo. Ctrl+C/V/D: copy/paste/duplicate. Ctrl+G: group. Ctrl+Shift+G: ungroup. F2: label. Delete: remove. Home: fit. Escape: cancel and select.\n\nDouble-click group headers to fold/unfold. All graph edits and layouts are undoable.");
    private Task ShowStatus() => ShowMessage("Implementation status", "VisualizeScene 0.1 is an independent graph toolkit and Uno workbench, not yFiles.\n\nImplemented: immutable graph model, groups/folding, ports/labels, transactional editing, undo/redo, six layouts, obstacle-aware orthogonal routing, graph analysis, JSON/GraphML interchange, SVG/PNG export, Skia rendering and reusable Uno controls.\n\nFull yFiles API/visual parity is not implemented. Advanced layout constraints, specialized BPMN/UML/table styles, WPF template compatibility, printing and full GraphML extensions remain outside this version. See the repository capability matrix for exact boundaries.\n\nRendering uses Uno’s shared Skia surface. Hardware acceleration depends on the platform and driver; CPU draw timings are not GPU timings.");
    private async Task ShowMessage(string title, string body) => await new ContentDialog { XamlRoot = XamlRoot, Title = title, Content = new ScrollViewer { Content = new TextBlock { Text = body, TextWrapping = TextWrapping.Wrap, MaxWidth = 550 } }, CloseButtonText = "Close" }.ShowAsync();
    private void PublishTestState()
    {
#if __WASM__
        if (Uno.Foundation.WebAssemblyRuntime.InvokeJS("new URLSearchParams(location.search).get('test') === '1' ? 'yes' : 'no'") != "yes") return;
        static string Number(double value) => double.IsFinite(value) ? value.ToString("R", CultureInfo.InvariantCulture) : "0";
        string Bounds(FrameworkElement element)
        {
            try { var p = element.TransformToVisual(this).TransformPoint(new Windows.Foundation.Point()); return $"{{\"x\":{Number(p.X)},\"y\":{Number(p.Y)},\"width\":{Number(element.ActualWidth)},\"height\":{Number(element.ActualHeight)}}}"; }
            catch { return "null"; }
        }
        var model = GraphIO.ToJson(document.Snapshot);
        var controls = string.Join(",", testControls.Where(p => p.Value.IsLoaded).Select(p => "\"" + p.Key + "\":" + Bounds(p.Value)));
        var selected = string.Join(",", graph.Session.Selection.Select(id => "\"" + id + "\""));
        var error = Convert.ToBase64String(Encoding.UTF8.GetBytes(lastError));
        var script = $"globalThis.visualizeSceneState={{ready:{(ready ? "true" : "false")},frames:{frameCount},revision:{document.Revision},mode:'{graph.Session.Mode}',zoom:{Number(graph.Session.Viewport.Zoom)},origin:{{x:{Number(graph.Session.Viewport.Origin.X)},y:{Number(graph.Session.Viewport.Origin.Y)}}},canvas:{Bounds(graph)},controls:{{{controls}}},selection:[{selected}],error:atob('{error}'),model:{model}}};";
        Uno.Foundation.WebAssemblyRuntime.InvokeJS(script);
#endif
    }
}
