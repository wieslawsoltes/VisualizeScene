namespace VisualizeScene.Core;

/// <summary>Tracks the immutable snapshot last persisted by a host. Undo/redo can return to that exact clean state.</summary>
public sealed class GraphSavePoint
{
    public GraphDocument Document { get; }
    public GraphSnapshot SavedSnapshot { get; private set; }
    public bool IsDirty => !ReferenceEquals(Document.Snapshot, SavedSnapshot);
    public GraphSavePoint(GraphDocument document)
    {
        Document = document ?? throw new ArgumentNullException(nameof(document)); SavedSnapshot = document.Snapshot;
    }
    /// <summary>Pass the snapshot actually written by an asynchronous save, which may no longer be current.</summary>
    public void MarkSaved(GraphSnapshot? snapshot = null)
    {
        snapshot ??= Document.Snapshot; GraphValidation.Validate(snapshot); SavedSnapshot = snapshot;
    }
}
