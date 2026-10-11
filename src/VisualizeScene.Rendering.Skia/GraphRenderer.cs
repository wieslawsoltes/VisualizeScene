using System.Diagnostics;
using SkiaSharp;
using SkiaSharp.HarfBuzz;
using VisualizeScene.Core;

namespace VisualizeScene.Rendering.Skia;

public sealed record RenderOptions(bool ShowGrid = true, double GridSize = 10, IReadOnlySet<string>? Selection = null,
    RectD? Marquee = null, (PointD Start, PointD End)? EdgePreview = null, bool Overview = false, PointD? CandidatePosition = null, bool IsValidConnection = true, IReadOnlyList<AlignmentGuide>? AlignmentGuides = null);
public sealed record RenderStatistics(int VisibleNodes, int VisibleEdges, int CachedNodePictures, double CpuDrawMilliseconds);

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
        GraphValidation.Validate(snapshot); graph = snapshot; index = snapshot.Index();
        order = snapshot.Nodes.Select((n, i) => (n.Id, i)).ToDictionary(p => p.Id, p => p.i); depths = [];
        foreach (var node in snapshot.Nodes)
        {
            var depth = 0; for (var parent = node.ParentId; parent is not null; parent = index.Nodes[parent].ParentId) depth++;
            depths[node.Id] = depth;
        }
        nodeIndex = new(snapshot.Nodes.Where(n => index.IsVisible(n.Id)).Select(n => (n.Bounds, n.Id)));
        foreach (var id in pictures.Keys.Where(id => !index.Nodes.ContainsKey(id)).ToArray()) { pictures[id].Picture.Dispose(); pictures.Remove(id); }
        foreach (var edge in edges.Values) edge.Path.Dispose(); edges.Clear();
        foreach (var edge in snapshot.Edges)
        {
            if (!GraphScene.IsEdgeVisible(index, edge)) continue;
            var points = index.EdgePath(edge); var path = new SKPath(); path.MoveTo(Point(points[0]));
            foreach (var point in points.Skip(1)) path.LineTo(Point(point)); edges[edge.Id] = new(edge, points, path);
        }
        edgeIndex = new(edges.Select(e => (RectD.Enclose(e.Value.Points.Select(p => new RectD(p.X, p.Y, 0, 0))).Inflate(14), e.Key)));
    }
    public void Render(SKCanvas canvas, GraphSnapshot snapshot, Viewport viewport, double width, double height, RenderOptions? options = null)
    {
        ObjectDisposedException.ThrowIf(disposed, this); options ??= new(); var timer = Stopwatch.StartNew(); Prepare(snapshot);
        var zoom = viewport.Zoom; var origin = viewport.Origin; var view = new RectD(origin.X, origin.Y, width / zoom, height / zoom).Inflate(20 / zoom);
        var visible = nodeIndex.Query(view).OrderBy(id => order[id]).Select(id => index!.Nodes[id]).ToArray(); var visibleEdges = edgeIndex.Query(view).Select(id => edges[id]).ToArray();
        var save = canvas.Save();
        try
        {
            canvas.ClipRect(SKRect.Create((float)width, (float)height)); canvas.Clear(SKColors.White); canvas.Scale((float)zoom); canvas.Translate((float)-origin.X, (float)-origin.Y);
            if (options.ShowGrid && !options.Overview) DrawGrid(canvas, view, zoom, options.GridSize);
            foreach (var node in visible.Where(n => n.IsGroup && !n.Collapsed).OrderBy(n => depths[n.Id])) DrawNode(canvas, node, zoom, options.Overview);
            foreach (var edge in visibleEdges) DrawEdge(canvas, edge, zoom, options);
            foreach (var node in visible.Where(n => !n.IsGroup || n.Collapsed)) DrawNode(canvas, node, zoom, options.Overview);
            if (!options.Overview)
            {
                if (zoom >= .25) DrawLabels(canvas, snapshot, view);
                foreach (var port in snapshot.Ports)
                {
                    if (!index!.IsVisible(port.NodeId) || !view.Contains(index.PortLocation(port.Id))) continue;
                    var p = index.PortLocation(port.Id); Use(SKColors.White); canvas.DrawCircle(Point(p), (float)(3.5 / zoom), paint); Use(new SKColor(56, 103, 151), SKPaintStyle.Stroke, (float)(1 / zoom)); canvas.DrawCircle(Point(p), (float)(3.5 / zoom), paint);
                }
                DrawSelection(canvas, zoom, options);
                DrawAlignmentGuides(canvas, zoom, options.AlignmentGuides);
            }
        }
        finally { canvas.RestoreToCount(save); }
        Statistics = new(visible.Length, visibleEdges.Length, pictures.Count, timer.Elapsed.TotalMilliseconds);
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
        foreach (var entry in pictures.Values) entry.Picture.Dispose(); pictures.Clear(); foreach (var edge in edges.Values) edge.Path.Dispose(); edges.Clear();
        paint.Dispose(); shaper.Dispose(); if (ownsTypeface) typeface.Dispose();
    }
}
