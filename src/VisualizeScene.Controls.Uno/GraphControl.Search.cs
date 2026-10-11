using VisualizeScene.Core;

namespace VisualizeScene.Controls.Uno;

public sealed partial class GraphControl
{
    /// <summary>Reveals a found item, or its visible folded representative, without modifying graph content.</summary>
    public bool RevealItem(string id)
    {
        ObjectDisposedException.ThrowIf(disposed, this); ArgumentNullException.ThrowIfNull(id);
        var index = Document.Snapshot.Index(); var target = id;
        if (index.Labels.TryGetValue(target, out var label)) target = label.OwnerId;
        if (index.Ports.TryGetValue(target, out var port)) target = port.NodeId;
        if (index.Edges.TryGetValue(target, out var edge))
        {
            if (!GraphScene.IsEdgeVisible(index, edge)) target = edge.SourceId;
        }
        if (index.Nodes.ContainsKey(target)) target = index.VisibleRepresentative(target);
        else if (!index.Edges.ContainsKey(target)) return false;
        var visible = index.Labels.TryGetValue(id, out var text) ? GraphScene.IsOwnerVisible(index, text.OwnerId) : GraphScene.IsOwnerVisible(index, id);
        if (visible) target = id;
        var revision = Document.Revision;
        Session.Select([target]);
        if (Document.Revision != revision || !Session.FocusItem(target)) return false;
        showKeyboardFocus = true; EnsureFocusedItemVisible(); return Document.Revision == revision;
    }
}
