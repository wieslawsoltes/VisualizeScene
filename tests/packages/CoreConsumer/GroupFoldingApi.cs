using System.Runtime.CompilerServices;
using VisualizeScene.Core;
using VisualizeScene.Editing;

static class GroupFoldingApi
{
    [ModuleInitializer]
    public static void Run()
    {
        var graph=new GraphSnapshot([new("g",new(0,0,400,300),IsGroup:true),
            new("h",new(20,40,200,150),IsGroup:true,ParentId:"g"),
            new("a",new(50,80,100,60),ParentId:"h")],[],[],[]);
        var document=new GraphDocument();document.Replace(graph,clearHistory:true);
        using var session=new EditorSession(document);session.Select(["g","h"]);var revision=document.Revision;
        session.FoldGroups(GroupFoldOperation.Collapse);
        if(document.Revision!=revision+1 || !document.Snapshot.Nodes[0].Collapsed || !document.Snapshot.Nodes[1].Collapsed)
            throw new Exception("Packaged group folding did not commit atomically.");
        document.Undo();
        if(!ReferenceEquals(document.Snapshot,graph) || document.CanUndo) throw new Exception("Packaged group folding undo failed.");
        if(GraphArrangeState.Calculate(graph,["g","h"]).CollapsibleGroups!=2) throw new Exception("Packaged fold availability failed.");
        Console.WriteLine("PASS packaged atomic group folding, availability and exact undo");
    }
}
