using System.Collections.Immutable;
using VisualizeScene.Core;
using VisualizeScene.Editing;
using VisualizeScene.Serialization;

static class GroupSizingTests
{
    static void Check(bool value) { if (!value) throw new Exception("Group sizing assertion failed"); }
    static void Throws<T>(Action action) where T : Exception { try { action(); } catch (T) { return; } throw new Exception("Expected " + typeof(T).Name); }
    static PointD Measure(GraphLabel label) => new(40, 20);
    static GroupSizingOptions NodesOnly => new(IncludeLabels: false);
    static GraphSnapshot Graph() => new([
        new("g", new(-100, -100, 900, 800), "Group", IsGroup: true),
        new("a", new(100, 100, 100, 60), ParentId: "g"), new("b", new(300, 200, 80, 50), ParentId: "g"),
        new("outside", new(2000, 2000, 100, 60))],
        [new("e", "a", "b", "Edge", Bends: [new(9000, 9000)])],
        [new("p", "b", new(1, .5))], [new("la", "a", "Note", new(0, 200)), new("lp", "p", "Port note", new(0, 100))]);

    // Deliberately simple oracle: rebuild the immutable index once per group and enumerate all its visible descendants.
    static GraphSnapshot Reference(GraphSnapshot graph, string[] ids, GroupSizingOptions options)
    {
        var originalIndex = graph.Index();
        foreach (var id in ids.Distinct().Where(id => originalIndex.Nodes.TryGetValue(id, out var n) && GraphGroupSizing.CanFit(originalIndex, n))
                     .OrderByDescending(originalIndex.Depth))
        {
            var index = graph.Index(); var descendants = index.Descendants(id).Where(n => index.IsVisible(n.Id)).ToArray();
            var bounds = descendants.Select(n => n.Bounds).ToList(); var owned = descendants.Select(n => n.Id).ToHashSet();
            if (options.IncludeLabels)
                foreach (var label in graph.Labels)
                {
                    var owner = index.Ports.TryGetValue(label.OwnerId, out var port) ? port.NodeId : label.OwnerId;
                    if (owned.Contains(owner)) bounds.Add(GraphScene.LabelBounds(index, label, Measure(label)));
                }
            var b = RectD.Enclose(bounds);
            b = new(b.X - options.Padding, b.Y - options.Padding - options.HeaderHeight,
                Math.Max(options.MinimumWidth, b.Width + 2 * options.Padding), Math.Max(options.MinimumHeight, b.Height + 2 * options.Padding + options.HeaderHeight));
            if (options.EnlargeOnly) b = RectD.Union(b, index.Nodes[id].Bounds);
            var edit = new GraphEdit(graph); edit.Update(index.Nodes[id] with { Bounds = b }); graph = edit.Build();
        }
        return graph;
    }
    public static void Run(Action<string, Action> test)
    {
        test("Group sizing: fitting changes only the frame, never child positions or dependent records", () => {
            var g = Graph(); var r = GraphGroupSizing.Calculate(g, ["g"], NodesOnly);
            Check(r.Graph.Nodes[0].Bounds == new RectD(72, 52, 336, 226) && r.ResizedGroups == 1);
            Check(r.Graph.Nodes.Skip(1).SequenceEqual(g.Nodes.Skip(1)) && r.Graph.Edges == g.Edges && r.Graph.Ports == g.Ports && r.Graph.Labels == g.Labels);
            Check(r.ExaminedNodes == 3 && r.ExaminedLabels == 0);
        });
        test("Group sizing: measured node and port labels enlarge their containing frame once", () => {
            var g = Graph(); var calls = 0;
            var r = GraphGroupSizing.Calculate(g, ["g"], measure: label => { calls++; return Measure(label); });
            Check(r.Graph.Nodes[0].Bounds == new RectD(72, 52, 336, 316));
            Check(calls == 2 && r.MeasuredLabels == 2 && r.ExaminedLabels == 2);
        });
        test("Group sizing: selected nested groups fit bottom-up independently of selection order", () => {
            var g = new GraphSnapshot([new("g", new(-500,-500,2000,2000), IsGroup:true),
                new("h",new(0,0,500,500),ParentId:"g",IsGroup:true),new("a",new(100,100,100,60),ParentId:"h")],[],[],[]);
            var a = GraphGroupSizing.Calculate(g,["g","h"],NodesOnly); var b = GraphGroupSizing.Calculate(g,["h","g","h"],NodesOnly);
            Check(GraphIO.ToJson(a.Graph)==GraphIO.ToJson(b.Graph));
            Check(a.Graph.Nodes[1].Bounds==new RectD(72,52,156,136) && a.Graph.Nodes[0].Bounds==new RectD(44,4,212,212));
            Check(a.ExaminedNodes==3 && a.ResizedGroups==2 && a.MaximumStackDepth==3);
        });
        test("Group sizing: annotation anchors use a nested group's new frame, not its former size", () => {
            var g = new GraphSnapshot([new("g",new(-500,-500,2000,2000),IsGroup:true),new("h",new(0,0,500,500),ParentId:"g",IsGroup:true),
                new("a",new(100,100,100,60),ParentId:"h")],[],[new("p","h",new(1,1))],[new("l","p","Port",new(100,100))]);
            var r=GraphGroupSizing.Calculate(g,["g","h"],measure:Measure);
            Check(GraphIO.ToJson(r.Graph)==GraphIO.ToJson(Reference(g,["g","h"],new())) && r.MeasuredLabels==1);
        });
        test("Group sizing: unselected group frames remain unchanged and descendants outside them are still enclosed", () => {
            var g=new GraphSnapshot([new("g",new(0,0,1000,1000),IsGroup:true),new("h",new(10,10,30,30),ParentId:"g",IsGroup:true),
                new("a",new(500,500,100,60),ParentId:"h")],[],[],[]);
            var r=GraphGroupSizing.Calculate(g,["g"],NodesOnly);
            Check(r.Graph.Nodes[1]==g.Nodes[1] && r.Graph.Nodes[2]==g.Nodes[2]);
            Check(Geometry.RectangleContainsRectangle(r.Graph.Nodes[0].Bounds,g.Nodes[2].Bounds));
        });
        test("Group sizing: collapsed groups contribute only their visible frame and annotations", () => {
            var g=new GraphSnapshot([new("g",new(0,0,1000,1000),IsGroup:true),new("h",new(100,100,80,60),ParentId:"g",IsGroup:true,Collapsed:true),
                new("a",new(1e6,1e6,100,60),ParentId:"h")],[],[],[new("l","a","Hidden",new(0,10))]);
            var r=GraphGroupSizing.Calculate(g,["g","h"],measure:_=>throw new Exception("Measured a hidden label"));
            Check(r.EligibleGroups==1 && r.ExaminedNodes==2 && r.Graph.Nodes[0].Bounds==new RectD(72,52,136,136));
        });
        test("Group sizing: locked frames never resize but their unlocked selected descendants can", () => {
            var g=new GraphSnapshot([new("g",new(0,0,1000,1000),IsGroup:true,Locked:true),new("h",new(100,100,500,500),ParentId:"g",IsGroup:true),
                new("a",new(200,200,100,60),ParentId:"h")],[],[],[]);
            var r=GraphGroupSizing.Calculate(g,["g","h"],NodesOnly);
            Check(r.Graph.Nodes[0]==g.Nodes[0] && r.ResizedGroups==1 && r.EligibleGroups==1 && r.ExaminedNodes==2);
        });
        test("Group sizing: empty, locked, leaf and non-node selections are inert", () => {
            var g=Graph();g=g with {Nodes=g.Nodes.Add(new("empty",new(0,0,100,60),IsGroup:true))};
            var r=GraphGroupSizing.Calculate(g,["empty","a","e","p","la"]);
            Check(ReferenceEquals(r.Graph,g) && r.EligibleGroups==0 && r.ExaminedNodes==0 && r.ExaminedLabels==0);
        });
        test("Group sizing: its own annotation and unrelated labels never feed the frame back into itself", () => {
            var g=Graph() with {Labels=[new("own","g","Self",new(1e5,1e5)),new("outside-label","outside","Other",new())]};
            var r=GraphGroupSizing.Calculate(g,["g"],measure:_=>throw new Exception("Unexpected measurement"));
            Check(r.Graph.Nodes[0].Bounds==new RectD(72,52,336,226) && r.MeasuredLabels==0);
        });
        test("Group sizing: minimum dimensions, custom margins and enlarge-only respect their contract", () => {
            var g=Graph();var r=GraphGroupSizing.Calculate(g,["g"],new(5,10,500,400,false));
            Check(r.Graph.Nodes[0].Bounds==new RectD(95,85,500,400));
            Check(ReferenceEquals(GraphGroupSizing.Calculate(g,["g"],new(IncludeLabels:false,EnlargeOnly:true)).Graph,g));
        });
        test("Group sizing: repeated fitting preserves exact snapshot identity and undo history", () => {
            var g=Graph();var d=new GraphDocument();d.Replace(g,clearHistory:true);using var s=new EditorSession(d);s.Select(["g"]);
            var selection=s.Selection;s.FitGroups(NodesOnly);var fitted=d.Snapshot;var revision=d.Revision;
            s.FitGroups(NodesOnly);Check(ReferenceEquals(fitted,d.Snapshot) && d.Revision==revision && ReferenceEquals(selection,s.Selection));
            d.Undo();Check(ReferenceEquals(g,d.Snapshot) && !d.CanUndo);d.Redo();Check(ReferenceEquals(fitted,d.Snapshot));
        });
        test("Group sizing: invalid options, IDs and metrics reject before publishing changes", () => {
            var g=Graph();Throws<ArgumentException>(()=>GraphGroupSizing.Calculate(g,["g","missing"]));
            foreach(var value in new[]{-1d,double.NaN,double.PositiveInfinity}) Throws<ArgumentOutOfRangeException>(()=>GraphGroupSizing.Calculate(g,[],new(Padding:value)));
            Throws<ArgumentOutOfRangeException>(()=>GraphGroupSizing.Calculate(g,[],new(MinimumWidth:0)));
            Throws<ArgumentException>(()=>GraphGroupSizing.Calculate(g,["g"],measure:_=>new(double.NaN,10)));
        });
        test("Group sizing: cancellation before and during measurement publishes no partial operation", () => {
            var g=Graph();using var c=new CancellationTokenSource();c.Cancel();
            Throws<OperationCanceledException>(()=>GraphGroupSizing.Calculate(g,[],cancellationToken:c.Token));
            using var during=new CancellationTokenSource();
            Throws<OperationCanceledException>(()=>GraphGroupSizing.Calculate(g,["g"],measure:l=>{during.Cancel();return Measure(l);},cancellationToken:during.Token));
        });
        test("Group sizing: overflow in annotations or union extents is rejected atomically", () => {
            var g=Graph() with {Labels=[new("l","a","Overflow",new(1.7e308,1.7e308))]};
            Throws<ArgumentException>(()=>GraphGroupSizing.Calculate(g,["g"],measure:_=>new(1.7e308,1.7e308)));
            g=new([new("g",new(0,0,100,60),IsGroup:true),new("a",new(-1e308,0,100,60),ParentId:"g"),new("b",new(1e308,0,100,60),ParentId:"g")],[],[],[]);
            Throws<ArgumentException>(()=>GraphGroupSizing.Calculate(g,["g"],NodesOnly));
        });
        test("Group sizing: reentrant selection changes during font measurement cannot commit a stale fit", () => {
            var d=new GraphDocument();d.Replace(Graph(),clearHistory:true);using var s=new EditorSession(d);s.Select(["g"]);var armed=false;
            s.LabelSizeProvider=l=>{if(armed){armed=false;s.Select(["a"]);}return Measure(l);};armed=true;
            var before=d.Snapshot;var revision=d.Revision;Throws<InvalidOperationException>(()=>s.FitGroups());
            Check(ReferenceEquals(before,d.Snapshot)&&revision==d.Revision&&!d.CanUndo&&s.Selection.Contains("a"));
        });
        test("Group sizing: arrangement availability counts eligible frames without measuring graph contents", () => {
            var g=Graph();var state=GraphArrangeState.Calculate(g,["g","a","e"]);
            Check(state.FittableGroups==1 && state.MovableRoots==1);
            g=g with {Nodes=g.Nodes.SetItem(0,g.Nodes[0] with {Collapsed=true})};Check(GraphArrangeState.Calculate(g,["g","a"]).FittableGroups==0);
        });
        test("Group sizing: shared traversal agrees with a repeated-index oracle across nested fixtures", () => {
            var random=new Random(673);
            for(var trial=0;trial<40;trial++) {
                var ns=ImmutableArray.CreateBuilder<GraphNode>();ns.Add(new("g",new(-200,-200,2000,2000),IsGroup:true));
                for(var i=0;i<5;i++){ns.Add(new("h"+i,new(i*200,0,300,400),ParentId:"g",IsGroup:true,Locked:i==4));
                    for(var j=0;j<3;j++)ns.Add(new("n"+i+"_"+j,new(random.Next(-300,500),random.Next(-200,700),80,50),ParentId:"h"+i));}
                var g=new GraphSnapshot(ns.ToImmutable(),[],[],[new("label","n0_0","Text",new(10,100))]);
                string[] ids=["g","h0","h1","h4"];var options=new GroupSizingOptions(EnlargeOnly:trial%2==0);
                var r=GraphGroupSizing.Calculate(g,ids,options,Measure);
                Check(GraphIO.ToJson(r.Graph)==GraphIO.ToJson(Reference(g,ids,options)) && r.ExaminedNodes==g.Nodes.Length && r.MeasuredLabels==1);
            }
        });
        test("Group sizing: measuring callbacks cannot overwrite a concurrent document edit", () => {
            var d=new GraphDocument();d.Replace(Graph(),clearHistory:true);using var s=new EditorSession(d);s.Select(["g"]);var armed=false;
            s.LabelSizeProvider=l=>{if(armed){armed=false;d.Edit("Concurrent",e=>e.Update(e.Node("outside") with {Text="Changed"}));}return Measure(l);};
            armed=true;var revision=d.Revision;Throws<InvalidOperationException>(()=>s.FitGroups());
            Check(d.Revision==revision+1 && d.Snapshot.Index().Nodes["outside"].Text=="Changed" && d.Snapshot.Nodes[0].Bounds==Graph().Nodes[0].Bounds);
        });
        test("Group sizing: shared nested traversal allocates less than repeatedly rebuilding every group index", () => {
            var nodes=Enumerable.Range(0,200).Select(i=>new GraphNode("g"+i,new(0,0,1e6,1e6),ParentId:i==0?null:"g"+(i-1),IsGroup:true)).Append(new GraphNode("leaf",new(0,0,100,60),ParentId:"g199")).ToImmutableArray();
            var g=new GraphSnapshot(nodes,[],[],[]);var ids=nodes.Where(n=>n.IsGroup).Select(n=>n.Id).ToArray();
            _=Reference(g,ids,NodesOnly);_=GraphGroupSizing.Calculate(g,ids,NodesOnly);
            var start=GC.GetAllocatedBytesForCurrentThread();var expected=Reference(g,ids,NodesOnly);var old=GC.GetAllocatedBytesForCurrentThread()-start;
            start=GC.GetAllocatedBytesForCurrentThread();var actual=GraphGroupSizing.Calculate(g,ids,NodesOnly);var current=GC.GetAllocatedBytesForCurrentThread()-start;
            Check(GraphIO.ToJson(actual.Graph)==GraphIO.ToJson(expected) && current<old/2);
        });
        test("Group sizing: 5000 selected nested frames are iterative and visit each visible node only once", () => {
            var nodes=Enumerable.Range(0,5000).Select(i=>new GraphNode("g"+i,new(0,0,1e6,1e6),ParentId:i==0?null:"g"+(i-1),IsGroup:true)).Append(new GraphNode("leaf",new(0,0,100,60),ParentId:"g4999")).ToImmutableArray();
            var g=new GraphSnapshot(nodes,[],[],[new("l","leaf","One measurement",new(0,80))]);var calls=0;
            var r=GraphGroupSizing.Calculate(g,nodes.Select(n=>n.Id),measure:l=>{calls++;return Measure(l);});
            Check(r.EligibleGroups==5000 && r.ResizedGroups==5000 && r.ExaminedNodes==5001 && r.MaximumStackDepth==5001 && calls==1);
        });
    }
}
