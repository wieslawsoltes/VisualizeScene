using Microsoft.UI.Xaml;
using VisualizeScene.Editing;

namespace VisualizeScene.Controls.Uno;

public sealed partial class GraphControl
{
    public void FoldGroups(GroupFoldOperation operation)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (!Enum.IsDefined(operation)) throw new ArgumentOutOfRangeException(nameof(operation));
        var owner = Document; var session = Session; var revision = owner.Revision; var selected = session.Selection;
        CloseGraphContextMenu(); CancelTouchNavigation();
        if (disposed || Document != owner || Session != session || owner.Revision != revision || !ReferenceEquals(selected, session.Selection))
            throw new InvalidOperationException("The graph or selection changed while preparing group folding.");
        session.FoldGroups(operation);
        Focus(FocusState.Programmatic); Invalidate();
    }
}
