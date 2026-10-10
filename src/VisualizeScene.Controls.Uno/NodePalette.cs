using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using VisualizeScene.Core;
using VisualizeScene.Editing;

namespace VisualizeScene.Controls.Uno;

public sealed record NodeTemplate(string Name, NodeShape Shape, string Fill, string Stroke, string Label);

/// <summary>Reusable template palette; applications may replace Items and handle TemplateSelected.</summary>
public sealed class NodePalette : UserControl
{
    private readonly StackPanel stack = new() { Spacing = 5 };
    public event EventHandler<NodeTemplate>? TemplateSelected;
    public IReadOnlyList<NodeTemplate> Items { get; private set; } = [];
    public GraphControl? Target { get; set; }
    public NodePalette()
    {
        Content = stack;
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
        Items = templates.ToArray(); stack.Children.Clear();
        foreach (var item in Items)
        {
            var button = new Button { HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Left, Padding = new(10, 7, 10, 7) };
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
            Shape swatch = item.Shape switch
            {
                NodeShape.Ellipse => new Ellipse(),
                NodeShape.Diamond => new Polygon { Points = new PointCollection { new(17, 0), new(34, 12), new(17, 24), new(0, 12) } },
                NodeShape.Hexagon => new Polygon { Points = new PointCollection { new(8.5, 0), new(25.5, 0), new(34, 12), new(25.5, 24), new(8.5, 24), new(0, 12) } },
                NodeShape.Parallelogram => new Polygon { Points = new PointCollection { new(7, 0), new(34, 0), new(27, 24), new(0, 24) } },
                _ => new Rectangle { RadiusX = item.Shape == NodeShape.RoundedRectangle ? 4 : 0, RadiusY = item.Shape == NodeShape.RoundedRectangle ? 4 : 0 }
            };
            swatch.Width = 34; swatch.Height = 24; swatch.Fill = Brush(item.Fill); swatch.Stroke = Brush(item.Stroke); swatch.StrokeThickness = 1;
            row.Children.Add(swatch); row.Children.Add(new TextBlock { Text = item.Name, VerticalAlignment = VerticalAlignment.Center, FontSize = 12 }); button.Content = row;
            AutomationProperties.SetName(button, "Create " + item.Name); AutomationProperties.SetAutomationId(button, "Palette" + item.Shape);
            button.Click += (_, _) =>
            {
                if (Target is not null)
                {
                    Target.Session.DefaultShape = item.Shape; Target.Session.DefaultFill = item.Fill; Target.Session.DefaultStroke = item.Stroke; Target.Session.DefaultText = item.Label; Target.SetMode(EditorMode.CreateNode);
                }
                TemplateSelected?.Invoke(this, item);
            };
            stack.Children.Add(button);
        }
    }
    internal static SolidColorBrush Brush(string color) => new(Windows.UI.Color.FromArgb(255, Convert.ToByte(color.Substring(1, 2), 16), Convert.ToByte(color.Substring(3, 2), 16), Convert.ToByte(color.Substring(5, 2), 16)));
}
