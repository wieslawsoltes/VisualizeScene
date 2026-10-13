using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using VisualizeScene.Core;
using VisualizeScene.Editing;
using Windows.System;

namespace VisualizeScene.Controls.Uno;

/// <summary>Reusable modeless graph Find/Replace. Queries are debounced; stale completions cannot edit or select a new revision.</summary>
public sealed partial class GraphSearchControl : UserControl, IDisposable
{
    private sealed record Row(int Number, GraphSearchMatch Match)
    {
        public override string ToString() => $"{Number + 1}. {Match.ItemId} [{Match.Field}] {Match.Preview}" +
            (!Match.IsVisible ? " (hidden)" : "") + (!Match.CanReplace ? " (read-only)" : "");
    }
    private readonly TextBox query = new() { PlaceholderText = "Find graph text", MaxLength = 4096, FontSize = 12 };
    private readonly TextBox replacement = new() { PlaceholderText = "Replace with (literal text)", FontSize = 12 };
    private readonly CheckBox matchCase = new() { Content = "Match case", FontSize = 12 };
    private readonly CheckBox wholeWord = new() { Content = "Whole word", FontSize = 12 };
    private readonly CheckBox includeIds = new() { Content = "Search IDs", FontSize = 12 };
    private readonly CheckBox includeHidden = new() { Content = "Hidden items", FontSize = 12 };
    private readonly CheckBox selectionOnly = new() { Content = "Captured selection", FontSize = 12 };
    private readonly ComboBox kind = new() { ItemsSource = new[] { "All items", "Nodes", "Edges", "Labels", "Groups", "Ports" }, SelectedIndex = 0, FontSize = 12, HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly TextBlock summary = new() { Text = "Enter text to find.", FontSize = 12, TextWrapping = TextWrapping.Wrap };
    private readonly ListView rows = new() { Height = 150, SelectionMode = ListViewSelectionMode.Single, FontSize = 12 };
    private readonly StackPanel replacePanel = new() { Spacing = 6 };
    private readonly DispatcherTimer debounce = new() { Interval = TimeSpan.FromMilliseconds(200) };
    private readonly Dictionary<string, FrameworkElement> elements = new(StringComparer.Ordinal);
    private Button previous = null!, next = null!, replaceOne = null!, replaceAll = null!, selectAll = null!;
    private CancellationTokenSource? cancellation;
    private GraphControl? target;
    private GraphSearchIndex? registry;
    private GraphSearchResult? result;
    private long generation, resultRevision;
    private string[]? scope;
    private bool disposed, binding;
    public GraphSearchResult? Results => result;
    public bool IsReady { get; private set; }
    public string QueryText => query.Text;
    public string LastError { get; private set; } = "";
    public string Status => summary.Text;
    public int CurrentIndex => rows.SelectedIndex;
    public GraphReplacementResult? LastReplacement { get; private set; }
    public FrameworkElement? FindElement(string name) => elements.GetValueOrDefault(name);
    public GraphControl? Target
    {
        get => target;
        set
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (ReferenceEquals(value, target)) return;
            Detach(); target = value; registry = null; scope = null;
            if (target is not null)
            {
                target.Document.Changed += Changed; target.DocumentChanged += Rebind;
                target.KeyDown += TargetKey; target.SizeChanged += TargetSizeChanged;
            }
            ConstrainToViewport(); RequestSearch();
        }
    }
    public GraphSearchControl()
    {
        Visibility = Visibility.Collapsed; HorizontalAlignment = HorizontalAlignment.Right; VerticalAlignment = VerticalAlignment.Top;
        Width = 320; MaxWidth = 320; Margin = new(8); AutomationProperties.SetName(this, "Find and replace graph text");
        var panel = new StackPanel { Spacing = 7, Padding = new(10), MinWidth = 270 };
        contentPanel = panel; ConstrainToViewport();
        var header = new Grid(); header.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) }); header.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        header.Children.Add(new TextBlock { Text = "Find / Replace", FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center });
        var close = Button("Close", "Close", Close); Grid.SetColumn(close, 1); header.Children.Add(close); panel.Children.Add(header);
        Register("Query", query); Register("Replacement", replacement); panel.Children.Add(query);
        void Options(params CheckBox[] boxes)
        {
            var line = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            foreach (var box in boxes) line.Children.Add(box); panel.Children.Add(line);
        }
        foreach (var box in new[] { matchCase, wholeWord, includeIds, includeHidden, selectionOnly })
        { box.MinHeight = 28; box.Height = 28; box.Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent); }
        Options(matchCase, wholeWord); Options(includeIds, includeHidden);
        foreach (var (name, box) in new[] { ("Case", matchCase), ("Word", wholeWord), ("Ids", includeIds), ("Hidden", includeHidden) })
        { Register(name, box); box.Checked += (_, _) => RequestSearch(); box.Unchecked += (_, _) => RequestSearch(); }
        var selectionRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        Register("Selection", selectionOnly); selectionRow.Children.Add(selectionOnly);
        selectionRow.Children.Add(Button("Capture", "Capture selection", () => { scope = target?.Session.Selection.ToArray() ?? []; RequestSearch(); })); panel.Children.Add(selectionRow);
        selectionOnly.Checked += (_, _) => { scope = target?.Session.Selection.ToArray() ?? []; RequestSearch(); };
        selectionOnly.Unchecked += (_, _) => RequestSearch();
        Register("Kind", kind); panel.Children.Add(kind); kind.SelectionChanged += (_, _) => RequestSearch();
        var commands = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 5 };
        previous = Button("Previous", "Previous", () => FindNext(true)); next = Button("Next", "Next", () => FindNext());
        selectAll = Button("SelectAll", "Select matches", SelectMatches); commands.Children.Add(previous); commands.Children.Add(next); commands.Children.Add(selectAll); panel.Children.Add(commands);
        // Status changes must not shift result navigation or replacement click targets.
        summary.Height = 44; summary.TextTrimming = TextTrimming.CharacterEllipsis;
        Register("Results", rows); panel.Children.Add(summary); panel.Children.Add(rows);
        replacePanel.Children.Add(replacement);
        var replaceCommands = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 5 };
        replaceOne = Button("Replace", "Replace", () => Replace(false)); replaceAll = Button("ReplaceAll", "Replace all", () => Replace(true));
        replaceCommands.Children.Add(replaceOne); replaceCommands.Children.Add(replaceAll); replacePanel.Children.Add(replaceCommands); panel.Children.Add(replacePanel);
        panel.Children.Add(new TextBlock { Text = "Enter / F3: next · Shift+F3: previous · Esc: close\nIDs and locked owners are read-only. Literal text only.", FontSize = 10, TextWrapping = TextWrapping.Wrap });
        Content = new Border { BorderBrush = new SolidColorBrush(Microsoft.UI.Colors.Gray), BorderThickness = new(1), Background = new SolidColorBrush(Microsoft.UI.Colors.White), Child = new ScrollViewer { Content = panel, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Auto } };
        query.TextChanged += (_, _) => RequestSearch(); debounce.Tick += async (_, _) => { debounce.Stop(); await SearchAsync(); };
        rows.SelectionChanged += (_, _) => { if (!binding) RevealCurrent(); UpdateCommands(); };
        KeyDown += Key; Unloaded += (_, _) => CancelPending(); UpdateCommands();
    }
    private void Register(string name, FrameworkElement element) { elements.Add(name, element); AutomationProperties.SetAutomationId(element, "Search" + name); }
    private Button Button(string name, string text, Action action)
    {
        var button = new Button { Content = text, FontSize = 12, Padding = new(7, 4, 7, 4) }; Register(name, button);
        button.Click += (_, _) => { try { action(); } catch (Exception ex) { ShowError(ex); } }; return button;
    }
    private void TargetKey(object sender, KeyRoutedEventArgs args)
    {
        if (args.Handled) return;
        bool Down(VirtualKey key) => Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(key).HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);
        if (Down(VirtualKey.Menu)) return;
        if (Down(VirtualKey.Control) && args.Key is VirtualKey.F or VirtualKey.H)
        { target?.CancelTouchNavigation(); Open(args.Key == VirtualKey.H); args.Handled = true; }
        else if (args.Key == VirtualKey.F3) { FindNext(Down(VirtualKey.Shift)); args.Handled = true; }
    }
    public void Open(bool showReplacement = false)
    {
        ObjectDisposedException.ThrowIf(disposed, this); Visibility = Visibility.Visible;
        replacePanel.Visibility = showReplacement ? Visibility.Visible : Visibility.Collapsed;
        ConstrainToViewport(); query.Focus(FocusState.Programmatic); query.SelectAll(); RequestSearch();
    }
    public void Close() { CancelPending(); Visibility = Visibility.Collapsed; target?.Focus(FocusState.Programmatic); }
    private void CancelPending() { generation++; debounce.Stop(); cancellation?.Cancel(); cancellation?.Dispose(); cancellation = null; }
    private void RequestSearch()
    {
        CancelPending(); result = null; IsReady = false; LastError = ""; rows.ItemsSource = null; UpdateCommands();
        if (!disposed && target is not null && Visibility == Visibility.Visible) { summary.Text = "Searching…"; debounce.Start(); }
    }
    private GraphSearchOptions Options() => new(matchCase.IsChecked == true, wholeWord.IsChecked == true, includeHidden.IsChecked == true,
        kind.SelectedIndex switch { 1 => SelectionKinds.Nodes, 2 => SelectionKinds.Edges, 3 => SelectionKinds.Labels, 4 => SelectionKinds.Groups, 5 => SelectionKinds.Ports, _ => SelectionKinds.All },
        includeIds.IsChecked == true ? GraphSearchFields.All : GraphSearchFields.Text);
    private async Task SearchAsync()
    {
        if (disposed || target is null || Visibility != Visibility.Visible) return;
        var ticket = generation; var document = target.Document; var snapshot = document.Snapshot; var revision = document.Revision;
        var text = query.Text; var options = Options(); var captured = selectionOnly.IsChecked == true ? scope ?? [] : null;
        cancellation = new(); var token = cancellation.Token; var prior = registry;
        try
        {
            var found = await Task.Run(() =>
            {
                token.ThrowIfCancellationRequested(); var index = ReferenceEquals(prior?.Snapshot, snapshot) ? prior! : new GraphSearchIndex(snapshot);
                return (Index: index, Result: index.Find(text, options, captured, token));
            }, token);
            if (disposed || ticket != generation || target?.Document != document || document.Revision != revision || Visibility != Visibility.Visible) return;
            registry = found.Index; result = found.Result; resultRevision = revision; IsReady = true;
            binding = true; rows.ItemsSource = result.Matches.Select((m, i) => new Row(i, m)).ToArray(); rows.SelectedIndex = result.Matches.IsEmpty ? -1 : 0; binding = false;
            summary.Text = result.Matches.Length == 0 ? "No matches." : $"{result.Matches.Length:N0} matches" + (result.IsTruncated ? " (limit reached; Replace All disabled)." : ".");
            if (captured is not null) summary.Text += $" Scope: {captured.Length:N0} captured items.";
            RevealCurrent(); UpdateCommands();
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { if (!disposed && ticket == generation) { binding = false; ShowError(ex); } }
    }
    private bool Current => result is not null && target is not null && ReferenceEquals(result.Snapshot, target.Document.Snapshot) && resultRevision == target.Document.Revision;
    private void RevealCurrent()
    {
        if (!Current || rows.SelectedIndex < 0 || rows.SelectedIndex >= result!.Matches.Length) return;
        var found = result; var control = target!; var match = found.Matches[rows.SelectedIndex];
        RevealRequests++; control.RevealItem(match.ItemId);
        if (!Current || !ReferenceEquals(result, found) || !ReferenceEquals(target, control)) return;
        if (!match.IsVisible) summary.Text = $"{result?.Matches.Length:N0} matches. Hidden match: showing its folded representative.";
    }
    public void FindNext(bool backward = false)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (!Current) { Open(replacePanel.Visibility == Visibility.Visible); return; }
        if (result!.Matches.IsEmpty) return;
        var found = result; var previousIndex = rows.SelectedIndex;
        rows.SelectedIndex = (previousIndex + (backward ? -1 : 1) + result.Matches.Length) % result.Matches.Length;
        // SelectionChanged performs the reveal for a changed index. A one-match cycle
        // does not raise that event and still needs to reveal the current item.
        if (!Current || !ReferenceEquals(result, found)) return;
        if (rows.SelectedIndex == previousIndex) RevealCurrent();
        if (Current && ReferenceEquals(result, found) && Visibility == Visibility.Visible && rows.SelectedItem is { } selectedRow)
            rows.ScrollIntoView(selectedRow);
    }
    private void SelectMatches()
    {
        if (!Current) return;
        target!.Session.Select(result!.Matches.Where(m => m.IsVisible).Select(m => m.ItemId));
    }
    private void Replace(bool all)
    {
        if (!Current) throw new InvalidOperationException("Search results are stale. Search again before replacing.");
        if (!all && rows.SelectedIndex < 0) return;
        LastReplacement = result!.Replace(target!.Document, replacement.Text, all ? null : rows.SelectedIndex, resultRevision);
        summary.Text = $"{LastReplacement.Value.ReplacedOccurrences} occurrences replaced; {LastReplacement.Value.SkippedReadOnly} read-only matches skipped.";
        RequestSearch();
    }
    private void UpdateCommands()
    {
        if (next is null) return;
        var available = Current && !result!.Matches.IsEmpty;
        previous.IsEnabled = next.IsEnabled = selectAll.IsEnabled = available;
        replaceOne.IsEnabled = available && rows.SelectedIndex >= 0 && result!.Matches[rows.SelectedIndex].CanReplace;
        replaceAll.IsEnabled = available && !result!.IsTruncated && result.Matches.Any(m => m.CanReplace);
    }
    private void ShowError(Exception ex) { rows.ItemsSource = null; LastError = ex.Message; summary.Text = ex.Message; IsReady = false; result = null; UpdateCommands(); }
    private void Changed(object? sender, GraphChangedEventArgs args) { registry = null; RequestSearch(); }
    private void Rebind(object? sender, GraphDocumentChangedEventArgs args)
    { args.Previous.Changed -= Changed; args.Current.Changed += Changed; registry = null; scope = null; RequestSearch(); }
    private void Detach()
    {
        CancelPending(); if (target is null) return;
        target.Document.Changed -= Changed; target.DocumentChanged -= Rebind; target.KeyDown -= TargetKey; target.SizeChanged -= TargetSizeChanged;
    }
    private void Key(object sender, KeyRoutedEventArgs args)
    {
        if (args.Key == VirtualKey.Escape) { Close(); args.Handled = true; }
        else if (args.Key is VirtualKey.Enter or VirtualKey.F3)
        {
            var shift = Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Shift).HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);
            FindNext(shift); args.Handled = true;
        }
    }
    public new void Dispose() { if (disposed) return; Detach(); disposed = true; target = null; registry = null; result = null; rows.ItemsSource = null; }
}
