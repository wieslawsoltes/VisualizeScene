using VisualizeScene.Core;

namespace VisualizeScene.Editing;

public sealed partial class EditorSession
{
    /// <summary>Prepares a context request without editing the document. Null preserves the keyboard selection.</summary>
    public HitResult PrepareContext(PointD? world = null)
    {
        if (world.HasValue && !world.Value.IsFinite) throw new ArgumentOutOfRangeException(nameof(world));
        var revision = Document.Revision;
        CancelGesture();
        if (Document.Revision != revision) throw new InvalidOperationException("The graph changed while opening the context menu.");
        var hit = world.HasValue ? Hit(world.Value, handles: false) : new HitResult(HitKind.None);
        if (world.HasValue)
        {
            if (hit.Id is null) ClearSelection();
            else if (!selection.Contains(hit.Id)) Select([hit.Id]);
        }
        if (Document.Revision != revision) throw new InvalidOperationException("The graph changed while selecting the context target.");
        return hit;
    }
}
