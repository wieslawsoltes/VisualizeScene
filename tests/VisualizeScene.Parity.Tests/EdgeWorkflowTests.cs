using VisualizeScene.Core;
using VisualizeScene.Editing;

static class EdgeWorkflowTests
{
    static void Check(bool value) { if (!value) throw new Exception("Assertion failed"); }
    static void Throws<T>(Action a) where T:Exception { try { a(); } catch(T) { return; } throw new Exception("Expected " + typeof(T).Name); }
    static GraphDocument Document()
    {
        var d = new GraphDocument(); d.Replace(new([new("a",new(0,0,100,60)),new("b",new(300,0,100,60))], [], [], []), clearHistory:true); return d;
    }
    public static void Run(Action<string,Action> test)
    {
        test("Edge UI contracts: creation defaults affect new edges only and retain one-step undo", () => {
            var d=Document(); var original=d.Snapshot; using var s=new EditorSession(d);
            s.EdgeDefaults=new(EdgeRouting.Polyline,"#112233",new(3,EdgeDash.DashDot,EdgeArrow.HollowDiamond,EdgeArrow.Open));
            Check(!d.CanUndo && ReferenceEquals(original,d.Snapshot)); s.Mode=EditorMode.CreateEdge;
            s.PointerDown(new(50,30)); s.PointerUp(new(50,30)); s.PointerDown(new(350,30));s.PointerUp(new(350,30));
            var e=d.Snapshot.Edges.Single(); Check(e.Style==s.EdgeDefaults.Style && e.Stroke=="#112233" && e.Routing==EdgeRouting.Polyline && e.Directed);
            d.Undo(); Check(ReferenceEquals(original,d.Snapshot)&&!d.CanUndo);
        });
        test("Edge UI contracts: changing presets cancels an unfinished two-click connection", () => {
            var d=Document(); using var s=new EditorSession(d); s.Mode=EditorMode.CreateEdge; s.PointerDown(new(50,30)); s.PointerUp(new(50,30));
            s.EdgeDefaults=new(Style:new(2,EdgeDash.Dot)); s.PointerDown(new(350,30)); s.PointerUp(new(350,30));
            Check(d.Snapshot.Edges.IsEmpty); s.PointerDown(new(50,30)); s.PointerUp(new(50,30));
            Check(d.Snapshot.Edges.Single().SourceId=="b" && d.Snapshot.Edges[0].Style!.Dash==EdgeDash.Dot);
        });
        test("Edge UI contracts: invalid defaults reject before replacing existing options", () => {
            using var s=new EditorSession(Document()); var old=s.EdgeDefaults;
            Throws<ArgumentException>(()=>s.EdgeDefaults=new(Stroke:"bad"));
            Throws<ArgumentException>(()=>s.EdgeDefaults=new((EdgeRouting)99));
            Throws<ArgumentOutOfRangeException>(()=>s.EdgeDefaults=new(Style:new(double.NaN)));
            Check(ReferenceEquals(old,s.EdgeDefaults));
        });
        test("Edge UI contracts: mixed style patches retain geometry, direction and unspecified fields", () => {
            var d=Document();d.Edit("Edges",e=>{e.Add(new GraphEdge("a1","a","b") {Style=new(2,EdgeDash.Dash)}); e.Add(new GraphEdge("b1","b","a",Directed:false));});
            var before=d.Snapshot;var captured=new GraphPropertySelection(before,["a1","b1","a"]);
            Check(captured.LineWidth.IsMixed && captured.LinePattern.IsMixed && captured.LineWidth.Count==2);
            captured.Apply(d,new(LineWidth:4,TargetArrow:EdgeArrow.HollowCircle));
            Check(d.Snapshot.Nodes==before.Nodes && d.Snapshot.Edges[0].Style!.Dash==EdgeDash.Dash && !d.Snapshot.Edges[1].Directed);
            Check(d.Snapshot.Edges.All(e=>e.Style!.Width==4 && e.Style.TargetArrow==EdgeArrow.HollowCircle));
            d.Undo();Check(ReferenceEquals(before,d.Snapshot));
        });
        test("Edge UI contracts: invalid batch appearance rolls back unrelated staged text", () => {
            var d=Document();d.Edit("Edge",e=>e.Add(new GraphEdge("e","a","b")));var before=d.Snapshot;
            var capture=new GraphPropertySelection(before,["a","e"]);
            Throws<ArgumentOutOfRangeException>(()=>capture.Apply(d,new(Text:"wrong",LineWidth:0)));
            Throws<ArgumentException>(()=>capture.Apply(d,new(LinePattern:(EdgeDash)99)));
            Check(ReferenceEquals(before,d.Snapshot));
        });
    }
}
