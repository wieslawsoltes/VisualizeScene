using System.Collections.Immutable;
using VisualizeScene.Core;
using VisualizeScene.Editing;

static class BatchPropertyTests
{
    static void Check(bool value) { if (!value) throw new Exception("Assertion failed"); }
    static void Throws<T>(Action a) where T : Exception { try { a(); } catch (T) { return; } throw new Exception("Expected " + typeof(T).Name); }
    static GraphSnapshot Graph() => new([new("a", new(0, 0, 100, 60), "Alpha", Fill: "#FFCC66"), new("b", new(200, 0, 150, 60), "Beta", Fill: "#DDEBFA")],
        [new("ab", "a", "b", "Edge", Weight: 7)], [new("p", "a", new(.5, .5))], [new("l", "a", "Label", new(0, 70))]);
    static GraphDocument Document(GraphSnapshot? snapshot = null) { var d = new GraphDocument(); d.Replace(snapshot ?? Graph(), clearHistory: true); return d; }
    public static void Run(Action<string, Action> test)
    {
        test("Batch: one pass reports common and mixed values with applicable counts", () => {
            var s = new GraphPropertySelection(Graph(), ["a", "b", "ab", "p", "l", "a"]);
            Check(s.ExaminedItems == 5 && s.ItemIds.Length == 5 && s.Text.Count == 4 && s.Text.IsMixed);
            Check(s.Fill.Count == 2 && s.Fill.IsMixed && s.Width.IsMixed && !s.Height.IsMixed && s.Height.Value == 60);
            Check(s.Weight.Count == 1 && s.Weight.Value == 7 && s.FontSize.Count == 1 && s.PortCount == 1);
        });
        test("Batch: sparse patches preserve every untouched field and exact undo", () => {
            var d = Document(); var before = d.Snapshot; var s = new GraphPropertySelection(before, ["a", "b", "ab", "l", "p"]);
            var r = s.Apply(d, new(Fill: "#DDEFE3")); Check(r.ChangedItems == 2 && d.Snapshot.Edges == before.Edges && d.Snapshot.Labels == before.Labels && d.Snapshot.Ports == before.Ports);
            Check(d.Snapshot.Nodes[0].Text == "Alpha" && d.Snapshot.Nodes[1].Text == "Beta" && d.Snapshot.Nodes[1].Bounds == before.Nodes[1].Bounds);
            d.Undo(); Check(ReferenceEquals(before, d.Snapshot) && !d.CanUndo); d.Redo(); Check(d.Snapshot.Nodes.All(n => n.Fill == "#DDEFE3"));
        });
        test("Batch: empty text, zero weight and false are explicit edits", () => {
            var d = Document(); var s = new GraphPropertySelection(d.Snapshot, ["a", "b", "ab", "l"]);
            s.Apply(d, new(Text: "", Weight: 0, Directed: false));
            Check(d.Snapshot.Nodes.All(n => n.Text == "") && d.Snapshot.Edges[0].Text == "" && d.Snapshot.Labels[0].Text == "");
            Check(d.Snapshot.Edges[0].Weight == 0 && !d.Snapshot.Edges[0].Directed);
        });
        test("Batch: locked-node failure rolls back preceding changes", () => {
            var g = Graph(); g = g with { Nodes = g.Nodes.SetItem(1, g.Nodes[1] with { Locked = true }) }; var d = Document(g);
            var s = new GraphPropertySelection(g, ["a", "b"]);
            Throws<InvalidOperationException>(() => s.Apply(d, new(Fill: "#000000", Width: 300, Locked: false)));
            Check(ReferenceEquals(d.Snapshot, g) && !d.CanUndo); s.Apply(d, new(Fill: "#000000")); Check(d.Snapshot.Nodes[1].Locked);
        });
        test("Batch: resizing group and child changes dimensions without translation", () => {
            var e = new GraphEdit(Graph()); var group = e.Group(["a", "b"]); var d = Document(e.Build()); var before = d.Snapshot;
            new GraphPropertySelection(before, [group, "a"]).Apply(d, new(Width: 400, Height: 150));
            var index = d.Snapshot.Index(); Check(index.Nodes["a"].Bounds.X == before.Index().Nodes["a"].Bounds.X && index.Nodes["a"].Bounds.Width == 400);
            Check(index.Nodes["b"] == before.Index().Nodes["b"] && d.Snapshot.Edges == before.Edges);
        });
        test("Batch: kind filters do not implicitly edit connected or descendant items", () => {
            var e = new GraphEdit(Graph()); var group = e.Group(["a"]); var d = Document(e.Build()); var before = d.Snapshot;
            var s = new GraphPropertySelection(before, [group, "a", "ab", "l"], SelectionKinds.Groups);
            Check(s.ItemIds.SequenceEqual([group])); s.Apply(d, new(Text: "Folder")); Check(d.Snapshot.Index().Nodes["a"].Text == "Alpha" && d.Snapshot.Edges == before.Edges);
        });
        test("Batch: stale snapshots and revision leases reject even after undo", () => {
            var d = Document(); var s = new GraphPropertySelection(d.Snapshot, ["a"]); var revision = d.Revision;
            d.Edit("Rename", e => e.Update(e.Node("b") with { Text = "Other" })); Throws<InvalidOperationException>(() => s.Apply(d, new(Text: "X")));
            d.Undo(); Throws<InvalidOperationException>(() => s.Apply(d, new(Text: "X"), revision)); Check(d.Snapshot.Nodes[0].Text == "Alpha");
        });
        test("Batch: invalid fields and overflow leave the document unchanged", () => {
            var d = Document(); var before = d.Snapshot; var s = new GraphPropertySelection(before, ["a", "b", "ab", "l"]);
            foreach (var patch in new[] { new GraphPropertyPatch(Fill: "bad"), new(Stroke: "#GGGGGG"), new(Shape: (NodeShape)999), new(Routing: (EdgeRouting)99) })
                Throws<ArgumentException>(() => s.Apply(d, patch));
            foreach (var patch in new[] { new GraphPropertyPatch(Width: 0), new(Height: double.NaN), new(Weight: double.PositiveInfinity), new(FontSize: 513) })
                Throws<ArgumentOutOfRangeException>(() => s.Apply(d, patch));
            Check(ReferenceEquals(d.Snapshot, before) && !d.CanUndo);
        });
        test("Batch: no-op, empty and inapplicable patches preserve snapshot identity", () => {
            var d = Document(); var before = d.Snapshot; var s = new GraphPropertySelection(before, ["a", "b"]);
            Check(s.Apply(d, new()).ChangedItems == 0); Check(s.Apply(d, new(Height: 60)).ChangedItems == 0);
            Check(s.Apply(d, new(FontSize: 30)).ChangedItems == 0); Check(new GraphPropertySelection(before, []).Apply(d, new(Text: "X")).ChangedItems == 0);
            Check(ReferenceEquals(before, d.Snapshot) && !d.CanUndo);
        });
        test("Batch: cancellation and invalid selection input cannot publish edits", () => {
            var d = Document(); using var c = new CancellationTokenSource(); c.Cancel();
            Throws<OperationCanceledException>(() => new GraphPropertySelection(d.Snapshot, [], cancellationToken: c.Token));
            var s = new GraphPropertySelection(d.Snapshot, ["a"]); Throws<OperationCanceledException>(() => s.Apply(d, new(Text: "X"), cancellationToken: c.Token));
            Throws<ArgumentException>(() => new GraphPropertySelection(d.Snapshot, ["absent"]));
            Throws<ArgumentOutOfRangeException>(() => new GraphPropertySelection(d.Snapshot, ["a"], (SelectionKinds)128)); Check(!d.CanUndo);
        });
        test("Batch: 10000-item mixed summary examines unique selection exactly once", () => {
            var nodes = Enumerable.Range(0, 10000).Select(i => new GraphNode("n" + i, new(0, i * 100, 100, 60), "Node " + i)).ToImmutableArray();
            var g = new GraphSnapshot(nodes, [], [], []); var ids = nodes.Select(n => n.Id).ToList(); var s = new GraphPropertySelection(g, ids.Concat(ids)); ids.Clear();
            Check(s.ExaminedItems == 10000 && s.NodeCount == 10000 && s.Text.IsMixed && !s.Fill.IsMixed && s.ItemIds.Length == 10000);
            var d = Document(g); Check(s.Apply(d, new(Stroke: "#000000")).ChangedItems == 10000); d.Undo(); Check(ReferenceEquals(g, d.Snapshot));
        });
        test("Batch: captured summaries support concurrent readers without mutable registry exposure", () => {
            var s = new GraphPropertySelection(Graph(), ["a", "b"]);
            Parallel.For(0, 64, _ => Check(s.Text.IsMixed && !s.Height.IsMixed && s.ItemIds.Length == 2));
        });
    }
}
