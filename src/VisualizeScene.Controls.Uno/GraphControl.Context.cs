using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using VisualizeScene.Core;
using VisualizeScene.Editing;
using Windows.Foundation;

namespace VisualizeScene.Controls.Uno;

public sealed partial class GraphControl
{
    private MenuFlyout? graphContextMenu;
    private readonly Dictionary<string, MenuFlyoutItem> contextItems = new(StringComparer.Ordinal);
    public bool IsContextMenuOpen { get; private set; }
    /// <summary>Monotonic identity of the most recently requested native context popup.</summary>
    public long ContextMenuGeneration { get; private set; }
    public FrameworkElement? FindContextCommand(string name) => contextItems.GetValueOrDefault(name);

    private void CloseGraphContextMenu()
    {
        var previous = graphContextMenu; graphContextMenu = null; IsContextMenuOpen = false; contextItems.Clear(); previous?.Hide();
    }
    private void ShowGraphContextMenu(PointD? screen = null)
    {
        if (disposed || editingLabel) return;
        if (screen.HasValue && !screen.Value.IsFinite) throw new ArgumentOutOfRangeException(nameof(screen));
        CloseGraphContextMenu();
        var owner = Document; var session = Session; var revision = owner.Revision;
        CancelTouchNavigation();
        session.PrepareContext(screen.HasValue ? session.Viewport.ToWorld(screen.Value) : null);
        Focus(FocusState.Programmatic);
        if (Document != owner || Session != session || owner.Revision != revision)
            throw new InvalidOperationException("The graph changed while opening its context menu.");
        var selected = session.Selection; var clipboardRevision = session.ClipboardRevision; var index = owner.Snapshot.Index();
        var nodes = selected.Where(index.Nodes.ContainsKey).Select(id => index.Nodes[id]).ToArray();
        var groups = nodes.Where(n => n.IsGroup).ToArray();
        var single = selected.Count == 1 ? selected.First() : null;
        var canEdit = single is not null && (index.Nodes.ContainsKey(single) || index.Edges.ContainsKey(single) || index.Labels.ContainsKey(single));
        var position = screen ?? (session.FocusedItemBounds is { } bounds ? session.Viewport.ToScreen(bounds.Center) : new(ActualWidth / 2, ActualHeight / 2));
        position = new(Math.Clamp(position.X, 0, Math.Max(0, ActualWidth)), Math.Clamp(position.Y, 0, Math.Max(0, ActualHeight)));
        var world = session.Viewport.ToWorld(position);
        var menu = new MenuFlyout(); graphContextMenu = menu; ContextMenuGeneration++;
        bool Current() => !disposed && Document == owner && Session == session && owner.Revision == revision && ReferenceEquals(selected, session.Selection) && clipboardRevision == session.ClipboardRevision;
        void Item(string id, string text, Action action, bool enabled = true)
        {
            var item = new MenuFlyoutItem { Text = text, IsEnabled = enabled };
            AutomationProperties.SetAutomationId(item, "GraphContext" + id); contextItems.Add(id, item); menu.Items.Add(item);
            item.Click += (_, _) => Run(() =>
            {
                if (!Current()) throw new InvalidOperationException("Context commands are stale. Open the menu again for the current selection.");
                action();
            });
        }
        Item("EditLabel", "Edit label  F2", () => _ = EditLabelAsync(single), canEdit);
        Item("AddLabel", "Add independent label", AddLabelToSelection, single is not null);
        Item("AddPort", "Add port", () => session.AddPort(nodes[0].Id, new(nodes[0].Bounds.Right, nodes[0].Bounds.Center.Y)), selected.Count == 1 && nodes.Length == 1 && !nodes[0].Locked);
        menu.Items.Add(new MenuFlyoutSeparator());
        Item("Copy", "Copy  Ctrl+C", () => ExecuteClipboardCommand(GraphClipboardCommand.Copy), selected.Count > 0);
        Item("Cut", "Cut  Ctrl+X", () => ExecuteClipboardCommand(GraphClipboardCommand.Cut), selected.Count > 0);
        Item("Paste", "Paste  Ctrl+V", () => ExecuteClipboardCommand(GraphClipboardCommand.Paste), session.CanPaste);
        Item("PasteInPlace", "Paste in place  Ctrl+Shift+V", () => ExecuteClipboardCommand(GraphClipboardCommand.PasteInPlace), session.CanPaste);
        Item("Duplicate", "Duplicate  Ctrl+D", () => ExecuteClipboardCommand(GraphClipboardCommand.Duplicate), selected.Count > 0);
        Item("Delete", "Delete  Del", session.DeleteSelection, selected.Count > 0);
        menu.Items.Add(new MenuFlyoutSeparator());
        Item("Group", "Group  Ctrl+G", session.Group, nodes.Length > 0 && nodes.All(n => n.ParentId == nodes[0].ParentId));
        Item("Ungroup", "Ungroup  Ctrl+Shift+G", session.Ungroup, groups.Length > 0);
        Item("Fold", "Fold / unfold", () => FoldGroups(GroupFoldOperation.Toggle), groups.Length > 0);
        Item("FitGroups", "Fit groups to children", () => ArrangeNodes(GraphArrangeCommand.FitGroups), groups.Any(n => GraphGroupSizing.CanFit(index, n)));
        Item("EnlargeGroups", "Enlarge groups to children", () => ArrangeNodes(GraphArrangeCommand.EnlargeGroups), groups.Any(n => GraphGroupSizing.CanFit(index, n)));
        menu.Items.Add(new MenuFlyoutSeparator());
        foreach (var operation in Enum.GetValues<DrawingOrder>())
            Item("Order" + operation, GraphOrderToolbar.Caption(operation), () => ChangeDrawingOrder(operation), selected.Any(id => !index.Ports.ContainsKey(id)));
        menu.Items.Add(new MenuFlyoutSeparator());
        Item("CreateNode", "Create node here", () => session.AddNode(world));
        Item("SelectAll", "Select all  Ctrl+A", session.SelectAll);
        Item("Fit", "Fit graph  Home", Fit);
        menu.Closed += (_, _) =>
        {
            if (!ReferenceEquals(graphContextMenu, menu)) return;
            graphContextMenu = null; IsContextMenuOpen = false; contextItems.Clear();
        };
        // ShowAt requests a popup; it can load and reposition asynchronously. Publish
        // open state only when this native flyout has actually opened, not before ShowAt.
        menu.Opened += (_, _) =>
        {
            if (ReferenceEquals(graphContextMenu, menu)) IsContextMenuOpen = true;
        };
        try { menu.ShowAt(this, new Point(position.X, position.Y)); }
        catch { CloseGraphContextMenu(); throw; }
    }
}
