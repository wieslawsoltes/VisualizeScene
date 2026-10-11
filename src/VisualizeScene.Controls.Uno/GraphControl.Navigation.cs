using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using VisualizeScene.Core;
using VisualizeScene.Editing;
using Windows.System;

namespace VisualizeScene.Controls.Uno;

public enum GraphTouchMode { Navigate, Edit, Disabled }

public sealed partial class GraphControl
{
    private TouchNavigator touchNavigator = null!;
    private readonly Dictionary<uint, Pointer> touchPointers = [];
    private GraphDocument? inputDocument;
    private GraphTouchMode touchMode;
    private EditorMode touchEditorMode;
    private long touchEditViewRevision;
    private bool touchEditing, cancelingTouches, showKeyboardFocus;
    public int TouchContactCount => touchNavigator.ContactCount;
    public TouchNavigationPhase TouchPhase => touchNavigator.Phase;
    public GraphTouchMode TouchMode
    {
        get => touchMode;
        set
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (!Enum.IsDefined(value)) throw new ArgumentOutOfRangeException(nameof(value));
            if (touchMode == value) return;
            CancelTouchNavigation(); touchMode = value;
        }
    }
    private void RunTouch(Action action)
    {
        try { action(); }
        catch (Exception ex) { CancelTouchNavigation(); Error?.Invoke(this, ex.Message); }
    }
    private static bool IsTouch(PointerRoutedEventArgs args)
        => args.Pointer.PointerDeviceType == Microsoft.UI.Input.PointerDeviceType.Touch;
    private void AttachNavigation()
    {
        if (inputDocument is not null) inputDocument.Changed -= InputDocumentChanged;
        inputDocument = Document; inputDocument.Changed += InputDocumentChanged;
        touchNavigator = new(Session.Viewport);
    }
    private void InputDocumentChanged(object? sender, GraphChangedEventArgs args) => CancelTouchNavigation();
    public void CancelTouchNavigation()
    {
        if (cancelingTouches) return;
        cancelingTouches = true;
        try
        {
            var captured = touchPointers.Values.ToArray(); touchPointers.Clear();
            touchNavigator?.Cancel(); touchEditing = false; clicks.Reset(); Session.CancelGesture();
            var prior = releasingCapture; releasingCapture = true;
            try { foreach (var pointer in captured) ReleasePointerCapture(pointer); }
            finally { releasingCapture = prior; }
        }
        finally { cancelingTouches = false; }
    }
    private void TouchPressed(PointerRoutedEventArgs args)
    {
        args.Handled = true;
        if (disposed || editingLabel || TouchMode == GraphTouchMode.Disabled) return;
        if (touchNavigator.ContactCount > 0 && touchEditorMode != Session.Mode) { CancelTouchNavigation(); return; }
        Focus(FocusState.Pointer); showKeyboardFocus = false;
        var screen = Position(args);
        if (touchNavigator.ContactCount == 0)
        {
            touchEditorMode = Session.Mode;
            touchNavigator.SingleFingerPanning = TouchMode != GraphTouchMode.Edit || Session.Mode != EditorMode.Select;
        }
        if (!touchNavigator.Press(args.Pointer.PointerId, screen, ClickTime)) return;
        touchPointers[args.Pointer.PointerId] = args.Pointer;
        if (!CapturePointer(args.Pointer)) { CancelTouchNavigation(); return; }
        if (touchNavigator.ContactCount > 1)
        {
            // Pinch owns the complete contact sequence. Cancel, never commit, a preceding one-finger edit.
            touchEditing = false; clicks.Reset(); Session.CancelGesture();
        }
        else
        {
            clicks.Press(Session.Hit(Session.Viewport.ToWorld(screen)), Session.Mode, Modifiers(), screen, Document.Revision, ClickTime);
            touchEditing = TouchMode == GraphTouchMode.Edit && Session.Mode == EditorMode.Select;
            if (touchEditing) { touchEditViewRevision = Session.Viewport.Revision; Session.PointerDown(screen, Modifiers()); }
        }
        Invalidate();
    }
    private void TouchMoved(PointerRoutedEventArgs args)
    {
        args.Handled = true;
        if (editingLabel || disposed || !touchNavigator.IsTracking(args.Pointer.PointerId)) return;
        if (touchEditorMode != Session.Mode || touchEditing && Session.Viewport.Revision != touchEditViewRevision) { CancelTouchNavigation(); return; }
        var screen = Position(args); clicks.Move(screen);
        touchNavigator.Move(args.Pointer.PointerId, screen);
        if (touchEditing && !touchNavigator.WasMultiTouch) Session.PointerMove(screen);
        else if (touchNavigator.Phase is TouchNavigationPhase.Panning or TouchNavigationPhase.Pinching) Session.CancelGesture();
        Invalidate();
    }
    private async Task TouchReleased(PointerRoutedEventArgs args)
    {
        args.Handled = true;
        if (!touchNavigator.IsTracking(args.Pointer.PointerId)) return;
        if (touchEditorMode != Session.Mode || touchEditing && Session.Viewport.Revision != touchEditViewRevision) { CancelTouchNavigation(); return; }
        var screen = Position(args); var editing = touchEditing && !touchNavigator.WasMultiTouch;
        var release = touchNavigator.Release(args.Pointer.PointerId, screen, ClickTime);
        touchPointers.Remove(args.Pointer.PointerId);
        var session = Session;
        if (editing) session.PointerUp(screen);
        else if (release.IsTap && !editingLabel && !disposed)
        {
            // Node/edge creation is deliberately deferred: a pinch can never create a node on its first contact.
            session.PointerDown(screen, Modifiers());
            if (ReferenceEquals(session, Session)) session.PointerUp(screen);
        }
        var activate = release.IsTap ? clicks.Release(screen, Document.Revision, ClickTime) : null;
        if (!release.IsTap) clicks.Reset();
        if (touchNavigator.ContactCount == 0) touchEditing = false;
        var prior = releasingCapture; releasingCapture = true;
        try { ReleasePointerCapture(args.Pointer); } finally { releasingCapture = prior; }
        Invalidate();
        if (activate is not null && ReferenceEquals(session, Session) && Session.Mode == EditorMode.Select && !editingLabel && !disposed)
            await ActivateDoubleClickAsync(activate, Session.Viewport.ToWorld(screen));
    }
    private void Canceled(object sender, PointerRoutedEventArgs args)
    {
        if (IsTouch(args)) CancelTouchNavigation();
        else { clicks.Reset(); Session.CancelGesture(); }
    }
    private void CaptureLost(object sender, PointerRoutedEventArgs args)
    {
        if (releasingCapture) return;
        if (IsTouch(args)) { if (touchNavigator.IsTracking(args.Pointer.PointerId)) CancelTouchNavigation(); }
        else if (Session.Mode != EditorMode.CreateEdge) { clicks.Reset(); Session.CancelGesture(); }
    }
    public bool Navigate(NavigationDirection direction, bool extendSelection = false)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        CancelTouchNavigation(); showKeyboardFocus = true;
        var result = Session.Navigate(direction, extendSelection); EnsureFocusedItemVisible(); return result;
    }
    public bool NavigateBoundary(bool last = false, bool extendSelection = false)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        CancelTouchNavigation(); showKeyboardFocus = true;
        var result = Session.NavigateBoundary(last, extendSelection); EnsureFocusedItemVisible(); return result;
    }
    public void EnsureFocusedItemVisible()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (Session.FocusedItemBounds is { } bounds) Session.Viewport.EnsureVisible(bounds, ActualWidth, ActualHeight);
        Invalidate();
    }
    public void PanPage(bool forward, bool horizontal = false)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        CancelTouchNavigation();
        var amount = (forward ? -.85 : .85) * (horizontal ? ActualWidth : ActualHeight);
        Session.Viewport.Pan(horizontal ? new(amount, 0) : new(0, amount)); Invalidate();
    }
    private bool NavigationKey(KeyRoutedEventArgs args)
    {
        var keys = Modifiers(); var control = keys.HasFlag(InputModifiers.Control); var shift = keys.HasFlag(InputModifiers.Shift);
        if (keys.HasFlag(InputModifiers.Alt)) return false;
        NavigationDirection? direction = args.Key switch
        {
            VirtualKey.Left => NavigationDirection.Left, VirtualKey.Right => NavigationDirection.Right,
            VirtualKey.Up => NavigationDirection.Up, VirtualKey.Down => NavigationDirection.Down, _ => null
        };
        if (control && direction is { } value) { Navigate(value, shift); return true; }
        if (control && args.Key is VirtualKey.Home or VirtualKey.End) { NavigateBoundary(args.Key == VirtualKey.End, shift); return true; }
        if (!control && args.Key is VirtualKey.PageUp or VirtualKey.PageDown) { PanPage(args.Key == VirtualKey.PageDown, shift); return true; }
        return false;
    }
}
