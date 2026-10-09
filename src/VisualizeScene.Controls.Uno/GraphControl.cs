using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using SkiaSharp;
using Uno.WinUI.Graphics2DSK;
using VisualizeScene.Core;
using VisualizeScene.Editing;
using VisualizeScene.Rendering.Skia;
using Windows.Foundation;
using Windows.System;
using Windows.UI.Core;

namespace VisualizeScene.Controls.Uno;

/// <summary>A reusable input-capable graph control. Rendering shares Uno's Skia composition canvas.</summary>
public sealed class GraphControl : ContentControl, IDisposable
{
    private sealed class Surface(GraphControl owner) : SKCanvasElement
    {
        protected override void RenderOverride(SKCanvas canvas, Size area)
        {
            if (owner.disposed) return;
            var s = owner.Session;
            owner.renderer.Render(canvas, s.DisplayGraph, s.Viewport, area.Width, area.Height,
                new(s.ShowGrid, s.GridSize, s.Selection, s.Marquee, s.EdgePreview));
            owner.FrameRendered?.Invoke(owner, EventArgs.Empty);
        }
    }
    private readonly Surface surface;
    private readonly GraphRenderer renderer = new();
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
            ArgumentNullException.ThrowIfNull(value);
            if (ReferenceEquals(document, value)) return;
            Session.Dispose(); document = value; Session = new(value); Attach(); Invalidate();
        }
    }
    public GraphControl()
    {
        DefaultStyleKey = typeof(ContentControl);
        IsTabStop = true; HorizontalContentAlignment = HorizontalAlignment.Stretch; VerticalContentAlignment = VerticalAlignment.Stretch;
        Background = new SolidColorBrush(Microsoft.UI.Colors.White);
        AutomationProperties.SetName(this, "Graph editor"); AutomationProperties.SetAutomationId(this, "GraphEditor");
        Session = new(document); surface = new Surface(this) { HorizontalAlignment = HorizontalAlignment.Stretch, VerticalAlignment = VerticalAlignment.Stretch };
        Content = surface; Attach();
        PointerPressed += Pressed; PointerMoved += Moved; PointerReleased += Released;
        PointerCanceled += (_, _) => Session.CancelGesture();
        PointerCaptureLost += (_, _) => { if (!releasingCapture && Session.Mode != EditorMode.CreateEdge) Session.CancelGesture(); };
        PointerWheelChanged += Wheel; DoubleTapped += DoubleTap; KeyDown += Key;
        SizeChanged += (_, _) => Invalidate();
        RightTapped += (_, args) => { ContextMenu().ShowAt(this, args.GetPosition(this)); args.Handled = true; };
    }
    private void Attach()
    {
        Session.Invalidated += (_, _) => Invalidate();
        Session.SelectionChanged += (_, _) => SelectionChanged?.Invoke(this, EventArgs.Empty);
        Session.StatusChanged += (_, text) => StatusChanged?.Invoke(this, text);
    }
    public void Invalidate() { if (!disposed) surface.Invalidate(); }
    private static InputModifiers Modifiers()
    {
        bool Down(VirtualKey key) => Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(key).HasFlag(CoreVirtualKeyStates.Down);
        return (Down(VirtualKey.Control) ? InputModifiers.Control : 0) | (Down(VirtualKey.Shift) ? InputModifiers.Shift : 0) | (Down(VirtualKey.Menu) ? InputModifiers.Alt : 0);
    }
    private void Run(Action action) { try { action(); } catch (Exception ex) { Error?.Invoke(this, ex.Message); } }
    private PointD Position(PointerRoutedEventArgs args) { var p = args.GetCurrentPoint(this).Position; return new(p.X, p.Y); }
    private void Pressed(object sender, PointerRoutedEventArgs args)
    {
        var point = args.GetCurrentPoint(this);
        if (point.Properties.IsRightButtonPressed) return;
        Focus(FocusState.Pointer);
        // A two-click edge command spans separate pointer contacts. Capturing and releasing
        // the first contact must not cancel the pending source endpoint.
        if (Session.Mode != EditorMode.CreateEdge) CapturePointer(args.Pointer);
        Run(() => Session.PointerDown(Position(args), Modifiers(), point.Properties.IsMiddleButtonPressed)); args.Handled = true;
    }
    private void Moved(object sender, PointerRoutedEventArgs args) => Run(() => Session.PointerMove(Position(args)));
    private void Released(object sender, PointerRoutedEventArgs args)
    {
        Run(() => Session.PointerUp(Position(args)));
        releasingCapture = true;
        try { ReleasePointerCapture(args.Pointer); }
        finally { releasingCapture = false; }
        args.Handled = true;
    }
    private void Wheel(object sender, PointerRoutedEventArgs args)
    {
        var delta = args.GetCurrentPoint(this).Properties.MouseWheelDelta;
        Session.Viewport.ZoomAt(Position(args), Math.Pow(1.15, delta / 120d)); Invalidate(); args.Handled = true;
    }
    private async void DoubleTap(object sender, DoubleTappedRoutedEventArgs args)
    {
        var p = args.GetPosition(this); var world = Session.Viewport.ToWorld(new(p.X, p.Y)); var hit = Session.Hit(world, false); args.Handled = true;
        if (Session.Mode != EditorMode.Select) return;
        if (hit.Kind == HitKind.None) Run(() => Session.AddNode(world));
        else if (hit.Kind == HitKind.Node && Document.Snapshot.Index().Nodes[hit.Id!].IsGroup) Run(() => Session.ToggleGroup(hit.Id!));
        else if (hit.Kind == HitKind.Edge && Modifiers().HasFlag(InputModifiers.Control)) Run(() => Session.AddBend(hit.Id!, world));
        else if (hit.Kind is HitKind.Node or HitKind.Edge) await EditLabelAsync(hit.Id!);
    }
    public async Task EditLabelAsync(string? id = null)
    {
        id ??= Session.Selection.FirstOrDefault(); if (id is null) return;
        var index = Document.Snapshot.Index(); var value = index.Nodes.TryGetValue(id, out var n) ? n.Text : index.Edges.TryGetValue(id, out var e) ? e.Text : "";
        var input = new TextBox { Text = value, AcceptsReturn = true, MinWidth = 320, MinHeight = 80 };
        AutomationProperties.SetAutomationId(input, "LabelText");
        var dialog = new ContentDialog { XamlRoot = XamlRoot, Title = "Edit label", Content = input, PrimaryButtonText = "Apply", CloseButtonText = "Cancel", DefaultButton = ContentDialogButton.Primary };
        dialog.Opened += (_, _) => { input.Focus(FocusState.Programmatic); input.SelectAll(); };
        try { if (await dialog.ShowAsync() == ContentDialogResult.Primary) Session.Rename(id, input.Text); }
        catch (Exception ex) { Error?.Invoke(this, ex.Message); }
        Focus(FocusState.Programmatic);
    }
    private async void Key(object sender, KeyRoutedEventArgs args)
    {
        var modifiers = Modifiers(); var control = modifiers.HasFlag(InputModifiers.Control); var shift = modifiers.HasFlag(InputModifiers.Shift); var handled = true;
        try
        {
            switch (args.Key)
            {
                case VirtualKey.A when control: Session.SelectAll(); break;
                case VirtualKey.C when control: Session.Copy(); break;
                case VirtualKey.X when control: Session.Copy(); Session.DeleteSelection(); break;
                case VirtualKey.V when control: Session.Paste(); break;
                case VirtualKey.D when control: Session.Duplicate(); break;
                case VirtualKey.Z when control && shift: Document.Redo(); break;
                case VirtualKey.Z when control: Document.Undo(); break;
                case VirtualKey.Y when control: Document.Redo(); break;
                case VirtualKey.G when control && shift: Session.Ungroup(); break;
                case VirtualKey.G when control: Session.Group(); break;
                case VirtualKey.Delete: case VirtualKey.Back: Session.DeleteSelection(); break;
                case VirtualKey.Escape: Session.CancelGesture(); Session.Mode = EditorMode.Select; break;
                case VirtualKey.F2: await EditLabelAsync(); break;
                case VirtualKey.Home: Fit(); break;
                case VirtualKey.Left: Session.Nudge(new(shift ? -10 : -1, 0)); break;
                case VirtualKey.Right: Session.Nudge(new(shift ? 10 : 1, 0)); break;
                case VirtualKey.Up: Session.Nudge(new(0, shift ? -10 : -1)); break;
                case VirtualKey.Down: Session.Nudge(new(0, shift ? 10 : 1)); break;
                default: handled = false; break;
            }
        }
        catch (Exception ex) { Error?.Invoke(this, ex.Message); }
        args.Handled = handled;
    }
    public void Fit()
    {
        var index = Document.Snapshot.Index(); var bounds = RectD.Enclose(Document.Snapshot.Nodes.Where(n => index.IsVisible(n.Id)).Select(n => n.Bounds));
        Session.Viewport.Fit(bounds, ActualWidth, ActualHeight); Invalidate();
    }
    public void Zoom(double factor) { Session.Viewport.ZoomAt(new(ActualWidth / 2, ActualHeight / 2), factor); Invalidate(); }
    public void SetMode(EditorMode mode) { Session.CancelGesture(); Session.Mode = mode; Focus(FocusState.Programmatic); Invalidate(); }
    public string AddNodeAtCenter() => Session.AddNode(Session.Viewport.ToWorld(new(ActualWidth / 2, ActualHeight / 2)));
    public byte[] ExportPng(double scale = 1) => renderer.ExportPng(Document.Snapshot, scale);
    private MenuFlyout ContextMenu()
    {
        var menu = new MenuFlyout();
        void Item(string text, Action action) { var item = new MenuFlyoutItem { Text = text }; item.Click += (_, _) => Run(action); menu.Items.Add(item); }
        Item("Edit label  F2", () => _ = EditLabelAsync()); Item("Duplicate  Ctrl+D", Session.Duplicate); Item("Delete  Del", Session.DeleteSelection);
        menu.Items.Add(new MenuFlyoutSeparator()); Item("Group  Ctrl+G", Session.Group); Item("Ungroup  Ctrl+Shift+G", Session.Ungroup);
        Item("Fold / unfold", () => { foreach (var id in Session.Selection.ToArray()) if (Document.Snapshot.Index().Nodes.TryGetValue(id, out var n) && n.IsGroup) Session.ToggleGroup(id); });
        menu.Items.Add(new MenuFlyoutSeparator()); Item("Fit graph  Home", Fit); return menu;
    }
    public void Dispose() { if (disposed) return; disposed = true; Session.Dispose(); renderer.Dispose(); }
}
