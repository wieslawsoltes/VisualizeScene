using VisualizeScene.Core;

namespace VisualizeScene.Editing;

public sealed partial class EditorSession
{
    public GraphGroupSizeResult? LastGroupSizing { get; private set; }
    /// <summary>Fits selected group frames, not their contents' positions. Rejects stale measurement/selection callbacks before committing.</summary>
    public void FitGroups(GroupSizingOptions? options = null, CancellationToken cancellationToken = default)
    {
        var revision = Document.Revision; var snapshot = Document.Snapshot; var selected = Selection; var measure = LabelSizeProvider;
        var result = GraphGroupSizing.Calculate(snapshot, selected, options, measure, cancellationToken);
        if (Document.Revision != revision || !ReferenceEquals(Document.Snapshot, snapshot) ||
            !ReferenceEquals(selected, Selection) || !ReferenceEquals(measure, LabelSizeProvider))
            throw new InvalidOperationException("The graph, selection or font metrics changed while fitting groups.");
        cancellationToken.ThrowIfCancellationRequested();
        Document.Replace(result.Graph, "Fit groups to children", expectedRevision: revision);
        LastGroupSizing = ReferenceEquals(Document.Snapshot, result.Graph) ? result : null;
    }
}
