using VisualizeScene.Core;

namespace VisualizeScene.Editing;

public sealed partial class EditorSession
{
    public GraphGroupFoldResult? LastGroupFolding { get; private set; }
    /// <summary>Publishes one undoable folding transaction. Selected nested groups are evaluated before any becomes hidden.</summary>
    public void FoldGroups(GroupFoldOperation operation, CancellationToken cancellationToken = default)
    {
        var revision = Document.Revision; var snapshot = Document.Snapshot; var selected = Selection;
        var result = GraphGroupFolding.Calculate(snapshot, selected, operation, cancellationToken);
        if (Document.Revision != revision || !ReferenceEquals(Document.Snapshot, snapshot) || !ReferenceEquals(selected, Selection))
            throw new InvalidOperationException("The graph or selection changed while folding groups.");
        cancellationToken.ThrowIfCancellationRequested();
        var name = operation switch
        {
            GroupFoldOperation.Toggle => "Fold or unfold groups",
            GroupFoldOperation.Expand => "Expand groups",
            GroupFoldOperation.Collapse => "Collapse groups",
            _ => throw new ArgumentOutOfRangeException(nameof(operation))
        };
        Document.Replace(result.Graph, name, expectedRevision: revision);
        LastGroupFolding = ReferenceEquals(Document.Snapshot, result.Graph) ? result : null;
    }
}
