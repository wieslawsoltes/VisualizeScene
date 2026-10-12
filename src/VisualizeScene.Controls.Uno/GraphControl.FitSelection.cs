using VisualizeScene.Core;

namespace VisualizeScene.Controls.Uno;

public sealed partial class GraphControl
{
    /// <summary>Fits explicitly selected visible geometry, including full groups and measured labels, without modifying graph history.</summary>
    public bool FitSelection(double padding = 40)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (!double.IsFinite(padding) || padding < 0) throw new ArgumentOutOfRangeException(nameof(padding));
        var owner = Document; var session = Session; var revision = owner.Revision;
        CancelTouchNavigation();
        if (owner != Document || session != Session || owner.Revision != revision)
            throw new InvalidOperationException("The graph changed while preparing selection navigation.");
        var selection = session.Selection;
        var bounds = GraphScene.BoundsForItems(owner.Snapshot, selection, MeasureLabel);
        if (owner != Document || session != Session || owner.Revision != revision || !ReferenceEquals(selection, session.Selection))
            throw new InvalidOperationException("The selection changed while measuring its bounds.");
        if (bounds is null) return false;
        var previous = session.Viewport.Revision;
        session.Viewport.Fit(bounds.Value, ActualWidth, ActualHeight, padding);
        Invalidate();
        return session.Viewport.Revision != previous;
    }
}
