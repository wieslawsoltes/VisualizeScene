using System.Globalization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using VisualizeScene.Core;

namespace VisualizeScene.Controls.Uno;

/// <summary>Edits selected graph items through one validated transaction; no direct mutation of model snapshots.</summary>
public sealed class GraphPropertyInspector : UserControl, IDisposable
{
    private readonly StackPanel fields = new() { Spacing = 9 };
    private readonly TextBlock title = new() { Text = "Nothing selected", FontSize = 13, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold };
    private readonly TextBlock message = new() { TextWrapping = TextWrapping.Wrap, FontSize = 11 };
    private GraphControl? target;
    public GraphControl? Target
    {
        get => target;
        set { if (target is not null) { target.SelectionChanged -= Update; target.Document.Changed -= Changed; } target = value; if (target is not null) { target.SelectionChanged += Update; target.Document.Changed += Changed; } Refresh(); }
    }
    public GraphPropertyInspector()
    {
        var panel = new StackPanel { Spacing = 14 }; panel.Children.Add(title); panel.Children.Add(fields); panel.Children.Add(message);
        Content = new ScrollViewer { Content = panel, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
    }
    private void Update(object? sender, EventArgs args) => Refresh();
    private void Changed(object? sender, GraphChangedEventArgs args) => Refresh();
    private TextBox Text(string label, string value)
    {
        var input = new TextBox { Header = label, Text = value, FontSize = 12, MinWidth = 0 };
        fields.Children.Add(input); return input;
    }
    public void Refresh()
    {
        fields.Children.Clear(); message.Text = "";
        if (target is null || target.Session.Selection.Count == 0) { title.Text = "Nothing selected"; message.Text = "Select a node or edge to inspect its properties. Changes are undoable."; return; }
        var ids = target.Session.Selection.ToArray(); title.Text = ids.Length == 1 ? "Item properties" : $"{ids.Length} items selected";
        if (ids.Length > 1) { message.Text = "Use the alignment and grouping commands for multiple items."; return; }
        var index = target.Document.Snapshot.Index(); var id = ids[0];
        var idLabel = new TextBlock { Text = "ID: " + id, FontSize = 10, TextTrimming = TextTrimming.CharacterEllipsis }; fields.Children.Add(idLabel);
        if (index.Nodes.TryGetValue(id, out var node))
        {
            var label = Text("Label", node.Text); label.AcceptsReturn = true;
            var shape = new ComboBox { Header = "Shape", ItemsSource = Enum.GetValues<NodeShape>(), SelectedItem = node.Shape, HorizontalAlignment = HorizontalAlignment.Stretch }; fields.Children.Add(shape);
            var fill = Text("Fill · #RRGGBB", node.Fill); var stroke = Text("Stroke · #RRGGBB", node.Stroke);
            var x = Text("X", F(node.Bounds.X)); var y = Text("Y", F(node.Bounds.Y)); var width = Text("Width", F(node.Bounds.Width)); var height = Text("Height", F(node.Bounds.Height));
            var locked = new CheckBox { Content = "Lock position", IsChecked = node.Locked }; fields.Children.Add(locked);
            Apply(() =>
            {
                var current = target.Document.Snapshot.Index().Nodes[id];
                target.Document.Edit("Edit node properties", edit => edit.Update(current with { Text = label.Text, Shape = (NodeShape)shape.SelectedItem, Fill = fill.Text, Stroke = stroke.Text, Bounds = new(N(x), N(y), N(width), N(height)), Locked = locked.IsChecked == true }));
            });
        }
        else if (index.Edges.TryGetValue(id, out var edge))
        {
            var label = Text("Label", edge.Text); var weight = Text("Weight / capacity", F(edge.Weight)); var stroke = Text("Stroke · #RRGGBB", edge.Stroke);
            var routing = new ComboBox { Header = "Routing", ItemsSource = Enum.GetValues<EdgeRouting>(), SelectedItem = edge.Routing, HorizontalAlignment = HorizontalAlignment.Stretch }; fields.Children.Add(routing);
            var directed = new CheckBox { Content = "Directed arrow", IsChecked = edge.Directed }; fields.Children.Add(directed);
            var endpoint = new TextBlock { Text = edge.SourceId + " → " + edge.TargetId, TextWrapping = TextWrapping.Wrap, FontSize = 11 }; fields.Children.Add(endpoint);
            Apply(() => { var current = target.Document.Snapshot.Index().Edges[id]; target.Document.Edit("Edit edge properties", edit => edit.Update(current with { Text = label.Text, Weight = N(weight), Stroke = stroke.Text, Routing = (EdgeRouting)routing.SelectedItem, Directed = directed.IsChecked == true })); });
            var clear = new Button { Content = "Clear bends", HorizontalAlignment = HorizontalAlignment.Stretch }; clear.Click += (_, _) => target.Document.Edit("Clear edge bends", edit => edit.Update(edit.Edge(id) with { Bends = [] })); fields.Children.Add(clear);
        }
    }
    private void Apply(Action action)
    {
        var apply = new Button { Content = "Apply properties", HorizontalAlignment = HorizontalAlignment.Stretch };
        apply.Click += (_, _) => { try { action(); message.Text = "Properties applied."; } catch (Exception ex) { message.Text = ex.Message; } }; fields.Children.Add(apply);
    }
    private static string F(double value) => value.ToString("0.###", CultureInfo.InvariantCulture);
    private static double N(TextBox input) => double.Parse(input.Text, NumberStyles.Float, CultureInfo.InvariantCulture);
    public void Dispose() => Target = null;
}
