using System.Globalization;
using SkiaSharp;
using SkiaSharp.HarfBuzz;
using VisualizeScene.Core;
namespace VisualizeScene.Rendering.Skia;

public sealed partial class GraphRenderer
{
    /// <summary>Bounded grapheme-safe truncation. Even widths smaller than one glyph terminate immediately.</summary>
    public static string Ellipsize(string text, double maxWidth, Func<string, double> measure)
    {
        ArgumentNullException.ThrowIfNull(text); ArgumentNullException.ThrowIfNull(measure);
        if (maxWidth <= 0 || double.IsNaN(maxWidth)) return "";
        if (measure(text) <= maxWidth) return text;
        const string ellipsis = "…"; if (measure(ellipsis) > maxWidth) return "";
        var starts = StringInfo.ParseCombiningCharacters(text); var low = 0; var high = starts.Length;
        while (low < high)
        {
            var middle = (low + high + 1) / 2; var end = middle == starts.Length ? text.Length : starts[middle];
            if (measure(text[..end] + ellipsis) <= maxWidth) low = middle; else high = middle - 1;
        }
        return (low == 0 ? "" : text[..(low == starts.Length ? text.Length : starts[low])]) + ellipsis;
    }
    private void DrawText(SKCanvas canvas, string text, PointD center, double size, double maxWidth = double.PositiveInfinity, double maxHeight = double.PositiveInfinity, bool background = false)
    {
        if (string.IsNullOrEmpty(text) || maxWidth <= 0 || maxHeight <= 0) return;
        using var font = new SKFont(typeface, (float)size) { Edging = SKFontEdging.Antialias, Subpixel = true }; var lines = new List<string>();
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
            var line = Ellipsize(lines[i], maxWidth, value => font.MeasureText(value)); var baseline = center.Y + (i - (lines.Count - 1) / 2d) * lineHeight - (metrics.Ascent + metrics.Descent) / 2;
            if (background)
            {
                var textWidth = font.MeasureText(line); Use(new SKColor(255, 255, 255, 238)); canvas.DrawRoundRect(new SKRect((float)(center.X - textWidth / 2 - 4), (float)(baseline + metrics.Ascent - 2), (float)(center.X + textWidth / 2 + 4), (float)(baseline + metrics.Descent + 2)), 3, 3, paint);
            }
            Use(new SKColor(33, 56, 77)); canvas.DrawShapedText(shaper, line, (float)center.X, (float)baseline, SKTextAlign.Center, font, paint);
        }
    }
}
