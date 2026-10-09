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
    private static InputModifiers Modifiers()
    {
        bool Down(VirtualKey key) => Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(key).HasFlag(CoreVirtualKeyStates.Down);
        return (Down(VirtualKey.Control) ? InputModifiers.Control : 0) | (Down(VirtualKey.Shift) ? InputModifiers.Shift : 0) | (Down(VirtualKey.Menu) ? InputModifiers.Alt : 0);
    }
    private PointD Position(PointerRoutedEventArgs args) { var p = args.GetCurrentPoint(this).Position; return new(p.X, p.Y); }
    private void Pressed(object sender, PointerRoutedEventArgs args)
    {
        var point = args.GetCurrentPoint(this); if (point.Properties.IsRightButtonPressed) return; Focus(FocusState.Pointer);
        // An edge command spans separate contacts; releasing the first must retain its source.
        if (Session.Mode != EditorMode.CreateEdge) CapturePointer(args.Pointer);
        Run(() => Session.PointerDown(Position(args), Modifiers(), point.Properties.IsMiddleButtonPressed)); args.Handled = true;
    }
    private void Moved(object sender, PointerRoutedEventArgs args) => Run(() => Session.PointerMove(Position(args)));
    private void Released(object sender, PointerRoutedEventArgs args)
    {
        Run(() => Session.PointerUp(Position(args))); releasingCapture = true;
        try { ReleasePointerCapture(args.Pointer); } finally { releasingCapture = false; } args.Handled = true;
    }
    private void Wheel(object sender, PointerRoutedEventArgs args)
    {
        var delta = args.GetCurrentPoint(this).Properties.MouseWheelDelta; Session.Viewport.ZoomAt(Position(args), Math.Pow(1.15, delta / 120d)); Invalidate(); args.Handled = true;
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
        var input = new TextBox { Text = value, AcceptsReturn = true, MinWidth = 320, MinHeight = 80 }; AutomationProperties.SetAutomationId(input, "LabelText");
        var dialog = new ContentDialog { XamlRoot = XamlRoot, Title = "Edit label", Content = input, PrimaryButtonText = "Apply", CloseButtonText = "Cancel", DefaultButton = ContentDialogButton.Primary };
        dialog.Opened += (_, _) => { input.Focus(FocusState.Programmatic); input.SelectAll(); };
        try { if (await dialog.ShowAsync() == ContentDialogResult.Primary) Session.Rename(id, input.Text); } catch (Exception ex) { Error?.Invoke(this, ex.Message); }
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
    private MenuFlyout ContextMenu()
    {
        var menu = new MenuFlyout();
        void Item(string text, Action action) { var item = new MenuFlyoutItem { Text = text }; item.Click += (_, _) => Run(action); menu.Items.Add(item); }
        Item("Edit label  F2", () => _ = EditLabelAsync()); Item("Duplicate  Ctrl+D", Session.Duplicate); Item("Delete  Del", Session.DeleteSelection);
        menu.Items.Add(new MenuFlyoutSeparator()); Item("Group  Ctrl+G", Session.Group); Item("Ungroup  Ctrl+Shift+G", Session.Ungroup);
        Item("Fold / unfold", () => { foreach (var id in Session.Selection.ToArray()) if (Document.Snapshot.Index().Nodes.TryGetValue(id, out var n) && n.IsGroup) Session.ToggleGroup(id); });
        menu.Items.Add(new MenuFlyoutSeparator()); Item("Fit graph  Home", Fit); return menu;
    }
}
