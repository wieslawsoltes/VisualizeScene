using VisualizeScene.Core;

namespace VisualizeScene.Editing;

public sealed partial class EditorSession
{
    private GraphGridSettings gridSettings = GraphGridSettings.Default;
    public GraphGridSettings GridSettings
    {
        get => gridSettings;
        set
        {
            ArgumentNullException.ThrowIfNull(value); value.Validate();
            if (gridSettings == value) return;
            // A preview calculated with the previous grid must never be committed using new settings.
            CancelGesture(false); gridSettings = value; Invalidate();
        }
    }
    public bool SnapToGrid { get => gridSettings.SnapEnabled; set => GridSettings = gridSettings with { SnapEnabled = value }; }
    public bool ShowGrid { get => gridSettings.Visible; set => GridSettings = gridSettings with { Visible = value }; }
    /// <summary>Legacy uniform-spacing setter. The getter returns horizontal spacing for a rectangular grid.</summary>
    public double GridSize { get => gridSettings.HorizontalSpacing; set => GridSettings = gridSettings with { HorizontalSpacing = value, VerticalSpacing = value }; }
    public PointD Snap(PointD point) => GraphGrid.Snap(point, gridSettings);
}
