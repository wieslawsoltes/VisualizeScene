using VisualizeScene.Controls.Uno;

namespace PackageConsumer;

public static class ClipboardApi
{
    public static void Verify(GraphControl graph)
    {
        graph.ClipboardChanged += (_,_) => { _ = graph.Session.ClipboardRevision; };
        foreach (var command in Enum.GetValues<GraphClipboardCommand>())
            if (graph.CanExecuteClipboardCommand(command)) graph.ExecuteClipboardCommand(command);
    }
}
