using System.Collections.Immutable;
using VisualizeScene.Core;
using VisualizeScene.Editing;

static class GroupFoldingTests
{
    static void Check(bool condition) { if (!condition) throw new Exception("Group folding assertion failed"); }
    static void Throws<T>(Action action) where T : Exception { try { action(); } catch (T) { return; } throw new Exception("Expected " + typeof(T).Name); }
    static GraphSnapshot Graph() => new([
        new("g", new(-100,-100,700,700), IsGroup:true),
        new("h", new(0,0,400,350), IsGroup:true, ParentId:"g"),
        new("a", new(100,120,100,60), ParentId:"h"),
        new("k", new(800,0,350,330), IsGroup:true, Collapsed:true, Locked:true),
        new("b", new(860,100,100,60), ParentId:"k"),
        new("z", new(600,600,100,60))], [new("e","a","z",Bends:[new(700,500)])],
        [new("p","a",new(1,.5))], [new("l","a","Note",new(0,90))]);
    public static void Run(Action<string,Action> test)
    {
        test("Group folding: nested mixed groups toggle from the captured visibility state in one node pass", () => {
            var g=Graph(); var r=GraphGroupFolding.Calculate(g,["g","h","k"],GroupFoldOperation.Toggle);
            Check(r.EligibleGroups==3 && r.ChangedGroups==3 && r.ExaminedNodes==g.Nodes.Length);
            Check(r.Graph.Index().Nodes["g"].Collapsed && r.Graph.Index().Nodes["h"].Collapsed && !r.Graph.Index().Nodes["k"].Collapsed);
            foreach(var n in g.Nodes) Check((r.Graph.Index().Nodes[n.Id] with {Collapsed=n.Collapsed})==n);
            Check(r.Graph.Edges==g.Edges && r.Graph.Ports==g.Ports && r.Graph.Labels==g.Labels);
        });
        test("Group folding: duplicate targets toggle once and non-group items remain unchanged", () => {
            var g=Graph();var r=GraphGroupFolding.Calculate(g,["g","g","a","e","p","l"],GroupFoldOperation.Toggle);
            Check(r.EligibleGroups==1 && r.ChangedGroups==1 && r.Graph.Nodes[0].Collapsed && r.Graph.Nodes.Skip(1).SequenceEqual(g.Nodes.Skip(1)));
        });
        test("Group folding: explicit operations are idempotent and preserve snapshot identity", () => {
            var g=Graph();var collapsed=GraphGroupFolding.Calculate(g,["g","h","k"],GroupFoldOperation.Collapse);
            Check(collapsed.ChangedGroups==2);
            Check(ReferenceEquals(GraphGroupFolding.Calculate(collapsed.Graph,["g","h","k"],GroupFoldOperation.Collapse).Graph,collapsed.Graph));
            var expanded=GraphGroupFolding.Calculate(g,["g","h","k"],GroupFoldOperation.Expand);
            Check(expanded.ChangedGroups==1 && !expanded.Graph.Index().Nodes["k"].Collapsed);
            Check(ReferenceEquals(GraphGroupFolding.Calculate(expanded.Graph,["g","h","k"],GroupFoldOperation.Expand).Graph,expanded.Graph));
        });
        test("Group folding: expanding a selected parent never changes a hidden unselected child flag", () => {
            var g=GraphGroupFolding.Calculate(Graph(),["g","h"],GroupFoldOperation.Collapse).Graph;
            var r=GraphGroupFolding.Calculate(g,["g","h"],GroupFoldOperation.Expand);
            Check(r.EligibleGroups==1 && !r.Graph.Index().Nodes["g"].Collapsed && r.Graph.Index().Nodes["h"].Collapsed);
        });
        test("Group folding: position locks and empty groups do not prohibit hierarchy expansion", () => {
            var g=new GraphSnapshot([new("empty",new(0,0,100,60),IsGroup:true,Locked:true)],[],[],[]);
            var r=GraphGroupFolding.Calculate(g,["empty"],GroupFoldOperation.Collapse);
            Check(r.ChangedGroups==1 && r.Graph.Nodes[0].Collapsed && r.Graph.Nodes[0].Bounds==g.Nodes[0].Bounds);
        });
        test("Group folding: session publishes one revision and one exact undo/redo for multiple groups", () => {
            var g=Graph();var d=new GraphDocument();d.Replace(g,clearHistory:true);using var s=new EditorSession(d);s.Select(["g","h","k"]);s.FocusItem("h");
            var changes=0;d.Changed+=(_,_)=>changes++;var revision=d.Revision;var camera=s.Viewport.State;
            s.FoldGroups(GroupFoldOperation.Toggle);var folded=d.Snapshot;
            Check(d.Revision==revision+1 && changes==1 && s.LastGroupFolding?.ChangedGroups==3 && s.Viewport.State==camera);
            Check(s.Selection.SetEquals(["g","k"]) && s.FocusedItemId=="g");
            Check(folded.Index().VisibleRepresentative("a")=="g" && folded.Index().IsVisible("b"));
            d.Undo();Check(ReferenceEquals(d.Snapshot,g) && !d.CanUndo);d.Redo();Check(ReferenceEquals(d.Snapshot,folded));
        });
        test("Group folding: no-op expansion preserves an existing redo entry", () => {
            var d=new GraphDocument();d.Replace(Graph(),clearHistory:true);using var s=new EditorSession(d);s.Select(["g"]);
            s.FoldGroups(GroupFoldOperation.Collapse);var after=d.Snapshot;d.Undo();var revision=d.Revision;
            s.FoldGroups(GroupFoldOperation.Expand);Check(d.Revision==revision && d.CanRedo && s.LastGroupFolding?.ChangedGroups==0);
            d.Redo();Check(ReferenceEquals(d.Snapshot,after));
        });
        test("Group folding: invalid operations, IDs and null inputs cannot partially change a snapshot", () => {
            var g=Graph();Throws<ArgumentOutOfRangeException>(()=>GraphGroupFolding.Calculate(g,[],(GroupFoldOperation)99));
            Throws<ArgumentException>(()=>GraphGroupFolding.Calculate(g,["g","missing"],GroupFoldOperation.Collapse));
            Throws<ArgumentNullException>(()=>GraphGroupFolding.Calculate(g,null!,GroupFoldOperation.Collapse));
            Throws<ArgumentNullException>(()=>GraphGroupFolding.Calculate(g,[null!],GroupFoldOperation.Collapse));
            Check(!g.Nodes[0].Collapsed);
        });
        test("Group folding: cancellation during target enumeration is atomic", () => {
            var g=Graph();using var source=new CancellationTokenSource();
            IEnumerable<string> Items(){yield return "g";source.Cancel();yield return "h";}
            Throws<OperationCanceledException>(()=>GraphGroupFolding.Calculate(g,Items(),GroupFoldOperation.Collapse,source.Token));
            Check(!g.Nodes[0].Collapsed && !g.Nodes[1].Collapsed);
        });
        test("Group folding: availability reports explicit operations and reuses lock-position semantics", () => {
            var g=Graph();var s=GraphArrangeState.Calculate(g,["g","h","k","a"]);
            Check(s.ExpandableGroups==1 && s.CollapsibleGroups==2 && s.FittableGroups==2);
            Check(GraphArrangeState.Calculate(g,["a"]).ExpandableGroups==0);
        });
        test("Group folding: an already-unfolded local selection avoids graph-sized scratch allocations", () => {
            var small=Graph();var large=small with {Nodes=small.Nodes.AddRange(Enumerable.Range(0,10000).Select(i=>new GraphNode("extra"+i,new(i*150,1000,100,60))))};
            string[] ids=["g"];
            long Measure(GraphSnapshot g){var start=GC.GetAllocatedBytesForCurrentThread();var r=GraphGroupFolding.Calculate(g,ids,GroupFoldOperation.Expand);var size=GC.GetAllocatedBytesForCurrentThread()-start;Check(ReferenceEquals(r.Graph,g)&&r.ExaminedNodes==0);return size;}
            _=Measure(small);_=Measure(large);Check(Measure(large)<=Measure(small)+4096);
        });
        test("Group folding: post-publication reentrant edits are retained and not reported as the old result", () => {
            var d=new GraphDocument();d.Replace(Graph(),clearHistory:true);using var s=new EditorSession(d);s.Select(["g"]);var armed=true;
            d.Changed+=(_,_)=>{if(armed){armed=false;d.Edit("Follow-up",e=>e.Update(e.Node("z") with {Text="Latest"}));}};
            s.FoldGroups(GroupFoldOperation.Collapse);
            Check(d.Snapshot.Nodes[0].Collapsed && d.Snapshot.Index().Nodes["z"].Text=="Latest" && s.LastGroupFolding is null);
        });
    }
}
