using SkiaSharp;
using VisualizeScene.Core;

namespace VisualizeScene.Rendering.Skia;

public sealed partial class GraphRenderer
{
    private readonly Dictionary<string, PointD> measuredCaptions = new(StringComparer.Ordinal);
    public int CachedCaptionMeasurements => measuredCaptions.Count;
    public long CaptionMeasurementBuilds { get; private set; }
    /// <summary>Measures all explicit caption lines at the exact node font size, without wrapping or ellipsis.</summary>
    /// <remarks>Cache keys are text, not node identity, since the plain node-caption font/style is fixed.
    /// The bounded cache is renderer-owned and is replaced together with a changed typeface.</remarks>
    public PointD MeasureNodeCaption(GraphNode node)
    {
        ObjectDisposedException.ThrowIf(disposed, this); ArgumentNullException.ThrowIfNull(node); ArgumentNullException.ThrowIfNull(node.Text);
        if (node.IsGroup) throw new ArgumentException("Group headers use a separate sizing contract.", nameof(node));
        if (measuredCaptions.TryGetValue(node.Text, out var cached)) return cached;
        using var font = new SKFont(typeface, (float)NodeCaptionLayout.FontSize) { Edging = SKFontEdging.Antialias, Subpixel = true };
        var lines = node.Text.Replace("\r", "").Split('\n'); var width = 0d;
        foreach (var line in lines) if (line.Length != 0) width = Math.Max(width, shaper.Shape(line, font).Width);
        var metrics = font.Metrics;
        // Match DrawText's complete-line budget as well as its baseline/ink height.
        var height = Math.Max(lines.Length * NodeCaptionLayout.LineHeight,
            metrics.Descent - metrics.Ascent + (lines.Length - 1) * NodeCaptionLayout.LineHeight);
        // Two world units of slack protect against rounding at the fit/wrapping boundary.
        var size = new PointD(width + 2, height + 2);
        if (!size.IsFinite || size.X < 0 || size.Y < 0) throw new InvalidOperationException("The font produced invalid caption metrics.");
        if (measuredCaptions.Count >= 2048) measuredCaptions.Clear();
        measuredCaptions.Add(node.Text, size); CaptionMeasurementBuilds++;
        return size;
    }
}
