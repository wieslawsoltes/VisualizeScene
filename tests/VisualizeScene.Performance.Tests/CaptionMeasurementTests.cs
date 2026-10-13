using SkiaSharp;
using VisualizeScene.Core;
using VisualizeScene.Editing;
using VisualizeScene.Rendering.Skia;

static class CaptionMeasurementTests
{
    public static void Run(Action<string,Action> test)
    {
        test("Caption metrics: explicit lines reserve the renderer's complete-line budget",()=>{
            using var r=new GraphRenderer();var n=new GraphNode("n",new(0,0,40,30),"First\nSecond\nThird");var size=r.MeasureNodeCaption(n);
            Cases.Check(size.IsFinite&&size.X>20&&size.Y>=3*NodeCaptionLayout.LineHeight);
            foreach(var shape in Enum.GetValues<NodeShape>()) {
                var fitted=NodeCaptionLayout.FittingSize(shape,size);
                Cases.Check(Math.Floor((fitted.Y-NodeCaptionLayout.HeightInset)/NodeCaptionLayout.LineHeight)>=3);
            }
        });
        test("Caption metrics: CRLF agrees with LF and Unicode input stays finite",()=>{
            using var r=new GraphRenderer();var n=new GraphNode("n",new(0,0,40,30),"Καλημέρα é 😀\nLong caption");
            var size=r.MeasureNodeCaption(n);Cases.Check(size==r.MeasureNodeCaption(n with {Text=n.Text.Replace("\n","\r\n")}));
            Cases.Check(size.X>0&&size.Y>0&&size.IsFinite);
        });
        test("Caption metrics: different IDs and shapes share the same plain caption measurement",()=>{
            using var r=new GraphRenderer();var n=new GraphNode("n",new(0,0,40,30),"Repeated");var size=r.MeasureNodeCaption(n);var builds=r.CaptionMeasurementBuilds;
            Cases.Check(size==r.MeasureNodeCaption(n with {Id="other",Shape=NodeShape.Diamond})&&r.CaptionMeasurementBuilds==builds);
            var start=GC.GetAllocatedBytesForCurrentThread();for(var i=0;i<1000;i++)_ = r.MeasureNodeCaption(n);
            Cases.Check(GC.GetAllocatedBytesForCurrentThread()-start<4096);
        });
        test("Caption metrics: cache capacity is bounded and disposed resources reject access",()=>{
            var r=new GraphRenderer();var n=new GraphNode("n",new(0,0,40,30));
            for(var i=0;i<2100;i++)_ = r.MeasureNodeCaption(n with {Text="Text "+i});
            Cases.Check(r.CachedCaptionMeasurements<=2048&&r.CaptionMeasurementBuilds==2100);
            r.Dispose();Cases.Check(r.CachedCaptionMeasurements==0);Cases.Throws<ObjectDisposedException>(()=>r.MeasureNodeCaption(n));
        });
        test("Caption metrics: group headers cannot accidentally use plain-node measurements",()=>{
            using var r=new GraphRenderer();Cases.Throws<ArgumentException>(()=>r.MeasureNodeCaption(new("g",new(0,0,100,100),IsGroup:true)));
        });
        test("Caption metrics: fitting using shaped text is exactly repeatable with retained and fresh rendering",()=>{
            var g=new GraphSnapshot([new("a",new(10,20,40,30),"Fit this caption\nSecond line",NodeShape.Diamond)],[],[],[]);
            using var r=new GraphRenderer();using var surface=SKSurface.Create(new SKImageInfo(600,300))!;
            r.Render(surface.Canvas,g,new Viewport(),600,300,new(ShowGrid:false));
            var fitted=GraphNodeCaptionSizing.Calculate(g,["a"],r.MeasureNodeCaption).Graph;
            Cases.Check(ReferenceEquals(GraphNodeCaptionSizing.Calculate(fitted,["a"],r.MeasureNodeCaption).Graph,fitted));
            r.Render(surface.Canvas,fitted,new Viewport(),600,300,new(ShowGrid:false));using var first=surface.Snapshot();using var firstBitmap=SKBitmap.FromImage(first);
            using var fresh=new GraphRenderer();fresh.Render(surface.Canvas,fitted,new Viewport(),600,300,new(ShowGrid:false));
            using var second=surface.Snapshot();using var secondBitmap=SKBitmap.FromImage(second);Cases.Check(firstBitmap.Bytes.SequenceEqual(secondBitmap.Bytes));
        });
    }
}
