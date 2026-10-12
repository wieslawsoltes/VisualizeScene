using System.Collections.Immutable;
using VisualizeScene.Core;
using VisualizeScene.Editing;
using VisualizeScene.Serialization;

static class EdgeStyleTests
{
    static void Check(bool condition) { if (!condition) throw new Exception("Assertion failed"); }
    static void Throws<T>(Action action) where T : Exception { try { action(); } catch (T) { return; } throw new Exception("Expected " + typeof(T).Name); }
    static GraphSnapshot Graph(EdgeStyle? style = null) => new([new("a",new(0,0,100,60)),new("b",new(300,0,100,60))],
        [new GraphEdge("ab","a","b",Bends:[],Routing:EdgeRouting.Polyline) { Style = style }],[],[]);
    public static void Run(Action<string,Action> test)
    {
        EdgeWorkflowTests.Run(test);
        test("Edge styles: old files and constructors retain solid directed defaults", () => {
            var g = Graph(); var json = GraphIO.ToJson(g); Check(!json.Contains("\"Style\""));
            Check(GraphIO.FromJson(json).Edges[0].Style is null);
            Check(EdgeStyle.Default.Width == 1.5 && EdgeStyle.Default.ResolveArrow(true,true) == EdgeArrow.None);
            Check(EdgeStyle.Default.ResolveArrow(false,true) == EdgeArrow.Triangle && EdgeStyle.Default.ResolveArrow(false,false) == EdgeArrow.None);
        });
        test("Edge styles: appearance validates every numeric and enum boundary", () => {
            foreach (var width in new[] { double.NaN, double.PositiveInfinity, 0, .249, 64.001 })
                Throws<ArgumentOutOfRangeException>(() => GraphValidation.Validate(Graph(new(width))));
            foreach (var style in new[] { new EdgeStyle(Dash:(EdgeDash)99), new(SourceArrow:(EdgeArrow)99), new(TargetArrow:(EdgeArrow)99) })
                Throws<ArgumentException>(() => GraphValidation.Validate(Graph(style)));
            GraphValidation.Validate(Graph(new(.25))); GraphValidation.Validate(Graph(new(64)));
        });
        test("Edge styles: explicit arrows are independent of graph direction", () => {
            var g = Graph(new(SourceArrow:EdgeArrow.Diamond,TargetArrow:EdgeArrow.None));
            var e = g.Edges[0]; Check(e.Directed && EdgeGeometry.Marker(e,g.Index().EdgePath(e),false) is null);
            e = e with { Directed = false, Style = new(TargetArrow:EdgeArrow.Triangle) };
            Check(EdgeGeometry.Marker(e,g.Index().EdgePath(e),false)?.Kind == EdgeArrow.Triangle);
        });
        test("Edge styles: all patterns and arrows round trip in JSON and native GraphML", () => {
            foreach(var dash in Enum.GetValues<EdgeDash>()) foreach(var arrow in Enum.GetValues<EdgeArrow>()) {
                var g=Graph(new(3.125,dash,arrow,arrow));var json=GraphIO.ToJson(g);
                Check(GraphIO.ToJson(GraphIO.FromJson(json))==json);
                Check(GraphIO.ToJson(GraphIO.FromGraphML(GraphIO.ToGraphML(g)).Graph)==json);
            }
        });
        test("Edge styles: invalid multi-edge edits reject atomically with no history", () => {
            var g=Graph();var d=new GraphDocument();d.Replace(g,clearHistory:true);
            Throws<ArgumentException>(()=>d.Edit("Invalid style",e=>{e.Update(e.Node("a") with {Text="changed"});e.Update(g.Edges[0] with {Style=new(Width:-1)});}));
            Check(ReferenceEquals(g,d.Snapshot)&&!d.CanUndo);
        });
        test("Edge styles: markers skip repeated endpoint segments and point away from the path", () => {
            PointD[] p=[new(0,0),new(0,0),new(100,0),new(100,0)];var e=Graph(new(SourceArrow:EdgeArrow.Open)).Edges[0];
            var a=EdgeGeometry.Marker(e,p,true)!.Value;var b=EdgeGeometry.Marker(e,p,false)!.Value;
            Check(a.Direction==new PointD(-1,0)&&b.Direction==new PointD(1,0));
            Check(EdgeGeometry.Marker(e,[new(0,0),new(0,0)],false) is null);
        });
        test("Edge styles: marker vertices and circle disks fit their computed bounds", () => {
            var path=new PointD[]{new(10,20),new(90,140)}; Span<PointD> vertices=stackalloc PointD[4];
            foreach(var kind in Enum.GetValues<EdgeArrow>()) {
                var e=Graph(new(8,SourceArrow:kind,TargetArrow:kind)).Edges[0];
                foreach(var source in new[]{true,false}) if(EdgeGeometry.Marker(e,path,source) is { } m) {
                    var count=m.CopyVertices(vertices); for(var i=0;i<count;i++) Check(m.Bounds.Inflate(1e-9).Contains(vertices[i]));
                    if(m.IsCircle) Check(m.Hit(m.CircleCenter));
                    Check(!m.Hit(new(-1000,-1000)));
                }
            }
        });
        test("Edge styles: thick lines participate in exact pointer and area geometry", () => {
            var e=Graph(new(64,TargetArrow:EdgeArrow.None)).Edges[0];PointD[] p=[new(100,30),new(300,30)];
            Check(EdgeGeometry.Hit(e,p,new(200,55),0));Check(!EdgeGeometry.Hit(e,p,new(200,63),0));
            Check(EdgeGeometry.MatchesArea(e,p,new(190,52,20,5),false));
            Check(!EdgeGeometry.MatchesArea(e,p,new(90,20,230,20),true));
            Check(EdgeGeometry.MatchesArea(e,p,new(60,-10,280,80),true));
        });
        test("Edge styles: marker picking rejects empty corners of its enclosing rectangle", () => {
            var e=Graph(new(TargetArrow:EdgeArrow.Triangle)).Edges[0];PointD[] p=[new(0,0),new(100,0)];
            var m=EdgeGeometry.Marker(e,p,false)!.Value;
            Check(m.Hit(new(95,0))&&!m.Hit(new(99,3)));
            Check(m.Intersects(new(94,-1,1,2))&&!m.Intersects(new(99,2.9,.1,.1)));
        });
        test("Edge styles: scene and selection bounds include explicit stroke and markers", () => {
            var g=Graph(new(64,SourceArrow:EdgeArrow.Tee,TargetArrow:EdgeArrow.HollowDiamond));var e=g.Edges[0];
            var b=EdgeGeometry.Bounds(e,g.Index().EdgePath(e));
            Check(GraphScene.BoundsForItems(g,[e.Id])==b);
            Check(Geometry.RectangleContainsRectangle(GraphScene.Bounds(g),b));
        });
        test("Edge styles: cancellation-independent appearance leaves routes and weights intact", () => {
            var g=Graph();var d=new GraphDocument();d.Replace(g,clearHistory:true);
            d.Edit("Style",e=>e.Update(g.Edges[0] with {Style=new(4,EdgeDash.DashDot,EdgeArrow.Circle,EdgeArrow.Open)}));
            Check(g.Index().EdgePath(g.Edges[0]).SequenceEqual(d.Snapshot.Index().EdgePath(d.Snapshot.Edges[0])));
            Check(d.Snapshot.Edges[0].Weight==g.Edges[0].Weight&&d.Snapshot.Nodes==g.Nodes);d.Undo();Check(ReferenceEquals(g,d.Snapshot));
        });
        test("Edge styles: warmed marker and bounds geometry allocate no temporary arrays", () => {
            var e=Graph(new(4,EdgeDash.DashDot,EdgeArrow.HollowCircle,EdgeArrow.Diamond)).Edges[0];PointD[] p=[new(0,0),new(100,0)];
            for(var i=0;i<100;i++){_=EdgeGeometry.Bounds(e,p);_=EdgeGeometry.Hit(e,p,new(95,0),0);_=EdgeStyle.DashPattern(EdgeDash.DashDot);}
            var start=GC.GetAllocatedBytesForCurrentThread();var sum=0d;
            for(var i=0;i<10000;i++){sum+=EdgeGeometry.Bounds(e,p).Width;_=EdgeGeometry.Hit(e,p,new(95,0),0);sum+=EdgeStyle.DashPattern(EdgeDash.DashDot)[0];}
            Check(GC.GetAllocatedBytesForCurrentThread()-start<1024&&sum>0);
        });
    }
}
