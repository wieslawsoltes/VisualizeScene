using VisualizeScene.Core;
using VisualizeScene.Controls.Uno;

namespace PackageConsumer;

public static class GridApi
{
    public static void Verify(GraphControl target)
    {
        using var editor=new GraphGridEditor();editor.LoadSettings(target.GridSettings);
        if(editor.TryReadSettings(out var settings)) target.GridSettings=settings with {Pattern=GridPattern.Lines};
        _=editor.FindElement("Horizontal");_=editor.ValidationError;_=target.GridStatistics.Plan.Marks;
    }
}
