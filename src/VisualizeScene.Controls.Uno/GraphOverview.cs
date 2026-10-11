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

/// <summary>Reusable navigable overview. Camera-only changes replay a retained graph layer; selection-only frames do no work.</summary>
public sealed class GraphOverview : ContentControl, IDisposable
{
    private sealed class Surface(GraphOverview owner) : SKCanvasElement
    {
        protected override void RenderOverride(SKCanvas canvas, Size area)
        {
            if (owner.disposed) return;
            var target = owner.target;
            if (target is null) { canvas.Clear(SKColors.White); return; }
            owner.renderer.Render(canvas, target.Document.Snapshot, target.ContentBounds, area.Width, area.Height,
                target.Session.Viewport.State, target.ActualWidth, target.ActualHeight);
        }
    }
    private readonly Surface surface;
    private readonly GraphOverviewRenderer renderer = new();
    private GraphControl? target;
    private GraphSnapshot? lastSnapshot;
    private ViewportState lastView;
    private RectD lastBounds;
    private double lastWidth, lastHeight;
    private bool disposed;
    private uint? activePointer;
    public OverviewStatistics Statistics => renderer.Statistics;
    public GraphControl? Target
    {
        get => target;
        set
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (ReferenceEquals(target, value)) return;
            if (target is not null) target.FrameRendered -= Updated;
            ReleasePointerCaptures(); activePointer = null; target = value; lastSnapshot = null; renderer.Clear();
            if (target is not null) target.FrameRendered += Updated;
            surface.Invalidate();
        }
    }
    public GraphOverview()
    {
        DefaultStyleKey = typeof(ContentControl); MinHeight = 100; Background = new SolidColorBrush(Microsoft.UI.Colors.White);
        HorizontalContentAlignment = HorizontalAlignment.Stretch; VerticalContentAlignment = VerticalAlignment.Stretch;
        surface = new Surface(this); Content = surface;
        SizeChanged += (_, _) => { if (!disposed) surface.Invalidate(); };
        PointerPressed += (_, args) =>
        {
            if (disposed || target is null || args.GetCurrentPoint(this).Properties.IsRightButtonPressed || activePointer.HasValue) return;
            if (!CapturePointer(args.Pointer)) return;
            activePointer = args.Pointer.PointerId; target.CancelTouchNavigation(); Navigate(args);
        };
        PointerMoved += (_, args) => { if (activePointer == args.Pointer.PointerId) Navigate(args); };
        PointerReleased += (_, args) => { if (activePointer != args.Pointer.PointerId) return; Navigate(args); activePointer = null; ReleasePointerCapture(args.Pointer); };
        PointerCanceled += (_, _) => { activePointer = null; ReleasePointerCaptures(); };
        PointerCaptureLost += (_, args) => { if (activePointer == args.Pointer.PointerId) activePointer = null; };
    }
    private void Updated(object? sender, EventArgs args)
    {
        if (disposed || target is null) return;
        var bounds = target.ContentBounds; var view = target.Session.Viewport.State;
        if (ReferenceEquals(lastSnapshot, target.Document.Snapshot) && bounds == lastBounds && view == lastView &&
            lastWidth == target.ActualWidth && lastHeight == target.ActualHeight) return;
        lastSnapshot = target.Document.Snapshot; lastBounds = bounds; lastView = view;
        lastWidth = target.ActualWidth; lastHeight = target.ActualHeight; surface.Invalidate();
    }
    private void Navigate(PointerRoutedEventArgs args)
    {
        if (disposed || target is null || !renderer.Statistics.HasPicture) return;
        var p = args.GetCurrentPoint(this).Position;
        target.Session.Viewport.Center(renderer.ToWorld(new(p.X, p.Y)), target.ActualWidth, target.ActualHeight);
        target.Invalidate(); args.Handled = true;
    }
    public new void Dispose()
    {
        if (disposed) return; Target = null; disposed = true; renderer.Dispose();
    }
}
