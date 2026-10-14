using System.Collections.Immutable;
using VisualizeScene.Core;

namespace VisualizeScene.Editing;

public sealed partial class EditorSession
{
    private int pasteCount;
    private long clipboardVersion;
    private long pasteStateVersion = -1;
    private GraphSnapshot? pasteStateSnapshot;
    private IReadOnlySet<string>? pasteStateSelection;
    private bool canPaste;
    public event EventHandler? ClipboardChanged;
    public long ClipboardRevision => clipboardVersion;
    /// <summary>Whether this session holds copied graph items or independently copied labels.</summary>
    public bool HasClipboardContent => !labelClipboard.IsDefaultOrEmpty || clipboard is { Nodes.Length: > 0 };
    /// <summary>Whether the current session clipboard has a valid visible destination.</summary>
    public bool CanPaste
    {
        get
        {
            if (pasteStateVersion == clipboardVersion && ReferenceEquals(pasteStateSnapshot, Document.Snapshot) && ReferenceEquals(pasteStateSelection, Selection)) return canPaste;
            var target = SinglePasteOwner();
            canPaste = !labelClipboard.IsDefaultOrEmpty
                ? target is not null ? GraphScene.IsOwnerVisible(index, target) : labelClipboard.All(l => GraphScene.IsOwnerVisible(index, l.OwnerId))
                : clipboard is { Nodes.Length: > 0 };
            pasteStateVersion = clipboardVersion; pasteStateSnapshot = Document.Snapshot; pasteStateSelection = Selection;
            return canPaste;
        }
    }
    private string? SinglePasteOwner()
    {
        string? owner = null;
        foreach (var selected in Selection)
        {
            var id = index.Labels.TryGetValue(selected, out var label) ? label.OwnerId : selected;
            if (index.Nodes.ContainsKey(id) || index.Edges.ContainsKey(id) || index.Ports.ContainsKey(id))
            { if (owner is not null && owner != id) return null; owner = id; }
        }
        return owner;
    }
    private ImmutableArray<GraphLabel> SelectedClipboardLabels()
        => Selection.Count > 0 && Selection.All(index.Labels.ContainsKey) ? Selection.Select(id => index.Labels[id]).ToImmutableArray() : [];
    private void PublishClipboard()
    {
        ClipboardChanged?.Invoke(this, EventArgs.Empty); Invalidate();
    }
    /// <summary>Copies committed selection. Empty selection is inert and does not erase the clipboard.</summary>
    public GraphSnapshot Copy()
    {
        if (Selection.Count == 0) return GraphSnapshot.Empty;
        var labels = SelectedClipboardLabels(); var data = GraphClipboard.Copy(Document.Snapshot, Selection);
        clipboard = data; labelClipboard = labels; pasteCount = 0; clipboardVersion++; PublishClipboard(); return data;
    }
    /// <summary>Copies and deletes one captured selection in one undoable document operation.</summary>
    public void Cut()
    {
        if (Selection.Count == 0) return;
        var before = Document.Snapshot; var revision = Document.Revision; var selected = Selection;
        var data = GraphClipboard.Copy(before, selected); var labels = SelectedClipboardLabels();
        var edit = new GraphEdit(before); edit.RemoveRange(selected); var next = edit.Build(); GraphValidation.Validate(next);
        CancelGesture(false);
        if (revision != Document.Revision || !ReferenceEquals(selected, Selection)) throw new InvalidOperationException("The cut selection changed.");
        var oldData = clipboard; var oldLabels = labelClipboard; var oldCount = pasteCount;
        clipboard = data; labelClipboard = labels; pasteCount = 0; var version = ++clipboardVersion;
        try { Document.Replace(next, "Cut selection", expectedRevision: revision); }
        catch
        {
            // A Changed subscriber can throw after a successful commit. Only roll back
            // clipboard publication if the document never committed and no newer copy intervened.
            if (Document.Revision == revision && clipboardVersion == version)
            { clipboard = oldData; labelClipboard = oldLabels; pasteCount = oldCount; clipboardVersion++; }
            throw;
        }
        // DocumentChanged already removes deleted IDs. Do not overwrite a subscriber's newer selection.
        PublishClipboard();
    }
    /// <summary>Implicit pastes advance by (30,30) after each success. Explicit data retains the historical single offset.</summary>
    public void Paste(GraphSnapshot? data = null)
    {
        var implicitData = data is null;
        if (implicitData && !HasClipboardContent) return;
        var count = implicitData ? checked(pasteCount + 1) : 1;
        PasteItems(data ?? clipboard, implicitData ? labelClipboard : [], new(30d * count, 30d * count), false, implicitData, "Paste");
    }
    /// <summary>Pastes at original graph coordinates, or original offsets on the chosen label owner. Does not advance the paste sequence.</summary>
    public void PasteInPlace()
    {
        if (HasClipboardContent) PasteItems(clipboard, labelClipboard, new(), false, false, "Paste in place");
    }
    /// <summary>Duplicates selection without altering clipboard contents or its next paste offset.</summary>
    public void Duplicate()
    {
        if (Selection.Count == 0) return;
        var labels = SelectedClipboardLabels();
        PasteItems(labels.IsEmpty ? GraphClipboard.Copy(Document.Snapshot, Selection) : null, labels, new(30, 30), true, false, "Duplicate");
    }
    private void PasteItems(GraphSnapshot? data, ImmutableArray<GraphLabel> labels, PointD offset, bool keepOwners, bool advance, string operation)
    {
        if (labels.IsDefaultOrEmpty && (data is null || data.Nodes.IsEmpty)) return;
        CancelGesture(false);
        var before = Document.Snapshot; var revision = Document.Revision; var selected = Selection;
        var edit = new GraphEdit(before); IReadOnlyList<string> added;
        if (!labels.IsDefaultOrEmpty)
        {
            var target = keepOwners ? null : SinglePasteOwner(); var created = new List<string>(labels.Length);
            foreach (var label in labels)
            {
                var owner = target ?? label.OwnerId;
                if (!GraphScene.IsOwnerVisible(index, owner)) throw new InvalidOperationException("Select an owner before pasting these labels.");
                var id = GraphEdit.NewId("l"); created.Add(id); edit.Add(label with { Id = id, OwnerId = owner, Offset = label.Offset + offset });
            }
            added = created;
        }
        else added = GraphClipboard.Paste(edit, data!, offset);
        var next = edit.Build(); GraphValidation.Validate(next);
        var oldCount = pasteCount; var version = clipboardVersion;
        if (advance) pasteCount = checked(pasteCount + 1);
        try { Document.Replace(next, operation, expectedRevision: revision); }
        catch
        {
            if (advance && Document.Revision == revision && clipboardVersion == version && pasteCount == oldCount + 1) pasteCount = oldCount;
            throw;
        }
        if (Document.Revision == revision + 1 && ReferenceEquals(Document.Snapshot, next) && ReferenceEquals(Selection, selected)) Select(added);
        // Reentrant Changed/measurement subscribers may have published a newer document/selection.
        // Do not replace it with the IDs from this older paste operation.
    }
}
