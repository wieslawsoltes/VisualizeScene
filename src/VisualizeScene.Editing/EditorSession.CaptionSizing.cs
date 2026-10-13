using VisualizeScene.Core;

namespace VisualizeScene.Editing;

public sealed partial class EditorSession
{
    private Func<GraphNode, PointD>? nodeCaptionSizeProvider;
    /// <summary>Measures the unwrapped centered caption block. Hosts must supply real metrics; no implicit character-count fallback.</summary>
    public Func<GraphNode, PointD>? NodeCaptionSizeProvider
    {
        get => nodeCaptionSizeProvider;
        set { if (ReferenceEquals(value, nodeCaptionSizeProvider)) return; nodeCaptionSizeProvider = value; CancelGesture(); }
    }
    public GraphNodeCaptionSizeResult? LastCaptionSizing { get; private set; }
    public void FitNodeCaptions(NodeCaptionSizingOptions? options = null, CancellationToken cancellationToken = default)
    {
        var revision = Document.Revision; var snapshot = Document.Snapshot; var selected = Selection;
        var measure = NodeCaptionSizeProvider ?? throw new InvalidOperationException("The host must provide node-caption font measurements before fitting text.");
        var result = GraphNodeCaptionSizing.Calculate(snapshot, selected, measure, options, cancellationToken);
        if (Document.Revision != revision || !ReferenceEquals(snapshot, Document.Snapshot) ||
            !ReferenceEquals(selected, Selection) || !ReferenceEquals(measure, NodeCaptionSizeProvider))
            throw new InvalidOperationException("The graph, selection or caption font metrics changed while fitting nodes.");
        cancellationToken.ThrowIfCancellationRequested();
        Document.Replace(result.Graph, "Fit nodes to text", expectedRevision: revision);
        LastCaptionSizing = ReferenceEquals(Document.Snapshot, result.Graph) ? result : null;
    }
}
