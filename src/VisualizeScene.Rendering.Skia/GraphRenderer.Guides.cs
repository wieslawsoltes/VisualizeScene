using SkiaSharp;
using VisualizeScene.Core;

namespace VisualizeScene.Rendering.Skia;

public sealed partial class GraphRenderer
{
    private void DrawAlignmentGuides(SKCanvas canvas, double zoom, IReadOnlyList<AlignmentGuide>? guides)
    {
        if (guides is null || guides.Count == 0) return;
        Use(new SKColor(173, 58, 161), SKPaintStyle.Stroke, (float)(1 / zoom));
        var extension = 12 / zoom; var cross = 3 / zoom;
        foreach (var guide in guides)
        {
            var direction = guide.End - guide.Start;
            var unit = direction.Length > 1e-12 ? direction * (1 / direction.Length) : new PointD(0, 1);
            canvas.DrawLine(Point(guide.Start - unit * extension), Point(guide.End + unit * extension), paint);
            foreach (var p in new[] { guide.Start, guide.End })
            {
                canvas.DrawLine(Point(p - new PointD(cross, 0)), Point(p + new PointD(cross, 0)), paint);
                canvas.DrawLine(Point(p - new PointD(0, cross)), Point(p + new PointD(0, cross)), paint);
            }
        }
    }
}
