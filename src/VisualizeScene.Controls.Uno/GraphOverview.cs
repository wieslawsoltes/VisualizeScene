using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using SkiaSharp;
using Uno.WinUI.Graphics2DSK;
using VisualizeScene.Core;
using VisualizeScene.Rendering.Skia;
using Windows.Foundation;

namespace VisualizeScene.Controls.Uno;

public sealed class GraphOverview : ContentControl, IDisposable
{
    private sealed class Surface(GraphOverview owner) : SKCanvasElement
    {
        protected override void RenderOverride(SKCanvas canvas, Size area)
        {
            var target = owner.target; if (target is null) return;
            owner.viewport.Fit(target.Document.Snapshot.Bounds, area.Width, area.Height, 10);
            owner.renderer.Render(canvas, target.Document.Snapshot, owner.viewport, area.Width, area.Height, new(ShowGrid: false, Overview: true));
            var origin = owner.viewport.ToScreen(target.Session.Viewport.Origin);
            var width = target.ActualWidth / target.Session.Viewport.Zoom * owner.viewport.Zoom; var height = target.ActualHeight / target.Session.Viewport.Zoom * owner.viewport.Zoom;
            using var paint = new SKPaint { Color = new SKColor(0, 112, 215, 28), IsAntialias = true };
            var rect = new SKRect((float)origin.X, (float)origin.Y, (float)(origin.X + width), (float)(origin.Y + height)); canvas.DrawRect(rect, paint);
            paint.Color = new SKColor(0, 112, 215); paint.Style = SKPaintStyle.Stroke; paint.StrokeWidth = 1.5f; canvas.DrawRect(rect, paint);
        }
    }
    private readonly Surface surface;
    private readonly GraphRenderer renderer = new();
    private readonly Viewport viewport = new();
    private GraphControl? target;
    public GraphControl? Target
    {
        get => target;
        set { if (target is not null) target.FrameRendered -= Updated; target = value; if (target is not null) target.FrameRendered += Updated; surface.Invalidate(); }
    }
    public GraphOverview()
    {
        DefaultStyleKey = typeof(ContentControl); MinHeight = 140; Background = new SolidColorBrush(Microsoft.UI.Colors.White);
        HorizontalContentAlignment = HorizontalAlignment.Stretch; VerticalContentAlignment = VerticalAlignment.Stretch;
        surface = new Surface(this); Content = surface;
        PointerPressed += (_, args) => { CapturePointer(args.Pointer); Navigate(args); };
        PointerMoved += (_, args) => { if (args.GetCurrentPoint(this).IsInContact) Navigate(args); };
        PointerReleased += (_, args) => ReleasePointerCapture(args.Pointer);
    }
    private void Updated(object? sender, EventArgs args) => surface.Invalidate();
    private void Navigate(PointerRoutedEventArgs args)
    {
        if (target is null) return; var p = args.GetCurrentPoint(this).Position;
        target.Session.Viewport.Center(viewport.ToWorld(new(p.X, p.Y)), target.ActualWidth, target.ActualHeight); target.Invalidate(); args.Handled = true;
    }
    public void Dispose() { Target = null; renderer.Dispose(); }
}
