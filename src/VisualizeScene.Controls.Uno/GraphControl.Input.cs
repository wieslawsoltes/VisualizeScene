using System.Diagnostics;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using VisualizeScene.Core;
using VisualizeScene.Editing;
using Windows.System;
using Windows.UI.Core;

namespace VisualizeScene.Controls.Uno;

public sealed partial class GraphControl
{
    private readonly DoubleClickRecognizer clicks = new();
    private bool editingLabel;
    private TextBox? labelInput;
    /// <summary>True after the label dialog has opened and transferred focus to its text input.</summary>
    public bool IsLabelEditorReady { get; private set; }
    /// <summary>Current dialog text for read-only host status and automation; null when closed.</summary>
    public string? EditingLabelText => labelInput?.Text;
    private static long ClickTime => (long)(Stopwatch.GetTimestamp() * (1000d / Stopwatch.Frequency));

    private static InputModifiers Modifiers()
    {
        bool Down(VirtualKey key) => Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(key).HasFlag(CoreVirtualKeyStates.Down);
        return (Down(VirtualKey.Control) ? InputModifiers.Control : 0) | (Down(VirtualKey.Shift) ? InputModifiers.Shift : 0) | (Down(VirtualKey.Menu) ? InputModifiers.Alt : 0);
    }
    private PointD Position(PointerRoutedEventArgs args) { var p = args.GetCurrentPoint(this).Position; return new(p.X, p.Y); }
    private void Pressed(object sender, PointerRoutedEventArgs args)
    {
        if (IsTouch(args)) { RunTouch(() => TouchPressed(args)); return; }
        if (touchNavigator.ContactCount > 0) { args.Handled = true; return; }
        showKeyboardFocus = false;
        var point = args.GetCurrentPoint(this);
        if (editingLabel) { clicks.Reset(); return; }
        if (point.Properties.IsRightButtonPressed) { CancelTouchNavigation(); return; }
        Focus(FocusState.Pointer);
        var screen = Position(args);
        if (point.Properties.IsMiddleButtonPressed) clicks.Reset();
        else clicks.Press(Session.Hit(Session.Viewport.ToWorld(screen)), Session.Mode, Modifiers(), screen, Document.Revision, ClickTime);
        // An edge command spans separate contacts; releasing the first must retain its source.
        if (Session.Mode != EditorMode.CreateEdge) CapturePointer(args.Pointer);
        Run(() => Session.PointerDown(Position(args), Modifiers(), point.Properties.IsMiddleButtonPressed)); args.Handled = true;
    }
    private void Moved(object sender, PointerRoutedEventArgs args)
    {
        if (IsTouch(args)) { RunTouch(() => TouchMoved(args)); return; }
        if (touchNavigator.ContactCount > 0) return;
        clicks.Move(Position(args));
        if (!editingLabel) Run(() => Session.PointerMove(Position(args)));
    }
    private async void Released(object sender, PointerRoutedEventArgs args)
    {
        if (IsTouch(args))
        {
            try { await TouchReleased(args); }
            catch (Exception ex) { CancelTouchNavigation(); Error?.Invoke(this, ex.Message); }
            return;
        }
        if (touchNavigator.ContactCount > 0) { args.Handled = true; return; }
        var screen = Position(args);
        Run(() => Session.PointerUp(screen));
        var doubleClick = clicks.Release(screen, Document.Revision, ClickTime);
        releasingCapture = true;
        try { ReleasePointerCapture(args.Pointer); } finally { releasingCapture = false; }
        args.Handled = true;
        if (doubleClick is not null && Session.Mode == EditorMode.Select && !editingLabel)
            await ActivateDoubleClickAsync(doubleClick, Session.Viewport.ToWorld(screen));
    }
    private void Wheel(object sender, PointerRoutedEventArgs args)
    {
        if (editingLabel) return;
        CancelTouchNavigation();
        var delta = args.GetCurrentPoint(this).Properties.MouseWheelDelta; Session.Viewport.ZoomAt(Position(args), Math.Pow(1.15, delta / 120d)); Invalidate(); args.Handled = true;
    }
    private async Task ActivateDoubleClickAsync(HitResult hit, PointD world)
    {
        if (hit.Kind == HitKind.None) Run(() => Session.AddNode(world));
        else if (hit.Kind == HitKind.Node && Document.Snapshot.Index().Nodes[hit.Id!].IsGroup) Run(() => Session.ToggleGroup(hit.Id!));
        else if (hit.Kind == HitKind.Edge && Modifiers().HasFlag(InputModifiers.Control)) Run(() => Session.AddBend(hit.Id!, world));
        else if (hit.Kind is HitKind.Node or HitKind.Edge or HitKind.Label) await EditLabelAsync(hit.Id!);
    }
    public async Task EditLabelAsync(string? id = null)
    {
        if (editingLabel) return;
        id ??= Session.Selection.FirstOrDefault(); if (id is null) return;
        var document = Document; var session = Session; var revision = document.Revision;
        var index = document.Snapshot.Index();
        if (!index.Nodes.ContainsKey(id) && !index.Edges.ContainsKey(id) && !index.Labels.ContainsKey(id)) return;
        var value = index.Nodes.TryGetValue(id, out var n) ? n.Text : index.Edges.TryGetValue(id, out var e) ? e.Text : index.Labels[id].Text;
        var input = new TextBox { Text = value, AcceptsReturn = true, MinWidth = 320, MinHeight = 80 }; AutomationProperties.SetAutomationId(input, "LabelText");
        var dialog = new ContentDialog { XamlRoot = XamlRoot, Title = "Edit label", Content = input, PrimaryButtonText = "Apply", CloseButtonText = "Cancel", DefaultButton = ContentDialogButton.Primary };
        dialog.Opened += (_, _) =>
        {
            // Opening animations can complete after a fast user has already typed.
            // Do not select over those edits when the Opened event arrives late.
            var unchanged = input.Text == value;
            IsLabelEditorReady = input.Focus(FocusState.Programmatic);
            if (unchanged) input.SelectAll();
        };
        CancelTouchNavigation(); editingLabel = true; labelInput = input; IsLabelEditorReady = false;
        try
        {
            if (await dialog.ShowAsync() == ContentDialogResult.Primary)
            {
                if (Document != document || Session != session) throw new InvalidOperationException("The edited document was replaced.");
                document.Edit("Edit label", edit =>
                {
                    if (index.Nodes.TryGetValue(id, out var node)) edit.Update(node with { Text = input.Text });
                    else if (index.Edges.TryGetValue(id, out var edge)) edit.Update(edge with { Text = input.Text });
                    else edit.Update(index.Labels[id] with { Text = input.Text });
                }, revision);
            }
        }
        catch (Exception ex) { Error?.Invoke(this, ex.Message); }
        finally { editingLabel = false; labelInput = null; IsLabelEditorReady = false; clicks.Reset(); if (!disposed) Focus(FocusState.Programmatic); }
    }
    private async void Key(object sender, KeyRoutedEventArgs args)
    {
        clicks.Reset();
        if (editingLabel) return;
        try { if (NavigationKey(args)) { args.Handled = true; return; } }
        catch (Exception ex) { Error?.Invoke(this, ex.Message); args.Handled = true; return; }
        if (touchNavigator.ContactCount > 0) CancelTouchNavigation();
        var modifiers = Modifiers(); var control = modifiers.HasFlag(InputModifiers.Control); var shift = modifiers.HasFlag(InputModifiers.Shift); var handled = true;
        try
        {
            switch (args.Key)
            {
                case VirtualKey.A when control && shift: Session.ClearSelection(); break;
                case VirtualKey.I when control: Session.InvertSelection(); break;
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
                case VirtualKey.F10 when shift: case VirtualKey.Application: ShowGraphContextMenu(); break;
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
}
