using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using VisualizeScene.Core;
using VisualizeScene.Editing;
using Windows.System;

namespace VisualizeScene.Controls.Uno;

/// <summary>Virtualized node/group outline with independent expansion and graph-selection synchronization.</summary>
public sealed class GraphStructureView : UserControl, IDisposable
{
    private sealed record Row(GraphStructureItem Item, bool Collapsed)
    {
        public override string ToString() => new string(' ', Math.Min(Item.Depth, 12) * 2) +
            (Item.HasChildren ? Collapsed ? "▸ " : "▾ " : "  ") +
            (string.IsNullOrEmpty(Item.Text) ? Item.Id : Item.Text) + (Item.IsVisible ? "" : " (hidden)");
    }
    private readonly ListView list = new() { SelectionMode = ListViewSelectionMode.Single, FontSize = 12 };
    private readonly TextBlock count = new() { FontSize = 11, Margin = new(6), Text = "No graph" };
    private readonly HashSet<string> collapsed = new(StringComparer.Ordinal);
    private readonly Button toggle = new() { Content = "Expand / collapse", FontSize = 11, Padding = new(6, 3, 6, 3) };
    private GraphControl? target;
    private GraphStructureIndex? index;
    private Dictionary<string, int> positions = new(StringComparer.Ordinal);
    private Row[] rows = [];
    private bool binding, disposed;
    public long IndexBuilds { get; private set; }
    public int VisibleRowCount => rows.Length;
    public string? SelectedId => list.SelectedItem is Row row ? row.Item.Id : null;
    public FrameworkElement ItemsControl => list;
    public FrameworkElement ToggleButton => toggle;
    public GraphControl? Target
    {
        get => target;
        set
        {
            ObjectDisposedException.ThrowIf(disposed, this); if (ReferenceEquals(target, value)) return;
            Detach(); target = value; collapsed.Clear(); index = null;
            if (target is not null) { target.Document.Changed += Changed; target.DocumentChanged += Rebind; target.SelectionChanged += Selected; }
            Refresh();
        }
    }
    public GraphStructureView()
    {
        var root = new Grid(); root.RowDefinitions.Add(new() { Height = GridLength.Auto }); root.RowDefinitions.Add(new() { Height = new(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new() { Height = GridLength.Auto }); root.Children.Add(count); Grid.SetRow(list, 1); root.Children.Add(list); Grid.SetRow(toggle, 2); root.Children.Add(toggle); Content = root;
        AutomationProperties.SetName(list, "Graph structure"); AutomationProperties.SetAutomationId(list, "StructureItems");
        AutomationProperties.SetAutomationId(toggle, "StructureToggle");
        list.SelectionChanged += (_, _) =>
        {
            if (binding || disposed || list.SelectedItem is not Row row) return;
            binding = true; try { target?.RevealItem(row.Item.Id); } finally { binding = false; }
        };
        toggle.Click += (_, _) => ToggleSelected();
        list.KeyDown += Key;
    }
    public void ToggleSelected()
    {
        if (list.SelectedItem is not Row { Item.HasChildren: true } row) return;
        if (!collapsed.Add(row.Item.Id)) collapsed.Remove(row.Item.Id);
        BindRows(row.Item.Id);
    }
    private void Key(object sender, KeyRoutedEventArgs args)
    {
        if (list.SelectedItem is not Row row) return;
        if (args.Key == VirtualKey.Right && row.Item.HasChildren && row.Collapsed || args.Key == VirtualKey.Left && row.Item.HasChildren && !row.Collapsed)
        { ToggleSelected(); args.Handled = true; }
    }
    private void Changed(object? sender, GraphChangedEventArgs args) => Refresh();
    private void Selected(object? sender, EventArgs args)
    {
        if (binding || target is null) return;
        var id = target.Session.Selection.FirstOrDefault();
        if (id is null) { binding = true; list.SelectedIndex = -1; binding = false; return; }
        var graph = target.Document.Snapshot.Index();
        if (graph.Labels.TryGetValue(id, out var label)) id = label.OwnerId;
        if (graph.Ports.TryGetValue(id, out var port)) id = port.NodeId;
        if (!positions.TryGetValue(id, out var position)) return;
        binding = true; list.SelectedIndex = position; binding = false;
    }
    private void Rebind(object? sender, GraphDocumentChangedEventArgs args)
    { args.Previous.Changed -= Changed; args.Current.Changed += Changed; collapsed.Clear(); index = null; Refresh(); }
    private void Refresh()
    {
        if (target is null) { index = null; rows = []; positions.Clear(); list.ItemsSource = null; count.Text = "No graph"; return; }
        var graph = target.Document.Snapshot;
        if (!ReferenceEquals(index?.Snapshot, graph)) { index = new(graph); IndexBuilds++; }
        var identities = graph.Index().Nodes; collapsed.RemoveWhere(id => !identities.ContainsKey(id));
        BindRows(SelectedId); Selected(this, EventArgs.Empty);
    }
    private void BindRows(string? selected)
    {
        if (index is null) return;
        rows = index.GetRows(collapsed).Select(item => new Row(item, collapsed.Contains(item.Id))).ToArray();
        positions = rows.Select((row, i) => (row.Item.Id, i)).ToDictionary(x => x.Id, x => x.i, StringComparer.Ordinal);
        binding = true;
        try { list.ItemsSource = rows; list.SelectedIndex = selected is not null && positions.TryGetValue(selected, out var i) ? i : -1; }
        finally { binding = false; }
        count.Text = $"{index.Count:N0} nodes · {rows.Length:N0} shown";
    }
    private void Detach()
    {
        if (target is null) return;
        target.Document.Changed -= Changed; target.DocumentChanged -= Rebind; target.SelectionChanged -= Selected;
    }
    public new void Dispose() { if (disposed) return; Detach(); disposed = true; target = null; index = null; rows = []; list.ItemsSource = null; collapsed.Clear(); positions.Clear(); }
}
