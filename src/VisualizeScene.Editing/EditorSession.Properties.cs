using VisualizeScene.Core;

namespace VisualizeScene.Editing;

public sealed partial class EditorSession
{
    /// <summary>Updates label fields atomically. Position changes obey the same owner locks as pointer movement.</summary>
    public void SetLabelProperties(string labelId, string text, PointD offset, double fontSize)
    {
        var label = index.Labels[labelId];
        if (label.Offset != offset && !CanMoveOwner(label.OwnerId))
            throw new InvalidOperationException("The label's owner is hidden or locked.");
        Document.Edit("Edit label properties", edit => edit.Update(label with { Text = text, Offset = offset, FontSize = fontSize }));
    }
    /// <summary>Sets exact relative coordinates without a world-coordinate round trip.</summary>
    public void SetPortLocation(string portId, PointD relativeLocation)
    {
        var port = index.Ports[portId];
        if (!CanMovePort(port)) throw new InvalidOperationException("This port cannot be moved.");
        if (!relativeLocation.IsFinite || relativeLocation.X < 0 || relativeLocation.X > 1 || relativeLocation.Y < 0 || relativeLocation.Y > 1)
            throw new ArgumentOutOfRangeException(nameof(relativeLocation), "Relative coordinates must be between zero and one.");
        Document.Edit("Edit port location", edit => edit.Update(port with { RelativeLocation = relativeLocation }));
    }
}
