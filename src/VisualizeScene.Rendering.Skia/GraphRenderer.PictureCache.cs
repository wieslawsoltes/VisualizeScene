using SkiaSharp;
using VisualizeScene.Core;

namespace VisualizeScene.Rendering.Skia;

public readonly record struct PictureCacheStatistics(int Retained, int Capacity, long NodeRecordings, long LabelRecordings, long Evictions);

public sealed partial class GraphRenderer
{
    private sealed record LabelPicture(string Text, double Size, PointD Center, SKPicture Picture);
    private readonly Dictionary<string, LabelPicture> labelPictures = new(StringComparer.Ordinal);
    private readonly LinkedList<(bool Node, string Id)> pictureLru = new();
    private readonly Dictionary<(bool Node, string Id), LinkedListNode<(bool Node, string Id)>> pictureUses = [];
    private int pictureCacheCapacity = 4096;
    private long nodeRecordings, labelRecordings, pictureEvictions;
    public PictureCacheStatistics PictureStatistics => new(pictureLru.Count, pictureCacheCapacity, nodeRecordings, labelRecordings, pictureEvictions);

    /// <summary>Combined node/label picture entry budget, not a native-byte limit. Zero disables retention.</summary>
    public int PictureCacheCapacity
    {
        get => pictureCacheCapacity;
        set
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (value < 0 || value > 65536) throw new ArgumentOutOfRangeException(nameof(value));
            pictureCacheCapacity = value; TrimPictures();
        }
    }
    private void TouchPicture(bool node, string id)
    {
        var key = (node, id);
        if (pictureUses.TryGetValue(key, out var entry)) pictureLru.Remove(entry);
        else pictureUses[key] = entry = new(key);
        pictureLru.AddLast(entry); TrimPictures();
    }
    private void DropPicture(bool node, string id, bool evicted = false)
    {
        if (node) { if (pictures.Remove(id, out var entry)) entry.Picture.Dispose(); }
        else { if (labelPictures.Remove(id, out var entry)) entry.Picture.Dispose(); }
        if (pictureUses.Remove((node, id), out var use)) pictureLru.Remove(use);
        if (evicted) pictureEvictions++;
    }
    private void TrimPictures()
    {
        while (pictureLru.Count > pictureCacheCapacity && pictureLru.First is { } first)
            DropPicture(first.Value.Node, first.Value.Id, evicted: true);
    }
    private void DrawLabelPicture(SKCanvas canvas, LabelVisual visual)
    {
        var label = visual.Label;
        if (pictureCacheCapacity == 0)
        { DrawText(canvas, label.Text, visual.Center, label.FontSize, background: true); return; }
        if (!labelPictures.TryGetValue(label.Id, out var cached) || cached.Text != label.Text || cached.Size != label.FontSize || cached.Center != visual.Center)
        {
            using var recorder = new SKPictureRecorder();
            var target = recorder.BeginRecording(Rect(visual.Bounds.Inflate(2)));
            // Record the original world-coordinate commands exactly, avoiding extra float translation/rounding.
            DrawText(target, label.Text, visual.Center, label.FontSize, background: true);
            var next = new LabelPicture(label.Text, label.FontSize, visual.Center, recorder.EndRecording());
            cached?.Picture.Dispose(); labelPictures[label.Id] = cached = next; labelRecordings++;
        }
        TouchPicture(false, label.Id); canvas.DrawPicture(cached.Picture);
    }
    private static bool SameNodePictureStyle(GraphNode a, GraphNode b)
        => a.Bounds.Width == b.Bounds.Width && a.Bounds.Height == b.Bounds.Height && a.Text == b.Text &&
           a.Shape == b.Shape && a.Fill == b.Fill && a.Stroke == b.Stroke && a.IsGroup == b.IsGroup && a.Collapsed == b.Collapsed;
    private void DisposePictures()
    {
        foreach (var entry in pictures.Values) entry.Picture.Dispose(); pictures.Clear();
        foreach (var entry in labelPictures.Values) entry.Picture.Dispose(); labelPictures.Clear();
        pictureUses.Clear(); pictureLru.Clear();
    }
}
