using Microsoft.UI.Xaml;
using VisualizeScene.Editing;

namespace VisualizeScene.Controls.Uno;

public sealed partial class GraphControl
{
    /// <summary>Moves selected items within the existing drawing layers as one undoable document operation.</summary>
    public void ChangeDrawingOrder(DrawingOrder operation)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        var document = Document; var session = Session; var revision = document.Revision; var selection = session.Selection;
        CloseGraphContextMenu(); CancelTouchNavigation();
        if (disposed || Document != document || Session != session || document.Revision != revision || !ReferenceEquals(selection, session.Selection))
            throw new InvalidOperationException("The graph or selection changed while preparing drawing order.");
        session.ChangeDrawingOrder(operation); Focus(FocusState.Programmatic); Invalidate();
    }
}
