using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace VisualizeScene.Controls.Uno;

/// <summary>Compact titled pane with real collapse behavior, independent of any graph or workbench.</summary>
public sealed class EditorPane : UserControl
{
    private readonly TextBlock title = new() { FontSize = 12, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Margin = new(7, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
    private readonly ContentControl body = new() { HorizontalContentAlignment = HorizontalAlignment.Stretch, VerticalContentAlignment = VerticalAlignment.Stretch, IsTabStop = false };
    private readonly Button collapse = new() { Content = "−", Padding = new(3, 0, 3, 0), MinWidth = 22, MinHeight = 20, FontSize = 12, IsTabStop = true };
    private readonly Grid layout = new();
    private bool isCollapsed;
    public string Title { get => title.Text; set { title.Text = value; AutomationProperties.SetName(collapse, "Collapse or expand " + value); } }
    public UIElement? PaneContent { get => body.Content as UIElement; set => body.Content = value; }
    public bool IsCollapsed
    {
        get => isCollapsed;
        set { if (value == isCollapsed) return; isCollapsed = value; body.Visibility = value ? Visibility.Collapsed : Visibility.Visible; collapse.Content = value ? "+" : "−"; CollapsedChanged?.Invoke(this, EventArgs.Empty); }
    }
    public FrameworkElement CollapseButton => collapse;
    public event EventHandler? CollapsedChanged;
    public EditorPane()
    {
        layout.RowDefinitions.Add(new() { Height = new(25) }); layout.RowDefinitions.Add(new() { Height = new(1, GridUnitType.Star) });
        var header = new Grid { Background = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 226, 232, 239)) };
        header.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) }); header.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        header.Children.Add(title); Grid.SetColumn(collapse, 1); header.Children.Add(collapse); layout.Children.Add(header);
        Grid.SetRow(body, 1); layout.Children.Add(body); collapse.Click += (_, _) => IsCollapsed = !IsCollapsed;
        Content = new Border { Child = layout, BorderBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 177, 187, 197)), BorderThickness = new(1), Background = new SolidColorBrush(Microsoft.UI.Colors.White) };
    }
}
