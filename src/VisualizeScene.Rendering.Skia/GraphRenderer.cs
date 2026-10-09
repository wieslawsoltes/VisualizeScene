using System.Diagnostics;
using System.Globalization;
using SkiaSharp;
using SkiaSharp.HarfBuzz;
using VisualizeScene.Core;

namespace VisualizeScene.Rendering.Skia;

public sealed record RenderOptions(bool ShowGrid = true, double GridSize = 10, IReadOnlySet<string>? Selection = null,
    RectD? Marquee = null, (PointD Start, PointD End)? EdgePreview = null, bool Overview = false);
public sealed record RenderStatistics(int VisibleNodes, int VisibleEdges, int CachedNodePictures, double CpuDrawMilliseconds);

/// <summary>Draws directly to the caller's Skia canvas, including Uno's shared composition surface. Dispose with the view.</summary>
public sealed class GraphRenderer : IDisposable
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
    private readonly SKShaper shaper;
    private readonly SKPaint paint = new() { IsAntialias = true };
    private bool disposed;
    public RenderStatistics Statistics { get; private set; } = new(0, 0, 0, 0);
    public GraphRenderer(SKTypeface? fontTypeface = null)
    {
        typeface = fontTypeface is null ? SKTypeface.FromFamilyName("Arial") ?? SKTypeface.Default : SKTypeface.FromFamilyName(fontTypeface.FamilyName, fontTypeface.FontStyle) ?? SKTypeface.Default;
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
            if (index.VisibleRepresentative(edge.SourceId) == index.VisibleRepresentative(edge.TargetId) && edge.SourceId != edge.TargetId) continue;
            var points = index.EdgePath(edge); var path = new SKPath(); path.MoveTo(Point(points[0]));
            foreach (var point in points.Skip(1)) path.LineTo(Point(point));
            edges[edge.Id] = new(edge, points, path);
        }
        edgeIndex = new(edges.Select(e => (RectD.Enclose(e.Value.Points.Select(p => new RectD(p.X, p.Y, 0, 0))).Inflate(14), e.Key)));
    }
    public void Render(SKCanvas canvas, GraphSnapshot snapshot, Viewport viewport, double width, double height, RenderOptions? options = null)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        options ??= new(); var timer = Stopwatch.StartNew(); Prepare(snapshot);
        var zoom = viewport.Zoom; var origin = viewport.Origin; var view = new RectD(origin.X, origin.Y, width / zoom, height / zoom).Inflate(20 / zoom);
        var visible = nodeIndex.Query(view).OrderBy(id => order[id]).Select(id => index!.Nodes[id]).ToArray();
        var visibleEdges = edgeIndex.Query(view).Select(id => edges[id]).ToArray();
        var save = canvas.Save();
        try
        {
            canvas.ClipRect(SKRect.Create((float)width, (float)height)); canvas.Clear(SKColors.White);
            canvas.Scale((float)zoom); canvas.Translate((float)-origin.X, (float)-origin.Y);
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
            }
        }
        finally { canvas.RestoreToCount(save); }
        Statistics = new(visible.Length, visibleEdges.Length, pictures.Count, timer.Elapsed.TotalMilliseconds);
    }
    private void DrawGrid(SKCanvas canvas, RectD view, double zoom, double step)
    {
        if (!double.IsFinite(step) || step <= 0) return;
        while (step * zoom < 14) step *= 2;
        while (step * zoom > 45) step /= 2;
        Use(new SKColor(211, 220, 229));
        for (var x = Math.Floor(view.X / step) * step; x <= view.Right; x += step)
            for (var y = Math.Floor(view.Y / step) * step; y <= view.Bottom; y += step)
                canvas.DrawCircle((float)x, (float)y, (float)(.65 / zoom), paint);
    }
    private static SKPath Shape(GraphNode node)
    {
        var b = node.Bounds; var path = new SKPath();
        switch (node.Shape)
        {
            case NodeShape.Rectangle: path.AddRect(Rect(b)); break;
            case NodeShape.RoundedRectangle: path.AddRoundRect(Rect(b), 6, 6); break;
            case NodeShape.Ellipse: path.AddOval(Rect(b)); break;
            default:
                PointD[] points = node.Shape switch
                {
                    NodeShape.Diamond => [new(b.Center.X, b.Y), new(b.Right, b.Center.Y), new(b.Center.X, b.Bottom), new(b.X, b.Center.Y)],
                    NodeShape.Hexagon => [new(b.X + b.Width / 4, b.Y), new(b.Right - b.Width / 4, b.Y), new(b.Right, b.Center.Y), new(b.Right - b.Width / 4, b.Bottom), new(b.X + b.Width / 4, b.Bottom), new(b.X, b.Center.Y)],
                    _ => [new(b.X + b.Width / 5, b.Y), new(b.Right, b.Y), new(b.Right - b.Width / 5, b.Bottom), new(b.X, b.Bottom)]
                };
                path.MoveTo(Point(points[0])); foreach (var point in points.Skip(1)) path.LineTo(Point(point)); path.Close(); break;
        }
        return path;
    }
    private void DrawNode(SKCanvas canvas, GraphNode node, double zoom, bool overview)
    {
        if (overview || zoom < .25)
        {
            using var shape = Shape(node); Use(SKColor.Parse(node.Fill)); canvas.DrawPath(shape, paint); Use(SKColor.Parse(node.Stroke), SKPaintStyle.Stroke, 1.5f); canvas.DrawPath(shape, paint); return;
        }
        var style = node with { Bounds = new(0, 0, node.Bounds.Width, node.Bounds.Height), ParentId = null, Locked = false };
        if (!pictures.TryGetValue(node.Id, out var cached) || cached.Style != style)
        {
            cached?.Picture.Dispose(); using var recorder = new SKPictureRecorder();
            var target = recorder.BeginRecording(new SKRect(-3, -3, (float)node.Bounds.Width + 3, (float)node.Bounds.Height + 3));
            using var shape = Shape(style); Use(SKColor.Parse(style.Fill)); target.DrawPath(shape, paint); Use(SKColor.Parse(style.Stroke), SKPaintStyle.Stroke, 1.4f); target.DrawPath(shape, paint);
            if (style.IsGroup && !style.Collapsed)
            {
                Use(new SKColor(221, 232, 243)); target.DrawRoundRect(new SKRect(1, 1, (float)style.Bounds.Width - 1, 28), 5, 5, paint);
                DrawText(target, style.Text, new(style.Bounds.Width / 2, 14), 12, Math.Max(0, style.Bounds.Width - 34));
            }
            else DrawText(target, style.Text, style.Bounds.Center, 13, style.Bounds.Width * (style.Shape == NodeShape.Diamond ? .6 : .82), Math.Max(0, style.Bounds.Height - 10));
            if (style.IsGroup)
            {
                Use(SKColor.Parse(style.Stroke), SKPaintStyle.Stroke, 1.2f); target.DrawRect(new SKRect(8, 8, 20, 20), paint); target.DrawLine(11, 14, 17, 14, paint); if (style.Collapsed) target.DrawLine(14, 11, 14, 17, paint);
            }
            pictures[node.Id] = cached = new(style, recorder.EndRecording());
        }
        var save = canvas.Save(); canvas.Translate((float)node.Bounds.X, (float)node.Bounds.Y); canvas.DrawPicture(cached.Picture); canvas.RestoreToCount(save);
    }
    /// <summary>Bounded grapheme-safe truncation. Even widths smaller than one glyph terminate immediately.</summary>
    public static string Ellipsize(string text, double maxWidth, Func<string, double> measure)
    {
        ArgumentNullException.ThrowIfNull(text); ArgumentNullException.ThrowIfNull(measure);
        if (maxWidth <= 0 || double.IsNaN(maxWidth)) return "";
        if (measure(text) <= maxWidth) return text;
        const string ellipsis = "…";
        if (measure(ellipsis) > maxWidth) return "";
        var starts = StringInfo.ParseCombiningCharacters(text); var low = 0; var high = starts.Length;
        while (low < high)
        {
            var middle = (low + high + 1) / 2;
            var end = middle == starts.Length ? text.Length : starts[middle];
            if (measure(text[..end] + ellipsis) <= maxWidth) low = middle; else high = middle - 1;
        }
        return (low == 0 ? "" : text[..(low == starts.Length ? text.Length : starts[low])]) + ellipsis;
    }
    private void DrawText(SKCanvas canvas, string text, PointD center, double size, double maxWidth = double.PositiveInfinity, double maxHeight = double.PositiveInfinity, bool background = false)
    {
        if (string.IsNullOrEmpty(text) || maxWidth <= 0 || maxHeight <= 0) return;
        using var font = new SKFont(typeface, (float)size) { Edging = SKFontEdging.Antialias, Subpixel = true };
        var lines = new List<string>();
        foreach (var paragraph in text.Replace("\r", "").Split('\n'))
        {
            if (!double.IsFinite(maxWidth) || font.MeasureText(paragraph) <= maxWidth) { lines.Add(paragraph); continue; }
            var line = "";
            foreach (var word in paragraph.Split(' '))
            {
                var next = line.Length == 0 ? word : line + " " + word;
                if (line.Length > 0 && font.MeasureText(next) > maxWidth) { lines.Add(line); line = word; } else line = next;
            }
            lines.Add(line);
        }
        var maxLines = double.IsFinite(maxHeight) ? Math.Max(1, (int)(maxHeight / (size * 1.25))) : lines.Count;
        if (lines.Count > maxLines) { lines.RemoveRange(maxLines, lines.Count - maxLines); lines[^1] += "…"; }
        var lineHeight = size * 1.25; var metrics = font.Metrics;
        for (var i = 0; i < lines.Count; i++)
        {
            var line = Ellipsize(lines[i], maxWidth, value => font.MeasureText(value));
            var baseline = center.Y + (i - (lines.Count - 1) / 2d) * lineHeight - (metrics.Ascent + metrics.Descent) / 2;
            if (background)
            {
                var textWidth = font.MeasureText(line); Use(new SKColor(255, 255, 255, 238));
                canvas.DrawRoundRect(new SKRect((float)(center.X - textWidth / 2 - 4), (float)(baseline + metrics.Ascent - 2), (float)(center.X + textWidth / 2 + 4), (float)(baseline + metrics.Descent + 2)), 3, 3, paint);
            }
            Use(new SKColor(33, 56, 77)); canvas.DrawShapedText(shaper, line, (float)center.X, (float)baseline, SKTextAlign.Center, font, paint);
        }
    }
    private void DrawEdge(SKCanvas canvas, EdgePicture edge, double zoom, RenderOptions options)
    {
        var selected = options.Selection?.Contains(edge.Edge.Id) == true;
        Use(selected ? new SKColor(0, 112, 215) : SKColor.Parse(edge.Edge.Stroke), SKPaintStyle.Stroke, selected ? (float)(2.5 / zoom) : 1.5f); canvas.DrawPath(edge.Path, paint);
        if (edge.Edge.Directed && edge.Points.Count >= 2)
        {
            var end = edge.Points[^1]; var previous = edge.Points.Reverse().Skip(1).FirstOrDefault(p => PointD.Distance(p, end) > .001, edge.Points[0]); var delta = end - previous; var length = delta.Length;
            if (length > .001)
            {
                var unit = delta * (1 / length); var normal = new PointD(-unit.Y, unit.X); var tipSize = options.Overview ? 5 : 8;
                using var arrow = new SKPath(); arrow.MoveTo(Point(end)); arrow.LineTo(Point(end - unit * tipSize + normal * (tipSize * .45))); arrow.LineTo(Point(end - unit * tipSize - normal * (tipSize * .45))); arrow.Close(); paint.Style = SKPaintStyle.Fill; canvas.DrawPath(arrow, paint);
            }
        }
        if (!options.Overview && zoom >= .35 && edge.Edge.Text.Length > 0)
        {
            var points = edge.Points; var position = PointD.Lerp(points[(points.Count - 1) / 2], points[points.Count / 2], .5);
            DrawText(canvas, edge.Edge.Text, position + new PointD(0, -9), 11, 220, 50, true);
        }
    }
    private void DrawLabels(SKCanvas canvas, GraphSnapshot snapshot, RectD view)
    {
        foreach (var label in snapshot.Labels)
        {
            PointD center;
            if (index!.Nodes.TryGetValue(label.OwnerId, out var node)) { if (!index.IsVisible(node.Id)) continue; center = node.Bounds.Center; }
            else if (index.Ports.TryGetValue(label.OwnerId, out var port)) { if (!index.IsVisible(port.NodeId)) continue; center = index.PortLocation(port.Id); }
            else if (edges.TryGetValue(label.OwnerId, out var edge)) center = PointD.Lerp(edge.Points[0], edge.Points[^1], .5);
            else continue;
            center += label.Offset; if (view.Inflate(200).Contains(center)) DrawText(canvas, label.Text, center, label.FontSize, background: true);
        }
    }
    private void DrawSelection(SKCanvas canvas, double zoom, RenderOptions options)
    {
        var accent = new SKColor(0, 112, 215); var handle = (float)(3.5 / zoom);
        void Handle(PointD center)
        {
            var rect = new SKRect((float)center.X - handle, (float)center.Y - handle, (float)center.X + handle, (float)center.Y + handle);
            Use(SKColors.White); canvas.DrawRect(rect, paint); Use(accent, SKPaintStyle.Stroke, (float)(1.3 / zoom)); canvas.DrawRect(rect, paint);
        }
        if (options.Selection is not null) foreach (var id in options.Selection)
        {
            if (index!.Nodes.TryGetValue(id, out var node) && index.IsVisible(id))
            {
                var b = node.Bounds; Use(accent, SKPaintStyle.Stroke, (float)(1.6 / zoom)); canvas.DrawRect(Rect(b.Inflate(2 / zoom)), paint);
                if (!node.Locked) { Handle(new(b.X, b.Y)); Handle(new(b.Right, b.Y)); Handle(new(b.Right, b.Bottom)); Handle(new(b.X, b.Bottom)); }
            }
            else if (index!.Edges.TryGetValue(id, out var edge) && !edge.Bends.IsDefault) foreach (var bend in edge.Bends) Handle(bend);
        }
        if (options.Marquee is { } marquee)
        {
            Use(new SKColor(0, 112, 215, 24)); canvas.DrawRect(Rect(marquee), paint); Use(accent, SKPaintStyle.Stroke, (float)(1 / zoom)); canvas.DrawRect(Rect(marquee), paint);
        }
        if (options.EdgePreview is { } preview)
        {
            Use(accent, SKPaintStyle.Stroke, (float)(1.5 / zoom)); canvas.DrawLine(Point(preview.Start), Point(preview.End), paint); Use(accent); canvas.DrawCircle(Point(preview.End), (float)(4 / zoom), paint);
        }
    }
    public byte[] ExportPng(GraphSnapshot snapshot, double scale = 1)
    {
        if (!double.IsFinite(scale) || scale < .05 || scale > 8) throw new ArgumentOutOfRangeException(nameof(scale), "Export scale must be between 0.05 and 8.");
        var bounds = snapshot.Bounds.Inflate(40); var pixelWidth = Math.Ceiling(bounds.Width * scale); var pixelHeight = Math.Ceiling(bounds.Height * scale);
        if (!double.IsFinite(pixelWidth) || !double.IsFinite(pixelHeight) || pixelWidth < 1 || pixelHeight < 1 || pixelWidth > 16384 || pixelHeight > 16384 || pixelWidth * pixelHeight > 64000000)
            throw new InvalidOperationException("Raster export is limited to 64 million pixels and 16384 pixels per side. Use SVG for larger diagrams.");
        var width = (int)pixelWidth; var height = (int)pixelHeight;
        using var surface = SKSurface.Create(new SKImageInfo(width, height)) ?? throw new InvalidOperationException("Could not allocate export surface.");
        var viewport = new Viewport(); viewport.ZoomAt(new(), scale); viewport.Center(bounds.Center, width, height);
        Render(surface.Canvas, snapshot, viewport, width, height, new(ShowGrid: false));
        using var image = surface.Snapshot(); using var data = image.Encode(SKEncodedImageFormat.Png, 100); return data.ToArray();
    }
    public void Dispose()
    {
        if (disposed) return; disposed = true;
        foreach (var entry in pictures.Values) entry.Picture.Dispose(); pictures.Clear();
        foreach (var edge in edges.Values) edge.Path.Dispose(); edges.Clear();
        paint.Dispose(); shaper.Dispose(); typeface.Dispose();
    }
}
