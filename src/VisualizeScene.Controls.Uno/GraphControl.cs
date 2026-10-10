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

/// <summary>A reusable input-capable graph control. Rendering shares Uno's Skia composition canvas.</summary>
public sealed partial class GraphControl : ContentControl, IDisposable
{
    private sealed class Surface(GraphControl owner) : SKCanvasElement
    {
        protected override void RenderOverride(SKCanvas canvas, Size area)
        {
            if (owner.disposed) return;
            var s = owner.Session;
            owner.renderer.Render(canvas, s.DisplayGraph, s.Viewport, area.Width, area.Height, new(s.ShowGrid, s.GridSize, s.Selection, s.Marquee, s.EdgePreview, CandidatePosition: s.CandidatePosition, IsValidConnection: s.CandidateDecision.Allowed));
            owner.FrameRendered?.Invoke(owner, EventArgs.Empty);
        }
    }
    private readonly Surface surface;
    private GraphRenderer renderer = new();
    private bool disposed, releasingCapture;
    private GraphDocument document = new();
    public EditorSession Session { get; private set; }
    public RenderStatistics RenderStatistics => renderer.Statistics;
    public event EventHandler? SelectionChanged;
    public event EventHandler? FrameRendered;
    public event EventHandler<string>? Error;
    public event EventHandler<string>? StatusChanged;
    public GraphDocument Document
    {
        get => document;
        set
        {
            ArgumentNullException.ThrowIfNull(value); if (ReferenceEquals(document, value)) return;
            var previous = document; clicks.Reset(); Session.Dispose(); document = value; Session = new(value); Attach();
            DocumentChanged?.Invoke(this, new(previous, value)); Invalidate();
        }
    }
    public GraphControl()
    {
        DefaultStyleKey = typeof(ContentControl); IsTabStop = true;
        HorizontalContentAlignment = HorizontalAlignment.Stretch; VerticalContentAlignment = VerticalAlignment.Stretch; Background = new SolidColorBrush(Microsoft.UI.Colors.White);
        AutomationProperties.SetName(this, "Graph editor"); AutomationProperties.SetAutomationId(this, "GraphEditor");
        Session = new(document); surface = new Surface(this) { HorizontalAlignment = HorizontalAlignment.Stretch, VerticalAlignment = VerticalAlignment.Stretch }; Content = surface; Attach();
        PointerPressed += Pressed; PointerMoved += Moved; PointerReleased += Released; PointerCanceled += (_, _) => { clicks.Reset(); Session.CancelGesture(); };
        PointerCaptureLost += (_, _) => { if (!releasingCapture && Session.Mode != EditorMode.CreateEdge) { clicks.Reset(); Session.CancelGesture(); } };
        PointerWheelChanged += Wheel; // Native double-tap can span different editor modes; recognition happens on completed clicks.
        DoubleTapped += (_, args) => args.Handled = true; KeyDown += Key; SizeChanged += (_, _) => Invalidate();
        RightTapped += (_, args) => { ContextMenu().ShowAt(this, args.GetPosition(this)); args.Handled = true; };
    }
    private void Attach()
    {
        Session.LabelSizeProvider = label => renderer.MeasureLabel(label);
        Session.Invalidated += (_, _) => Invalidate(); Session.SelectionChanged += (_, _) => SelectionChanged?.Invoke(this, EventArgs.Empty); Session.StatusChanged += (_, text) => StatusChanged?.Invoke(this, text);
    }
    public void Invalidate() { if (!disposed) surface.Invalidate(); }
    private void Run(Action action) { try { action(); } catch (Exception ex) { Error?.Invoke(this, ex.Message); } }
    public void Fit()
    {
        Session.Viewport.Fit(ContentBounds, ActualWidth, ActualHeight); Invalidate();
    }
    public void Zoom(double factor) { Session.Viewport.ZoomAt(new(ActualWidth / 2, ActualHeight / 2), factor); Invalidate(); }
    public void SetMode(EditorMode mode) { clicks.Reset(); Session.CancelGesture(); Session.Mode = mode; Focus(FocusState.Programmatic); Invalidate(); }
    public string AddNodeAtCenter() => Session.AddNode(Session.Viewport.ToWorld(new(ActualWidth / 2, ActualHeight / 2)));
    public byte[] ExportPng(double scale = 1) => renderer.ExportPng(Document.Snapshot, scale);
    public new void Dispose()
    {
        if (disposed) return; disposed = true; Session.Dispose(); renderer.Dispose(); customTypeface?.Dispose(); customTypeface = null;
    }
}
