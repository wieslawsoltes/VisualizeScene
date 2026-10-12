using System.Globalization;
using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace VisualizeScene.Workbench;

public sealed partial class WorkbenchPage
{
    private void RefreshPropertyDiagnostics()
    {
        foreach (var name in new[] { "Label", "Text", "X", "Y", "Width", "Height", "Shape", "Fill", "Stroke", "FillPalette", "StrokePalette", "FillSwatchFFCC66", "FillSwatchDDEFE3", "OffsetX", "OffsetY", "FontSize", "RelativeX", "RelativeY", "Locked", "GroupGeometry", "GroupAppearance", "Apply", "Reset", "ClearText", "Directed", "Weight", "Routing", "LineWidth", "LinePattern", "SourceArrow", "TargetArrow" })
        {
            var key = "Property" + name;
            if (inspector.FindPropertyEditor(name) is { } editor) testControls[key] = editor; else testControls.Remove(key);
        }
    }
    private void PublishWorkspaceState()
    {
#if __WASM__
        if (!DiagnosticsEnabled || !ready) return;
        PublishScrollState();
        RefreshPropertyDiagnostics();
        static string N(double n) => double.IsFinite(n) ? n.ToString("R", CultureInfo.InvariantCulture) : "0";
        static string Q(string s) => "\"" + JsonEncodedText.Encode(s).ToString() + "\"";
        string Bounds(FrameworkElement e)
        {
            if (!e.IsLoaded) return "null";
            try
            {
                var p = e.TransformToVisual(this).TransformPoint(new Windows.Foundation.Point());
                return $"{{x:{N(p.X)},y:{N(p.Y)},width:{N(e.ActualWidth)},height:{N(e.ActualHeight)}}}";
            }
            catch { return "null"; }
        }
        var editorText = string.Join(",", new[] { "Label", "Fill", "Stroke", "Width", "Height", "FontSize", "Weight", "LineWidth" }.Select(name => (Name: name, Editor: inspector.FindPropertyEditor(name))).Where(pair => pair.Editor is TextBox).Select(pair => Q(pair.Name) + ":" + Q(((TextBox)pair.Editor!).Text)));
        var choices = string.Join(",", new[] { "Shape", "Routing", "LinePattern", "SourceArrow", "TargetArrow" }
            .Select(name => (Name: name, Editor: inspector.FindPropertyEditor(name))).Where(pair => pair.Editor is ComboBox)
            .Select(pair =>
            {
                var input = (ComboBox)pair.Editor!;
                return Q(pair.Name) + $":{{index:{input.SelectedIndex},open:{(input.IsDropDownOpen ? "true" : "false")},value:{Q(input.SelectedItem?.ToString() ?? "")}}}";
            }));
        var focused = new[] { "Label", "Text", "X", "Y", "Width", "Height", "Fill", "Stroke", "LineWidth", "FontSize", "OffsetX", "OffsetY", "RelativeX", "RelativeY", "Weight" }
            .FirstOrDefault(name => inspector.FindPropertyEditor(name) is Control input && input.FocusState != FocusState.Unfocused);
        var inspectorState = $"{{choices:{{{choices}}},focused:{(focused is null ? "null" : Q(focused))},batchCount:{inspector.BatchItemCount},batchExamined:{inspector.BatchExaminedItems},text:{{{editorText}}},count:{inspector.PropertyCount},geometryExpanded:{(inspector.IsPropertyGroupExpanded("Geometry") ? "true" : "false")},message:{Q(inspector.MessageText)}}}";
        foreach (var name in new[] { "EditLabel", "AddLabel", "AddPort", "Copy", "Cut", "Paste", "Duplicate", "Delete", "Group", "Ungroup", "Fold", "FitGroups", "CreateNode", "SelectAll", "Fit", "OrderBringToFront", "OrderSendToBack", "OrderRaise", "OrderLower" })
        {
            var key = "Context" + name;
            if (graph.IsContextMenuOpen && graph.FindContextCommand(name) is { } command) testControls[key] = command; else testControls.Remove(key);
        }
        var commands = string.Join(",", testControls.Where(p => p.Value is Control).Select(p => Q(p.Key) + ":" + (((Control)p.Value).IsEnabled ? "true" : "false")));
        var state = $"{{contextOpen:{(graph.IsContextMenuOpen ? "true" : "false")},modeCaption:{Q(modeText.Text)},inspector:{inspectorState},leftWidth:{N(workspace.LeftPaneWidth)},rightWidth:{N(workspace.RightPaneWidth)},leftVisible:{(workspace.IsLeftPaneVisible ? "true" : "false")},rightVisible:{(workspace.IsRightPaneVisible ? "true" : "false")},leftCollapsed:{(palettePane?.IsCollapsed == true ? "true" : "false")},overviewCollapsed:{(overviewPane?.IsCollapsed == true ? "true" : "false")},leftTab:{Q(structure.IsLoaded ? "Structure" : "Palette")},documentTitle:{Q(documentCaption.Text)},enabled:{{{commands}}},structure:{{rows:{structure.VisibleRowCount},builds:{structure.IndexBuilds},selected:{(structure.SelectedId is { } id ? Q(id) : "null")},bounds:{Bounds(structure.ItemsControl)}}},overview:{{recordings:{overview.Statistics.SceneRecordings},frames:{overview.Statistics.Frames},bounds:{Bounds(overview)}}}}}";
        Uno.Foundation.WebAssemblyRuntime.InvokeJS("globalThis.visualizeSceneWorkspace=" + state + ";");
#endif
    }
}
