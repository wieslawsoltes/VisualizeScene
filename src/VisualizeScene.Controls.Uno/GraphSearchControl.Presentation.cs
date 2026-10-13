using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace VisualizeScene.Controls.Uno;

public sealed partial class GraphSearchControl
{
    private StackPanel? contentPanel;
    /// <summary>Actual requests to reveal a search match. This counts navigation work, not rendered frames.</summary>
    public long RevealRequests { get; private set; }

    private void TargetSizeChanged(object sender, SizeChangedEventArgs args) => ConstrainToViewport();
    private void ConstrainToViewport()
    {
        if (disposed) return;
        // Stable preferred width prevents long result/status text from moving the entire
        // right-aligned panel. Recompute only for the host size, never a search result.
        var width = target is { ActualWidth: > 0 }
            ? Math.Min(320, Math.Max(0, target.ActualWidth - Margin.Left - Margin.Right)) : 320;
        if (Width != width) Width = width;
        // Constrain wrapping even though the outer scroller supports very narrow hosts.
        // Otherwise horizontal scrolling would measure long text at unbounded width.
        if (contentPanel is not null) contentPanel.Width = Math.Max(270, width - 2);
        var height = target is { ActualHeight: > 0 }
            ? Math.Max(0, target.ActualHeight - Margin.Top - Margin.Bottom) : double.PositiveInfinity;
        if (MaxHeight != height) MaxHeight = height;
    }
}
