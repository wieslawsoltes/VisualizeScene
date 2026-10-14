using VisualizeScene.Core;

namespace VisualizeScene.Rendering.Skia;

public sealed partial class GraphRenderer
{
    private readonly GraphGridRenderer gridRenderer = new();
    private GraphGridSettings legacyGrid = GraphGridSettings.Default;
    public GridRenderStatistics GridStatistics => gridRenderer.Statistics;
    private GraphGridSettings ResolveGrid(RenderOptions options)
    {
        if (options.GridSettings is { } grid) return grid;
        if (legacyGrid.HorizontalSpacing != options.GridSize)
            legacyGrid = GraphGridSettings.Default with { HorizontalSpacing = options.GridSize, VerticalSpacing = options.GridSize };
        return legacyGrid;
    }
}
