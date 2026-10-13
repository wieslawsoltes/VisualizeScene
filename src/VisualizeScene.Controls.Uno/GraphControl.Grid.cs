using VisualizeScene.Core;
using VisualizeScene.Rendering.Skia;

namespace VisualizeScene.Controls.Uno;

public sealed partial class GraphControl
{
    public GraphGridSettings GridSettings { get => Session.GridSettings; set { ObjectDisposedException.ThrowIf(disposed, this); Session.GridSettings = value; } }
    public GridRenderStatistics GridStatistics => renderer.GridStatistics;
}
