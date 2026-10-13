using VisualizeScene.Core;

namespace VisualizeScene.Controls.Uno;

public sealed record GraphDocumentChangedEventArgs(GraphDocument Previous, GraphDocument Current);

public sealed partial class GraphControl
{
    public int PictureCacheCapacity { get => renderer.PictureCacheCapacity; set { renderer.PictureCacheCapacity = value; Invalidate(); } }
    public long PictureCacheByteBudget { get => renderer.PictureCacheByteBudget; set { renderer.PictureCacheByteBudget = value; Invalidate(); } }
    public VisualizeScene.Rendering.Skia.PictureCacheStatistics PictureStatistics => renderer.PictureStatistics;
    private GraphSnapshot? boundsSnapshot;
    private RectD contentBounds;
    public event EventHandler<GraphDocumentChangedEventArgs>? DocumentChanged;
    public PointD MeasureLabel(GraphLabel label) => renderer.MeasureLabel(label);

    /// <summary>Visible nodes, routed edges and shaped label extents, cached per immutable snapshot.</summary>
    public RectD ContentBounds
    {
        get
        {
            if (!ReferenceEquals(boundsSnapshot, Document.Snapshot))
            {
                contentBounds = GraphScene.Bounds(Document.Snapshot, MeasureLabel); boundsSnapshot = Document.Snapshot;
            }
            return contentBounds;
        }
    }

    public void AddLabelToSelection()
    {
        var id = Session.Selection.FirstOrDefault(); if (id is null) return;
        var index = Document.Snapshot.Index();
        if (index.Labels.TryGetValue(id, out var label)) id = label.OwnerId;
        Session.AddLabel(id); Focus(Microsoft.UI.Xaml.FocusState.Programmatic);
    }
}
