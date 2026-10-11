using SkiaSharp;
using VisualizeScene.Core;

namespace VisualizeScene.Rendering.Skia;

public sealed partial class GraphRenderer
{
    private sealed record LabelVisual(GraphLabel Label, PointD Center, RectD Bounds, int Order);
    private sealed record PortVisual(string Id, PointD Center, int Order);
    private SpatialIndex<LabelVisual> labelIndex = new([]);
    private SpatialIndex<PortVisual> portIndex = new([]);
    private readonly List<LabelVisual> visibleLabels = [];
    private readonly List<PortVisual> visiblePorts = [];
    private Dictionary<string, LabelVisual> labelVisuals = [];
    private void PrepareAnnotations(GraphSnapshot snapshot)
    {
        var next = new Dictionary<string, LabelVisual>(StringComparer.Ordinal);
        for (var i = 0; i < snapshot.Labels.Length; i++)
        {
            var label = snapshot.Labels[i]; if (!GraphScene.IsOwnerVisible(index!, label.OwnerId)) continue;
            var center = GraphScene.LabelAnchor(index!, label.OwnerId) + label.Offset;
            if (labelVisuals.TryGetValue(label.Id, out var old) && old.Label == label && old.Center == center && old.Order == i)
                next[label.Id] = old;
            else next[label.Id] = new(label, center, GraphScene.LabelBounds(index!, label, MeasureLabel(label)), i);
        }
        foreach (var id in labelPictures.Keys.Where(id => !next.ContainsKey(id)).ToArray()) DropPicture(false, id);
        labelVisuals = next;
        labelIndex = new(next.Values.Select(v => (v.Bounds, v)));
        var ports = new List<(RectD, PortVisual)>();
        for (var i = 0; i < snapshot.Ports.Length; i++)
        {
            var port = snapshot.Ports[i]; if (!index!.IsVisible(port.NodeId)) continue;
            var p = index.PortLocation(port.Id); ports.Add((new(p.X, p.Y, 0, 0), new(port.Id, p, i)));
        }
        portIndex = new(ports);
    }
    private void DrawPorts(SKCanvas canvas, RectD view, double zoom)
    {
        visiblePorts.Clear(); var query = portIndex.Query(view, visiblePorts); examinedItems += query.ExaminedEntries;
        visiblePorts.Sort((a, b) => a.Order.CompareTo(b.Order));
        foreach (var port in visiblePorts)
        {
            Use(SKColors.White); canvas.DrawCircle(Point(port.Center), (float)(3.5 / zoom), paint);
            Use(new SKColor(56, 103, 151), SKPaintStyle.Stroke, (float)(1 / zoom)); canvas.DrawCircle(Point(port.Center), (float)(3.5 / zoom), paint);
            framePorts++;
        }
    }
    private void ClearAnnotationCache()
    {
        visibleLabels.Clear(); visiblePorts.Clear(); labelVisuals.Clear(); labelIndex = new([]); portIndex = new([]);
    }
}
