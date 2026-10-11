using VisualizeScene.Core;

namespace VisualizeScene.Editing;

public sealed partial class EditorSession
{
    private static RectD ResizeBounds(RectD bounds, PointD point, int corner)
    {
        var left = corner is 0 or 3 ? Math.Min(point.X, bounds.Right - 20) : bounds.X;
        var right = corner is 1 or 2 ? Math.Max(point.X, bounds.X + 20) : bounds.Right;
        var top = corner is 0 or 1 ? Math.Min(point.Y, bounds.Bottom - 20) : bounds.Y;
        var bottom = corner is 2 or 3 ? Math.Max(point.Y, bounds.Y + 20) : bounds.Bottom;
        return new(left, top, right - left, bottom - top);
    }
    public void Align(Alignment alignment)
    {
        if (!Enum.IsDefined(alignment)) throw new ArgumentOutOfRangeException(nameof(alignment));
        // A selected group owns the movement of its descendants. Never align those a second time.
        var candidates = selection.Where(index.Nodes.ContainsKey).Where(id => !index.Nodes[id].Locked).ToHashSet();
        bool IsRoot(string id)
        {
            for (var parent = index.Nodes[id].ParentId; parent is not null; parent = index.Nodes[parent].ParentId)
                if (candidates.Contains(parent)) return false;
            return true;
        }
        var nodes = candidates.Where(IsRoot).Select(id => index.Nodes[id]).ToArray(); if (nodes.Length < 2) return;
        var bounds = RectD.Enclose(nodes.Select(n => n.Bounds)); var ordered = nodes.OrderBy(n => alignment == Alignment.DistributeVertical ? n.Bounds.Y : n.Bounds.X).ToArray();
        var gap = alignment == Alignment.DistributeVertical ? (bounds.Height - nodes.Sum(n => n.Bounds.Height)) / (nodes.Length - 1) : (bounds.Width - nodes.Sum(n => n.Bounds.Width)) / (nodes.Length - 1);
        var cursor = alignment == Alignment.DistributeVertical ? bounds.Y : bounds.X;
        Document.Edit("Align nodes", edit =>
        {
            foreach (var node in ordered)
            {
                var b = node.Bounds;
                var target = alignment switch
                {
                    Alignment.Left => b with { X = bounds.X }, Alignment.HorizontalCenter => b with { X = bounds.Center.X - b.Width / 2 }, Alignment.Right => b with { X = bounds.Right - b.Width },
                    Alignment.Top => b with { Y = bounds.Y }, Alignment.VerticalCenter => b with { Y = bounds.Center.Y - b.Height / 2 }, Alignment.Bottom => b with { Y = bounds.Bottom - b.Height },
                    Alignment.DistributeHorizontal => b with { X = cursor }, Alignment.DistributeVertical => b with { Y = cursor }, _ => b
                };
                edit.Move([node.Id], new(target.X - b.X, target.Y - b.Y)); cursor += (alignment == Alignment.DistributeVertical ? b.Height : b.Width) + gap;
            }
        });
    }
}
