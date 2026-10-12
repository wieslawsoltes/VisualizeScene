using SkiaSharp;
using VisualizeScene.Core;
using VisualizeScene.Rendering.Skia;

static class EdgeRenderingTests
{
    static void Check(bool value){if(!value)throw new Exception("Assertion failed");}
    static byte[] Pixels(SKSurface surface){using var image=surface.Snapshot();using var bitmap=SKBitmap.FromImage(image);return bitmap.Bytes;}
    static GraphEdge Edge(EdgeStyle? style=null)=>new GraphEdge("e","a","b",Stroke:"#000000",Routing:EdgeRouting.Polyline){Style=style};
    public static void Run(Action<string,Action> test)
    {
        test("Edge renderer: solid/dashed switching resets native paint state",()=>{
            using var surface=SKSurface.Create(new SKImageInfo(220,100));using var renderer=new EdgeStyleRenderer();PointD[] points=[new(20,50),new(200,50)];
            surface.Canvas.Clear(SKColors.White);renderer.Draw(surface.Canvas,Edge(new(3,EdgeDash.Dash,TargetArrow:EdgeArrow.None)),points);var dashed=Pixels(surface);
            surface.Canvas.Clear(SKColors.White);renderer.Draw(surface.Canvas,Edge(new(3,TargetArrow:EdgeArrow.None)),points);var solid=Pixels(surface);Check(!solid.SequenceEqual(dashed));
            surface.Canvas.Clear(SKColors.White);renderer.Draw(surface.Canvas,Edge(new(3,EdgeDash.Dash,TargetArrow:EdgeArrow.None)),points);Check(Pixels(surface).SequenceEqual(dashed));
        });
        test("Edge renderer: every explicit marker paints and reused state matches a fresh painter",()=>{
            using var surface=SKSurface.Create(new SKImageInfo(220,100));using var renderer=new EdgeStyleRenderer();PointD[] points=[new(20,50),new(200,50)];
            foreach(var kind in Enum.GetValues<EdgeArrow>()) {
                var edge=Edge(new(3,EdgeDash.DashDot,kind,kind));surface.Canvas.Clear(SKColors.White);renderer.Draw(surface.Canvas,edge,points);var expected=Pixels(surface);
                using var fresh=new EdgeStyleRenderer();surface.Canvas.Clear(SKColors.White);fresh.Draw(surface.Canvas,edge,points);Check(expected.SequenceEqual(Pixels(surface)));
                Check(expected.Any(b=>b!=255));
            }
        });
        test("Edge renderer: native dash resources are bounded and disposed",()=>{
            using var surface=SKSurface.Create(new SKImageInfo(220,100));using var renderer=new EdgeStyleRenderer();PointD[] points=[new(20,50),new(200,50)];
            for(var i=0;i<128;i++)renderer.Draw(surface.Canvas,Edge(new(.25+i*.25,EdgeDash.Dash)),points);
            Check(renderer.CachedDashEffects==EdgeStyleRenderer.MaximumDashEffects&&renderer.DashEffectBuilds==128);
            renderer.Dispose();Check(renderer.CachedDashEffects==0);Cases.Throws<ObjectDisposedException>(()=>renderer.Draw(surface.Canvas,Edge(),points));
        });
        test("Edge renderer: style changes reuse routes and update only affected spatial bounds",()=>{
            var graph=new GraphSnapshot([new("a",new(0,20,50,50)),new("b",new(300,20,50,50))],[Edge()],[],[]);
            using var surface=SKSurface.Create(new SKImageInfo(400,160));using var renderer=new GraphRenderer();var view=new Viewport();var options=new RenderOptions(ShowGrid:false);
            renderer.Render(surface.Canvas,graph,view,400,160,options);var before=renderer.PreparationStatistics;
            foreach(var kind in Enum.GetValues<EdgeArrow>()) {
                var next=graph with{Edges=[graph.Edges[0] with{Style=new(32,EdgeDash.Dash,kind,kind)}]};renderer.Render(surface.Canvas,next,view,400,160,options);var pixels=Pixels(surface);
                using var fresh=new GraphRenderer();fresh.Render(surface.Canvas,next,view,400,160,options);Check(pixels.SequenceEqual(Pixels(surface)));
            }
            var after=renderer.PreparationStatistics;Check(after.CreatedPaths==before.CreatedPaths&&after.NodeIndexBuilds==before.NodeIndexBuilds&&after.AnnotationIndexBuilds==before.AnnotationIndexBuilds&&after.EdgeIndexBuilds>before.EdgeIndexBuilds);
        });
        test("Edge renderer: dash/color-only revision preserves spatial indexes",()=>{
            var graph=new GraphSnapshot([new("a",new(0,20,50,50)),new("b",new(300,20,50,50))],[Edge(new(4))],[],[]);
            using var surface=SKSurface.Create(new SKImageInfo(400,160));using var renderer=new GraphRenderer();var view=new Viewport();
            renderer.Render(surface.Canvas,graph,view,400,160);var before=renderer.PreparationStatistics;
            renderer.Render(surface.Canvas,graph with{Edges=[graph.Edges[0] with{Stroke="#FF0000",Style=new(4,EdgeDash.Dot)}]},view,400,160);
            var after=renderer.PreparationStatistics;Check(before.EdgeIndexBuilds==after.EdgeIndexBuilds&&before.CreatedPaths==after.CreatedPaths);
        });
        test("Edge renderer: wide stroke remains visible when centerline lies outside viewport query",()=>{
            var graph=new GraphSnapshot([new("a",new(-50,-100,10,10)),new("b",new(-50,200,10,10))],
                [Edge(new(64,TargetArrow:EdgeArrow.None))],[],[]);
            using var surface=SKSurface.Create(new SKImageInfo(100,100));using var renderer=new GraphRenderer();var view=new Viewport();view.Restore(new(new(-20,0),1));
            renderer.Render(surface.Canvas,graph,view,100,100,new(ShowGrid:false));Check(renderer.Statistics.VisibleEdges==1);
            using var image=surface.Snapshot();using var bitmap=SKBitmap.FromImage(image);Check(bitmap.GetPixel(2,50)!=SKColors.White);
        });
        test("Edge renderer: visible caption is not culled with its offscreen route",()=>{
            var graph=new GraphSnapshot([new("a",new(-80,-200,10,10)),new("b",new(-80,300,10,10))],
                [Edge() with{Text="A long edge caption reaching into view"}],[],[]);
            using var surface=SKSurface.Create(new SKImageInfo(180,150));using var renderer=new GraphRenderer();
            renderer.Render(surface.Canvas,graph,new Viewport(),180,150,new(ShowGrid:false));Check(renderer.Statistics.VisibleEdges==1);
        });
    }
}
