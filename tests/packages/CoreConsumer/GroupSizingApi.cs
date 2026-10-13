using System.Runtime.CompilerServices;
using VisualizeScene.Core;
using VisualizeScene.Editing;

// Package-only executable coverage, initialized before the existing consumer Main.
static class GroupSizingApi
{
    [ModuleInitializer]
    public static void Run()
    {
        var graph = new GraphSnapshot([new("g", new(0,0,500,500), IsGroup:true),
            new("child", new(100,100,100,60), ParentId:"g")], [], [], []);
        var result = GraphGroupSizing.Calculate(graph, ["g"], new(IncludeLabels:false));
        if (result.Graph.Nodes[0].Bounds != new RectD(72,52,156,136) || result.Graph.Nodes[1] != graph.Nodes[1])
            throw new Exception("Packaged group fitting geometry failed.");
        var document = new GraphDocument(); document.Replace(graph, clearHistory:true);
        using var editor = new EditorSession(document); editor.Select(["g"]); editor.FitGroups(new(IncludeLabels:false));
        if (editor.LastGroupSizing?.ResizedGroups != 1 || GraphArrangeState.Calculate(graph,["g"]).FittableGroups != 1)
            throw new Exception("Packaged group fitting integration failed.");
        document.Undo();
        if (!ReferenceEquals(document.Snapshot,graph) || document.CanUndo) throw new Exception("Packaged group fit undo failed.");
        Console.WriteLine("PASS packaged group frame fitting, eligibility and exact undo");
    }
}
