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
            owner.renderer.Render(canvas, s.DisplayGraph, s.Viewport, area.Width, area.Height, new(s.ShowGrid, s.GridSize, s.DisplaySelection, s.Marquee, s.EdgePreview, CandidatePosition: s.CandidatePosition, IsValidConnection: s.CandidateDecision.Allowed, AlignmentGuides: s.AlignmentGuides, MarqueeContains: s.IsMarqueeContainment, FocusBounds: owner.showKeyboardFocus && owner.FocusState != FocusState.Unfocused ? s.FocusedItemBounds : null) { GridSettings = s.GridSettings });
            owner.FrameRendered?.Invoke(owner, EventArgs.Empty);
        }
    }
    private readonly Surface surface;
    private GraphRenderer renderer = new();
    private bool disposed, releasingCapture;
    private GraphDocument document = new();
    public EditorSession Session { get; private set; }
    public RenderStatistics RenderStatistics => renderer.Statistics;
    public RenderPreparationStatistics PreparationStatistics => renderer.PreparationStatistics;
    public event EventHandler? SelectionChanged;
    public event EventHandler? FrameRendered;
    public event EventHandler<string>? Error;
    public event EventHandler<string>? StatusChanged;
    public GraphDocument Document
    {
        get => document;
        set
        {
            ObjectDisposedException.ThrowIf(disposed, this); ArgumentNullException.ThrowIfNull(value); if (ReferenceEquals(document, value)) return;
            var previous = document; var grid = Session.GridSettings; CloseGraphContextMenu(); CancelTouchNavigation(); Session.Dispose(); document = value; Session = new(value) { GridSettings = grid }; Attach();
            DocumentChanged?.Invoke(this, new(previous, value)); Invalidate();
        }
    }
    public GraphControl()
    {
        DefaultStyleKey = typeof(ContentControl); IsTabStop = true;
        ManipulationMode = Microsoft.UI.Xaml.Input.ManipulationModes.None;
        HorizontalContentAlignment = HorizontalAlignment.Stretch; VerticalContentAlignment = VerticalAlignment.Stretch; Background = new SolidColorBrush(Microsoft.UI.Colors.White);
        AutomationProperties.SetName(this, "Graph editor"); AutomationProperties.SetAutomationId(this, "GraphEditor");
        Session = new(document); surface = new Surface(this) { HorizontalAlignment = HorizontalAlignment.Stretch, VerticalAlignment = VerticalAlignment.Stretch }; Content = surface; Attach();
        PointerPressed += Pressed; PointerMoved += Moved; PointerReleased += Released; PointerCanceled += Canceled;
        PointerCaptureLost += CaptureLost;
        LostFocus += (_, _) => { showKeyboardFocus = false; CancelTouchNavigation(); };
        Unloaded += (_, _) => { CloseGraphContextMenu(); CancelTouchNavigation(); };
        PointerWheelChanged += Wheel; // Native double-tap can span different editor modes; recognition happens on completed clicks.
        DoubleTapped += (_, args) => args.Handled = true; KeyDown += Key; SizeChanged += (_, _) => Invalidate();
        RightTapped += (_, args) => { var p = args.GetPosition(this); Run(() => ShowGraphContextMenu(new(p.X, p.Y))); args.Handled = true; };
    }
    private void Attach()
    {
        AttachNavigation();
        Session.ClipboardChanged += (_, _) => { CloseGraphContextMenu(); ClipboardChanged?.Invoke(this, EventArgs.Empty); };
        Session.LabelSizeProvider = label => renderer.MeasureLabel(label);
        Session.NodeCaptionSizeProvider = renderer.MeasureNodeCaption;
        Session.Invalidated += (_, _) => Invalidate(); Session.SelectionChanged += (_, _) => { CloseGraphContextMenu(); SelectionChanged?.Invoke(this, EventArgs.Empty); }; Session.StatusChanged += (_, text) => { CloseGraphContextMenu(); StatusChanged?.Invoke(this, text); };
    }
    public void Invalidate() { if (!disposed) surface.Invalidate(); }
    private void Run(Action action) { try { action(); } catch (Exception ex) { Error?.Invoke(this, ex.Message); } }
    public void Fit()
    {
        CancelTouchNavigation(); Session.Viewport.Fit(ContentBounds, ActualWidth, ActualHeight); Invalidate();
    }
    public void Zoom(double factor) { CancelTouchNavigation(); Session.Viewport.ZoomAt(new(ActualWidth / 2, ActualHeight / 2), factor); Invalidate(); }
    public void SetMode(EditorMode mode) { CancelTouchNavigation(); Session.Mode = mode; Focus(FocusState.Programmatic); Invalidate(); }
    public string AddNodeAtCenter() => Session.AddNode(Session.Viewport.ToWorld(new(ActualWidth / 2, ActualHeight / 2)));
    public byte[] ExportPng(double scale = 1) => renderer.ExportPng(Document.Snapshot, scale);
    public new void Dispose()
    {
        if (disposed) return; CloseGraphContextMenu(); CancelTouchNavigation(); disposed = true;
        if (inputDocument is not null) inputDocument.Changed -= InputDocumentChanged;
        Session.Dispose(); renderer.Dispose(); customTypeface?.Dispose(); customTypeface = null;
    }
}
