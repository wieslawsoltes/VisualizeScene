using System.Collections.Immutable;
using SkiaSharp;
using VisualizeScene.Core;
using VisualizeScene.Rendering.Skia;

static class CaptionRenderingTests
{
    static byte[] Render(GraphRenderer renderer,GraphSnapshot graph,Viewport? view=null)
    {
        using var surface=SKSurface.Create(new SKImageInfo(300,180))!;
        renderer.Render(surface.Canvas,graph,view??new(),300,180,new(ShowGrid:false));
        using var image=surface.Snapshot();using var bitmap=SKBitmap.FromImage(image);return bitmap.Bytes;
    }
    static GraphSnapshot Graph() => new([new("a",new(-180,50,50,50)),new("b",new(-180,280,50,50))],
        [new("e","a","b","Caption initially outside viewport",Routing:EdgeRouting.Polyline)],[],[]);
    public static void Run(Action<string,Action> test)
    {
        test("Caption index: caption edits retain path and route indexes but refresh measured visibility",()=>{
            var g=Graph();using var cached=new GraphRenderer();Render(cached,g);var before=cached.PreparationStatistics;var count=cached.CaptionIndexBuilds;
            var next=g with {Edges=g.Edges.SetItem(0,g.Edges[0] with {Text="Updated multi word caption with wrapping",Directed=false})};
            var actual=Render(cached,next);using var fresh=new GraphRenderer();Cases.Check(actual.SequenceEqual(Render(fresh,next)));
            Cases.Check(cached.PreparationStatistics.EdgeIndexBuilds==before.EdgeIndexBuilds && cached.PreparationStatistics.CreatedPaths==before.CreatedPaths);
            Cases.Check(cached.CaptionIndexBuilds==count+1);
        });
        test("Caption index: visible wrapped caption is retained when its vertical route is offscreen",()=>{
            var g=Graph() with {Nodes=[new("a",new(-85,-20,20,20)),new("b",new(-85,300,20,20))]};
            using var renderer=new GraphRenderer();Render(renderer,g);
            // Route center x=-75 is outside the padded view; the right part of the caption reaches the view query.
            Cases.Check(renderer.Statistics.VisibleEdges==1);
        });
        test("Caption index: warm camera frames, dash changes and color edits do not rebuild captions",()=>{
            var g=Graph();using var r=new GraphRenderer();Render(r,g);var count=r.CaptionIndexBuilds;
            var view=new Viewport();view.Pan(new(20,10));Render(r,g,view);
            var next=g with {Edges=g.Edges.SetItem(0,g.Edges[0] with {Style=new(3,EdgeDash.Dot),Stroke="#CC2255"})};
            Render(r,next);Cases.Check(r.CaptionIndexBuilds==count);
        });
        test("Caption index: deleting captions removes stale candidates; routes remain reusable",()=>{
            var g=Graph();using var r=new GraphRenderer();Render(r,g);var before=r.PreparationStatistics;
            var next=g with {Edges=g.Edges.SetItem(0,g.Edges[0] with {Text=""})};
            var actual=Render(r,next);using var fresh=new GraphRenderer();Cases.Check(actual.SequenceEqual(Render(fresh,next)));
            Cases.Check(r.PreparationStatistics.EdgeIndexBuilds==before.EdgeIndexBuilds && r.CaptionIndexBuilds==2);
        });
    }
}
