using System.Runtime.CompilerServices;
using VisualizeScene.Core;
using VisualizeScene.Editing;
using VisualizeScene.Rendering.Skia;

static class CaptionSizingApi
{
    [ModuleInitializer]
    public static void Run()
    {
        var graph = new GraphSnapshot([new("n",new(10,20,30,24),"Package-owned caption",NodeShape.Rectangle)],[],[],[]);
        using var renderer = new GraphRenderer();
        var document = new GraphDocument(); document.Replace(graph,clearHistory:true);
        using var editor = new EditorSession(document) { NodeCaptionSizeProvider = renderer.MeasureNodeCaption };
        editor.Select(["n"]); editor.FitNodeCaptions();
        if (editor.LastCaptionSizing?.ResizedNodes != 1 || document.Snapshot.Nodes[0].Bounds.Width <= 30)
            throw new Exception("Packaged measured caption sizing failed.");
        var fitted = document.Snapshot; editor.FitNodeCaptions();
        if (!ReferenceEquals(fitted,document.Snapshot)) throw new Exception("Packaged caption fit no-op failed.");
        document.Undo();
        if (!ReferenceEquals(graph,document.Snapshot) || document.CanUndo) throw new Exception("Packaged caption fit undo failed.");
        Console.WriteLine("PASS package-only shaped caption fitting, exact no-op and undo");
    }
}
