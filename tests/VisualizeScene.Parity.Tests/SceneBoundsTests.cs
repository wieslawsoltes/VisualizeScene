using System.Collections.Immutable;
using VisualizeScene.Core;

static class SceneBoundsTests
{
    static void Check(bool v) { if (!v) throw new Exception("Assertion failed"); }
    static void Throws<T>(Action a) where T : Exception { try { a(); } catch (T) { return; } throw new Exception("Expected " + typeof(T).Name); }
    static PointD Measure(GraphLabel _) => new(80, 20);
    static GraphSnapshot Graph() => new([new("a", new(0,0,100,60)), new("b", new(300,200,100,60))],
        [new("e", "a", "b", "Caption", Bends:[new(-80,70),new(250,-200)], Routing:EdgeRouting.Polyline)],
        [new("p", "a", new(.3,.5))], [new("l", "a", "Far label", new(800,500))]);
    // Previous production algorithm retained as an independent allocation-heavy reference.
    static RectD Reference(GraphSnapshot g, Func<GraphLabel,PointD> measure)
    {
        var i=g.Index(); var b=new List<RectD>();
        b.AddRange(g.Nodes.Where(n=>i.IsVisible(n.Id)).Select(n=>n.Bounds));
        foreach(var e in g.Edges.Where(e=>GraphScene.IsEdgeVisible(i,e))) {
            b.AddRange(i.EdgePath(e).Select(p=>new RectD(p.X,p.Y,0,0)));
            if(e.Text.Length>0) {var l=new GraphLabel("",e.Id,e.Text,new(0,-9),11);b.Add(GraphScene.LabelBounds(i,l,measure(l)));}
        }
        foreach(var l in g.Labels.Where(l=>GraphScene.IsOwnerVisible(i,l.OwnerId))) b.Add(GraphScene.LabelBounds(i,l,measure(l)));
        return RectD.Enclose(b);
    }
    public static void Run(Action<string,Action> test)
    {
        test("Scene bounds: streaming preserves exact reference arithmetic across edits and folding",()=>{
            var random=new Random(831);
            for(var trial=0;trial<100;trial++) {
                var g=Graph();var edit=new GraphEdit(g);
                edit.Move(["a"],new(random.NextDouble()*200-100,random.NextDouble()*80));
                if(trial%3==0) {var group=edit.Group(["a","b"]);if(trial%2==0) edit.Update(edit.Node(group) with {Collapsed=true});}
                g=edit.Build(); Check(GraphScene.Bounds(g,Measure)==Reference(g,Measure));
            }
        });
        test("Scene bounds: empty scene keeps legacy default but empty selection returns null",()=>{
            Check(GraphScene.Bounds(GraphSnapshot.Empty)==new RectD(0,0,1,1));
            Check(GraphScene.BoundsForItems(Graph(),[]) is null);
        });
        test("Selection bounds: only selected geometry is measured, not unselected dependencies",()=>{
            var g=Graph();var calls=0;PointD M(GraphLabel l){calls++;return Measure(l);}
            Check(GraphScene.BoundsForItems(g,["a"],M)==g.Nodes[0].Bounds && calls==0);
            Check(GraphScene.BoundsForItems(g,["l"],M)==new RectD(810,520,80,20) && calls==1);
        });
        test("Selection bounds: edge bends and its measured caption are enclosed",()=>{
            var g=Graph();var index=g.Index();var edge=g.Edges[0];
            var caption=new GraphLabel("",edge.Id,edge.Text,new(0,-9),11);
            var expected=RectD.Enclose(index.EdgePath(edge).Select(p=>new RectD(p.X,p.Y,0,0)).Append(GraphScene.LabelBounds(index,caption,Measure(caption))));
            Check(GraphScene.BoundsForItems(g,["e"],Measure)==expected);
            Check(expected.Contains(new(-80,70))&&expected.Contains(new(250,-200)));
        });
        test("Selection bounds: expanded groups use their full rectangle and hidden descendants are skipped",()=>{
            var edit=new GraphEdit(Graph());var id=edit.Group(["a","b"]);var expanded=edit.Build();
            Check(GraphScene.BoundsForItems(expanded,[id],Measure)==expanded.Index().Nodes[id].Bounds);
            edit.Update(edit.Node(id) with {Collapsed=true});var folded=edit.Build();
            Check(GraphScene.BoundsForItems(folded,["a","b","e","p","l"],Measure) is null);
            Check(GraphScene.BoundsForItems(folded,[id,"a"],Measure)==folded.Index().Nodes[id].Bounds);
        });
        test("Selection bounds: port-only selection uses the exact relative anchor",()=>{
            var g=Graph();Check(GraphScene.BoundsForItems(g,["p"],Measure)==new RectD(30,30,0,0));
            Check(GraphScene.BoundsForItems(g,["a","a"],Measure)==g.Nodes[0].Bounds);
        });
        test("Selection bounds: unknown IDs, invalid metrics and cancellation reject atomically",()=>{
            var g=Graph();var d=new GraphDocument();d.Replace(g,clearHistory:true);var before=d.Revision;
            Throws<ArgumentException>(()=>GraphScene.BoundsForItems(g,["a","unknown"]));
            Throws<ArgumentException>(()=>GraphScene.BoundsForItems(g,["l"],_=>new(double.NaN,20)));
            using var c=new CancellationTokenSource();c.Cancel();
            Throws<OperationCanceledException>(()=>GraphScene.BoundsForItems(g,[],cancellationToken:c.Token));
            Check(d.Revision==before && ReferenceEquals(d.Snapshot,g) && !d.CanUndo);
        });
        test("Scene bounds: overflowing label anchors and full extents are diagnosed",()=>{
            var g=new GraphSnapshot([new("a",new(1e308,0,100,60))],[],[],[new("l","a","x",new(1e308,0))]);
            Throws<ArgumentException>(()=>GraphScene.Bounds(g,Measure));
            Throws<ArgumentException>(()=>GraphScene.BoundsForItems(g,["l"],Measure));
            g=new([new("a",new(-1e308,0,100,60)),new("b",new(1e308,0,100,60))],[],[],[]);
            Throws<ArgumentException>(()=>GraphScene.Bounds(g,Measure));
        });
        test("Scene bounds: warmed 10000-item traversal retains no graph-sized temporary list",()=>{
            var nodes=Enumerable.Range(0,10000).Select(n=>new GraphNode("n"+n,new(n*150,0,100,60))).ToImmutableArray();
            var edges=Enumerable.Range(0,9999).Select(n=>new GraphEdge("e"+n,"n"+n,"n"+(n+1))).ToImmutableArray();
            var labels=Enumerable.Range(0,10000).Select(n=>new GraphLabel("l"+n,"n"+n,"Label",new(0,80))).ToImmutableArray();
            var g=new GraphSnapshot(nodes,edges,[],labels);Func<GraphLabel,PointD> measure=Measure;
            for(var i=0;i<3;i++) _=GraphScene.Bounds(g,measure);
            var start=GC.GetAllocatedBytesForCurrentThread();RectD b=default;
            for(var i=0;i<10;i++) b=GraphScene.Bounds(g,measure);
            Check(GC.GetAllocatedBytesForCurrentThread()-start<1024 && b.Width>1000000);
        });
        test("Selection bounds: one selected node in 10000 does not enumerate text or all items",()=>{
            var nodes=Enumerable.Range(0,10000).Select(n=>new GraphNode("n"+n,new(n*150,0,100,60))).ToImmutableArray();
            var g=new GraphSnapshot(nodes,[],[],[]);Check(GraphScene.BoundsForItems(g,["n9999"],_=>throw new Exception("Unexpected text measurement"))==nodes[^1].Bounds);
        });
    }
}
