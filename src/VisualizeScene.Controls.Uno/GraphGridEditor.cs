using System.Globalization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using SkiaSharp;
using Uno.WinUI.Graphics2DSK;
using VisualizeScene.Core;
using VisualizeScene.Rendering.Skia;
using Windows.Foundation;

namespace VisualizeScene.Controls.Uno;

/// <summary>Compact staged grid preferences. Does not mutate a graph or session; the host explicitly applies TryReadSettings.</summary>
public sealed class GraphGridEditor : UserControl, IDisposable
{
    private sealed class Preview : SKCanvasElement, IDisposable
    {
        private readonly GraphGridRenderer renderer = new();
        private bool disposed;
        public GraphGridSettings Settings { get; set; } = GraphGridSettings.Default;
        protected override void RenderOverride(SKCanvas canvas, Size area)
        {
            if (disposed) return;
            canvas.Clear(SKColors.White); renderer.Render(canvas, Settings, new(new(), 1), area.Width, area.Height);
        }
        public void Dispose() { if (disposed) return; disposed = true; renderer.Dispose(); }
    }
    private readonly EditorPropertyGrid properties = new();
    private readonly Dictionary<string, FrameworkElement> elements = [];
    private readonly TextBox horizontal = Input(), vertical = Input(), originX = Input(), originY = Input(), color = Input(), thickness = Input();
    private readonly ComboBox pattern = Choice(["Dots", "Crosses", "Lines", "Horizontal lines", "Vertical lines"]);
    private readonly ComboBox axes = Choice(["None", "Vertical lines (X)", "Horizontal lines (Y)", "Grid points (X and Y)"]);
    private readonly ComboBox anchor = Choice(["Top-left corner", "Node center"]);
    private readonly CheckBox visible = new() { Content = "Show grid", FontSize = 12, MinHeight = 30 };
    private readonly CheckBox snapping = new() { Content = "Snap to grid", FontSize = 12, MinHeight = 30 };
    private readonly TextBlock feedback = new() { FontSize = 11, Height = 34, TextWrapping = TextWrapping.Wrap };
    private readonly Preview preview = new() { Height = 70, HorizontalAlignment = HorizontalAlignment.Stretch };
    private bool loading, disposed;
    public string ValidationError { get; private set; } = "";
    public event EventHandler? DraftChanged;
    public FrameworkElement? FindElement(string name) => elements.GetValueOrDefault(name);
    public GraphGridEditor()
    {
        MinWidth = 280; MaxWidth = 440; MaxHeight = 550; HorizontalAlignment = HorizontalAlignment.Stretch;
        var root = new StackPanel { Spacing = 5 };
        var toggles = new Grid(); toggles.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) }); toggles.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
        toggles.Children.Add(visible); Grid.SetColumn(snapping, 1); toggles.Children.Add(snapping); root.Children.Add(toggles);
        properties.AddGroup("Grid appearance"); Add("Pattern", "Pattern", pattern); Add("Color", "Color", color); Add("Thickness", "Thickness", thickness);
        properties.AddGroup("World coordinates"); Add("Spacing X", "Horizontal", horizontal); Add("Spacing Y", "Vertical", vertical); Add("Origin X", "OriginX", originX); Add("Origin Y", "OriginY", originY);
        properties.AddGroup("Snapping"); Add("Snap axes", "Axes", axes); Add("Node anchor", "Anchor", anchor); root.Children.Add(properties);
        root.Children.Add(new Border { Child = preview, BorderThickness = new(1), BorderBrush = NodePalette.Brush("#BCC6D0") });
        root.Children.Add(new TextBlock { Text = "Preview at 100%. Visibility and snapping are independent. Spacing is in graph units; thickness is in screen units.", FontSize = 11, TextWrapping = TextWrapping.Wrap });
        var reset = new Button { Content = "Reset to defaults", FontSize = 12, MinHeight = 27, Padding = new(7,3,7,3) };
        Register("Reset", reset); Register("Visible", visible); Register("Snap", snapping); Register("Preview", preview); Register("Feedback", feedback);
        reset.Click += (_, _) => LoadSettings(GraphGridSettings.Default);
        // Only the preference rows and preview scroll. Feedback and Reset must remain
        // reachable even when the native dialog constrains the available height.
        var scrolling = new ScrollViewer { Content = root, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        var footer = new Grid { Margin = new(0,5,0,0) };
        footer.RowDefinitions.Add(new() { Height = new(34) }); footer.RowDefinitions.Add(new() { Height = GridLength.Auto });
        footer.Children.Add(feedback); Grid.SetRow(reset,1); reset.HorizontalAlignment = HorizontalAlignment.Left; footer.Children.Add(reset);
        var layout = new Grid(); layout.RowDefinitions.Add(new() { Height = new(1,GridUnitType.Star) });
        layout.RowDefinitions.Add(new() { Height = GridLength.Auto }); layout.Children.Add(scrolling); Grid.SetRow(footer,1); layout.Children.Add(footer);
        Register("Body",scrolling); Register("Footer",footer); Content = layout;
        foreach (var field in new[] { horizontal, vertical, originX, originY, color, thickness }) field.TextChanged += (_, _) => Refresh();
        foreach (var field in new[] { pattern, axes, anchor }) field.SelectionChanged += (_, _) => Refresh();
        visible.Checked += (_, _) => Refresh(); visible.Unchecked += (_, _) => Refresh(); snapping.Checked += (_, _) => Refresh(); snapping.Unchecked += (_, _) => Refresh();
        LoadSettings(GraphGridSettings.Default);
    }
    private static TextBox Input() => new() { FontSize = 12, MinHeight = 27, Padding = new(5,2,5,2), BorderThickness = new(0) };
    private static ComboBox Choice(string[] items) => new() { ItemsSource = items, FontSize = 12, MinHeight = 27, Padding = new(5,2,5,2), BorderThickness = new(0), HorizontalAlignment = HorizontalAlignment.Stretch };
    private void Add(string label, string name, FrameworkElement editor) { properties.AddProperty(label, editor); Register(name, editor); }
    private void Register(string name, FrameworkElement element) { elements[name] = element; AutomationProperties.SetAutomationId(element, "GridOption" + name); }
    public void LoadSettings(GraphGridSettings settings)
    {
        ObjectDisposedException.ThrowIf(disposed, this); ArgumentNullException.ThrowIfNull(settings); settings.Validate(); loading = true;
        try
        {
            static string N(double value) => value.ToString("R", CultureInfo.InvariantCulture);
            horizontal.Text = N(settings.HorizontalSpacing); vertical.Text = N(settings.VerticalSpacing); originX.Text = N(settings.Origin.X); originY.Text = N(settings.Origin.Y);
            color.Text = settings.Color; thickness.Text = N(settings.Thickness); pattern.SelectedIndex = (int)settings.Pattern; axes.SelectedIndex = (int)settings.SnapAxes; anchor.SelectedIndex = (int)settings.NodeAnchor;
            visible.IsChecked = settings.Visible; snapping.IsChecked = settings.SnapEnabled;
        }
        finally { loading = false; }
        Refresh();
    }
    public bool TryReadSettings(out GraphGridSettings settings)
    {
        ObjectDisposedException.ThrowIf(disposed, this); settings = GraphGridSettings.Default;
        static double N(TextBox input, string name)
        {
            if (!double.TryParse(input.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) || !double.IsFinite(number))
                throw new ArgumentException(name + " must be a finite number (use a decimal point).");
            return number;
        }
        try
        {
            var next = new GraphGridSettings { HorizontalSpacing = N(horizontal,"Spacing X"), VerticalSpacing = N(vertical,"Spacing Y"), Origin = new(N(originX,"Origin X"), N(originY,"Origin Y")),
                Color = color.Text.Trim(), Thickness = N(thickness,"Thickness"), Pattern = (GridPattern)pattern.SelectedIndex, SnapAxes = (GridSnapAxes)axes.SelectedIndex,
                NodeAnchor = (GridNodeAnchor)anchor.SelectedIndex, Visible = visible.IsChecked == true, SnapEnabled = snapping.IsChecked == true };
            next.Validate(); settings = next; ValidationError = ""; feedback.Text = ""; return true;
        }
        catch (ArgumentException ex) { ValidationError = ex.Message.Split('\n')[0]; feedback.Text = ValidationError; return false; }
    }
    public void ShowError(string message) { ValidationError = message; feedback.Text = message; }
    private void Refresh()
    {
        if (loading || disposed) return;
        if (TryReadSettings(out var next)) { preview.Settings = next; preview.Invalidate(); }
        DraftChanged?.Invoke(this, EventArgs.Empty);
    }
    public void Dispose() { if (disposed) return; disposed = true; preview.Dispose(); }
}
