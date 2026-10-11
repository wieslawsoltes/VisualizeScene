using System.Globalization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Automation;
using VisualizeScene.Core;

namespace VisualizeScene.Controls.Uno;

/// <summary>Edits selected graph items through one validated transaction; no direct mutation of model snapshots.</summary>
public sealed class GraphPropertyInspector : UserControl, IDisposable
{
    private readonly StackPanel fields = new() { Spacing = 9 };
    private readonly TextBlock title = new() { Text = "Nothing selected", FontSize = 13, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold };
    private readonly TextBlock message = new() { TextWrapping = TextWrapping.Wrap, FontSize = 11 };
    private readonly Dictionary<string, FrameworkElement> editors = new(StringComparer.Ordinal);
    public FrameworkElement? FindPropertyEditor(string name) => editors.GetValueOrDefault(name);
    private GraphControl? target;
    private GraphDocument? inspectedDocument;
    private long inspectedRevision;
    public GraphControl? Target
    {
        get => target;
        set { if (target is not null) { target.SelectionChanged -= Update; target.DocumentChanged -= Rebind; target.Document.Changed -= Changed; } target = value; if (target is not null) { target.SelectionChanged += Update; target.DocumentChanged += Rebind; target.Document.Changed += Changed; } Refresh(); }
    }
    public GraphPropertyInspector()
    {
        var panel = new StackPanel { Spacing = 14 }; panel.Children.Add(title); panel.Children.Add(fields); panel.Children.Add(message);
        Content = new ScrollViewer { Content = panel, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
    }
    private void Update(object? sender, EventArgs args) => Refresh();
    private void Rebind(object? sender, GraphDocumentChangedEventArgs args)
    {
        args.Previous.Changed -= Changed; args.Current.Changed += Changed; Refresh();
    }
    private void Changed(object? sender, GraphChangedEventArgs args) => Refresh();
    private TextBox Text(string label, string value)
    {
        var input = new TextBox { Header = label, Text = value, FontSize = 12, MinWidth = 0 };
        var name = label.Split(' ')[0]; editors[name] = input;
        AutomationProperties.SetAutomationId(input, "Property" + name);
        fields.Children.Add(input); return input;
    }
    public void Refresh()
    {
        fields.Children.Clear(); editors.Clear(); message.Text = "";
        inspectedDocument = target?.Document; inspectedRevision = inspectedDocument?.Revision ?? 0;
        if (target is null || target.Session.Selection.Count == 0) { title.Text = "Nothing selected"; message.Text = "Select a node, edge, port or label to inspect its properties. Changes are undoable."; return; }
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
                var bounds = new RectD(N(x), N(y), N(width), N(height));
                target.Document.Edit("Edit node properties", edit =>
                {
                    // Set bounds before changing the lock flag; an already locked item cannot be moved silently.
                    edit.SetNodeBounds(id, bounds);
                    edit.Update(edit.Node(id) with { Text = label.Text, Shape = (NodeShape)shape.SelectedItem, Fill = fill.Text, Stroke = stroke.Text, Locked = locked.IsChecked == true });
                });
            });
        }
        else if (index.Edges.TryGetValue(id, out var edge))
        {
            var label = Text("Label", edge.Text); var weight = Text("Weight / capacity", F(edge.Weight)); var stroke = Text("Stroke · #RRGGBB", edge.Stroke);
            var routing = new ComboBox { Header = "Routing", ItemsSource = Enum.GetValues<EdgeRouting>(), SelectedItem = edge.Routing, HorizontalAlignment = HorizontalAlignment.Stretch }; fields.Children.Add(routing);
            var directed = new CheckBox { Content = "Directed arrow", IsChecked = edge.Directed }; fields.Children.Add(directed);
            var endpoint = new TextBlock { Text = edge.SourceId + " → " + edge.TargetId, TextWrapping = TextWrapping.Wrap, FontSize = 11 }; fields.Children.Add(endpoint);
            Apply(() => { var current = target.Document.Snapshot.Index().Edges[id]; target.Document.Edit("Edit edge properties", edit => edit.Update(current with { Text = label.Text, Weight = N(weight), Stroke = stroke.Text, Routing = (EdgeRouting)routing.SelectedItem, Directed = directed.IsChecked == true })); });
            var clear = new Button { Content = "Clear bends", HorizontalAlignment = HorizontalAlignment.Stretch };
            Bind(clear, () => target.Document.Edit("Clear edge bends", edit => edit.Update(edit.Edge(id) with { Bends = [] }))); fields.Children.Add(clear);
        }
        else if (index.Labels.TryGetValue(id, out var independent))
        {
            fields.Children.Add(new TextBlock { Text = "Owner: " + independent.OwnerId, FontSize = 11 });
            var text = Text("Text", independent.Text); text.AcceptsReturn = true;
            var x = Text("Offset X", F(independent.Offset.X)); var y = Text("Offset Y", F(independent.Offset.Y));
            var size = Text("Font size", F(independent.FontSize));
            Apply(() =>
            {
                var current = target.Document.Snapshot.Index().Labels[id];
                target.Document.Edit("Edit label properties", edit => edit.Update(current with { Text = text.Text, Offset = new(N(x), N(y)), FontSize = N(size) }));
            });
            message.Text = "This label follows its owner. Drag it to change its offset; F2 edits its text.";
        }
        else if (index.Ports.TryGetValue(id, out var port))
        {
            fields.Children.Add(new TextBlock { Text = "Owner: " + port.NodeId, FontSize = 11 });
            var x = Text("Relative X · 0 to 1", F(port.RelativeLocation.X));
            var y = Text("Relative Y · 0 to 1", F(port.RelativeLocation.Y));
            Apply(() =>
            {
                var rx = N(x); var ry = N(y);
                if (!double.IsFinite(rx) || !double.IsFinite(ry) || rx < 0 || rx > 1 || ry < 0 || ry > 1)
                    throw new ArgumentException("Relative port coordinates must be between 0 and 1.");
                var node = target.Document.Snapshot.Index().Nodes[port.NodeId];
                target.Session.MovePort(id, new(node.Bounds.X + rx * node.Bounds.Width, node.Bounds.Y + ry * node.Bounds.Height));
            });
            message.Text = "Drag the port to relocate attached edges. Delete detaches its endpoints and remains undoable.";
        }
    }
    private void Apply(Action action)
    {
        var apply = new Button { Content = "Apply properties", HorizontalAlignment = HorizontalAlignment.Stretch };
        editors["Apply"] = apply; AutomationProperties.SetAutomationId(apply, "PropertyApply");
        Bind(apply, action); fields.Children.Add(apply);
    }
    private void Bind(Button button, Action action)
    {
        var document = inspectedDocument; var revision = inspectedRevision;
        button.Click += (_, _) =>
        {
            try
            {
                if (target?.Document != document || document?.Revision != revision)
                    throw new InvalidOperationException("The graph changed; review the current properties before applying.");
                action(); message.Text = "Properties applied.";
            }
            catch (Exception ex) { message.Text = ex.Message; }
        };
    }
    private static string F(double value) => value.ToString("R", CultureInfo.InvariantCulture);
    private static double N(TextBox input) => double.Parse(input.Text, NumberStyles.Float, CultureInfo.InvariantCulture);
    public new void Dispose() => Target = null;
}
