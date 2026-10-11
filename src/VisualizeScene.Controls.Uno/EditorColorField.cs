using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using VisualizeScene.Core;

namespace VisualizeScene.Controls.Uno;

/// <summary>Literal #RRGGBB editor with preview and a lazily-created swatch flyout. Typing never edits a graph.</summary>
public sealed class EditorColorField : UserControl
{
    private static readonly string[] colors = ["#FFFFFF", "#E6E6E6", "#808080", "#000000", "#FFCC66", "#FFF1CC", "#DDEBFA", "#DDEFE3", "#E9E2F3", "#F9E5DB", "#E2EDF0", "#F44336", "#4778A5", "#5D9770", "#9375AD", "#C39A38"];
    private readonly TextBox input = new() { FontSize = 11, MinHeight = 26, Padding = new(3, 2, 3, 2), BorderThickness = new(0), CornerRadius = new(0) };
    private readonly Button preview = new() { Width = 26, MinWidth = 0, MinHeight = 26, Padding = new(3), BorderThickness = new(1), CornerRadius = new(0) };
    private readonly Dictionary<string, Button> swatches = new(StringComparer.OrdinalIgnoreCase);
    private Flyout? flyout;
    public TextBox Input => input;
    public Button PaletteButton => preview;
    public string Value { get => input.Text; set => input.Text = value; }
    public bool IsValid => GraphValidation.ValidColor(Value);
    public FrameworkElement? FindSwatch(string color) => swatches.GetValueOrDefault(color);
    public event EventHandler? ValueChanged;
    public EditorColorField()
    {
        var row = new Grid(); row.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) }); row.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        row.Children.Add(input); Grid.SetColumn(preview, 1); row.Children.Add(preview); Content = row;
        AutomationProperties.SetName(preview, "Choose color"); ToolTipService.SetToolTip(preview, "Choose a color; Apply properties commits it");
        input.TextChanged += (_, _) => { UpdatePreview(); ValueChanged?.Invoke(this, EventArgs.Empty); };
        preview.Click += (_, _) => { EnsurePalette(); flyout!.ShowAt(preview); };
        Value = "#FFFFFF";
    }
    private void UpdatePreview()
    {
        preview.Background = IsValid ? NodePalette.Brush(Value) : new SolidColorBrush(Microsoft.UI.Colors.LightGray);
        preview.Content = IsValid ? "" : "?";
        AutomationProperties.SetName(preview, IsValid ? "Choose color, current " + Value : "Choose color, invalid color text");
    }
    private void EnsurePalette()
    {
        if (flyout is not null) return;
        var grid = new Grid();
        for (var i = 0; i < 4; i++) { grid.ColumnDefinitions.Add(new() { Width = GridLength.Auto }); grid.RowDefinitions.Add(new() { Height = GridLength.Auto }); }
        for (var i = 0; i < colors.Length; i++)
        {
            var color = colors[i]; var button = new Button { Width = 30, Height = 27, MinWidth = 0, MinHeight = 0, Padding = new(0), Margin = new(2), Background = NodePalette.Brush(color), BorderThickness = new(1), CornerRadius = new(0) };
            AutomationProperties.SetName(button, color); ToolTipService.SetToolTip(button, color);
            button.Click += (_, _) => { Value = color; flyout!.Hide(); input.Focus(FocusState.Programmatic); };
            Grid.SetColumn(button, i % 4); Grid.SetRow(button, i / 4); grid.Children.Add(button); swatches.Add(color, button);
        }
        flyout = new Flyout { Content = grid };
    }
}
