using System.Globalization;
using System.Text;
using System.Text.Json;
using Microsoft.UI.Xaml;
using VisualizeScene.Serialization;

namespace VisualizeScene.Workbench;

public sealed partial class WorkbenchPage
{
    private bool? diagnosticsEnabled;
    private bool DiagnosticsEnabled
    {
        get
        {
#if __WASM__
            return diagnosticsEnabled ??= Uno.Foundation.WebAssemblyRuntime.InvokeJS("new URLSearchParams(location.search).get('test') === '1' ? 'yes' : 'no'") == "yes";
#else
            return false;
#endif
        }
    }
    private void StartDiagnostics()
    {
        if (!DiagnosticsEnabled) return;
        diagnosticTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) }; diagnosticTimer.Tick += (_, _) => PublishTestState(); diagnosticTimer.Start();
    }
    private void PublishTestState()
    {
#if __WASM__
        if (!DiagnosticsEnabled) return;
        static string Number(double value) => double.IsFinite(value) ? value.ToString("R", CultureInfo.InvariantCulture) : "0";
        static string String(string value) => "\"" + JsonEncodedText.Encode(value).ToString() + "\"";
        string Bounds(FrameworkElement element)
        {
            try { var p = element.TransformToVisual(this).TransformPoint(new Windows.Foundation.Point()); return $"{{\"x\":{Number(p.X)},\"y\":{Number(p.Y)},\"width\":{Number(element.ActualWidth)},\"height\":{Number(element.ActualHeight)}}}"; }
            catch { return "null"; }
        }
        var model = GraphIO.ToJson(document.Snapshot); var controls = string.Join(",", testControls.Where(p => p.Value.IsLoaded).Select(p => String(p.Key) + ":" + Bounds(p.Value)));
        var displaySelection = string.Join(",", graph.Session.DisplaySelection.Select(String));
        var marquee = graph.Session.Marquee is { } rectangle ? $"{{\"x\":{Number(rectangle.X)},\"y\":{Number(rectangle.Y)},\"width\":{Number(rectangle.Width)},\"height\":{Number(rectangle.Height)}}}" : "null";
        var selected = string.Join(",", graph.Session.Selection.Select(String));
        var index = document.Snapshot.Index();
        var paths = string.Join(",", document.Snapshot.Edges.Select(e => String(e.Id) + ":[" + string.Join(",", index.EdgePath(e).Select(p => $"{{\"x\":{Number(p.X)},\"y\":{Number(p.Y)}}}")) + "]"));
        var labels = string.Join(",", document.Snapshot.Labels.Select(label =>
        {
            var b = graph.Session.GetLabelBounds(label);
            return String(label.Id) + $":{{\"x\":{Number(b.X)},\"y\":{Number(b.Y)},\"width\":{Number(b.Width)},\"height\":{Number(b.Height)}}}";
        }));
        var guides = string.Join(",", graph.Session.AlignmentGuides.Select(g => $"{{\"x1\":{Number(g.Start.X)},\"y1\":{Number(g.Start.Y)},\"x2\":{Number(g.End.X)},\"y2\":{Number(g.End.Y)}}}"));
        var placement = lastLabelPlacement is { } result ? $"{{\"moved\":{result.MovedLabels},\"eligible\":{result.EligibleLabels},\"conflicts\":{result.Conflicts.Length}}}" : "null";
        var script = $"globalThis.visualizeSceneState={{ready:{(ready ? "true" : "false")},frames:{frameCount},revision:{document.Revision},mode:'{graph.Session.Mode}',zoom:{Number(graph.Session.Viewport.Zoom)},origin:{{x:{Number(graph.Session.Viewport.Origin.X)},y:{Number(graph.Session.Viewport.Origin.Y)}}},canvas:{Bounds(graph)},controls:{{{controls}}},selection:[{selected}],displaySelection:[{displaySelection}],marquee:{marquee},marqueeContains:{(graph.Session.IsMarqueeContainment ? "true" : "false")},marqueeMode:'{graph.Session.MarqueeMode}',marqueeKinds:{(int)graph.Session.MarqueeItemKinds},guides:[{guides}],snapToItems:{(graph.Session.SnapToItems ? "true" : "false")},snapToGrid:{(graph.Session.SnapToGrid ? "true" : "false")},labelPlacement:{placement},edgePaths:{{{paths}}},labelBounds:{{{labels}}},labelEditor:{{ready:{(graph.IsLabelEditorReady ? "true" : "false")},text:{(graph.EditingLabelText is { } labelText ? String(labelText) : "null")}}},error:{String(lastError)},analysis:{String(analysisOutput.Text)},status:{String(status.Text)},model:{model}}};";
        Uno.Foundation.WebAssemblyRuntime.InvokeJS(script);
#endif
    }
}
