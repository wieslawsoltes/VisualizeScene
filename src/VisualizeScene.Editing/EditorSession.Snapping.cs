using System.Collections.Immutable;
using VisualizeScene.Core;

namespace VisualizeScene.Editing;

public sealed partial class EditorSession
{
    private ItemSnapIndex itemSnaps = null!;
    private HashSet<string> snapExclusions = [];
    private RectD movingBounds;
    private bool snapToItems = true;
    private double itemSnapDistance = 6;
    public bool SnapToItems
    {
        get => snapToItems;
        set { snapToItems = value; AlignmentGuides = []; Invalidate(); }
    }
    public double ItemSnapDistance
    {
        get => itemSnapDistance;
        set { if (!double.IsFinite(value) || value < 0 || value > 100) throw new ArgumentOutOfRangeException(nameof(value)); itemSnapDistance = value; }
    }
    public ImmutableArray<AlignmentGuide> AlignmentGuides { get; private set; } = [];

    private void BeginItemSnapping(HitResult hit)
    {
        AlignmentGuides = []; snapExclusions.Clear();
        if (hit.Kind is not (HitKind.Node or HitKind.ResizeHandle)) return;
        var moving = hit.Kind == HitKind.ResizeHandle ? new HashSet<string> { hit.Id! }
            : selection.Where(index.Nodes.ContainsKey).ToHashSet();
        foreach (var id in moving.ToArray()) moving.UnionWith(index.Descendants(id).Select(n => n.Id));
        snapExclusions.UnionWith(moving);
        foreach (var id in moving) for (var parent = index.Nodes[id].ParentId; parent is not null; parent = index.Nodes[parent].ParentId) snapExclusions.Add(parent);
        movingBounds = RectD.Enclose(moving.Select(id => index.Nodes[id]).Where(n => index.IsVisible(n.Id) && !n.Locked).Select(n => n.Bounds));
    }
    private PointD SnapNodeTranslation(GraphNode node, PointD delta)
    {
        var position = gridSettings.NodeAnchor == GridNodeAnchor.Center ? node.Bounds.Center : new PointD(node.Bounds.X, node.Bounds.Y);
        var gridDelta = SnapToGrid ? Snap(position + delta) - position : delta;
        if (!SnapToItems) return gridDelta;
        var result = itemSnaps.Snap(movingBounds.Move(delta), gridDelta - delta, snapExclusions, Viewport.Zoom, ItemSnapDistance);
        AlignmentGuides = result.Guides; return delta + result.Correction;
    }
    private PointD SnapResizePoint(PointD world)
    {
        var grid = Snap(world); if (!SnapToItems) return grid;
        var result = itemSnaps.Snap(new(world.X, world.Y, 0, 0), grid - world, snapExclusions, Viewport.Zoom, ItemSnapDistance);
        AlignmentGuides = result.Guides; return world + result.Correction;
    }
}
