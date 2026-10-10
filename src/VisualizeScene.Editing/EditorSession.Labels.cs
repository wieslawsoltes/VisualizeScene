using System.Collections.Immutable;
using VisualizeScene.Core;

namespace VisualizeScene.Editing;

public sealed partial class EditorSession
{
    private Func<GraphLabel, PointD> labelSizeProvider = GraphScene.EstimateLabelSize;
    private SpatialIndex<string> labelIndex = new([]);
    private ImmutableArray<GraphLabel> labelClipboard = [];

    /// <summary>Host font metrics. Headless clients default to an explicit font-independent estimate.</summary>
    public Func<GraphLabel, PointD> LabelSizeProvider
    {
        get => labelSizeProvider;
        set { ArgumentNullException.ThrowIfNull(value); labelSizeProvider = value; ReindexLabels(); Invalidate(); }
    }

    public RectD GetLabelBounds(GraphLabel label) => GraphScene.LabelBounds(index, label, labelSizeProvider(label));

    private void ReindexLabels() => labelIndex = new(Document.Snapshot.Labels
        .Where(l => GraphScene.IsOwnerVisible(index, l.OwnerId)).Select(l => (GetLabelBounds(l), l.Id)));

    private bool CanMoveOwner(string ownerId)
    {
        if (!GraphScene.IsOwnerVisible(index, ownerId)) return false;
        if (index.Nodes.TryGetValue(ownerId, out var node)) return !node.Locked;
        if (index.Ports.TryGetValue(ownerId, out var port)) return !index.Nodes[port.NodeId].Locked;
        var edge = index.Edges[ownerId];
        return !index.Nodes[edge.SourceId].Locked && !index.Nodes[edge.TargetId].Locked;
    }

    public string AddLabel(string ownerId, string text = "Label", PointD? offset = null, double fontSize = 12)
    {
        if (!GraphScene.IsOwnerVisible(index, ownerId)) throw new InvalidOperationException("Select a visible node, edge or port.");
        var id = GraphEdit.NewId("l");
        Document.Edit("Add label", edit => edit.Add(new GraphLabel(id, ownerId, text, offset ?? new(0, 40), fontSize)));
        Select([id]); return id;
    }

    public void MoveLabel(string labelId, PointD world)
    {
        var label = index.Labels[labelId];
        if (!CanMoveOwner(label.OwnerId)) throw new InvalidOperationException("The label's owner is hidden or locked.");
        Document.Edit("Move label", edit => edit.Update(label with { Offset = world - GraphScene.LabelAnchor(index, label.OwnerId) }));
    }

    private void MoveSelection(GraphEdit edit, PointD delta)
    {
        if (!delta.IsFinite) throw new ArgumentException("Movement must be finite.", nameof(delta));
        edit.Move(selection, delta);
        var next = edit.Build().Index();
        foreach (var port in selection.Where(index.Ports.ContainsKey).Select(id => index.Ports[id]))
        {
            if (!CanMovePort(port)) continue;
            // A selected port keeps the same world-space movement as its selected owner, without doubling it.
            var desired = index.PortLocation(port.Id) + delta;
            edit.Update(port with { RelativeLocation = RelativePortLocation(next.Nodes[port.NodeId], desired) });
        }
        next = edit.Build().Index();
        foreach (var label in selection.Where(index.Labels.ContainsKey).Select(id => index.Labels[id]))
        {
            if (!CanMoveOwner(label.OwnerId)) continue;
            var oldAnchor = GraphScene.LabelAnchor(index, label.OwnerId);
            var newAnchor = GraphScene.LabelAnchor(next, label.OwnerId);
            edit.Update(label with { Offset = label.Offset + delta + oldAnchor - newAnchor });
        }
    }

    private bool CopyLabelsOnly()
    {
        labelClipboard = [];
        if (selection.Count == 0 || selection.Any(id => !index.Labels.ContainsKey(id))) return false;
        labelClipboard = selection.Select(id => index.Labels[id]).ToImmutableArray(); return true;
    }

    private void PasteLabels(IReadOnlyList<GraphLabel> labels, bool keepOwners = false)
    {
        var owners = selection.Where(id => index.Nodes.ContainsKey(id) || index.Edges.ContainsKey(id) || index.Ports.ContainsKey(id)).ToArray();
        var target = !keepOwners && owners.Length == 1 ? owners[0] : null;
        var created = new List<string>();
        Document.Edit("Paste labels", edit =>
        {
            foreach (var label in labels)
            {
                var owner = target ?? label.OwnerId;
                if (!GraphScene.IsOwnerVisible(index, owner)) throw new InvalidOperationException("Select an owner before pasting these labels.");
                var id = GraphEdit.NewId("l"); created.Add(id);
                edit.Add(label with { Id = id, OwnerId = owner, Offset = label.Offset + new PointD(30, 30) });
            }
        });
        Select(created);
    }
}
