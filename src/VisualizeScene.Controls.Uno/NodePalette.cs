using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using VisualizeScene.Core;
using VisualizeScene.Editing;

namespace VisualizeScene.Controls.Uno;

public sealed record NodeTemplate(string Name, NodeShape Shape, string Fill, string Stroke, string Label);

/// <summary>Reusable list or tile palette. Templates are validated before publication; selection is visible and keyboard accessible.</summary>
public sealed class NodePalette : UserControl
{
    private readonly Grid tiles = new();
    private readonly List<Button> buttons = [];
    private bool useTiles;
    public event EventHandler<NodeTemplate>? TemplateSelected;
    public IReadOnlyList<NodeTemplate> Items { get; private set; } = [];
    public GraphControl? Target { get; set; }
    public NodeTemplate? SelectedTemplate { get; private set; }
    public bool UseTiles { get => useTiles; set { if (useTiles == value) return; useTiles = value; Rebuild(); } }
    public FrameworkElement? GetTemplateButton(int index) => index >= 0 && index < buttons.Count ? buttons[index] : null;
    public NodePalette()
    {
        Content = tiles;
        SetItems([
            new("Process", NodeShape.RoundedRectangle, "#DDEBFA", "#4778A5", "Process"),
            new("Decision", NodeShape.Diamond, "#FFF1CC", "#C39A38", "Decision?"),
            new("Start / end", NodeShape.Ellipse, "#DDEFE3", "#5D9770", "Start"),
            new("Data", NodeShape.Parallelogram, "#E9E2F3", "#9375AD", "Data"),
            new("Preparation", NodeShape.Hexagon, "#F9E5DB", "#BF8063", "Prepare"),
            new("Entity", NodeShape.Rectangle, "#E2EDF0", "#678F99", "Entity")
        ]);
    }
    public void SetItems(IEnumerable<NodeTemplate> templates)
    {
        ArgumentNullException.ThrowIfNull(templates); var items = templates.ToArray();
        foreach (var item in items)
            if (item is null || string.IsNullOrWhiteSpace(item.Name) || item.Label is null || !Enum.IsDefined(item.Shape) || !GraphValidation.ValidColor(item.Fill) || !GraphValidation.ValidColor(item.Stroke))
                throw new ArgumentException("Palette templates require names, labels and valid shape/colors.", nameof(templates));
        Items = Array.AsReadOnly(items); SelectedTemplate = null; Rebuild();
    }
    private void Rebuild()
    {
        tiles.Children.Clear(); tiles.RowDefinitions.Clear(); tiles.ColumnDefinitions.Clear(); buttons.Clear();
        var columns = useTiles ? 3 : 1;
        for (var i = 0; i < columns; i++) tiles.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
        for (var i = 0; i < (Items.Count + columns - 1) / columns; i++) tiles.RowDefinitions.Add(new() { Height = GridLength.Auto });
        for (var i = 0; i < Items.Count; i++)
        {
            var item = Items[i];
            var button = new Button { HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = useTiles ? HorizontalAlignment.Center : HorizontalAlignment.Left,
                Padding = new(5), Margin = new(2), MinWidth = 0, MinHeight = useTiles ? 46 : 35, Background = Brush("#FFFFFF"), BorderBrush = Brush("#D5DCE3") };
            var row = new StackPanel { Orientation = useTiles ? Orientation.Vertical : Orientation.Horizontal, Spacing = 4 };
            Shape swatch = item.Shape switch
            {
                NodeShape.Ellipse => new Ellipse(),
                NodeShape.Diamond => new Polygon { Points = new PointCollection { new(17, 0), new(34, 12), new(17, 24), new(0, 12) } },
                NodeShape.Hexagon => new Polygon { Points = new PointCollection { new(8.5, 0), new(25.5, 0), new(34, 12), new(25.5, 24), new(8.5, 24), new(0, 12) } },
                NodeShape.Parallelogram => new Polygon { Points = new PointCollection { new(7, 0), new(34, 0), new(27, 24), new(0, 24) } },
                _ => new Rectangle { RadiusX = item.Shape == NodeShape.RoundedRectangle ? 4 : 0, RadiusY = item.Shape == NodeShape.RoundedRectangle ? 4 : 0 }
            };
            swatch.Width = 34; swatch.Height = 24; swatch.Fill = Brush(item.Fill); swatch.Stroke = Brush(item.Stroke); swatch.StrokeThickness = 1;
            row.Children.Add(swatch);
            if (!useTiles) row.Children.Add(new TextBlock { Text = item.Name, VerticalAlignment = VerticalAlignment.Center, FontSize = 12 });
            button.Content = row; ToolTipService.SetToolTip(button, item.Name + " — select, then click the diagram");
            AutomationProperties.SetName(button, "Create " + item.Name); AutomationProperties.SetAutomationId(button, "Palette" + item.Shape);
            button.Click += (_, _) =>
            {
                SelectedTemplate = item;
                foreach (var b in buttons) b.Background = Brush(ReferenceEquals(button, b) ? "#DCEBFA" : "#FFFFFF");
                if (Target is not null)
                {
                    Target.Session.DefaultShape = item.Shape; Target.Session.DefaultFill = item.Fill; Target.Session.DefaultStroke = item.Stroke; Target.Session.DefaultText = item.Label; Target.SetMode(EditorMode.CreateNode);
                }
                TemplateSelected?.Invoke(this, item);
            };
            Grid.SetRow(button, i / columns); Grid.SetColumn(button, i % columns); tiles.Children.Add(button); buttons.Add(button);
        }
    }
    internal static SolidColorBrush Brush(string color) => new(Windows.UI.Color.FromArgb(255, Convert.ToByte(color.Substring(1, 2), 16), Convert.ToByte(color.Substring(3, 2), 16), Convert.ToByte(color.Substring(5, 2), 16)));
}
