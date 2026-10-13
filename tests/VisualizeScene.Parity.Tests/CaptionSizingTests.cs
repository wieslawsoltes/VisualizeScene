using System.Collections.Immutable;
using VisualizeScene.Core;
using VisualizeScene.Editing;

static class CaptionSizingTests
{
    static void Check(bool value) { if (!value) throw new Exception("Caption sizing assertion failed"); }
    static void Throws<T>(Action action) where T : Exception { try { action(); } catch (T) { return; } throw new Exception("Expected " + typeof(T).Name); }
    static PointD Measure(GraphNode _) => new(100, 40);
    static GraphSnapshot Graph() => new([new("a", new(10,20,40,30), "Long caption", NodeShape.Rectangle),
        new("b",new(300,200,80,60),"Other",NodeShape.Ellipse)],
        [new("ab","a","b",Bends:[new(200,150)])], [new("p","a",new(1,.5))], [new("l","a","Annotation",new(0,100))]);
    static GraphDocument Document(GraphSnapshot? graph=null) { var d=new GraphDocument();d.Replace(graph??Graph(),clearHistory:true);return d; }
    public static void Run(Action<string,Action> test)
    {
        test("Caption sizing: all six shapes contain the padded text block and renderer width budget",()=>{
            foreach(var shape in Enum.GetValues<NodeShape>()) {
                var size=NodeCaptionLayout.FittingSize(shape,new(100,40));
                var node=new GraphNode("n",new(0,0,size.X,size.Y),Shape:shape);
                foreach(var x in new[]{-58d,58d})foreach(var y in new[]{-26d,26d})
                    Check(Geometry.HitNode(node,node.Bounds.Center+new PointD(x,y)));
                Check(size.X*NodeCaptionLayout.WidthFraction(shape)>=116 && size.Y-NodeCaptionLayout.HeightInset>=40);
            }
        });
        test("Caption sizing: fractional and extreme aspect ratios fit shape interiors",()=>{
            foreach(var shape in Enum.GetValues<NodeShape>())foreach(var content in new[]{new PointD(.1,10000),new PointD(10000,.1),new PointD(0,0)}) {
                var size=NodeCaptionLayout.FittingSize(shape,content);var node=new GraphNode("n",new(0,0,size.X,size.Y),Shape:shape);
                foreach(var dx in new[]{-1,1})foreach(var dy in new[]{-1,1})
                    Check(Geometry.HitNode(node,node.Bounds.Center+new PointD(dx*content.X/2,dy*content.Y/2)));
            }
        });
        test("Caption sizing: one batch changes dimensions only and retains all other records",()=>{
            var g=Graph();var r=GraphNodeCaptionSizing.Calculate(g,["a","b"],Measure);
            Check(r.ResizedNodes==2 && r.MeasuredNodes==2 && r.ExaminedNodes==2);
            Check(r.Graph.Nodes[0].Bounds==new RectD(10,20,142,52));
            Check(r.Graph.Nodes[1].Bounds==new RectD(300,200,165,74));
            for(var i=0;i<2;i++)Check(r.Graph.Nodes[i] with {Bounds=g.Nodes[i].Bounds}==g.Nodes[i]);
            Check(r.Graph.Edges==g.Edges && r.Graph.Ports==g.Ports && r.Graph.Labels==g.Labels);
        });
        test("Caption sizing: group frames, hidden children and position-locked nodes are excluded",()=>{
            var g=Graph();g=g with {Nodes=g.Nodes.SetItem(0,g.Nodes[0] with {Locked=true})
                .SetItem(1,g.Nodes[1] with {ParentId="g"}).Add(new("g",new(0,0,1000,1000),IsGroup:true,Collapsed:true))};
            var r=GraphNodeCaptionSizing.Calculate(g,["a","b","g","p","l","ab"],_=>throw new Exception("Measured ineligible node"));
            Check(ReferenceEquals(r.Graph,g)&&r.EligibleNodes==0&&r.MeasuredNodes==0);
        });
        test("Caption sizing: only selected nodes are measured and duplicate IDs are inert",()=>{
            var calls=new List<string>();var r=GraphNodeCaptionSizing.Calculate(Graph(),["a","a","l","p","ab"],n=>{calls.Add(n.Id);return Measure(n);});
            Check(calls.SequenceEqual(["a"]) && r.EligibleNodes==1 && r.ResizedNodes==1);
        });
        test("Caption sizing: invalid IDs fail before any application callback",()=>{
            var called=false;Throws<ArgumentException>(()=>GraphNodeCaptionSizing.Calculate(Graph(),["a","missing"],n=>{called=true;return Measure(n);}));Check(!called);
        });
        test("Caption sizing: matching dimensions return the exact snapshot without a node-array pass",()=>{
            var g=GraphNodeCaptionSizing.Calculate(Graph(),["a","b"],Measure).Graph;
            var result=GraphNodeCaptionSizing.Calculate(g,["a","b"],Measure);
            Check(ReferenceEquals(g,result.Graph)&&result.ResizedNodes==0&&result.ExaminedNodes==0);
        });
        test("Caption sizing: minima and enlarge-only preserve existing space and top-left positions",()=>{
            var g=Graph();var options=new NodeCaptionSizingOptions(MinimumWidth:500,MinimumHeight:200);
            var grown=GraphNodeCaptionSizing.Calculate(g,["a"],Measure,options).Graph;Check(grown.Nodes[0].Bounds==new RectD(10,20,500,200));
            Check(ReferenceEquals(GraphNodeCaptionSizing.Calculate(grown,["a"],Measure,new(EnlargeOnly:true)).Graph,grown));
        });
        test("Caption sizing: invalid options and measurements never publish partial dimensions",()=>{
            foreach(var value in new[]{double.NaN,double.PositiveInfinity,-1}) {
                Throws<ArgumentOutOfRangeException>(()=>GraphNodeCaptionSizing.Calculate(Graph(),[],Measure,new(HorizontalPadding:value)));
                Throws<ArgumentOutOfRangeException>(()=>GraphNodeCaptionSizing.Calculate(Graph(),["a"],_=>new(value,40)));
            }
            Throws<ArgumentOutOfRangeException>(()=>GraphNodeCaptionSizing.Calculate(Graph(),[],Measure,new(MinimumHeight:0)));
            Throws<ArgumentOutOfRangeException>(()=>NodeCaptionLayout.FittingSize((NodeShape)99,new(10,10)));
        });
        test("Caption sizing: arithmetic overflow rejects computed sizes and final coordinates",()=>{
            Throws<ArgumentException>(()=>NodeCaptionLayout.FittingSize(NodeShape.Diamond,new(double.MaxValue,20)));
            var g=Graph();g=g with {Nodes=g.Nodes.SetItem(0,g.Nodes[0] with {Bounds=new(1.5e308,20,40,30)})};
            Throws<ArgumentException>(()=>GraphNodeCaptionSizing.Calculate(g,["a"],_=>new(1e308,20)));
        });
        test("Caption sizing: pre-cancellation and cancellation during a measurement are atomic",()=>{
            using var cancel=new CancellationTokenSource();cancel.Cancel();
            Throws<OperationCanceledException>(()=>GraphNodeCaptionSizing.Calculate(Graph(),[],Measure,cancellationToken:cancel.Token));
            var d=Document();using var s=new EditorSession(d);s.Select(["a","b"]);var before=d.Snapshot;
            using var during=new CancellationTokenSource();s.NodeCaptionSizeProvider=n=>{during.Cancel();return Measure(n);};
            Throws<OperationCanceledException>(()=>s.FitNodeCaptions(cancellationToken:during.Token));Check(ReferenceEquals(before,d.Snapshot)&&!d.CanUndo);
        });
        test("Caption sizing: a throwing later measurement cannot commit the earlier candidate",()=>{
            var d=Document();using var s=new EditorSession(d);s.Select(["a","b"]);var before=d.Snapshot;
            s.NodeCaptionSizeProvider=n=>n.Id=="a"?Measure(n):throw new InvalidOperationException("Font failure");
            Throws<InvalidOperationException>(()=>s.FitNodeCaptions());Check(ReferenceEquals(before,d.Snapshot)&&!d.CanUndo);
        });
        test("Caption sizing: session requires host metrics instead of guessing from text length",()=>{
            using var s=new EditorSession(Document());s.Select(["a"]);Throws<InvalidOperationException>(()=>s.FitNodeCaptions());Check(!s.Document.CanUndo);
        });
        test("Caption sizing: one revision preserves selection and camera with exact undo and redo",()=>{
            var g=Graph();var d=Document(g);using var s=new EditorSession(d);s.Select(["a","b"]);s.NodeCaptionSizeProvider=Measure;
            var selection=s.Selection;var origin=s.Viewport.Origin;var zoom=s.Viewport.Zoom;var revision=d.Revision;
            s.FitNodeCaptions();var next=d.Snapshot;Check(d.Revision==revision+1&&s.LastCaptionSizing!.ResizedNodes==2);
            Check(ReferenceEquals(selection,s.Selection)&&origin==s.Viewport.Origin&&zoom==s.Viewport.Zoom);
            d.Undo();Check(ReferenceEquals(g,d.Snapshot)&&!d.CanUndo);d.Redo();Check(ReferenceEquals(next,d.Snapshot));
        });
        test("Caption sizing: repeated fitting preserves redo history",()=>{
            var g=GraphNodeCaptionSizing.Calculate(Graph(),["a"],Measure).Graph;var d=Document(g);using var s=new EditorSession(d);
            s.Select(["a"]);s.NodeCaptionSizeProvider=Measure;s.Rename("b","Changed");d.Undo();var revision=d.Revision;
            s.FitNodeCaptions();Check(d.CanRedo&&d.Revision==revision&&ReferenceEquals(g,d.Snapshot));
        });
        test("Caption sizing: reentrant document changes cannot be overwritten",()=>{
            var d=Document();using var s=new EditorSession(d);s.Select(["a"]);var bounds=d.Snapshot.Nodes[0].Bounds;
            s.NodeCaptionSizeProvider=n=>{s.Rename("b","Concurrent");return Measure(n);};
            Throws<InvalidOperationException>(()=>s.FitNodeCaptions());Check(d.Snapshot.Nodes[0].Bounds==bounds&&d.Snapshot.Nodes[1].Text=="Concurrent");
        });
        test("Caption sizing: changing selection or metric provider invalidates the captured operation",()=>{
            foreach(var changeFont in new[]{false,true}) {
                var d=Document();using var s=new EditorSession(d);s.Select(["a"]);var before=d.Snapshot;
                s.NodeCaptionSizeProvider=n=>{if(changeFont)s.NodeCaptionSizeProvider=Measure;else s.ClearSelection();return Measure(n);};
                Throws<InvalidOperationException>(()=>s.FitNodeCaptions());Check(ReferenceEquals(before,d.Snapshot)&&!d.CanUndo);
            }
        });
        test("Caption sizing: edits during publication clear the result cache rather than reporting stale dimensions",()=>{
            var d=Document();using var s=new EditorSession(d);s.Select(["a"]);s.NodeCaptionSizeProvider=Measure;var armed=true;
            d.Changed+=(_,_)=>{if(armed){armed=false;s.Rename("b","After");}};
            s.FitNodeCaptions();Check(s.LastCaptionSizing is null&&d.Snapshot.Nodes[1].Text=="After");
        });
        test("Caption sizing: warm no-op allocation is independent of unrelated node count",()=>{
            var small=GraphNodeCaptionSizing.Calculate(Graph(),["a"],Measure).Graph;
            var large=small with {Nodes=small.Nodes.AddRange(Enumerable.Range(0,10000).Select(i=>new GraphNode("n"+i,new(i*200,0,100,60))))};
            string[] ids=["a"];Func<GraphNode,PointD> measure=Measure;
            long Run(GraphSnapshot graph){var start=GC.GetAllocatedBytesForCurrentThread();var r=GraphNodeCaptionSizing.Calculate(graph,ids,measure);
                var bytes=GC.GetAllocatedBytesForCurrentThread()-start;Check(ReferenceEquals(r.Graph,graph)&&r.MeasuredNodes==1&&r.ExaminedNodes==0);return bytes;}
            _=Run(small);_=Run(large);Check(Run(large)<=Run(small)+4096);
        });
        test("Caption sizing: callbacks may return per-node metrics without text-only kernel memoization",()=>{
            var g=Graph();g=g with {Nodes=g.Nodes.SetItem(1,g.Nodes[1] with {Text=g.Nodes[0].Text})};
            var r=GraphNodeCaptionSizing.Calculate(g,["a","b"],n=>n.Id=="a"?new(40,20):new(300,100));
            Check(r.MeasuredNodes==2&&r.Graph.Nodes[1].Bounds.Width>r.Graph.Nodes[0].Bounds.Width);
        });
    }
}
