using System.Globalization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Automation;
using VisualizeScene.Core;

namespace VisualizeScene.Controls.Uno;

/// <summary>Compact property grid. All item changes use a captured document/revision lease and one validated edit.</summary>
public sealed class GraphPropertyInspector : UserControl, IDisposable
{
    private readonly EditorPropertyGrid fields = new();
    private readonly StackPanel commands = new() { Spacing = 5 };
    private readonly TextBlock title = new() { Text = "Nothing selected", FontSize = 12, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold };
    private readonly TextBlock identity = new() { FontSize = 10, TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly TextBlock message = new() { TextWrapping = TextWrapping.Wrap, FontSize = 11 };
    private readonly Dictionary<string, FrameworkElement> editors = new(StringComparer.Ordinal);
    private readonly Dictionary<string, EditorColorField> colors = new(StringComparer.Ordinal);
    private GraphControl? target;
    private GraphDocument? inspectedDocument;
    private long inspectedRevision;
    private bool disposed;
    public string MessageText => message.Text;
    public int PropertyCount => fields.PropertyCount;
    public bool IsPropertyGroupExpanded(string name) => fields.IsGroupExpanded(name);
    public FrameworkElement? FindPropertyEditor(string name)
    {
        if (editors.TryGetValue(name, out var editor)) return editor;
        if (name.StartsWith("Group", StringComparison.Ordinal)) return fields.FindGroupToggle(name[5..]);
        foreach (var pair in colors)
            if (name.StartsWith(pair.Key + "Swatch", StringComparison.Ordinal)) return pair.Value.FindSwatch("#" + name[(pair.Key.Length + 6)..]);
        return null;
    }
    public GraphControl? Target
    {
        get => target;
        set
        {
            ObjectDisposedException.ThrowIf(disposed, this); if (ReferenceEquals(target, value)) return;
            Detach(); target = value;
            if (target is not null) { target.SelectionChanged += Update; target.DocumentChanged += Rebind; target.Document.Changed += Changed; }
            Refresh();
        }
    }
    public GraphPropertyInspector()
    {
        var panel = new StackPanel { Spacing = 7 }; panel.Children.Add(title); panel.Children.Add(identity); panel.Children.Add(fields); panel.Children.Add(commands); panel.Children.Add(message);
        Content = new ScrollViewer { Content = panel, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
    }
    private void Update(object? sender, EventArgs args) => Refresh();
    private void Rebind(object? sender, GraphDocumentChangedEventArgs args)
    { args.Previous.Changed -= Changed; args.Current.Changed += Changed; Refresh(); }
    private void Changed(object? sender, GraphChangedEventArgs args) => Refresh();
    private void Register(string name, FrameworkElement input)
    { editors.Add(name, input); AutomationProperties.SetAutomationId(input, "Property" + name); }
    private TextBox Text(string name, string label, string value, bool multiline = false)
    {
        var input = new TextBox { Text = value, AcceptsReturn = multiline, FontSize = 11, MinWidth = 0, MinHeight = multiline ? 44 : 26, MaxHeight = multiline ? 90 : 48,
            Padding = new(3, 2, 3, 2), BorderThickness = new(0), CornerRadius = new(0) };
        Register(name, input); fields.AddProperty(label, input); return input;
    }
    private EditorColorField Color(string name, string value)
    {
        var input = new EditorColorField { Value = value }; colors.Add(name, input);
        Register(name, input.Input); Register(name + "Palette", input.PaletteButton); fields.AddProperty(name, input); return input;
    }
    private ComboBox Choice<T>(string name, T selected) where T : struct, Enum
    {
        var input = new ComboBox { ItemsSource = Enum.GetValues<T>(), SelectedItem = selected, FontSize = 11, MinHeight = 26,
            Padding = new(3, 2, 3, 2), BorderThickness = new(0), CornerRadius = new(0), HorizontalAlignment = HorizontalAlignment.Stretch };
        Register(name, input); fields.AddProperty(name, input); return input;
    }
    private CheckBox Check(string name, string label, bool value)
    {
        var input = new CheckBox { IsChecked = value, MinHeight = 26, Padding = new(3, 0, 0, 0) };
        Register(name, input); fields.AddProperty(label, input); return input;
    }
    public void Refresh()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        fields.Clear(); commands.Children.Clear(); editors.Clear(); colors.Clear(); message.Text = ""; identity.Text = "";
        inspectedDocument = target?.Document; inspectedRevision = inspectedDocument?.Revision ?? 0;
        if (target is null || target.Session.Selection.Count == 0)
        { title.Text = "Nothing selected"; message.Text = "Select a node, edge, port or label. Property changes are applied together and can be undone."; return; }
        if (target.Session.Selection.Count > 1)
        { title.Text = $"{target.Session.Selection.Count:N0} items selected"; message.Text = "Use the Arrange and Selection commands for multiple items."; return; }
        var index = target.Document.Snapshot.Index(); var id = target.Session.Selection.First(); identity.Text = "ID: " + id; ToolTipService.SetToolTip(identity, id);
        if (index.Nodes.TryGetValue(id, out var node))
        {
            title.Text = node.IsGroup ? "Group properties" : "Node properties";
            fields.AddGroup("Label"); var label = Text("Label", "Text", node.Text, true);
            fields.AddGroup("Appearance"); var shape = Choice("Shape", node.Shape); var fill = Color("Fill", node.Fill); var stroke = Color("Stroke", node.Stroke);
            fields.AddGroup("Geometry"); var x = Text("X", "X", F(node.Bounds.X)); var y = Text("Y", "Y", F(node.Bounds.Y));
            var width = Text("Width", "Width", F(node.Bounds.Width)); var height = Text("Height", "Height", F(node.Bounds.Height));
            foreach (var input in new[] { x, y, width, height }) input.IsReadOnly = node.Locked;
            var locked = Check("Locked", "Lock position", node.Locked);
            Apply(() =>
            {
                var bounds = new RectD(N(x), N(y), N(width), N(height));
                target.Document.Edit("Edit node properties", edit =>
                {
                    edit.SetNodeBounds(id, bounds);
                    edit.Update(edit.Node(id) with { Text = label.Text, Shape = (NodeShape)shape.SelectedItem, Fill = fill.Value, Stroke = stroke.Value, Locked = locked.IsChecked == true });
                });
            });
        }
        else if (index.Edges.TryGetValue(id, out var edge))
        {
            title.Text = "Edge properties";
            fields.AddGroup("Label"); var label = Text("Label", "Text", edge.Text, true);
            fields.AddGroup("Appearance"); var stroke = Color("Stroke", edge.Stroke); var routing = Choice("Routing", edge.Routing); var directed = Check("Directed", "Directed", edge.Directed);
            fields.AddGroup("Data"); var weight = Text("Weight", "Weight", F(edge.Weight));
            Apply(() => target.Document.Edit("Edit edge properties", edit => edit.Update(edit.Edge(id) with
            { Text = label.Text, Weight = N(weight), Stroke = stroke.Value, Routing = (EdgeRouting)routing.SelectedItem, Directed = directed.IsChecked == true })));
            var clear = Command("ClearBends", "Clear bends", () => target.Document.Edit("Clear edge bends", edit => edit.Update(edit.Edge(id) with { Bends = [] })));
            clear.IsEnabled = !edge.Bends.IsDefaultOrEmpty;
            message.Text = edge.SourceId + " → " + edge.TargetId;
        }
        else if (index.Labels.TryGetValue(id, out var independent))
        {
            title.Text = "Label properties"; fields.AddGroup("Label"); var text = Text("Text", "Text", independent.Text, true);
            var size = Text("FontSize", "Font size", F(independent.FontSize)); fields.AddGroup("Geometry");
            var x = Text("OffsetX", "Offset X", F(independent.Offset.X)); var y = Text("OffsetY", "Offset Y", F(independent.Offset.Y));
            Apply(() => target.Session.SetLabelProperties(id, text.Text, new(N(x), N(y)), N(size)));
            message.Text = "Owner: " + independent.OwnerId + ". Offsets follow the owner's position.";
        }
        else if (index.Ports.TryGetValue(id, out var port))
        {
            title.Text = "Port properties"; fields.AddGroup("Geometry");
            var x = Text("RelativeX", "Relative X", F(port.RelativeLocation.X)); var y = Text("RelativeY", "Relative Y", F(port.RelativeLocation.Y));
            Apply(() => target.Session.SetPortLocation(id, new(N(x), N(y))));
            message.Text = "Owner: " + port.NodeId + ". Relative coordinates must be between 0 and 1.";
        }
    }
    private void Apply(Action action) => Command("Apply", "Apply properties", action);
    private Button Command(string name, string text, Action action)
    {
        var button = new Button { Content = text, FontSize = 12, MinHeight = 28, Padding = new(5, 3, 5, 3), HorizontalAlignment = HorizontalAlignment.Stretch };
        Register(name, button); var document = inspectedDocument; var revision = inspectedRevision;
        button.Click += (_, _) =>
        {
            try
            {
                if (disposed || target?.Document != document || document?.Revision != revision)
                    throw new InvalidOperationException("The graph changed; review the current properties before applying.");
                action(); message.Text = "Properties applied.";
            }
            catch (Exception ex) { message.Text = ex.Message; }
        };
        commands.Children.Add(button); return button;
    }
    private static string F(double value) => value.ToString("R", CultureInfo.InvariantCulture);
    private static double N(TextBox input) => double.Parse(input.Text, NumberStyles.Float, CultureInfo.InvariantCulture);
    private void Detach()
    {
        if (target is null) return;
        target.SelectionChanged -= Update; target.DocumentChanged -= Rebind; target.Document.Changed -= Changed;
    }
    public new void Dispose()
    {
        if (disposed) return; Detach(); disposed = true; target = null; inspectedDocument = null; fields.Clear(); commands.Children.Clear(); editors.Clear(); colors.Clear();
    }
}
