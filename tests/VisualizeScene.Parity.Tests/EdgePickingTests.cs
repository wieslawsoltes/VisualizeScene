using System.Collections.Immutable;
using VisualizeScene.Core;
using VisualizeScene.Editing;

static class EdgePickingTests
{
    static void Check(bool v) { if (!v) throw new Exception("Assertion failed"); }
    static GraphDocument Document(EdgeStyle? style = null)
    {
        var d = new GraphDocument(); d.Replace(new([new("a",new(0,0,100,60)),new("b",new(300,0,100,60))],
            [new GraphEdge("e","a","b",Routing:EdgeRouting.Polyline) {Style=style}],[],[]),clearHistory:true);return d;
    }
    public static void Run(Action<string,Action> test)
    {
        test("Edge picking: actual editor finds a thick stroke beyond its route centerline",()=>{
            using var s=new EditorSession(Document(new(64,TargetArrow:EdgeArrow.None)));
            Check(s.Hit(new(200,61),handles:false).Id=="e");
            Check(s.Hit(new(200,70),handles:false).Kind==HitKind.None);
        });
        test("Edge picking: actual editor finds marker-only area at high zoom and rejects empty corners",()=>{
            using var s=new EditorSession(Document(new(TargetArrow:EdgeArrow.Triangle)));s.Viewport.Restore(new(new(),8));
            Check(s.Hit(new(294,32.2),handles:false).Id=="e");
            Check(s.Hit(new(299,33.55),handles:false).Kind==HitKind.None);
        });
        test("Edge picking: unstyled legacy edges keep centerline-only selection",()=>{
            using var s=new EditorSession(Document());s.Viewport.Restore(new(new(),8));
            Check(s.Hit(new(294,32.2),handles:false).Kind==HitKind.None);
            Check(s.QueryArea(new(100,30,200,0),AreaSelectionMode.Contains,SelectionKinds.Edges).SequenceEqual(["e"]));
        });
        test("Edge picking: marquee crossing includes painted width and containment requires all painted bounds",()=>{
            using var s=new EditorSession(Document(new(64,TargetArrow:EdgeArrow.None)));
            Check(s.QueryArea(new(190,58,20,3),kinds:SelectionKinds.Edges).SequenceEqual(["e"]));
            Check(s.QueryArea(new(90,20,220,20),AreaSelectionMode.Contains,SelectionKinds.Edges).IsEmpty);
            Check(s.QueryArea(new(60,-10,280,80),AreaSelectionMode.Contains,SelectionKinds.Edges).SequenceEqual(["e"]));
        });
        test("Edge picking: style edits and undo replace actual editor geometry without stale bounds",()=>{
            var d=Document();using var s=new EditorSession(d);var original=d.Snapshot;
            Check(s.Hit(new(200,61),false).Kind==HitKind.None);
            d.Edit("Thicken",e=>e.Update(e.Edge("e") with {Style=new(64)}));Check(s.Hit(new(200,61),false).Id=="e");
            d.Undo();Check(ReferenceEquals(d.Snapshot,original)&&s.Hit(new(200,61),false).Kind==HitKind.None);
        });
        test("Edge picking: spatial area candidates agree with exhaustive styled geometry",()=>{
            var d=Document(new(16,EdgeDash.Dot,EdgeArrow.HollowDiamond,EdgeArrow.Tee));using var s=new EditorSession(d);
            var edge=d.Snapshot.Edges[0];var path=d.Snapshot.Index().EdgePath(edge);var random=new Random(781);
            for(var i=0;i<1000;i++){
                var r=new RectD(random.Next(50,350),random.Next(-40,100),random.Next(0,100),random.Next(0,80));
                foreach(var contains in new[]{false,true}) Check(s.QueryArea(r,contains?AreaSelectionMode.Contains:AreaSelectionMode.Intersects,SelectionKinds.Edges).Contains("e")==EdgeGeometry.MatchesArea(edge,path,r,contains));
            }
        });
        test("Edge picking: keyboard focus includes explicit markers and stroke extent",()=>{
            var d=Document(new(32,SourceArrow:EdgeArrow.Open,TargetArrow:EdgeArrow.HollowCircle));using var s=new EditorSession(d);
            Check(s.FocusItem("e"));Check(s.FocusedItemBounds==EdgeGeometry.Bounds(d.Snapshot.Edges[0],d.Snapshot.Index().EdgePath(d.Snapshot.Edges[0])));
        });
        test("Edge picking: ten thousand styled edges retain a viewport-local broad phase",()=>{
            var nodes=Enumerable.Range(0,10000).SelectMany(i=>new[]{new GraphNode("a"+i,new(0,i*200,100,60)),new GraphNode("b"+i,new(300,i*200,100,60))}).ToImmutableArray();
            var edges=Enumerable.Range(0,10000).Select(i=>new GraphEdge("e"+i,"a"+i,"b"+i,Routing:EdgeRouting.Polyline){Style=new(64)}).ToImmutableArray();
            var d=new GraphDocument();d.Replace(new(nodes,edges,[],[]),clearHistory:true);using var s=new EditorSession(d);
            Check(s.Hit(new(200,61),false).Id=="e0" && s.LastHitStatistics.ExaminedEntries<100 && s.LastHitStatistics.CandidateItems<10);
        });
    }
}
