using System.Diagnostics;
using SkiaSharp;
using SkiaSharp.HarfBuzz;
using VisualizeScene.Core;

namespace VisualizeScene.Rendering.Skia;

public sealed record RenderOptions(bool ShowGrid = true, double GridSize = 10, IReadOnlySet<string>? Selection = null,
    RectD? Marquee = null, (PointD Start, PointD End)? EdgePreview = null, bool Overview = false, PointD? CandidatePosition = null, bool IsValidConnection = true, IReadOnlyList<AlignmentGuide>? AlignmentGuides = null, bool MarqueeContains = false, RectD? FocusBounds = null);
public sealed record RenderStatistics(int VisibleNodes, int VisibleEdges, int CachedNodePictures, double CpuDrawMilliseconds, int VisibleLabels = 0, int VisiblePorts = 0, int ExaminedItems = 0, int SelectionAdorners = 0);
public sealed record RenderPreparationStatistics(int SceneBuilds, int NodeIndexBuilds, int EdgeIndexBuilds, int AnnotationIndexBuilds, int CreatedPaths, int ReusedPaths);

/// <summary>Draws directly to the caller's Skia canvas, including Uno's shared composition surface. Dispose with the view.</summary>
public sealed partial class GraphRenderer : IDisposable
{
    private sealed record NodePicture(GraphNode Style, SKPicture Picture);
    private sealed record EdgePicture(GraphEdge Edge, IReadOnlyList<PointD> Points, SKPath Path);
    private GraphSnapshot? graph;
    private GraphIndex? index;
    private SpatialIndex<string> nodeIndex = new([]), edgeIndex = new([]);
    private readonly Dictionary<string, NodePicture> pictures = [];
    private readonly Dictionary<string, EdgePicture> edges = [];
    private Dictionary<string, int> order = [], depths = [];
    private readonly List<string> visibleNodeIds = [], visibleEdgeIds = [];
    private readonly List<GraphNode> visibleGroups = [], visibleNodes = [];
    private int sceneBuilds, nodeIndexBuilds, edgeIndexBuilds, annotationIndexBuilds, createdPaths, reusedPaths;
    private int frameLabels, framePorts, examinedItems, frameAdorners;
    public RenderPreparationStatistics PreparationStatistics => new(sceneBuilds, nodeIndexBuilds, edgeIndexBuilds, annotationIndexBuilds, createdPaths, reusedPaths);
    private readonly SKTypeface typeface;
    private readonly bool ownsTypeface;
    private readonly SKShaper shaper;
    private readonly SKPaint paint = new() { IsAntialias = true };
    private bool disposed;
    public RenderStatistics Statistics { get; private set; } = new(0, 0, 0, 0);
    /// <param name="fontTypeface">Optional caller-owned face. It must outlive this renderer and is not disposed by it.</param>
    public GraphRenderer(SKTypeface? fontTypeface = null)
    {
        typeface = fontTypeface ?? SKTypeface.FromFamilyName("Arial") ?? SKTypeface.Default;
        ownsTypeface = fontTypeface is null && !ReferenceEquals(typeface, SKTypeface.Default);
        shaper = new SKShaper(typeface);
    }
    private static SKRect Rect(RectD b) => new((float)b.X, (float)b.Y, (float)b.Right, (float)b.Bottom);
    private static SKPoint Point(PointD p) => new((float)p.X, (float)p.Y);
    private void Use(SKColor color, SKPaintStyle style = SKPaintStyle.Fill, float width = 1)
    {
        paint.Color = color; paint.Style = style; paint.StrokeWidth = width; paint.StrokeCap = SKStrokeCap.Round; paint.StrokeJoin = SKStrokeJoin.Round; paint.PathEffect = null;
    }
    private void Prepare(GraphSnapshot snapshot)
    {
        if (ReferenceEquals(graph, snapshot)) return;
        GraphValidation.Validate(snapshot); var previous = graph; index = snapshot.Index(); sceneBuilds++;
        var nodesChanged = previous is null || previous.Nodes != snapshot.Nodes;
        var geometryChanged = nodesChanged || previous!.Ports != snapshot.Ports;
        if (nodesChanged)
        {
            order = snapshot.Nodes.Select((n, i) => (n.Id, i)).ToDictionary(p => p.Id, p => p.i);
            depths = snapshot.Nodes.ToDictionary(n => n.Id, n => index.Depth(n.Id));
            nodeIndex = new(snapshot.Nodes.Where(n => index.IsVisible(n.Id)).Select(n => (n.Bounds, n.Id))); nodeIndexBuilds++;
            foreach (var id in pictures.Keys.Where(id => !index.Nodes.ContainsKey(id) || !index.IsVisible(id)).ToArray())
            { DropPicture(true, id); }
        }
        if (geometryChanged || previous!.Edges != snapshot.Edges)
        {
            var routesChanged = previous is null || !previous.Edges.Select(e => e.Id).SequenceEqual(snapshot.Edges.Select(e => e.Id));
            foreach (var id in edges.Keys.Where(id => !index.Edges.ContainsKey(id)).ToArray())
            { edges[id].Path.Dispose(); edges.Remove(id); routesChanged = true; }
            foreach (var edge in snapshot.Edges)
            {
                if (!GraphScene.IsEdgeVisible(index, edge))
                { if (edges.Remove(edge.Id, out var hidden)) { hidden.Path.Dispose(); routesChanged = true; } continue; }
                var points = index.EdgePath(edge);
                if (edges.TryGetValue(edge.Id, out var old) && old.Points.SequenceEqual(points))
                { edges[edge.Id] = new(edge, points, old.Path); reusedPaths++; continue; }
                old?.Path.Dispose(); var path = new SKPath(); path.MoveTo(Point(points[0]));
                foreach (var point in points.Skip(1)) path.LineTo(Point(point));
                edges[edge.Id] = new(edge, points, path); createdPaths++; routesChanged = true;
            }
            if (routesChanged || previous is null)
            {
                edgeIndex = new(snapshot.Edges.Where(e => edges.ContainsKey(e.Id)).Select(e => (RectD.Enclose(edges[e.Id].Points.Select(p => new RectD(p.X, p.Y, 0, 0))).Inflate(14), e.Id)));
                edgeIndexBuilds++;
            }
        }
        if (previous is null || geometryChanged || previous.Edges != snapshot.Edges || previous.Labels != snapshot.Labels)
        { PrepareAnnotations(snapshot); annotationIndexBuilds++; }
        graph = snapshot;
    }
    public void Render(SKCanvas canvas, GraphSnapshot snapshot, Viewport viewport, double width, double height, RenderOptions? options = null)
    {
        ObjectDisposedException.ThrowIf(disposed, this); options ??= new(); var timer = Stopwatch.StartNew(); Prepare(snapshot);
        var zoom = viewport.Zoom; var origin = viewport.Origin; var view = new RectD(origin.X, origin.Y, width / zoom, height / zoom).Inflate(20 / zoom);
        visibleNodeIds.Clear(); visibleEdgeIds.Clear(); visibleGroups.Clear(); visibleNodes.Clear();
        var nodeQuery = nodeIndex.Query(view, visibleNodeIds); var edgeQuery = edgeIndex.Query(view, visibleEdgeIds);
        foreach (var id in visibleNodeIds)
        {
            var node = index!.Nodes[id];
            if (node.IsGroup && !node.Collapsed) visibleGroups.Add(node); else visibleNodes.Add(node);
        }
        visibleNodes.Sort((a, b) => order[a.Id].CompareTo(order[b.Id]));
        visibleGroups.Sort((a, b) => { var c = depths[a.Id].CompareTo(depths[b.Id]); return c != 0 ? c : order[a.Id].CompareTo(order[b.Id]); });
        visibleLabels.Clear(); visiblePorts.Clear();
        frameLabels = framePorts = frameAdorners = 0; examinedItems = nodeQuery.ExaminedEntries + edgeQuery.ExaminedEntries;
        var save = canvas.Save();
        try
        {
            canvas.ClipRect(SKRect.Create((float)width, (float)height)); canvas.Clear(SKColors.White); canvas.Scale((float)zoom); canvas.Translate((float)-origin.X, (float)-origin.Y);
            if (options.ShowGrid && !options.Overview) DrawGrid(canvas, view, zoom, options.GridSize);
            foreach (var node in visibleGroups) DrawNode(canvas, node, zoom, options.Overview);
            foreach (var id in visibleEdgeIds) DrawEdge(canvas, edges[id], zoom, options);
            foreach (var node in visibleNodes) DrawNode(canvas, node, zoom, options.Overview);
            if (!options.Overview)
            {
                if (zoom >= .25) DrawLabels(canvas, snapshot, view);
                DrawPorts(canvas, view, zoom);
                DrawSelection(canvas, zoom, options);
                DrawAlignmentGuides(canvas, zoom, options.AlignmentGuides);
                if (options.FocusBounds is { } focus) DrawKeyboardFocus(canvas, focus, zoom);
            }
        }
        finally { canvas.RestoreToCount(save); }
        Statistics = new(visibleNodeIds.Count, visibleEdgeIds.Count, pictures.Count, timer.Elapsed.TotalMilliseconds, frameLabels, framePorts, examinedItems, frameAdorners);
    }
    public byte[] ExportPng(GraphSnapshot snapshot, double scale = 1)
    {
        if (!double.IsFinite(scale) || scale < .05 || scale > 8) throw new ArgumentOutOfRangeException(nameof(scale), "Export scale must be between 0.05 and 8.");
        var bounds = GraphScene.Bounds(snapshot, MeasureLabel).Inflate(40); var pixelWidth = Math.Ceiling(bounds.Width * scale); var pixelHeight = Math.Ceiling(bounds.Height * scale);
        if (!double.IsFinite(pixelWidth) || !double.IsFinite(pixelHeight) || pixelWidth < 1 || pixelHeight < 1 || pixelWidth > 16384 || pixelHeight > 16384 || pixelWidth * pixelHeight > 64000000)
            throw new InvalidOperationException("Raster export is limited to 64 million pixels and 16384 pixels per side. Use SVG for larger diagrams.");
        var width = (int)pixelWidth; var height = (int)pixelHeight;
        using var surface = SKSurface.Create(new SKImageInfo(width, height)) ?? throw new InvalidOperationException("Could not allocate export surface.");
        var viewport = new Viewport(); viewport.ZoomAt(new(), scale); viewport.Center(bounds.Center, width, height); Render(surface.Canvas, snapshot, viewport, width, height, new(ShowGrid: false));
        using var image = surface.Snapshot(); using var data = image.Encode(SKEncodedImageFormat.Png, 100); return data.ToArray();
    }
    public void Dispose()
    {
        if (disposed) return; disposed = true;
        DisposePictures(); foreach (var edge in edges.Values) edge.Path.Dispose(); edges.Clear();
        paint.Dispose(); shaper.Dispose(); if (ownsTypeface) typeface.Dispose();
        graph = null; index = null; measuredLabels.Clear(); ClearAnnotationCache(); visibleNodeIds.Clear(); visibleEdgeIds.Clear(); visibleNodes.Clear(); visibleGroups.Clear();
        nodeIndex = new([]); edgeIndex = new([]); order.Clear(); depths.Clear();
    }
}
