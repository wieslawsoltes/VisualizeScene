using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using SkiaSharp;
using Uno.WinUI.Graphics2DSK;
using VisualizeScene.Core;
using VisualizeScene.Editing;
using VisualizeScene.Rendering.Skia;
using Windows.Foundation;

namespace VisualizeScene.Controls.Uno;

public sealed record EdgeTemplate(string Name, EdgeCreationOptions Options);

/// <summary>Reusable visual connection presets, rendered with the same edge painter as the graph.</summary>
/// <remarks>Templates configure future connections only. Existing edges and algorithm direction are not changed.</remarks>
public sealed class EdgePalette : UserControl
{
    private sealed class Preview(EdgeCreationOptions options) : SKCanvasElement
    {
        protected override void RenderOverride(SKCanvas canvas, Size area)
        {
            var save = canvas.Save();
            try
            {
                var width = Math.Max(25, area.Width); var y = area.Height / 2;
                PointD[] path = options.Routing == EdgeRouting.Orthogonal
                    ? [new(4, y + 5), new(width / 2, y + 5), new(width / 2, y - 5), new(width - 4, y - 5)]
                    : [new(4, y), new(width - 4, y)];
                using var painter = new EdgeStyleRenderer();
                painter.Draw(canvas, new GraphEdge("preview", "source", "target", Stroke: options.Stroke) { Style = options.Style }, path);
            }
            finally { canvas.RestoreToCount(save); }
        }
    }
    private readonly Grid grid = new();
    private readonly List<Button> buttons = [];
    public GraphControl? Target { get; set; }
    public IReadOnlyList<EdgeTemplate> Items { get; private set; } = [];
    public EdgeTemplate? SelectedTemplate { get; private set; }
    public event EventHandler<EdgeTemplate>? TemplateSelected;
    public event EventHandler<string>? Error;
    public FrameworkElement? GetTemplateButton(int index) => index >= 0 && index < buttons.Count ? buttons[index] : null;
    public EdgePalette()
    {
        Content = grid;
        SetItems([
            new("Orthogonal arrow", new()),
            new("Dashed arrow", new(EdgeRouting.Polyline, "#607D99", new(2, EdgeDash.Dash))),
            new("Solid connection", new(EdgeRouting.Polyline, "#607D99", new(TargetArrow: EdgeArrow.None))),
            new("Open arrow", new(EdgeRouting.Polyline, "#607D99", new(TargetArrow: EdgeArrow.Open))),
            new("Diamond source", new(EdgeRouting.Polyline, "#607D99", new(SourceArrow: EdgeArrow.HollowDiamond, TargetArrow: EdgeArrow.None))),
            new("Dotted endpoints", new(EdgeRouting.Polyline, "#607D99", new(2, EdgeDash.Dot, EdgeArrow.Circle, EdgeArrow.HollowCircle)))
        ]);
    }
    public void SetItems(IEnumerable<EdgeTemplate> items)
    {
        ArgumentNullException.ThrowIfNull(items); var copy = items.ToArray();
        foreach (var template in copy)
        {
            if (template is null || string.IsNullOrWhiteSpace(template.Name) || template.Options is null)
                throw new ArgumentException("Connection templates require names and options.", nameof(items));
            template.Options.Validate();
        }
        // Validate the whole incoming list before replacing the old UI.
        Items = Array.AsReadOnly(copy); SelectedTemplate = null; buttons.Clear(); grid.Children.Clear();
        grid.RowDefinitions.Clear(); grid.ColumnDefinitions.Clear();
        for (var column = 0; column < 2; column++) grid.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
        for (var row = 0; row < (copy.Length + 1) / 2; row++) grid.RowDefinitions.Add(new() { Height = GridLength.Auto });
        for (var i = 0; i < copy.Length; i++)
        {
            var template = copy[i];
            var button = new Button { Content = new Preview(template.Options) { Height = 28, IsHitTestVisible = false },
                MinWidth = 0, MinHeight = 34, Margin = new(2), Padding = new(3),
                HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch,
                Background = NodePalette.Brush("#FFFFFF"), BorderBrush = NodePalette.Brush("#D5DCE3"), CornerRadius = new(1) };
            AutomationProperties.SetName(button, "Create " + template.Name);
            ToolTipService.SetToolTip(button, template.Name + " — click source, then target");
            button.Click += (_, _) =>
            {
                try
                {
                    if (Target is not null) { Target.Session.EdgeDefaults = template.Options; Target.SetMode(EditorMode.CreateEdge); }
                    SelectedTemplate = template;
                    foreach (var other in buttons) other.Background = NodePalette.Brush(ReferenceEquals(other, button) ? "#DCEBFA" : "#FFFFFF");
                    TemplateSelected?.Invoke(this, template);
                }
                catch (Exception ex) { Error?.Invoke(this, ex.Message); }
            };
            Grid.SetColumn(button, i % 2); Grid.SetRow(button, i / 2); grid.Children.Add(button); buttons.Add(button);
        }
    }
}
