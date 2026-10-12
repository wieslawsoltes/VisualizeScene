using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using SkiaSharp;
using Uno.WinUI.Graphics2DSK;
using VisualizeScene.Core;
using VisualizeScene.Editing;
using Windows.Foundation;

namespace VisualizeScene.Controls.Uno;

/// <summary>Reusable compact drawing-order commands. Availability updates on input/document events, not rendered frames.</summary>
public sealed class GraphOrderToolbar : StackPanel, IDisposable
{
    private sealed class OrderIcon(DrawingOrder operation) : SKCanvasElement
    {
        protected override void RenderOverride(SKCanvas canvas, Size area)
        {
            var saved = canvas.Save();
            try
            {
                canvas.Scale((float)(area.Width / 18), (float)(area.Height / 18));
                using var paint = new SKPaint { IsAntialias = true, StrokeWidth = 1.1f };
                void Sheet(float x, float y, SKColor color)
                {
                    paint.Color = color; paint.Style = SKPaintStyle.Fill; canvas.DrawRect(x, y, 9, 9, paint);
                    paint.Color = new SKColor(65, 82, 101); paint.Style = SKPaintStyle.Stroke; canvas.DrawRect(x, y, 9, 9, paint);
                }
                var amber = new SKColor(255, 204, 102); var blue = new SKColor(219, 232, 247);
                Sheet(5, 2, operation == DrawingOrder.SendToBack ? amber : blue);
                Sheet(8, 6, operation == DrawingOrder.SendToBack ? blue : amber);
                if (operation is DrawingOrder.Raise or DrawingOrder.Lower)
                {
                    paint.Color = new SKColor(65, 82, 101);
                    var tip = operation == DrawingOrder.Raise ? 2 : 15;
                    var tail = operation == DrawingOrder.Raise ? 6 : 11;
                    canvas.DrawLine(2, 2, 2, 15, paint);
                    canvas.DrawLine(0, tail, 2, tip, paint); canvas.DrawLine(4, tail, 2, tip, paint);
                }
            }
            finally { canvas.RestoreToCount(saved); }
        }
    }
    private readonly Dictionary<DrawingOrder, Button> buttons = [];
    private GraphControl? target;
    private bool disposed;
    public event EventHandler<string>? Error;
    public GraphControl? Target
    {
        get => target;
        set
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (ReferenceEquals(target, value)) return;
            Detach(); target = value;
            if (target is not null) { target.SelectionChanged += Refresh; target.Document.Changed += Changed; target.DocumentChanged += Rebind; }
            Refresh(this, EventArgs.Empty);
        }
    }
    public FrameworkElement? FindButton(DrawingOrder operation) => buttons.GetValueOrDefault(operation);
    public static string Caption(DrawingOrder operation) => operation switch
    {
        DrawingOrder.BringToFront => "Bring to front", DrawingOrder.SendToBack => "Send to back",
        DrawingOrder.Raise => "Raise one step", DrawingOrder.Lower => "Lower one step",
        _ => throw new ArgumentOutOfRangeException(nameof(operation))
    };
    public GraphOrderToolbar()
    {
        Orientation = Orientation.Horizontal; Spacing = 1;
        foreach (var operation in Enum.GetValues<DrawingOrder>())
        {
            var button = new Button { Content = new OrderIcon(operation) { Width = 18, Height = 18, IsHitTestVisible = false },
                Width = 25, Height = 25, MinWidth = 0, MinHeight = 0, Padding = new(3), CornerRadius = new(1),
                Background = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 246, 248, 251)), IsEnabled = false };
            AutomationProperties.SetName(button, Caption(operation)); AutomationProperties.SetAutomationId(button, "Order" + operation);
            ToolTipService.SetToolTip(button, Caption(operation) + " within the current drawing layer");
            button.Click += (_, _) => { try { target?.ChangeDrawingOrder(operation); } catch (Exception ex) { Error?.Invoke(this, ex.Message); } };
            buttons.Add(operation, button); Children.Add(button);
        }
    }
    private void Refresh(object? sender, EventArgs args)
    {
        var available = target is not null && target.Session.Selection.Any(id => !target.Document.Snapshot.Index().Ports.ContainsKey(id));
        foreach (var button in buttons.Values) button.IsEnabled = available;
    }
    private void Changed(object? sender, GraphChangedEventArgs args) => Refresh(sender, EventArgs.Empty);
    private void Rebind(object? sender, GraphDocumentChangedEventArgs args)
    { args.Previous.Changed -= Changed; args.Current.Changed += Changed; Refresh(sender, EventArgs.Empty); }
    private void Detach()
    {
        if (target is null) return;
        target.SelectionChanged -= Refresh; target.Document.Changed -= Changed; target.DocumentChanged -= Rebind;
    }
    public new void Dispose() { if (disposed) return; Detach(); disposed = true; target = null; }
}
