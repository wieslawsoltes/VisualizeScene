using System.Runtime.CompilerServices;
using SkiaSharp;
using VisualizeScene.Core;
using VisualizeScene.Editing;
using VisualizeScene.Rendering.Skia;

static class GridApi
{
    [ModuleInitializer]
    public static void Run()
    {
        var grid=new GraphGridSettings {HorizontalSpacing=25,VerticalSpacing=40,Origin=new(3,7),Pattern=GridPattern.Crosses,NodeAnchor=GridNodeAnchor.Center};
        var plan=GraphGrid.Plan(grid,new(new(),1),800,400);
        if(plan.Marks==0||plan.Marks>65536||GraphGrid.Snap(new(26,45),grid)!=new PointD(28,47)) throw new Exception("Packaged grid geometry failed.");
        using var session=new EditorSession(new()) {GridSettings=grid};var id=session.AddNode(new(26,45));
        if(session.Document.Snapshot.Index().Nodes[id].Bounds.Center!=new PointD(28,47)) throw new Exception("Packaged grid input failed.");
        using var renderer=new GraphGridRenderer();using var surface=SKSurface.Create(new SKImageInfo(800,400))!;
        renderer.Render(surface.Canvas,grid,new(new(),1),800,400);renderer.Render(surface.Canvas,grid,new(new(),1),800,400);
        if(renderer.Statistics.GeometryBuilds!=1||renderer.Statistics.DrawCalls!=2) throw new Exception("Packaged grid reuse failed.");
        Console.WriteLine("PASS package-only grid geometry, snapped creation and native cached rendering");
    }
}
