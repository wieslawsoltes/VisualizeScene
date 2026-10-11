using VisualizeScene.Core;

namespace VisualizeScene.Editing;

public sealed partial class EditorSession
{
    private string? focusedItem;
    private GraphNavigationIndex? navigationIndex;
    private bool navigationLabelsVisible;
    private double navigationZoom;
    private SelectionKinds navigationKinds = SelectionKinds.Nodes | SelectionKinds.Groups;
    public string? FocusedItemId => focusedItem;
    public event EventHandler? FocusedItemChanged;
    public SelectionKinds NavigationKinds
    {
        get => navigationKinds;
        set { ValidateKinds(value); if (value == navigationKinds) return; navigationKinds = value; navigationIndex = null; }
    }
    public RectD? FocusedItemBounds => focusedItem is null ? null : GetItemBounds(focusedItem);
    public RectD? GetItemBounds(string id)
    {
        ArgumentNullException.ThrowIfNull(id); if (!IsSelectable(id)) return null;
        if (index.Nodes.TryGetValue(id, out var node))
            return node.IsGroup && !node.Collapsed ? new(node.Bounds.X, node.Bounds.Y, node.Bounds.Width, Math.Min(28, node.Bounds.Height)) : node.Bounds;
        if (index.Ports.ContainsKey(id)) { var p = index.PortLocation(id); var r = 3.5 / Viewport.Zoom; return new(p.X - r, p.Y - r, r * 2, r * 2); }
        if (index.Labels.TryGetValue(id, out var label)) return GetLabelBounds(label);
        return RectD.Enclose(paths[id].Select(p => new RectD(p.X, p.Y, 0, 0)));
    }
    public bool FocusItem(string? id)
    {
        if (id is not null && !IsSelectable(id)) return false;
        if (focusedItem == id) return true;
        focusedItem = id; FocusedItemChanged?.Invoke(this, EventArgs.Empty); Invalidate();
        return focusedItem == id && (id is null || IsSelectable(id));
    }
    private GraphNavigationIndex NavigationIndex()
    {
        var labelsVisible = Viewport.Zoom >= .25;
        // Port hit sizes depend on zoom; rebuilding for that opt-in kind avoids stale geometric ranking.
        if (navigationIndex is null || navigationLabelsVisible != labelsVisible || NavigationKinds.HasFlag(SelectionKinds.Ports) && navigationZoom != Viewport.Zoom)
        {
            navigationIndex = new(VisibleItems(NavigationKinds).Where(id => labelsVisible || !index.Labels.ContainsKey(id))
                .Select(id => new NavigationTarget(id, GetItemBounds(id)!.Value)));
            navigationLabelsVisible = labelsVisible; navigationZoom = Viewport.Zoom;
        }
        return navigationIndex;
    }
    public bool Navigate(NavigationDirection direction, bool extendSelection = false)
    {
        var from = focusedItem ?? Selection.LastOrDefault();
        var id = NavigationIndex().Next(direction, from, Viewport.Origin);
        return CommitNavigation(id, extendSelection);
    }
    public bool NavigateBoundary(bool last = false, bool extendSelection = false)
        => CommitNavigation(NavigationIndex().Boundary(last), extendSelection);
    private bool CommitNavigation(string? id, bool extend)
    {
        if (id is null) return false;
        var revision = Document.Revision;
        ApplySelection([id], extend ? SelectionOperation.Add : SelectionOperation.Replace);
        // User event handlers may replace the graph during selection publication.
        return revision == Document.Revision && FocusItem(id) && revision == Document.Revision;
    }
    private void RefreshNavigation()
    {
        navigationIndex = null;
        if (focusedItem is null || IsSelectable(focusedItem)) return;
        var id = focusedItem;
        if (index.Labels.TryGetValue(id, out var label)) id = label.OwnerId;
        if (index.Ports.TryGetValue(id, out var port)) id = port.NodeId;
        else if (index.Edges.TryGetValue(id, out var edge)) id = edge.SourceId;
        FocusItem(index.Nodes.ContainsKey(id) ? index.VisibleRepresentative(id) : null);
    }
}
