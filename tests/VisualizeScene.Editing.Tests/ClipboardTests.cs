using System.Collections.Immutable;
using VisualizeScene.Core;
using VisualizeScene.Editing;

static class ClipboardTests
{
    static void Check(bool condition) { if (!condition) throw new Exception("Clipboard assertion failed."); }
    static void Throws<T>(Action action) where T : Exception { try { action(); } catch (T) { return; } throw new Exception("Expected " + typeof(T).Name); }
    static GraphSnapshot Fixture() => new([
        new("group", new(-10, -10, 300, 170), IsGroup: true), new("a", new(10, 20, 100, 50), "A", ParentId: "group"),
        new("b", new(350, 30, 90, 60), "B"), new("nested", new(150, 20, 120, 100), IsGroup: true, ParentId: "group"),
        new("child", new(160, 50, 70, 30), ParentId: "nested")],
        [new("ab", "a", "b", "Edge", Bends: [new(220, 130)], SourcePortId: "pa"), new("ac", "a", "child")],
        [new("pa", "a", new(1, .5))], [new("la", "a", "Label", new(2, 3)), new("lp", "pa", "Port label", new()), new("le", "ab", "Edge label", new(4, 5))]);
    static GraphDocument Document() { var d = new GraphDocument(); d.Replace(Fixture(), clearHistory: true); return d; }
    // Frozen pre-change reference, deliberately retaining its independent expansion algorithm.
    static GraphSnapshot Reference(GraphSnapshot graph, IEnumerable<string> selection)
    {
        var index = graph.Index(); var selected = selection.ToHashSet();
        foreach (var label in graph.Labels.Where(l => selected.Contains(l.Id)).ToArray()) selected.Add(label.OwnerId);
        var nodes = selected.Where(index.Nodes.ContainsKey).ToHashSet();
        foreach (var port in graph.Ports.Where(p => selected.Contains(p.Id))) nodes.Add(port.NodeId);
        foreach (var edge in graph.Edges.Where(e => selected.Contains(e.Id))) { nodes.Add(edge.SourceId); nodes.Add(edge.TargetId); }
        foreach (var id in nodes.ToArray()) nodes.UnionWith(index.Descendants(id).Select(n => n.Id));
        var edges = graph.Edges.Where(e => nodes.Contains(e.SourceId) && nodes.Contains(e.TargetId)).ToImmutableArray();
        var ports = graph.Ports.Where(p => nodes.Contains(p.NodeId)).ToImmutableArray();
        var owners = nodes.Concat(edges.Select(e => e.Id)).Concat(ports.Select(p => p.Id)).ToHashSet();
        return new(graph.Nodes.Where(n => nodes.Contains(n.Id)).Select(n => n with { ParentId = n.ParentId is not null && nodes.Contains(n.ParentId) ? n.ParentId : null }).ToImmutableArray(),
            edges, ports, graph.Labels.Where(l => owners.Contains(l.OwnerId)).ToImmutableArray());
    }
    static bool Same(GraphSnapshot a, GraphSnapshot b) => a.Nodes.SequenceEqual(b.Nodes) && a.Edges.SequenceEqual(b.Edges) && a.Ports.SequenceEqual(b.Ports) && a.Labels.SequenceEqual(b.Labels);
    public static void Run(Action<string, Action> test)
    {
        test("Clipboard: one-pass owner expansion matches the legacy oracle across shuffled mixed selections", () => {
            var random = new Random(931); var g = Fixture();
            var ids = g.Nodes.Select(n => n.Id).Concat(g.Edges.Select(e => e.Id)).Concat(g.Ports.Select(p => p.Id)).Concat(g.Labels.Select(l => l.Id)).Append("missing").ToArray();
            for (var i = 0; i < 200; i++) {
                var next = g with { Nodes = g.Nodes.OrderBy(_ => random.Next()).ToImmutableArray(), Edges = g.Edges.Reverse().ToImmutableArray() };
                var selected = Enumerable.Range(0, random.Next(20)).Select(_ => ids[random.Next(ids.Length)]).ToArray();
                var actual = GraphClipboard.Copy(next, selected); Check(Same(actual, Reference(next, selected))); GraphValidation.Validate(actual);
            }
        });
        test("Clipboard: selecting every nested group includes each descendant once without recursion", () => {
            var nodes = Enumerable.Range(0, 10000).Select(i => new GraphNode("n"+i, new(0, 0, 100, 60), ParentId: i == 0 ? null : "n"+(i-1), IsGroup: true)).Reverse().ToImmutableArray();
            var g = new GraphSnapshot(nodes, [], [], []); var result = GraphClipboard.Copy(g, nodes.Select(n => n.Id));
            Check(result.Nodes.Length == 10000 && Same(result, g) && ReferenceEquals(result.Nodes[0], nodes[0]));
        });
        test("Clipboard: selection is read once and cancellation cannot publish a partial copy", () => {
            var calls = 0; IEnumerable<string> Once() { if (++calls != 1) throw new Exception("Enumerated twice"); yield return "group"; yield return "nested"; }
            var g = Fixture(); Check(GraphClipboard.Copy(g, Once()).Nodes.Length == 4 && calls == 1);
            using var cts = new CancellationTokenSource(); IEnumerable<string> Cancel() { yield return "a"; cts.Cancel(); yield return "group"; }
            Throws<OperationCanceledException>(() => GraphClipboard.Copy(g, Cancel(), cts.Token)); Check(g.Nodes.Length == 5);
            Throws<OperationCanceledException>(() => GraphClipboard.Copy(g, [], cts.Token));
        });
        test("Clipboard: selected child detaches absent parent and related records keep exact metadata", () => {
            var g = Fixture(); var copied = GraphClipboard.Copy(g, ["le", "lp"]); Check(copied.Nodes.Length == 2 && copied.Nodes[0].ParentId is null);
            Check(copied.Edges[0] == g.Edges[0] && copied.Ports[0] == g.Ports[0] && copied.Labels.Length == 3);
        });
        test("Clipboard: invalid translated geometry is rejected before changing caller-owned edit", () => {
            var edit = new GraphEdit(Fixture()); var original = edit.Build();
            var g = new GraphSnapshot([new("one", new(0, 0, 20, 20)), new("huge", new(double.MaxValue, 0, 20, 20))], [], [], []);
            Throws<OverflowException>(() => GraphClipboard.Paste(edit, g, new(double.MaxValue, 0))); Check(ReferenceEquals(edit.Build(), original));
            Throws<ArgumentOutOfRangeException>(() => GraphClipboard.Paste(edit, Fixture(), new(double.NaN, 0))); Check(ReferenceEquals(edit.Build(), original));
        });
        test("Clipboard: pasted parent, port, label and bend identities remap with exact translation", () => {
            var g = Fixture(); var edit = new GraphEdit(GraphSnapshot.Empty); GraphClipboard.Paste(edit, g, new(-7.5, 12.25)); var next = edit.Build(); GraphValidation.Validate(next);
            Check(next.Nodes.Length == 5 && next.Nodes[1].ParentId == next.Nodes[0].Id && next.Nodes[4].ParentId == next.Nodes[3].Id);
            Check(next.Ports[0].NodeId == next.Nodes[1].Id && next.Edges[0].SourcePortId == next.Ports[0].Id && next.Labels[1].OwnerId == next.Ports[0].Id);
            Check(next.Edges[0].Bends[0] == g.Edges[0].Bends[0] + new PointD(-7.5, 12.25));
            Check(next.Nodes.Select(n => n.Id).All(id => !g.Index().Nodes.ContainsKey(id)) && next.Labels[0].Offset == g.Labels[0].Offset);
        });
        test("Clipboard: regular repeated pastes advance by 30 while exact undo and redo are retained", () => {
            var d = Document(); using var s = new EditorSession(d); var original = d.Snapshot; s.Select(["a"]); s.Copy(); s.Paste(); var first = d.Snapshot;
            Check(first.Nodes[^1].Bounds.X == 40 && first.Nodes[^1].Bounds.Y == 50); s.Paste(); var second = d.Snapshot;
            Check(second.Nodes[^1].Bounds.X == 70 && second.Nodes[^1].Bounds.Y == 80);
            d.Undo(); Check(ReferenceEquals(d.Snapshot, first)); d.Undo(); Check(ReferenceEquals(d.Snapshot, original)); d.Redo(); d.Redo(); Check(ReferenceEquals(d.Snapshot, second));
        });
        test("Clipboard: in-place and duplicate operations preserve the next regular paste offset", () => {
            var d = Document(); using var s = new EditorSession(d); s.Select(["a"]); s.Copy(); var version = s.ClipboardRevision;
            s.PasteInPlace(); Check(d.Snapshot.Nodes[^1].Bounds == Fixture().Nodes[1].Bounds); s.Select(["b"]); s.Duplicate();
            s.Paste(); Check(d.Snapshot.Nodes[^1].Text == "A" && d.Snapshot.Nodes[^1].Bounds.X == 40 && s.ClipboardRevision == version);
        });
        test("Clipboard: empty Copy, Duplicate and Paste are inert and empty Copy does not clear existing contents", () => {
            var d = Document(); using var s = new EditorSession(d); var before = d.Snapshot; s.Paste(); s.PasteInPlace(); s.Copy(); s.Duplicate(); Check(!s.CanPaste && ReferenceEquals(before, d.Snapshot));
            s.Select(["a"]); s.Copy(); s.Paste(); d.Undo(); s.ClearSelection(); var version = s.ClipboardRevision; s.Copy(); s.Duplicate();
            Check(s.ClipboardRevision == version && s.CanPaste && d.CanRedo && ReferenceEquals(d.Snapshot, before)); s.Paste(); Check(d.Snapshot.Nodes[^1].Bounds.X == 70);
        });
        test("Clipboard: copying again resets the implicit paste sequence and raises availability without a graph edit", () => {
            var d = Document(); using var s = new EditorSession(d); var changed = 0; s.ClipboardChanged += (_,_) => changed++;
            s.Select(["a"]); var rev = d.Revision; s.Copy(); Check(d.Revision == rev && s.CanPaste && changed == 1); s.Paste(); s.Select(["b"]); s.Copy(); s.Paste();
            Check(changed == 2 && d.Snapshot.Nodes[^1].Bounds.X == 380);
        });
        test("Clipboard: independently copied labels stay on the chosen owner across consecutive pastes", () => {
            var d = Document(); using var s = new EditorSession(d); s.Select(["la"]); s.Copy(); s.Select(["b"]); s.Paste();
            Check(d.Snapshot.Labels[^1].OwnerId == "b" && d.Snapshot.Labels[^1].Offset == new PointD(32, 33)); s.Paste();
            Check(d.Snapshot.Labels[^1].OwnerId == "b" && d.Snapshot.Labels[^1].Offset == new PointD(62, 63)); s.PasteInPlace();
            Check(d.Snapshot.Labels[^1].OwnerId == "b" && d.Snapshot.Labels[^1].Offset == new PointD(2, 3) && d.Snapshot.Nodes.Length == 5);
        });
        test("Clipboard: missing label owners disable Paste; a new owner re-enables it without consuming a failed offset", () => {
            var d = Document(); using var s = new EditorSession(d); s.Select(["la"]); s.Copy(); d.Edit("Remove owner", e => e.Remove("a"));
            Check(!s.CanPaste && s.HasClipboardContent); var before = d.Snapshot; Throws<InvalidOperationException>(() => s.Paste()); Check(ReferenceEquals(before, d.Snapshot));
            s.Select(["b"]); Check(s.CanPaste); s.Paste(); Check(d.Snapshot.Labels[^1].Offset == new PointD(32, 33));
        });
        test("Clipboard: Cut uses one captured subgraph and one history entry", () => {
            var d = Document(); using var s = new EditorSession(d); var before = d.Snapshot; s.Select(["group", "nested"]); s.Cut();
            Check(d.UndoName == "Cut selection" && d.Snapshot.Nodes.Length == 1 && s.Selection.Count == 0 && s.CanPaste); d.Undo(); Check(ReferenceEquals(before, d.Snapshot));
            s.PasteInPlace(); Check(d.Snapshot.Nodes.Length == 9);
        });
        test("Clipboard: reentrant selection during paste is not replaced with stale pasted IDs", () => {
            var d = Document(); using var s = new EditorSession(d); s.Select(["a"]); s.Copy();
            d.Changed += (_,e) => { if (e.Operation == "Paste") s.Select(["b"]); }; s.Paste(); Check(s.Selection.SetEquals(["b"]));
        });
        test("Clipboard: reentrant replacement during paste is not overwritten or followed by stale selection", () => {
            var d = Document(); using var s = new EditorSession(d); s.Select(["a"]); s.Copy();
            d.Changed += (_,e) => { if (e.Operation == "Paste") d.Replace(GraphSnapshot.Empty, clearHistory: true); }; s.Paste(); Check(d.Snapshot.Nodes.IsEmpty && s.Selection.Count == 0);
        });
        test("Clipboard: reentrant Cut subscribers retain their newer selection and copied contents", () => {
            var d = Document(); using var s = new EditorSession(d); s.Select(["a"]);
            d.Changed += (_,e) => { if (e.Operation == "Cut selection") { s.Select(["b"]); s.Copy(); } }; s.Cut(); Check(s.Selection.SetEquals(["b"]));
            s.Paste(); Check(d.Snapshot.Nodes[^1].Text == "B");
        });
        test("Clipboard: nested document-edit rejection restores clipboard state and paste count", () => {
            var d = Document(); using var s = new EditorSession(d); s.Select(["a"]); s.Copy(); s.Paste(); var before = d.Snapshot; s.Select(["b"]);
            Throws<InvalidOperationException>(() => d.Edit("Outer", _ => s.Cut())); Check(ReferenceEquals(before, d.Snapshot));
            Throws<InvalidOperationException>(() => d.Edit("Outer", _ => s.Paste())); Check(ReferenceEquals(before, d.Snapshot)); s.Paste();
            Check(d.Snapshot.Nodes[^1].Text == "A" && d.Snapshot.Nodes[^1].Bounds.X == 70);
        });
        test("Clipboard: a throwing post-commit subscriber does not erase a completed cut", () => {
            var d = Document(); using var s = new EditorSession(d); s.Select(["a"]);
            EventHandler<GraphChangedEventArgs> fail = (_,_) => throw new InvalidOperationException("Post-commit subscriber"); d.Changed += fail;
            Throws<InvalidOperationException>(() => s.Cut()); d.Changed -= fail; Check(!d.Snapshot.Index().Nodes.ContainsKey("a") && s.CanPaste); s.PasteInPlace(); Check(d.Snapshot.Nodes[^1].Text == "A");
        });
    }
}
