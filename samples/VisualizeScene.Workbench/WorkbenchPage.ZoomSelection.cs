using System.Globalization;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;

namespace VisualizeScene.Workbench;

public sealed partial class WorkbenchPage
{
    private long zoomFocusRequest;
    private void InitializeZoomSelection()
    {
        AutomationProperties.SetName(zoomLevels, "Graph zoom");
        RegisterMenuDiagnostics("ZoomLevels", zoomLevels, includeControl: true);
        zoomLevels.SelectionChanged += (_, _) => Run(() =>
        {
            if (updatingZoom || zoomLevels.SelectedItem is not string value) return;
            graph.Zoom(double.Parse(value.TrimEnd('%'), CultureInfo.InvariantCulture) / 100 / graph.Session.Viewport.Zoom);
            // Do not move focus inside a selection callback while the native popup
            // may still be closing. Closed-keyboard changes also retain their focus.
        });
        zoomLevels.DropDownOpened += (_, _) => zoomFocusRequest++;
        zoomLevels.DropDownClosed += (_, _) =>
        {
            var request = ++zoomFocusRequest;
            DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () =>
            {
                if (request != zoomFocusRequest || XamlRoot is not { } root || !graph.IsLoaded ||
                    !zoomLevels.IsLoaded || zoomLevels.IsDropDownOpen || graph.EditingLabelText is not null) return;
                var focused = FocusManager.GetFocusedElement(root) as DependencyObject;
                if (focused is null || InZoomControl(focused) || ReferenceEquals(focused, graph))
                    graph.Focus(FocusState.Programmatic);
                PublishTestState();
            });
        };
    }
    private bool InZoomControl(DependencyObject? element)
    {
        for (var current = element; current is not null; current = VisualTreeHelper.GetParent(current))
        {
            if (ReferenceEquals(current, zoomLevels)) return true;
            // A dropdown's item containers live under its popup, not under the ComboBox.
            if (current is ComboBoxItem)
                for (var i = 0; i < zoomLevels.Items.Count; i++)
                    if (ReferenceEquals(current, zoomLevels.ContainerFromIndex(i))) return true;
        }
        return false;
    }
    private void PublishZoomChoice()
    {
#if __WASM__
        if (!DiagnosticsEnabled) return;
        var open = zoomLevels.IsDropDownOpen;
        var ready = open && zoomLevels.ContainerFromIndex(0) is FrameworkElement { IsLoaded: true, ActualWidth: > 0, ActualHeight: > 0 };
        var focused = XamlRoot is { } root && InZoomControl(FocusManager.GetFocusedElement(root) as DependencyObject);
        Uno.Foundation.WebAssemblyRuntime.InvokeJS($"globalThis.visualizeSceneZoomChoice={{open:{(open ? "true" : "false")},index:{zoomLevels.SelectedIndex},ready:{(ready ? "true" : "false")},focused:{(focused ? "true" : "false")}}};");
#endif
    }
}
