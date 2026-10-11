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
    public GraphAlignmentResult? LastAlignment { get; private set; }
    public void Align(Alignment alignment)
    {
        var revision = Document.Revision;
        var result = GraphAlignment.Calculate(Document.Snapshot, selection, alignment);
        Document.Replace(result.Graph, "Align nodes", expectedRevision: revision);
        LastAlignment = result;
    }
}
