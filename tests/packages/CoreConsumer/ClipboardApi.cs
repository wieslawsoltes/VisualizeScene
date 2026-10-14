using System.Runtime.CompilerServices;
using VisualizeScene.Core;
using VisualizeScene.Editing;

static class ClipboardApi
{
    [ModuleInitializer]
    public static void Run()
    {
        var document = new GraphDocument(); document.Edit("Fixture", e => e.Add(new GraphNode("a", new(0, 0, 100, 60))));
        using var session = new EditorSession(document); session.Select(["a"]); session.Copy();
        if (!session.CanPaste || !session.HasClipboardContent) throw new Exception("Packaged clipboard availability failed.");
        session.Paste(); session.Paste(); if (document.Snapshot.Nodes[^1].Bounds.X != 60) throw new Exception("Packaged progressive paste failed.");
        session.PasteInPlace(); if (document.Snapshot.Nodes[^1].Bounds.X != 0) throw new Exception("Packaged in-place paste failed.");
        session.Select(["a"]); session.Cut(); if (document.UndoName != "Cut selection") throw new Exception("Packaged cut failed.");
        var copied = GraphClipboard.Copy(document.Snapshot, session.Selection, CancellationToken.None);
        if (!copied.Nodes.IsEmpty) throw new Exception("Packaged empty copy failed.");
        Console.WriteLine("PASS packaged clipboard availability, progressive and in-place paste, cut and cancellation overload");
    }
}
