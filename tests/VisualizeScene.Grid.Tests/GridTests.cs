using VisualizeScene.Core;
using VisualizeScene.Editing;

static class GridTests
{
    static void Check(bool condition) { if (!condition) throw new Exception("Grid assertion failed."); }
    static void Throws<T>(Action action) where T : Exception { try { action(); } catch (T) { return; } throw new Exception("Expected " + typeof(T).Name); }
    static GraphSnapshot Graph() => new([new("n", new(3, 7, 90, 50))], [], [], []);
    public static void Run(Action<string, Action> test)
    {
        test("Grid: defaults preserve uniform top-left snapping and midpoint-to-even ties", () => {
            using var s = new EditorSession(new()); Check(s.Snap(new(15,-15)) == new PointD(20,-20)); Check(s.Snap(new(25,-25)) == new PointD(20,-20));
            var id=s.AddNode(new(100,100)); Check(s.Document.Snapshot.Index().Nodes[id].Bounds == new RectD(40,70,130,56));
        });
        test("Grid: rectangular spacing and signed origin use one lattice", () => {
            var g = new GraphGridSettings { HorizontalSpacing = 25, VerticalSpacing = 40, Origin = new(-3,7) };
            Check(GraphGrid.Snap(new(21,48),g)==new PointD(22,47)); Check(GraphGrid.Snap(new(-29,-34),g)==new PointD(-28,-33));
        });
        test("Grid: each snapping axis is independent of grid visibility and drawing pattern", () => {
            var g = new GraphGridSettings { Visible=false, Pattern=GridPattern.Crosses };
            Check(GraphGrid.Snap(new(13,17),g with { SnapAxes=GridSnapAxes.X })==new PointD(10,17));
            Check(GraphGrid.Snap(new(13,17),g with { SnapAxes=GridSnapAxes.Y })==new PointD(13,20));
            Check(GraphGrid.Snap(new(13,17),g with { SnapAxes=GridSnapAxes.None })==new PointD(13,17));
            Check(GraphGrid.Snap(new(13,17),g with { SnapEnabled=false })==new PointD(13,17));
        });
        test("Grid: invalid settings reject atomically without changing a session or active preview", () => {
            var d = new GraphDocument();d.Replace(Graph());using var s=new EditorSession(d) { SnapToItems=false };s.Select(["n"]);
            s.PointerDown(new(35,30));s.PointerMove(new(62,54));var preview=s.DisplayGraph;var settings=s.GridSettings;
            foreach(var bad in new[]{settings with { HorizontalSpacing=0 },settings with { VerticalSpacing=double.NaN },settings with { Origin=new(double.PositiveInfinity,0) },
                settings with { Pattern=(GridPattern)99 },settings with { SnapAxes=(GridSnapAxes)8 },settings with { NodeAnchor=(GridNodeAnchor)99 },
                settings with { Color="#bad" },settings with { Thickness=5 }})
                Throws<ArgumentException>(()=>s.GridSettings=bad);
            Check(ReferenceEquals(settings,s.GridSettings)&&ReferenceEquals(preview,s.DisplayGraph));s.CancelGesture();
        });
        test("Grid: nonfinite snap input and unrepresentable results are diagnosed", () => {
            Throws<ArgumentOutOfRangeException>(()=>GraphGrid.Snap(new(double.NaN,0),GraphGridSettings.Default));
            Throws<OverflowException>(()=>GraphGrid.Snap(new(double.MaxValue,0),new() { HorizontalSpacing=double.Epsilon }));
            Throws<ArgumentNullException>(()=>GraphGrid.Snap(new(),null!));
        });
        test("Grid: uniform compatibility setter updates both rectangular axes", () => {
            using var s = new EditorSession(new()) { GridSettings=new() { HorizontalSpacing=20,VerticalSpacing=35,Origin=new(2,3) } };
            Check(s.GridSize==20);s.GridSize=50;Check(s.GridSettings.HorizontalSpacing==50&&s.GridSettings.VerticalSpacing==50&&s.GridSettings.Origin==new PointD(2,3));
            Throws<ArgumentOutOfRangeException>(()=>s.GridSize=-1);Check(s.GridSize==50);
        });
        test("Grid: changing preferences cancels a captured drag without publishing history", () => {
            var d=new GraphDocument();d.Replace(Graph(),clearHistory:true);using var s=new EditorSession(d) { SnapToItems=false };
            s.Select(["n"]);var before=d.Snapshot;var revision=d.Revision;var view=s.Viewport.State;
            s.PointerDown(new(35,30));s.PointerMove(new(62,54));Check(!ReferenceEquals(s.DisplayGraph,before));
            s.GridSettings=s.GridSettings with { HorizontalSpacing=25 };s.PointerUp(new(62,54));
            Check(ReferenceEquals(d.Snapshot,before)&&d.Revision==revision&&!d.CanUndo&&s.Selection.SetEquals(["n"])&&s.Viewport.State==view);
        });
        test("Grid: equal preferences are inert and preserve the active gesture and redo", () => {
            var d=new GraphDocument();d.Replace(Graph(),clearHistory:true);using var s=new EditorSession(d) { SnapToItems=false };s.Select(["n"]);s.Nudge(new(1,0));d.Undo();
            var invalidated=0;s.Invalidated+=(_,_)=>invalidated++;s.GridSettings=s.GridSettings with {};Check(invalidated==0&&d.CanRedo);
            s.PointerDown(new(35,30));s.PointerMove(new(62,54));var preview=s.DisplayGraph;s.GridSettings=s.GridSettings with {};
            Check(ReferenceEquals(preview,s.DisplayGraph));s.PointerUp(new(62,54));Check(d.CanUndo);
        });
        test("Grid: center-anchored creation is explicit and preserves exact undo", () => {
            var d=new GraphDocument();using var s=new EditorSession(d) { GridSettings=new() { HorizontalSpacing=25,VerticalSpacing=40,Origin=new(2,7),NodeAnchor=GridNodeAnchor.Center } };
            var before=d.Snapshot;var id=s.AddNode(new(103,92));Check(d.Snapshot.Index().Nodes[id].Bounds.Center==new PointD(102,87));d.Undo();Check(ReferenceEquals(before,d.Snapshot));
        });
        test("Grid: center-anchored node movement uses rectangular lattice while preserving dimensions", () => {
            var d=new GraphDocument();d.Replace(Graph(),clearHistory:true);using var s=new EditorSession(d) { SnapToItems=false,GridSettings=new() { HorizontalSpacing=25,VerticalSpacing=40,Origin=new(2,7),NodeAnchor=GridNodeAnchor.Center } };
            s.Select(["n"]);s.PointerDown(new(35,30));s.PointerUp(new(62,54));Check(d.Snapshot.Nodes[0].Bounds.Center==new PointD(77,47));Check(d.Snapshot.Nodes[0].Bounds.Width==90);d.Undo();Check(d.Snapshot.Nodes[0].Bounds==Graph().Nodes[0].Bounds);
        });
        test("Grid: appearance and visibility changes preserve saved graph state and redo", () => {
            var d=new GraphDocument();d.Replace(Graph(),clearHistory:true);using var s=new EditorSession(d);s.Select(["n"]);s.Nudge(new(10,0));d.Undo();var before=d.Snapshot;var rev=d.Revision;var save=new GraphSavePoint(d);
            s.GridSettings=s.GridSettings with { Pattern=GridPattern.Lines,Color="#112233",Thickness=2 };s.ShowGrid=false;s.SnapToGrid=false;
            Check(ReferenceEquals(before,d.Snapshot)&&rev==d.Revision&&d.CanRedo&&!save.IsDirty);
        });
        test("Grid: a zoomed-in grid never invents points between snapping coordinates", () => {
            var g=new GraphGridSettings { HorizontalSpacing=100,VerticalSpacing=80 };var p=GraphGrid.Plan(g,new(new(),8),1600,900);
            Check(p.X.Step==800&&p.Y.Step==640&&p.X.DecimationExponent==0&&p.Y.DecimationExponent==0);
        });
        test("Grid: low zoom removes points only by powers of two", () => {
            var g=GraphGridSettings.Default;var p=GraphGrid.Plan(g,new(new(),.05),800,400);
            Check(p.X.Step>=14&&p.X.DecimationExponent>0&&p.X.Step==Math.ScaleB(g.HorizontalSpacing,p.X.DecimationExponent)*.05);
        });
        test("Grid: sampling agrees with an independent finite-range lattice enumeration", () => {
            var random=new Random(492);
            for(var i=0;i<150;i++) {
                var spacing=random.Next(15,90);var o=random.Next(-400,400);var v=random.Next(-400,400);var width=random.Next(100,800);var zoom=new[]{.5,1,2,8}[i%4];
                var g=new GraphGridSettings { HorizontalSpacing=spacing,Origin=new(o,0) };var p=GraphGrid.Plan(g,new(new(v,0),zoom),width,300).X;
                var step=Math.ScaleB(spacing,p.DecimationExponent);
                var expected=Enumerable.Range(-300,601).Select(k=>(o+k*step-v)*zoom).Where(x=>x>=-4&&x<=width+4).Order().ToArray();
                Check(expected.Length==p.Count);for(var n=0;n<p.Count;n++) Check(Math.Abs(expected[n]-p.Position(n))<1e-8);
            }
        });
        test("Grid: giant viewports and subnormal spacing retain a fixed mark budget", () => {
            foreach(var size in new[]{double.Epsilon,1e-200,1e-5,10,1e100,double.MaxValue})
            foreach(var width in new[]{1d,800,1e6,1e100,double.MaxValue}) {
                var p=GraphGrid.Plan(new() { HorizontalSpacing=size,VerticalSpacing=size },new(new(),.05),width,width);
                Check(p.X.Count<=GraphGrid.MaximumAxisMarks&&p.Y.Count<=GraphGrid.MaximumAxisMarks&&p.Marks<=65536);
                for(var n=0;n<p.X.Count;n++) Check(double.IsFinite(p.X.Position(n)));
            }
        });
        test("Grid: reduced phases avoid overflow between opposite extreme origins", () => {
            var p=GraphGrid.Plan(new() { Origin=new(double.MaxValue,-double.MaxValue),HorizontalSpacing=100,VerticalSpacing=100 },new(new(-double.MaxValue,double.MaxValue),1),800,400);
            Check(p.Marks>0&&!p.X.Suppressed&&!p.Y.Suppressed);for(var i=0;i<p.X.Count;i++) Check(double.IsFinite(p.X.Position(i)));
        });
        test("Grid: all styles report only their requested primitive count", () => {
            var g=GraphGridSettings.Default;
            foreach(var pattern in Enum.GetValues<GridPattern>()) {var p=GraphGrid.Plan(g with { Pattern=pattern },new(new(),1),800,400);Check(p.Marks==(pattern switch {GridPattern.HorizontalLines=>p.Y.Count,GridPattern.VerticalLines=>p.X.Count,GridPattern.Lines=>p.X.Count+p.Y.Count,_=>p.X.Count*p.Y.Count}));}
        });
        test("Grid: hidden and zero-sized grids produce no primitives", () => {
            Check(GraphGrid.Plan(new() { Visible=false },new(new(),1),800,400).Marks==0);
            Check(GraphGrid.Plan(new(),new(new(),1),0,400).Marks==0);
            Throws<ArgumentOutOfRangeException>(()=>GraphGrid.Plan(new(),new(new(),0),800,400));
            Throws<ArgumentOutOfRangeException>(()=>GraphGrid.Plan(new(),new(new(),1),-1,400));
        });
        test("Grid: warmed planning allocates no per-frame managed arrays", () => {
            var g=GraphGridSettings.Default;var v=new ViewportState(new(2,3),1);for(var i=0;i<100;i++) GraphGrid.Plan(g,v,800,400);
            var before=GC.GetAllocatedBytesForCurrentThread();var marks=0;for(var i=0;i<1000;i++) marks+=GraphGrid.Plan(g,v,800,400).Marks;
            Check(marks>0&&GC.GetAllocatedBytesForCurrentThread()-before<1024);
        });
    }
}
