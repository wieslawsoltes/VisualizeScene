using Microsoft.UI.Xaml;
using VisualizeScene.Editing;

namespace VisualizeScene.Controls.Uno;

public sealed partial class GraphControl
{
    /// <summary>Runs a selection-bound arrangement command after cancelling unfinished input. One graph transaction, no camera reset.</summary>
    public void ArrangeNodes(GraphArrangeCommand command)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (!Enum.IsDefined(command)) throw new ArgumentOutOfRangeException(nameof(command));
        var document = Document; var session = Session; var revision = document.Revision; var selection = session.Selection;
        CloseGraphContextMenu(); CancelTouchNavigation();
        if (disposed || Document != document || Session != session || document.Revision != revision || !ReferenceEquals(selection, session.Selection))
            throw new InvalidOperationException("The graph or selection changed while preparing arrangement.");
        switch (command)
        {
            case GraphArrangeCommand.SameWidth: session.MatchNodeSize(NodeSizeMatch.Width); break;
            case GraphArrangeCommand.SameHeight: session.MatchNodeSize(NodeSizeMatch.Height); break;
            case GraphArrangeCommand.SameSize: session.MatchNodeSize(NodeSizeMatch.Both); break;
            default: session.Align((Alignment)(int)command); break;
        }
        Focus(FocusState.Programmatic); Invalidate();
    }
}
