using SkiaSharp;
using VisualizeScene.Core;

namespace VisualizeScene.Rendering.Skia;

public readonly record struct PictureCacheStatistics(int Retained, int Capacity, long NodeRecordings, long LabelRecordings, long Evictions)
{
    /// <summary>Sum of Skia's approximate picture costs, not process/GPU memory or referenced font/image storage.</summary>
    public long EstimatedRetainedBytes { get; init; }
    public long ByteBudget { get; init; }
    public long OversizedBypasses { get; init; }
}

public sealed partial class GraphRenderer
{
    private sealed record LabelPicture(string Text, double Size, PointD Center, SKPicture Picture);
    private sealed record PictureUse(bool Node, string Id, long EstimatedBytes);
    private readonly Dictionary<string, LabelPicture> labelPictures = new(StringComparer.Ordinal);
    private readonly LinkedList<PictureUse> pictureLru = new();
    private readonly Dictionary<(bool Node, string Id), LinkedListNode<PictureUse>> pictureUses = [];
    private int pictureCacheCapacity = 4096;
    private long pictureCacheByteBudget = 16L * 1024 * 1024;
    private long retainedPictureBytes, oversizedPictureBypasses;
    private long nodeRecordings, labelRecordings, pictureEvictions;
    public PictureCacheStatistics PictureStatistics => new(pictureLru.Count, pictureCacheCapacity, nodeRecordings, labelRecordings, pictureEvictions)
    { EstimatedRetainedBytes = retainedPictureBytes, ByteBudget = pictureCacheByteBudget, OversizedBypasses = oversizedPictureBypasses };

    /// <summary>Combined node/label picture entry budget. Zero disables retention.</summary>
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
    /// <summary>Combined estimated picture-byte budget, in addition to the entry limit. Defaults to 16 MiB; zero disables retention.</summary>
    /// <remarks>SKPicture.ApproximateBytesUsed excludes large referenced objects. This is not a strict native, font, GPU or process-memory ceiling.
    /// One temporary picture can exceed the budget while being recorded/replayed; such a picture is disposed instead of retained.</remarks>
    public long PictureCacheByteBudget
    {
        get => pictureCacheByteBudget;
        set
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (value < 0) throw new ArgumentOutOfRangeException(nameof(value));
            pictureCacheByteBudget = value; TrimPictures();
        }
    }
    private bool PictureRetentionEnabled => pictureCacheCapacity > 0 && pictureCacheByteBudget > 0;
    private void TouchPicture(bool node, string id)
    {
        var entry = pictureUses[(node, id)]; pictureLru.Remove(entry); pictureLru.AddLast(entry);
    }
    // Only called for a new picture after removing an older entry with the same key.
    // Evict BEFORE registration so an oversized incoming picture is never disposed before replay.
    private bool RetainPicture(bool node, string id, SKPicture picture)
    {
        if (!PictureRetentionEnabled) return false;
        var bytes = Math.Max(1L, (long)picture.ApproximateBytesUsed);
        if (bytes > pictureCacheByteBudget) { oversizedPictureBypasses++; return false; }
        while ((pictureLru.Count >= pictureCacheCapacity || retainedPictureBytes > pictureCacheByteBudget - bytes) && pictureLru.First is { } first)
            DropPicture(first.Value.Node, first.Value.Id, evicted: true);
        var use = new LinkedListNode<PictureUse>(new(node, id, bytes));
        pictureUses.Add((node, id), use); pictureLru.AddLast(use); retainedPictureBytes += bytes;
        return true;
    }
    private void DropPicture(bool node, string id, bool evicted = false)
    {
        if (node) { if (pictures.Remove(id, out var entry)) entry.Picture.Dispose(); }
        else { if (labelPictures.Remove(id, out var entry)) entry.Picture.Dispose(); }
        if (pictureUses.Remove((node, id), out var use))
        { pictureLru.Remove(use); retainedPictureBytes -= use.Value.EstimatedBytes; if (evicted) pictureEvictions++; }
    }
    private void TrimPictures()
    {
        while ((pictureLru.Count > pictureCacheCapacity || retainedPictureBytes > pictureCacheByteBudget) && pictureLru.First is { } first)
            DropPicture(first.Value.Node, first.Value.Id, evicted: true);
    }
    private void DrawLabelPicture(SKCanvas canvas, LabelVisual visual)
    {
        var label = visual.Label;
        if (!PictureRetentionEnabled)
        { DrawText(canvas, label.Text, visual.Center, label.FontSize, background: true); return; }
        var retained = true;
        if (!labelPictures.TryGetValue(label.Id, out var cached) || cached.Text != label.Text || cached.Size != label.FontSize || cached.Center != visual.Center)
        {
            using var recorder = new SKPictureRecorder();
            var target = recorder.BeginRecording(Rect(visual.Bounds.Inflate(2)));
            // Record the original world-coordinate commands exactly, avoiding extra float translation/rounding.
            DrawText(target, label.Text, visual.Center, label.FontSize, background: true);
            var next = new LabelPicture(label.Text, label.FontSize, visual.Center, recorder.EndRecording());
            DropPicture(false, label.Id); cached = next; labelRecordings++;
            retained = RetainPicture(false, label.Id, cached.Picture);
            if (retained) labelPictures.Add(label.Id, cached);
        }
        else TouchPicture(false, label.Id);
        try { canvas.DrawPicture(cached.Picture); }
        finally { if (!retained) cached.Picture.Dispose(); }
    }
    private static bool SameNodePictureStyle(GraphNode a, GraphNode b)
        => a.Bounds.Width == b.Bounds.Width && a.Bounds.Height == b.Bounds.Height && a.Text == b.Text &&
           a.Shape == b.Shape && a.Fill == b.Fill && a.Stroke == b.Stroke && a.IsGroup == b.IsGroup && a.Collapsed == b.Collapsed;
    private void DisposePictures()
    {
        foreach (var entry in pictures.Values) entry.Picture.Dispose(); pictures.Clear();
        foreach (var entry in labelPictures.Values) entry.Picture.Dispose(); labelPictures.Clear();
        pictureUses.Clear(); pictureLru.Clear(); retainedPictureBytes = 0;
    }
}
