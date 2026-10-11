using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace VisualizeScene.Controls.Uno;

/// <summary>Compact categorized property rows, independent of graph data. Group expansion survives row rebuilds.</summary>
public sealed class EditorPropertyGrid : UserControl
{
    private readonly StackPanel groups = new() { Spacing = 1 };
    private readonly Dictionary<string, (Button Toggle, StackPanel Rows)> sections = new(StringComparer.Ordinal);
    private readonly HashSet<string> collapsed = new(StringComparer.Ordinal);
    private StackPanel? current;
    public EditorPropertyGrid() => Content = groups;
    public int PropertyCount { get; private set; }
    public bool IsGroupExpanded(string name) => sections.ContainsKey(name) && !collapsed.Contains(name);
    public FrameworkElement? FindGroupToggle(string name) => sections.TryGetValue(name, out var section) ? section.Toggle : null;
    public void Clear()
    {
        groups.Children.Clear(); sections.Clear(); current = null; PropertyCount = 0;
    }
    public void AddGroup(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (sections.ContainsKey(name)) throw new ArgumentException("Duplicate property group.", nameof(name));
        var rows = new StackPanel { Spacing = 0, Visibility = collapsed.Contains(name) ? Visibility.Collapsed : Visibility.Visible };
        var toggle = new Button { Content = (collapsed.Contains(name) ? "▸ " : "▾ ") + name,
            FontSize = 12, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, MinHeight = 25, Padding = new(5, 2, 5, 2),
            HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Left,
            Background = Brush(226, 232, 239), BorderThickness = new(0), CornerRadius = new(0) };
        AutomationProperties.SetName(toggle, "Expand or collapse " + name);
        sections.Add(name, (toggle, rows)); groups.Children.Add(toggle); groups.Children.Add(rows); current = rows;
        toggle.Click += (_, _) => SetGroupExpanded(name, collapsed.Contains(name));
    }
    public void SetGroupExpanded(string name, bool expanded)
    {
        if (!sections.TryGetValue(name, out var group)) throw new KeyNotFoundException(name);
        if (expanded) collapsed.Remove(name); else collapsed.Add(name);
        group.Rows.Visibility = expanded ? Visibility.Visible : Visibility.Collapsed;
        group.Toggle.Content = (expanded ? "▾ " : "▸ ") + name;
    }
    public void AddProperty(string label, FrameworkElement editor)
    {
        ArgumentNullException.ThrowIfNull(editor);
        if (current is null) throw new InvalidOperationException("Add a category before adding properties.");
        var row = new Grid { MinHeight = 27, Background = Brush(255, 255, 255) };
        row.ColumnDefinitions.Add(new() { Width = new(86) }); row.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
        var text = new TextBlock { Text = label, FontSize = 11, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap, Margin = new(5, 2, 3, 2) };
        var labelHost = new Border { Child = text, Background = Brush(245, 247, 250), BorderBrush = Brush(213, 220, 228), BorderThickness = new(0, 0, 1, 0) };
        row.Children.Add(labelHost); Grid.SetColumn(editor, 1); editor.HorizontalAlignment = HorizontalAlignment.Stretch; editor.MinWidth = 0; row.Children.Add(editor);
        AutomationProperties.SetName(editor, label);
        current.Children.Add(new Border { Child = row, BorderBrush = Brush(218, 224, 230), BorderThickness = new(0, 0, 0, 1) });
        PropertyCount++;
    }
    private static SolidColorBrush Brush(byte r, byte g, byte b) => new(Windows.UI.Color.FromArgb(255, r, g, b));
}
