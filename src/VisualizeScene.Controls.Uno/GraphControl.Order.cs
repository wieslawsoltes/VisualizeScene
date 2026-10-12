using Microsoft.UI.Xaml;
using VisualizeScene.Editing;

namespace VisualizeScene.Controls.Uno;

public sealed partial class GraphControl
{
    /// <summary>Moves selected items within the existing drawing layers as one undoable document operation.</summary>
    public void ChangeDrawingOrder(DrawingOrder operation)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        CloseGraphContextMenu(); CancelTouchNavigation();
        Session.ChangeDrawingOrder(operation); Focus(FocusState.Programmatic); Invalidate();
    }
}
