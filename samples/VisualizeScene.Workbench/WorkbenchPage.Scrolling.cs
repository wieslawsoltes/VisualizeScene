using System.Globalization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using VisualizeScene.Controls.Uno;
using VisualizeScene.Core;

namespace VisualizeScene.Workbench;

public sealed partial class WorkbenchPage
{
    private GraphScrollViewer? scrollViewer;
    private GraphScrollViewer CreateScrollViewer()
    {
        var view = new GraphScrollViewer { Target = graph, Overlay = search };
        view.Error += (_, text) => Report(text);
        Register("GraphHScroll", view.HorizontalBar); Register("GraphVScroll", view.VerticalBar); Register("GraphScrollFit", view.FitButton);
        scrollViewer = view; return view;
    }
    private void AddScrollMenu(MenuBar menu)
    {
        AddSelectionNavigation(menu);
        var item = new ToggleMenuFlyoutItem { Text = "Canvas scrollbars", IsChecked = true };
        Register("ToggleCanvasScrollbars", item);
        item.Click += (_, _) => { if (scrollViewer is not null) scrollViewer.ScrollBarsVisible = item.IsChecked; };
        menu.Items.First(m => m.Title == "View").Items.Add(item);
    }
    private void PublishScrollState()
    {
#if __WASM__
        if (!DiagnosticsEnabled || scrollViewer is null) return;
        static string N(double v) => double.IsFinite(v) ? v.ToString("R", CultureInfo.InvariantCulture) : "0";
        static string Axis(ViewportScrollAxis a) => $"{{min:{N(a.MinimumOrigin)},max:{N(a.MaximumOrigin)},origin:{N(a.Origin)},value:{N(a.Value)},page:{N(a.ViewportSize)},small:{N(a.SmallChange)},large:{N(a.LargeChange)},enabled:{(a.CanScroll ? "true" : "false")}}}";
        foreach (var (name, horizontal) in new[] { ("GraphHThumb", true), ("GraphVThumb", false) })
            if (scrollViewer.FindThumb(horizontal) is { } thumb) testControls[name] = thumb; else testControls.Remove(name);
        var model = scrollViewer.ScrollModel;
        Uno.Foundation.WebAssemblyRuntime.InvokeJS($"globalThis.visualizeSceneScroll={{visible:{(scrollViewer.ScrollBarsVisible ? "true" : "false")},dragging:{(scrollViewer.IsDragging ? "true" : "false")},builds:{scrollViewer.ModelBuilds},horizontal:{Axis(model.Horizontal)},vertical:{Axis(model.Vertical)}}};");
#endif
    }
}
