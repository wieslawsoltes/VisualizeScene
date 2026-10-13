using System.Globalization;
using System.Text.Json;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using VisualizeScene.Controls.Uno;
using VisualizeScene.Core;

namespace VisualizeScene.Workbench;

public sealed partial class WorkbenchPage
{
    private sealed class GridSettingsDialog : ContentDialog
    {
        // ContentDialog buttons belong to its template namescope, not the page namescope.
        public FrameworkElement? NativePart(string name) => GetTemplateChild(name) as FrameworkElement;
    }
    private GraphGridEditor? gridEditor;
    private ContentDialog? gridDialog;
    private bool gridDialogReady;
    private ToggleMenuFlyoutItem? showGridItem, snapGridItem;
    private void AddGridMenu(MenuBarItem view)
    {
        showGridItem = new() { Text = "Show grid", IsChecked = graph.Session.ShowGrid };
        snapGridItem = new() { Text = "Snap to grid", IsChecked = graph.Session.SnapToGrid };
        Register("ToggleGrid",showGridItem); Register("ToggleGridSnap",snapGridItem);
        showGridItem.Click += (_,_) => Run(() => { graph.Session.ShowGrid = showGridItem.IsChecked; ReturnSelectionFocus(view,showGridItem); });
        snapGridItem.Click += (_,_) => Run(() => { graph.Session.SnapToGrid = snapGridItem.IsChecked; ReturnSelectionFocus(view,snapGridItem); });
        view.Items.Add(showGridItem);view.Items.Add(snapGridItem);
        var options = new MenuFlyoutItem { Text = "Grid settings…" };Register("GridSettings",options);view.Items.Add(options);
        options.Click += (_,_) => {
            void Open() => DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () => { if(IsLoaded && XamlRoot is not null) _ = RunAsync(ShowGridSettings); });
            if(!options.IsLoaded) { Open(); return; }
            RoutedEventHandler? closed = null; closed = (_,_) => { options.Unloaded -= closed; Open(); }; options.Unloaded += closed;
        };
        graph.Session.Invalidated += (_,_) => SyncGridMenu();
    }
    private async Task ShowGridSettings()
    {
        if (gridDialog is not null || XamlRoot is not { } root || !graph.IsLoaded) return;
        var session = graph.Session; var original = graph.GridSettings; session.CancelGesture();
        using var editor = new GraphGridEditor(); editor.LoadSettings(original); gridEditor = editor;
        // Give the reusable editor a finite viewport; do not put its pinned footer
        // inside another scrolling region. Resize while open as well as at creation.
        void SizeEditor() => editor.Height = Math.Max(120,Math.Min(550,root.Size.Height - 200));
        void RootChanged(XamlRoot sender, XamlRootChangedEventArgs args) => SizeEditor();
        SizeEditor();
        var dialog = new GridSettingsDialog { XamlRoot = root, Title = "Grid settings", Content = editor, PrimaryButtonText = "Apply", CloseButtonText = "Cancel", DefaultButton = ContentDialogButton.Primary };
        gridDialog = dialog; gridDialogReady = false; Register("GridOptionsDialog",dialog); Register("GridOptionsEditor",editor);
        // Parent viewport observations have no pointer observer: they must not replace
        // the target control's native PointerEntered token when events bubble.
        foreach(var name in new[]{"Body","Footer"}) if(editor.FindElement(name) is { } part) Register("GridOption"+name,part);
        foreach(var name in new[]{"Pattern","Color","Thickness","Horizontal","Vertical","OriginX","OriginY","Axes","Anchor","Visible","Snap","Reset","Preview","Feedback"})
            if(editor.FindElement(name) is { } element) { Register("GridOption"+name,element); if(element is Control) RegisterMenuDiagnostics("GridOption"+name,element,includeControl:true); }
        dialog.Opened += (_,_) => {
            gridDialogReady = true;
            if(dialog.NativePart("PrimaryButton") is FrameworkElement apply) { Register("GridOptionsApply",apply); RegisterMenuDiagnostics("GridOptionsApply",apply,includeControl:true); }
            if(dialog.NativePart("CloseButton") is FrameworkElement cancel) { Register("GridOptionsCancel",cancel); RegisterMenuDiagnostics("GridOptionsCancel",cancel,includeControl:true); }
            editor.FindElement("Horizontal")?.FocusIfControl(); PublishTestState();
        };
        var accepted = false;
        dialog.PrimaryButtonClick += (_,args) => {
            if(!editor.TryReadSettings(out var settings)) { args.Cancel = true; PublishTestState(); return; }
            if(!graph.IsLoaded || !ReferenceEquals(session,graph.Session) || !ReferenceEquals(original,graph.GridSettings)) {
                editor.ShowError("Grid settings changed while this dialog was open. Cancel and reopen it.");args.Cancel = true;PublishTestState();return;
            }
            graph.GridSettings = settings; accepted = true;
        };
        RoutedEventHandler unloaded = (_,_) => dialog.Hide();
        Unloaded += unloaded; root.Changed += RootChanged;
        try { await dialog.ShowAsync(); }
        finally {
            Unloaded -= unloaded; root.Changed -= RootChanged; gridDialogReady = false; gridDialog = null; gridEditor = null;
            foreach(var key in testControls.Keys.Where(k=>k.StartsWith("GridOption",StringComparison.Ordinal)).ToArray()) testControls.Remove(key);
            SyncGridMenu();
            if(graph.IsLoaded && ReferenceEquals(session,graph.Session)) { if(accepted) status.Text = "Grid settings applied."; graph.Focus(FocusState.Programmatic); }
            PublishTestState();
        }
    }
    private void SyncGridMenu()
    { if(showGridItem is not null) showGridItem.IsChecked = graph.Session.ShowGrid; if(snapGridItem is not null) snapGridItem.IsChecked = graph.Session.SnapToGrid; }
    private string GridDiagnosticsJson()
    {
        static string N(double value) => double.IsFinite(value) ? value.ToString("R",CultureInfo.InvariantCulture) : "0";
        static string S(string value) => "\""+JsonEncodedText.Encode(value)+"\"";
        var g = graph.GridSettings;var stats = graph.GridStatistics;var plan = stats.Plan;
        var scroller = gridEditor?.FindElement("Body") as ScrollViewer;
        var inputs = "{" + string.Join(",", new[]{"Horizontal","Vertical","OriginX","OriginY","Color","Thickness"}.Select(name => S(name)+":"+S((gridEditor?.FindElement(name) as TextBox)?.Text??"")))+"}";
        string Combo(string name) => gridEditor?.FindElement(name) is ComboBox c ? $"{{index:{c.SelectedIndex},open:{(c.IsDropDownOpen?"true":"false")}}}" : "null";
        return $"{{inputs:{inputs},scrollOffset:{N(scroller?.VerticalOffset??0)},scrollableHeight:{N(scroller?.ScrollableHeight??0)},ready:{(gridDialogReady?"true":"false")},error:{S(gridEditor?.ValidationError??"")},horizontal:{N(g.HorizontalSpacing)},vertical:{N(g.VerticalSpacing)},origin:{{x:{N(g.Origin.X)},y:{N(g.Origin.Y)}}},pattern:{S(g.Pattern.ToString())},color:{S(g.Color)},thickness:{N(g.Thickness)},visible:{(g.Visible?"true":"false")},snap:{(g.SnapEnabled?"true":"false")},axes:{(int)g.SnapAxes},anchor:{S(g.NodeAnchor.ToString())},marks:{plan.Marks},xStep:{N(plan.X.Step)},yStep:{N(plan.Y.Step)},xFirst:{N(plan.X.First)},yFirst:{N(plan.Y.First)},xCount:{plan.X.Count},yCount:{plan.Y.Count},builds:{stats.GeometryBuilds},patternInput:{Combo("Pattern")},axesInput:{Combo("Axes")},anchorInput:{Combo("Anchor")}}}";
    }
}

internal static class GridEditorFocus
{
    internal static void FocusIfControl(this FrameworkElement element) { if(element is Control c) c.Focus(FocusState.Programmatic); }
}
