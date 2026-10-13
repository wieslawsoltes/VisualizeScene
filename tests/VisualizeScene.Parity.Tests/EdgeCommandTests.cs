using System.Collections.Immutable;
using VisualizeScene.Core;
using VisualizeScene.Editing;
using VisualizeScene.Serialization;

static class EdgeCommandTests
{
    static void Check(bool value) { if (!value) throw new Exception("Edge command assertion failed"); }
    static void Throws<T>(Action action) where T : Exception { try { action(); } catch (T) { return; } throw new Exception("Expected " + typeof(T).Name); }
    sealed class Policy(Func<GraphIndex, ConnectionRequest, ConnectionDecision> evaluate) : IConnectionPolicy
    {
        public ConnectionDecision CanStart(GraphIndex graph, ConnectionEnd endpoint, EdgeEnd end) => ConnectionDecision.Accept;
        public ConnectionDecision Evaluate(GraphIndex graph, ConnectionRequest request) => evaluate(graph, request);
    }
    static GraphSnapshot Graph() => new([
        new("a", new(0,0,100,60)), new("b", new(400,100,100,60)), new("c", new(700,0,100,60))],
        [new GraphEdge("e","a","b", "Caption", 3.5, [new(180,30),new(220,90),new(300,130)], "#112233", true,
            EdgeRouting.Polyline, "pa", "pb") { Style = new(3, EdgeDash.Dash, EdgeArrow.Diamond, EdgeArrow.Open) }],
        [new("pa","a",new(1,.5)),new("pb","b",new(0,.5))], [new("le","e","Edge label",new(0,35))]);
    static GraphDocument Document(GraphSnapshot? graph = null)
    { var d = new GraphDocument(); d.Replace(graph ?? Graph(), clearHistory:true); return d; }
    public static void Run(Action<string, Action> test)
    {
        test("Edge commands: reverse swaps endpoints, ports and bend order but retains all other data", () => {
            var g=Graph(); var r=GraphEdgeCommands.Calculate(g,["e"],EdgeCommand.Reverse); var e=r.Graph.Edges[0];
            Check(e.SourceId=="b" && e.TargetId=="a" && e.SourcePortId=="pb" && e.TargetPortId=="pa");
            Check(e.Bends.SequenceEqual(g.Edges[0].Bends.Reverse()) && e.Style==g.Edges[0].Style && e.Directed && e.Weight==3.5 && e.Text=="Caption");
            Check(r.Graph.Nodes==g.Nodes && r.Graph.Labels==g.Labels && r.Graph.Ports==g.Ports && r.ChangedEdges==1);
            Check(r.Graph.Index().EdgePath(e).SequenceEqual(g.Index().EdgePath(g.Edges[0]).Reverse()));
            Check(GraphIO.ToJson(GraphEdgeCommands.Calculate(r.Graph,["e"],EdgeCommand.Reverse).Graph)==GraphIO.ToJson(g));
        });
        test("Edge commands: clearing bends retains routing, endpoints, appearance and direction", () => {
            var g=Graph(); var r=GraphEdgeCommands.Calculate(g,["e"],EdgeCommand.ClearBends);
            Check(r.Graph.Edges[0] == g.Edges[0] with {Bends=[]});
            Check(r.Graph.Nodes==g.Nodes && r.Graph.Ports==g.Ports && r.Graph.Labels==g.Labels);
            Check(ReferenceEquals(GraphEdgeCommands.Calculate(r.Graph,["e"],EdgeCommand.ClearBends).Graph,r.Graph));
        });
        test("Edge commands: duplicates and known non-edge items do not multiply edits", () => {
            var r=GraphEdgeCommands.Calculate(Graph(),["e","a","e","pa","le"],EdgeCommand.Reverse);
            Check(r.EligibleEdges==1 && r.ChangedEdges==1 && r.ExaminedEdges==1);
        });
        test("Edge commands: hidden, folded-projection and locked endpoints are excluded", () => {
            var g=Graph(); g=g with {Nodes=g.Nodes.SetItem(0,g.Nodes[0] with {Locked=true})};
            Check(ReferenceEquals(GraphEdgeCommands.Calculate(g,["e"],EdgeCommand.ClearBends).Graph,g));
            g=g with {Nodes=g.Nodes.SetItem(0,g.Nodes[0] with {Locked=false,ParentId="folder"}).Add(new("folder",new(-50,-50,300,200),IsGroup:true,Collapsed:true))};
            Check(GraphScene.IsEdgeVisible(g.Index(),g.Edges[0]));
            Check(ReferenceEquals(GraphEdgeCommands.Calculate(g,["e"],EdgeCommand.Reverse).Graph,g));
            Check(GraphEdgeCommandState.Calculate(g,["e"]).EditableEdges==0);
        });
        test("Edge commands: default/empty bends and symmetric self loops are true no-ops", () => {
            foreach (var bends in new[] {default(ImmutableArray<PointD>), ImmutableArray<PointD>.Empty, ImmutableArray.Create(new PointD(150,100)), ImmutableArray.Create(new PointD(150,100),new PointD(200,50),new PointD(150,100))}) {
                var g=Graph();g=g with {Edges=[g.Edges[0] with {TargetId="a",TargetPortId="pa",Bends=bends}]};
                Check(ReferenceEquals(GraphEdgeCommands.Calculate(g,["e"],EdgeCommand.Reverse).Graph,g));
                if(bends.IsDefaultOrEmpty)Check(ReferenceEquals(GraphEdgeCommands.Calculate(g,["e"],EdgeCommand.ClearBends).Graph,g));
            }
        });
        test("Edge commands: undirected edges remain undirected and same-node distinct ports can reverse", () => {
            var g=Graph();g=g with {Edges=[g.Edges[0] with {Directed=false}]};
            Check(!GraphEdgeCommands.Calculate(g,["e"],EdgeCommand.Reverse).Graph.Edges[0].Directed);
            g=g with {Ports=g.Ports.Add(new("pa2","a",new(0,.5))),Edges=[g.Edges[0] with {TargetId="a",TargetPortId="pa2"}]};
            var r=GraphEdgeCommands.Calculate(g,["e"],EdgeCommand.Reverse);Check(r.Graph.Edges[0].SourcePortId=="pa2" && r.ChangedEdges==1);
        });
        test("Edge commands: final-topology validation allows swapping an antiparallel pair", () => {
            var g=Graph();g=g with {Edges=g.Edges.Add(new("back","b","a"))};
            var r=GraphEdgeCommands.Calculate(g,["e","back"],EdgeCommand.Reverse,new ConnectionPolicy {AllowParallelEdges=false});
            Check(r.ChangedEdges==2 && r.Graph.Edges[0].SourceId=="b" && r.Graph.Edges[1].SourceId=="a");
        });
        test("Edge commands: parallel, cyclic and incompatible-port reversals are rejected atomically", () => {
            var g=Graph();g=g with {Edges=g.Edges.Add(new("back","b","a"))};
            Throws<InvalidOperationException>(()=>GraphEdgeCommands.Calculate(g,["e"],EdgeCommand.Reverse,new ConnectionPolicy {AllowParallelEdges=false}));
            g=Graph();g=g with {Edges=g.Edges.Add(new("ac","a","c")).Add(new("cb","c","b"))};
            Throws<InvalidOperationException>(()=>GraphEdgeCommands.Calculate(g,["e"],EdgeCommand.Reverse,new ConnectionPolicy {AllowCycles=false}));
            var d=Document();using var s=new EditorSession(d);s.Select(["e"]);s.ConnectionPolicy=new ConnectionPolicy {Ports=new Dictionary<string,PortConstraint>{{"pa",new(PortRoles.Source)}}};
            var before=d.Snapshot;Throws<InvalidOperationException>(()=>s.EditEdges(EdgeCommand.Reverse));Check(ReferenceEquals(before,d.Snapshot)&&!d.CanUndo);
        });
        test("Edge commands: degree and capacity rules inspect the proposed complete topology", () => {
            var g=Graph();g=g with {Edges=g.Edges.Add(new("bc","b","c"))};
            Throws<InvalidOperationException>(()=>GraphEdgeCommands.Calculate(g,["e"],EdgeCommand.Reverse,new ConnectionPolicy {MaximumOutgoing=1}));
            var policy=new ConnectionPolicy {Ports=new Dictionary<string,PortConstraint>{{"pa",new(Capacity:1)},{"pb",new(Capacity:1)}}};
            Check(GraphEdgeCommands.Calculate(Graph(),["e"],EdgeCommand.Reverse,policy).ChangedEdges==1);
        });
        test("Edge commands: all custom validations see one shared final index", () => {
            var g=Graph();g=g with {Edges=g.Edges.Add(new("back","b","a"))};GraphIndex? observed=null;var calls=0;
            var policy=new Policy((index,request)=>{observed??=index;Check(ReferenceEquals(observed,index));calls++;
                Check(index.Edges["e"].SourceId=="b" && index.Edges["back"].SourceId=="a" && request.ReplacedEdgeId is not null);return ConnectionDecision.Accept;});
            var r=GraphEdgeCommands.Calculate(g,["e","back"],EdgeCommand.Reverse,policy);Check(calls==2 && ReferenceEquals(observed!.Graph,r.Graph));
        });
        test("Edge commands: invalid input and cancellation do not publish", () => {
            Throws<ArgumentOutOfRangeException>(()=>GraphEdgeCommands.Calculate(Graph(),[],(EdgeCommand)99));
            Throws<ArgumentException>(()=>GraphEdgeCommands.Calculate(Graph(),["e","missing"],EdgeCommand.ClearBends));
            using var c=new CancellationTokenSource();c.Cancel();Throws<OperationCanceledException>(()=>GraphEdgeCommands.Calculate(Graph(),[],EdgeCommand.Reverse,cancellationToken:c.Token));
            using var later=new CancellationTokenSource();
            Throws<OperationCanceledException>(()=>GraphEdgeCommands.Calculate(Graph(),["e"],EdgeCommand.Reverse,
                new Policy((_,_)=>{later.Cancel();return ConnectionDecision.Accept;}),later.Token));
        });
        test("Edge commands: batch transaction preserves selection, camera and exact undo/redo", () => {
            var g=Graph();g=g with {Edges=g.Edges.Add(new("back","b","a"))};var d=Document(g);using var s=new EditorSession(d);
            s.Select(["e","back"]);var selected=s.Selection;var zoom=s.Viewport.Zoom;var origin=s.Viewport.Origin;var revision=d.Revision;
            s.EditEdges(EdgeCommand.Reverse);var reversed=d.Snapshot;Check(d.Revision==revision+1 && s.LastEdgeCommand!.ChangedEdges==2);
            Check(ReferenceEquals(selected,s.Selection)&&s.Viewport.Zoom==zoom&&s.Viewport.Origin==origin);
            d.Undo();Check(ReferenceEquals(g,d.Snapshot)&&!d.CanUndo);d.Redo();Check(ReferenceEquals(reversed,d.Snapshot));
        });
        test("Edge commands: a no-op clear preserves redo and skips edge-array copy", () => {
            var g=Graph();g=g with {Edges=[g.Edges[0] with {Bends=[]}]};var d=Document(g);using var s=new EditorSession(d);s.Select(["e"]);
            s.Rename("a","Changed");d.Undo();var revision=d.Revision;s.EditEdges(EdgeCommand.ClearBends);
            Check(d.CanRedo&&d.Revision==revision&&ReferenceEquals(g,d.Snapshot)&&s.LastEdgeCommand!.ExaminedEdges==0);
        });
        test("Edge commands: no-op scratch allocation does not scale with unrelated edges", () => {
            var small=Graph();small=small with {Edges=[small.Edges[0] with {Bends=[]}]};
            var large=small with {Edges=small.Edges.AddRange(Enumerable.Range(0,10000).Select(i=>new GraphEdge("extra"+i,"a","b")))};string[] ids=["e"];
            long Measure(GraphSnapshot graph){var start=GC.GetAllocatedBytesForCurrentThread();var r=GraphEdgeCommands.Calculate(graph,ids,EdgeCommand.ClearBends);
                var bytes=GC.GetAllocatedBytesForCurrentThread()-start;Check(ReferenceEquals(r.Graph,graph)&&r.ExaminedEdges==0);return bytes;}
            _=Measure(small);_=Measure(large);Check(Measure(large)<=Measure(small)+4096);
        });
        test("Edge commands: policy callbacks cannot overwrite a concurrent edit", () => {
            var d=Document();using var s=new EditorSession(d);s.Select(["e"]);var armed=true;var before=d.Snapshot;
            s.ConnectionPolicy=new Policy((_,_)=>{if(armed){armed=false;s.Rename("a","Concurrent");}return ConnectionDecision.Accept;});
            Throws<InvalidOperationException>(()=>s.EditEdges(EdgeCommand.Reverse));Check(d.Snapshot.Nodes[0].Text=="Concurrent"&&d.Snapshot.Edges==before.Edges);
        });
        test("Edge commands: changed selection or policy invalidates a pending command", () => {
            foreach(var changePolicy in new[]{false,true}) {
                var d=Document();using var s=new EditorSession(d);s.Select(["e"]);var before=d.Snapshot;
                s.ConnectionPolicy=new Policy((_,_)=>{if(changePolicy)s.ConnectionPolicy=new ConnectionPolicy();else s.Select(["a"]);return ConnectionDecision.Accept;});
                Throws<InvalidOperationException>(()=>s.EditEdges(EdgeCommand.Reverse));Check(ReferenceEquals(before,d.Snapshot)&&!d.CanUndo);
            }
        });
        test("Edge commands: post-publication edits invalidate only the last-result cache", () => {
            var d=Document();using var s=new EditorSession(d);s.Select(["e"]);var armed=true;
            d.Changed+=(_,_)=>{if(armed){armed=false;s.Rename("a","After");}};
            s.EditEdges(EdgeCommand.Reverse);Check(s.LastEdgeCommand is null && d.Snapshot.Edges[0].SourceId=="b"&&d.Snapshot.Nodes[0].Text=="After");
        });
        test("Reconnect review: invalid enum is rejected without treating it as a target", () => {
            var d=Document();using var s=new EditorSession(d);var before=d.Snapshot;
            Throws<ArgumentOutOfRangeException>(()=>s.Reconnect("e",(EdgeEnd)99,new("c")));Check(ReferenceEquals(before,d.Snapshot)&&!d.CanUndo);
        });
        test("Reconnect review: policy reentrant edit cannot overwrite current edge metadata", () => {
            var d=Document();using var s=new EditorSession(d);var before=d.Snapshot;
            s.ConnectionPolicy=new Policy((_,_)=>{s.Rename("e","New caption");return ConnectionDecision.Accept;});
            Throws<InvalidOperationException>(()=>s.Reconnect("e",EdgeEnd.Target,new("c")));
            Check(d.Snapshot.Edges[0].Text=="New caption"&&d.Snapshot.Edges[0].TargetId==before.Edges[0].TargetId);
        });
        test("Reconnect review: selection and policy changes reject stale endpoint requests", () => {
            foreach(var changePolicy in new[]{false,true}) {
                var d=Document();using var s=new EditorSession(d);s.Select(["e"]);var before=d.Snapshot;
                s.ConnectionPolicy=new Policy((_,_)=>{if(changePolicy)s.ConnectionPolicy=new ConnectionPolicy();else s.ClearSelection();return ConnectionDecision.Accept;});
                Throws<InvalidOperationException>(()=>s.Reconnect("e",EdgeEnd.Target,new("c")));Check(ReferenceEquals(before,d.Snapshot)&&!d.CanUndo);
            }
        });
    }
}
