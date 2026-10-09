using SkiaSharp;
using VisualizeScene.Core;
namespace VisualizeScene.Rendering.Skia;

public sealed partial class GraphRenderer
{
    private void DrawGrid(SKCanvas canvas, RectD view, double zoom, double step)
    {
        if (!double.IsFinite(step) || step <= 0) return;
        while (step * zoom < 14) step *= 2;
        while (step * zoom > 45) step /= 2;
        Use(new SKColor(211, 220, 229));
        var startX = Math.Floor(view.X / step) * step; var startY = Math.Floor(view.Y / step) * step;
        // Integer iteration guarantees termination when large coordinates cannot represent +step.
        var columns = Math.Clamp((int)Math.Ceiling(view.Width / step) + 2, 0, 4096); var rows = Math.Clamp((int)Math.Ceiling(view.Height / step) + 2, 0, 4096);
        for (var x = 0; x < columns; x++) for (var y = 0; y < rows; y++) canvas.DrawCircle((float)(startX + x * step), (float)(startY + y * step), (float)(.65 / zoom), paint);
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
                Use(new SKColor(221, 232, 243)); target.DrawRoundRect(new SKRect(1, 1, (float)style.Bounds.Width - 1, 28), 5, 5, paint); DrawText(target, style.Text, new(style.Bounds.Width / 2, 14), 12, Math.Max(0, style.Bounds.Width - 34));
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
            var points = edge.Points; var position = PointD.Lerp(points[(points.Count - 1) / 2], points[points.Count / 2], .5); DrawText(canvas, edge.Edge.Text, position + new PointD(0, -9), 11, 220, 50, true);
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
            var rect = new SKRect((float)center.X - handle, (float)center.Y - handle, (float)center.X + handle, (float)center.Y + handle); Use(SKColors.White); canvas.DrawRect(rect, paint); Use(accent, SKPaintStyle.Stroke, (float)(1.3 / zoom)); canvas.DrawRect(rect, paint);
        }
        if (options.Selection is not null) foreach (var id in options.Selection)
        {
            if (index!.Nodes.TryGetValue(id, out var node) && index.IsVisible(id))
            {
                var b = node.Bounds; Use(accent, SKPaintStyle.Stroke, (float)(1.6 / zoom)); canvas.DrawRect(Rect(b.Inflate(2 / zoom)), paint); if (!node.Locked) { Handle(new(b.X, b.Y)); Handle(new(b.Right, b.Y)); Handle(new(b.Right, b.Bottom)); Handle(new(b.X, b.Bottom)); }
            }
            else if (index!.Edges.TryGetValue(id, out var edge) && !edge.Bends.IsDefault) foreach (var bend in edge.Bends) Handle(bend);
        }
        if (options.Marquee is { } marquee) { Use(new SKColor(0, 112, 215, 24)); canvas.DrawRect(Rect(marquee), paint); Use(accent, SKPaintStyle.Stroke, (float)(1 / zoom)); canvas.DrawRect(Rect(marquee), paint); }
        if (options.EdgePreview is { } preview) { Use(accent, SKPaintStyle.Stroke, (float)(1.5 / zoom)); canvas.DrawLine(Point(preview.Start), Point(preview.End), paint); Use(accent); canvas.DrawCircle(Point(preview.End), (float)(4 / zoom), paint); }
    }
}
