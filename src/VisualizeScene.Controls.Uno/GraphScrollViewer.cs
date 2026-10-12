using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using VisualizeScene.Core;
using Windows.System;

namespace VisualizeScene.Controls.Uno;

/// <summary>Desktop-style scrollbars around a GraphControl. The host retains ownership of the graph and optional overlay.</summary>
public sealed class GraphScrollViewer : UserControl, IDisposable
{
    private sealed class AxisBar(GraphScrollViewer owner, bool horizontal) : ScrollBar
    {
        protected override void OnKeyDown(KeyRoutedEventArgs args)
        {
            if (owner.ScrollKey(horizontal, args.Key)) args.Handled = true;
            else base.OnKeyDown(args);
        }
    }
    private readonly Grid root = new(), canvasHost = new();
    private readonly AxisBar horizontal, vertical;
    private readonly Button fit = new() { Padding = new(0), MinWidth = 0, MinHeight = 0, BorderThickness = new(0), Content = new EditorIcon { Kind = EditorIconKind.Fit, Width = 12, Height = 12 } };
    private GraphControl? target;
    private UIElement? overlay;
    private GraphSnapshot? lastGraph;
    private ViewportState lastView, expectedView;
    private double lastWidth, lastHeight;
    private RectD lastBounds;
    private bool updating, disposed, rejectThumb, visible = true;
    private bool? draggingHorizontal;
    private GraphDocument? gestureDocument;
    public ViewportScrollModel ScrollModel { get; private set; }
    public long ModelBuilds { get; private set; }
    public bool IsDragging => draggingHorizontal.HasValue;
    public event EventHandler<string>? Error;
    public FrameworkElement HorizontalBar => horizontal;
    public FrameworkElement VerticalBar => vertical;
    public FrameworkElement FitButton => fit;
    /// <summary>Read-only access to the native scrollbar thumb for host automation.</summary>
    public FrameworkElement? FindThumb(bool isHorizontal) => Find(isHorizontal ? horizontal : vertical);
    private static Thumb? Find(DependencyObject root)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is Thumb thumb && thumb.ActualWidth > 0 && thumb.ActualHeight > 0) return thumb;
            if (Find(child) is { } found) return found;
        }
        return null;
    }
    public bool ScrollBarsVisible
    {
        get => visible;
        set
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (visible == value) return;
            RejectGesture(); visible = value;
            root.ColumnDefinitions[1].Width = new(value ? 16 : 0);
            root.RowDefinitions[1].Height = new(value ? 16 : 0);
            horizontal.Visibility = vertical.Visibility = fit.Visibility = value ? Visibility.Visible : Visibility.Collapsed;
            if (value) Synchronize(true);
        }
    }
    public UIElement? Overlay
    {
        get => overlay;
        set
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (ReferenceEquals(overlay, value)) return;
            if (value is not null && (ReferenceEquals(value, target) || VisualTreeHelper.GetParent(value) is not null))
                throw new ArgumentException("The overlay must be an unparented element distinct from the graph.", nameof(value));
            if (overlay is not null) canvasHost.Children.Remove(overlay);
            overlay = value; if (overlay is not null) canvasHost.Children.Add(overlay);
        }
    }
    public GraphControl? Target
    {
        get => target;
        set
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (ReferenceEquals(target, value)) return;
            if (value is not null && (ReferenceEquals(value, overlay) || VisualTreeHelper.GetParent(value) is not null))
                throw new ArgumentException("The graph must not already belong to another visual parent.", nameof(value));
            Detach(); target = value; lastGraph = null;
            if (target is not null)
            {
                canvasHost.Children.Insert(0, target);
                target.FrameRendered += Changed; target.SizeChanged += Sized;
                target.DocumentChanged += Rebind; target.Document.Changed += Edited;
            }
            Synchronize(true);
        }
    }
    public GraphScrollViewer()
    {
        horizontal = new(this, true) { Orientation = Orientation.Horizontal, Height = 16, MinHeight = 16 };
        vertical = new(this, false) { Orientation = Orientation.Vertical, Width = 16, MinWidth = 16 };
        root.RowDefinitions.Add(new() { Height = new(1, GridUnitType.Star) }); root.RowDefinitions.Add(new() { Height = new(16) });
        root.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) }); root.ColumnDefinitions.Add(new() { Width = new(16) });
        root.Children.Add(canvasHost); Grid.SetRow(horizontal, 1); root.Children.Add(horizontal);
        Grid.SetColumn(vertical, 1); root.Children.Add(vertical); Grid.SetColumn(fit, 1); Grid.SetRow(fit, 1); root.Children.Add(fit);
        Content = root; Setup(horizontal, true); Setup(vertical, false);
        fit.Background = new SolidColorBrush(Windows.UI.Color.FromArgb(255,235,239,244));
        AutomationProperties.SetName(fit, "Fit graph"); ToolTipService.SetToolTip(fit, "Fit graph · Home");
        fit.Click += (_, _) => Run(() => { EndGesture(); target?.Fit(); });
        Loaded += (_, _) => Synchronize(true);
        Unloaded += (_, _) => RejectGesture();
    }
    private void Setup(AxisBar bar, bool isHorizontal)
    {
        bar.Minimum = 0; bar.Maximum = 1; bar.IsTabStop = true;
        bar.IndicatorMode = ScrollingIndicatorMode.MouseIndicator;
        AutomationProperties.SetName(bar, isHorizontal ? "Horizontal graph scroll" : "Vertical graph scroll");
        bar.AddHandler(PointerPressedEvent, new PointerEventHandler((_, _) => { EndGesture(); rejectThumb = false; bar.Focus(FocusState.Pointer); Synchronize(); }), true);
        bar.Scroll += (_, args) => Run(() =>
        {
            if (updating || disposed) return;
            if (args.ScrollEventType == ScrollEventType.EndScroll) { EndGesture(); rejectThumb = false; Synchronize(true); return; }
            if (rejectThumb && args.ScrollEventType is ScrollEventType.ThumbTrack or ScrollEventType.ThumbPosition) return;
            if (args.ScrollEventType is not (ScrollEventType.ThumbTrack or ScrollEventType.ThumbPosition)) rejectThumb = false;
            Apply(isHorizontal, args.NewValue, args.ScrollEventType == ScrollEventType.ThumbTrack);
        });
        bar.PointerCanceled += (_, _) => { RejectGesture(); Synchronize(true); };
        bar.LostFocus += (_, _) => { RejectGesture(); Synchronize(); };
    }
    private void Changed(object? sender, EventArgs args) => Synchronize();
    private void Sized(object sender, SizeChangedEventArgs args) { RejectGesture(); Synchronize(true); }
    private void Edited(object? sender, GraphChangedEventArgs args) { RejectGesture(); Synchronize(true); }
    private void Rebind(object? sender, GraphDocumentChangedEventArgs args)
    {
        args.Previous.Changed -= Edited; args.Current.Changed += Edited; RejectGesture(); lastGraph = null; Synchronize(true);
    }
    private void Synchronize(bool force = false)
    {
        if (disposed || updating || !visible) return;
        if (target is null)
        { horizontal.IsEnabled = vertical.IsEnabled = fit.IsEnabled = false; ScrollModel = default; return; }
        var state = target.Session.Viewport.State;
        if (IsDragging)
        {
            if (gestureDocument == target.Document && state == expectedView) return;
            RejectGesture(); // An external camera/document change invalidates the old drag coordinate frame.
        }
        var snapshot = target.Document.Snapshot;
        var contentBounds = target.ContentBounds;
        if (!force && lastBounds == contentBounds && ReferenceEquals(lastGraph, target.Document.Snapshot) && lastView == state &&
            lastWidth == target.ActualWidth && lastHeight == target.ActualHeight) return;
        Run(() =>
        {
            var model = ViewportScrollModel.Calculate(contentBounds, state, target.ActualWidth, target.ActualHeight);
            updating = true;
            try
            {
                SetBar(horizontal, model.Horizontal); SetBar(vertical, model.Vertical); fit.IsEnabled = true;
                ScrollModel = model; ModelBuilds++; lastGraph = snapshot; lastView = state;
                lastWidth = target.ActualWidth; lastHeight = target.ActualHeight; lastBounds = contentBounds;
            }
            finally { updating = false; }
        });
    }
    private static void SetBar(ScrollBar bar, ViewportScrollAxis axis)
    {
        bar.IsEnabled = axis.CanScroll; bar.ViewportSize = axis.ViewportSize;
        bar.SmallChange = axis.SmallChange; bar.LargeChange = axis.LargeChange; bar.Value = axis.Value;
    }
    private void Apply(bool isHorizontal, double value, bool track)
    {
        if (target is null || disposed || !visible) return;
        var document = target.Document; var session = target.Session; var state = session.Viewport.State;
        if (IsDragging && (gestureDocument != document || expectedView != state || draggingHorizontal != isHorizontal))
        { RejectGesture(); Synchronize(true); return; }
        if (!IsDragging && (!ReferenceEquals(lastGraph, document.Snapshot) || lastView != state))
        { if (track) rejectThumb = true; Synchronize(true); return; }
        var axis = isHorizontal ? ScrollModel.Horizontal : ScrollModel.Vertical;
        if (!axis.CanScroll) return;
        target.CancelTouchNavigation();
        if (target.Document != document || target.Session != session || !ReferenceEquals(document.Snapshot, lastGraph) || session.Viewport.State != state)
        { if (track) rejectThumb = true; EndGesture(); Synchronize(true); return; }
        var origin = axis.GetOrigin(value);
        session.Viewport.Restore(new(isHorizontal ? state.Origin with { X = origin } : state.Origin with { Y = origin }, state.Zoom));
        if (track) { draggingHorizontal = isHorizontal; gestureDocument = document; }
        expectedView = session.Viewport.State; target.Invalidate();
        if (!track) { EndGesture(); Synchronize(); }
    }
    private bool ScrollKey(bool isHorizontal, VirtualKey key)
    {
        var axis = isHorizontal ? ScrollModel.Horizontal : ScrollModel.Vertical;
        var value = isHorizontal ? horizontal.Value : vertical.Value;
        double? next = key switch
        {
            VirtualKey.Home => 0, VirtualKey.End => 1,
            VirtualKey.PageUp => value - axis.LargeChange, VirtualKey.PageDown => value + axis.LargeChange,
            VirtualKey.Left when isHorizontal => value - axis.SmallChange,
            VirtualKey.Right when isHorizontal => value + axis.SmallChange,
            VirtualKey.Up when !isHorizontal => value - axis.SmallChange,
            VirtualKey.Down when !isHorizontal => value + axis.SmallChange,
            _ => null
        };
        if (next is null) return false;
        Run(() => { EndGesture(); rejectThumb = false; Apply(isHorizontal, Math.Clamp(next.Value, 0, 1), false); }); return true;
    }
    private void RejectGesture() { if (IsDragging) rejectThumb = true; EndGesture(); }
    private void EndGesture() { draggingHorizontal = null; gestureDocument = null; }
    private void Run(Action action)
    {
        try { action(); }
        catch (Exception ex) { EndGesture(); Error?.Invoke(this, ex.Message); }
    }
    private void Detach()
    {
        RejectGesture(); if (target is null) return;
        target.FrameRendered -= Changed; target.SizeChanged -= Sized; target.DocumentChanged -= Rebind; target.Document.Changed -= Edited;
        canvasHost.Children.Remove(target);
    }
    public new void Dispose()
    {
        if (disposed) return; Detach(); disposed = true; target = null; lastGraph = null; overlay = null; canvasHost.Children.Clear();
    }
}
