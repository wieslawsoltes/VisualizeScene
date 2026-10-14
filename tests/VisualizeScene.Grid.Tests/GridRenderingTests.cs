using SkiaSharp;
using VisualizeScene.Core;
using VisualizeScene.Rendering.Skia;

static class GridRenderingTests
{
    static SKBitmap Draw(GraphGridRenderer renderer, GraphGridSettings settings, ViewportState? view = null)
    {
        using var surface = SKSurface.Create(new SKImageInfo(240,160))!;surface.Canvas.Clear(SKColors.White);
        renderer.Render(surface.Canvas,settings,view??new(new(),1),240,160);using var image=surface.Snapshot();return SKBitmap.FromImage(image);
    }
    static bool Ink(SKBitmap b,int x,int y) => b.GetPixel(x,y)!=SKColors.White;
    public static void Run(Action<string,Action> test)
    {
        test("Grid rendering: high zoom does not paint intermediate non-snappable marks",()=>{
            using var r=new GraphGridRenderer();using var b=Draw(r,new() { HorizontalSpacing=20,VerticalSpacing=20,Thickness=4 },new(new(),8));
            Cases.Check(Ink(b,160,0)&&!Ink(b,80,80)&&!Ink(b,40,40));Cases.Check(r.Statistics.Plan.X.Step==160);
        });
        test("Grid rendering: all five patterns paint correct lattice points and directional lines",()=>{
            foreach(var pattern in Enum.GetValues<GridPattern>()) {
                using var r=new GraphGridRenderer();using var b=Draw(r,new() {HorizontalSpacing=20,VerticalSpacing=30,Origin=new(3,7),Thickness=2,Color="#336699",Pattern=pattern});
                Cases.Check(Ink(b,23,37));Cases.Check(!Ink(b,12,22));
                Cases.Check(Ink(b,23,22)==(pattern is GridPattern.Lines or GridPattern.VerticalLines));
                Cases.Check(Ink(b,12,37)==(pattern is GridPattern.Lines or GridPattern.HorizontalLines));
                if(pattern==GridPattern.Crosses) Cases.Check(Ink(b,25,37)&&Ink(b,23,39));
            }
        });
        test("Grid rendering: warm repaints and color changes reuse one native geometry",()=>{
            using var r=new GraphGridRenderer();var g=GraphGridSettings.Default;using var first=Draw(r,g);
            for(var i=0;i<10;i++){using var b=Draw(r,g);Cases.Check(first.Bytes.SequenceEqual(b.Bytes));}
            var builds=r.Statistics.GeometryBuilds;using var recolored=Draw(r,g with { Color="#993311" });
            Cases.Check(builds==1&&r.Statistics.GeometryBuilds==builds&&!first.Bytes.SequenceEqual(recolored.Bytes));
        });
        test("Grid rendering: pattern, phase, spacing and dot diameter invalidate geometry",()=>{
            using var r=new GraphGridRenderer();var g=GraphGridSettings.Default;using var first=Draw(r,g);
            foreach(var next in new[]{g with {Pattern=GridPattern.Crosses},g with {Origin=new(3,4)},g with {HorizontalSpacing=50},g with {Thickness=3}}) {
                var before=r.Statistics.GeometryBuilds;using var b=Draw(r,next);using var fresh=new GraphGridRenderer();using var expected=Draw(fresh,next);
                Cases.Check(b.Bytes.SequenceEqual(expected.Bytes)&&r.Statistics.GeometryBuilds==before+1);
            }
        });
        test("Grid rendering: hiding and restoring does not rebuild retained lattice geometry",()=>{
            using var r=new GraphGridRenderer();var g=GraphGridSettings.Default;using var first=Draw(r,g);var builds=r.Statistics.GeometryBuilds;
            using(var hidden=Draw(r,g with {Visible=false})) {Cases.Check(r.Statistics.Plan.Marks==0&&!Ink(hidden,20,20));}
            using var again=Draw(r,g);Cases.Check(r.Statistics.GeometryBuilds==builds&&first.Bytes.SequenceEqual(again.Bytes));
        });
        test("Grid rendering: native caller clip and matrix are preserved",()=>{
            using var surface=SKSurface.Create(new SKImageInfo(300,200))!;var c=surface.Canvas;c.Clear(SKColors.White);c.Translate(10,20);var matrix=c.TotalMatrix;var count=c.SaveCount;
            using var r=new GraphGridRenderer();r.Render(c,new() {Pattern=GridPattern.Lines,Color="#112233"},new(new(),1),100,80);
            Cases.Check(c.TotalMatrix==matrix&&c.SaveCount==count);using var image=surface.Snapshot();using var b=SKBitmap.FromImage(image);
            Cases.Check(Ink(b,30,40)&&!Ink(b,210,120));
        });
        test("Grid rendering: settings-only repaints never rebuild graph spatial indexes",()=>{
            using var r=new GraphRenderer();using var surface=SKSurface.Create(new SKImageInfo(240,160))!;var graph=Cases.Graph();var v=new Viewport();
            r.Render(surface.Canvas,graph,v,240,160);var preparation=r.PreparationStatistics;
            foreach(var pattern in Enum.GetValues<GridPattern>()) r.Render(surface.Canvas,graph,v,240,160,new() {GridSettings=new() {Pattern=pattern}});
            Cases.Check(r.PreparationStatistics==preparation);
            r.Render(surface.Canvas,graph,v,240,160,new(ShowGrid:false));Cases.Check(r.GridStatistics.Plan.Marks==0);
            r.Render(surface.Canvas,graph,v,240,160,new(Overview:true));Cases.Check(r.GridStatistics.Plan.Marks==0);
        });
        test("Grid rendering: invalid settings preserve geometry and a disposed renderer rejects use",()=>{
            var r=new GraphGridRenderer();using var surface=SKSurface.Create(new SKImageInfo(240,160))!;using var first=Draw(r,new());var before=r.Statistics;
            Cases.Throws<ArgumentException>(()=>r.Render(surface.Canvas,new() {Thickness=double.NaN},new(new(),1),240,160));Cases.Check(r.Statistics==before);
            r.Dispose();Cases.Check(r.Statistics.Plan.Marks==0);Cases.Throws<ObjectDisposedException>(()=>r.Render(surface.Canvas,new(),new(new(),1),240,160));
        });
        test("Grid rendering: extreme origins and tiny spacing stay finite and bounded",()=>{
            using var r=new GraphGridRenderer();using var b=Draw(r,new() {HorizontalSpacing=double.Epsilon,VerticalSpacing=1e-200,Origin=new(double.MaxValue,-double.MaxValue)},new(new(-double.MaxValue,double.MaxValue),.05));
            Cases.Check(r.Statistics.Plan.Marks<=65536&&r.Statistics.DrawCalls<=1);
        });
    }
}
