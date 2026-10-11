using System.Collections.Immutable;
using System.Globalization;
using System.Xml.Linq;
using VisualizeScene.Core;
using VisualizeScene.Editing;

var tests = new List<(string Name, string? Error)>();
void Test(string name, Action action) { try { action(); tests.Add((name, null)); Console.WriteLine("PASS " + name); } catch (Exception e) { tests.Add((name, e.ToString())); Console.WriteLine("FAIL " + name + "\n" + e); } }
void Check(bool condition) { if (!condition) throw new Exception("Assertion failed"); }
void Throws<T>(Action action) where T : Exception { try { action(); } catch (T) { return; } throw new Exception("Expected " + typeof(T).Name); }
GraphSnapshot Graph() => new([new("a", new(0, 0, 100, 60), "Alpha alpha alphabet"), new("b", new(200, 0, 100, 60), "Beta")],
    [new("ab", "a", "b", "Alpha route")], [new("ap", "a", new(1, .5))], [new("la", "a", "Alpha annotation", new(0, 90))]);
GraphDocument Document(GraphSnapshot? graph = null) { var d = new GraphDocument(); d.Replace(graph ?? Graph(), clearHistory: true); return d; }
Test("Literal search spans nodes, edges and annotations in stable order", () => {
    var result = new GraphSearchIndex(Graph()).Find("alpha"); Check(result.Matches.Length == 5 && result.Matches[0].Start == 0 && result.Matches[1].Start == 6 && result.Matches[^1].ItemId == "la");
});
Test("Case and whole-word matching filter occurrences", () => {
    var index = new GraphSearchIndex(Graph()); Check(index.Find("Alpha", new(MatchCase: true)).Matches.Length == 3); Check(index.Find("alpha", new(WholeWord: true)).Matches.Length == 4);
});
Test("Whole words respect combining marks and supplementary letters", () => {
    var g = Graph(); g = g with { Nodes = [g.Nodes[0] with { Text = "e e\u0301 _e e_ 𐐀e e𐐀 e!" }] , Edges = [], Ports = [], Labels = [] };
    Check(new GraphSearchIndex(g).Find("e", new(WholeWord: true)).Matches.Length == 2);
});
Test("Literal search does not split UTF-16 surrogate pairs", () => {
    var g = Graph() with { Nodes = [new("a", new(0, 0, 100, 60), "😀😀")], Edges = [], Ports = [], Labels = [] };
    var index = new GraphSearchIndex(g); Check(index.Find("😀").Matches.Length == 2); Check(index.Find("\ud83d").Matches.IsEmpty); Check(index.Find("\ude00").Matches.IsEmpty);
});
Test("Ordinal case matching is independent of current culture", () => {
    var prior = CultureInfo.CurrentCulture;
    try { CultureInfo.CurrentCulture = new("tr-TR"); Check(new GraphSearchIndex(Graph()).Find("ALPHA").Matches.Length == 5); }
    finally { CultureInfo.CurrentCulture = prior; }
});
Test("ID matches are read-only and include ports", () => {
    var d = Document(); var r = new GraphSearchIndex(d.Snapshot).Find("ap", new(Fields: GraphSearchFields.Id));
    Check(r.Matches.Length == 1 && r.Matches[0].ItemId == "ap" && !r.Matches[0].CanReplace); var x = r.Replace(d, "changed"); Check(x.SkippedReadOnly == 1 && !d.CanUndo);
});
Test("Kind filters distinguish groups, nodes and labels", () => {
    var g = Graph(); var edit = new GraphEdit(g); var id = edit.Group(["a"]); edit.Update(edit.Node(id) with { Text = "Alpha group" });
    var index = new GraphSearchIndex(edit.Build()); Check(index.Find("alpha", new(Kinds: SelectionKinds.Groups)).Matches.Single().ItemId == id);
    Check(index.Find("alpha", new(Kinds: SelectionKinds.Labels)).Matches.Single().ItemId == "la");
});
Test("Hidden owners are omitted unless explicitly included", () => {
    var edit = new GraphEdit(Graph()); var group = edit.Group(["a"]); edit.Update(edit.Node(group) with { Collapsed = true }); var index = new GraphSearchIndex(edit.Build());
    Check(index.Find("alpha").Matches.Length == 1); var all = index.Find("alpha", new(IncludeHidden: true)); Check(all.Matches.Length == 5 && all.Matches.Count(m => !m.IsVisible) == 4);
});
Test("Locked ancestors make dependent text read-only", () => {
    var edit = new GraphEdit(Graph()); var group = edit.Group(["a"]); edit.Update(edit.Node(group) with { Locked = true }); var d = Document(edit.Build());
    var result = new GraphSearchIndex(d.Snapshot).Find("alpha"); Check(result.Matches.All(m => !m.CanReplace)); Check(result.Replace(d, "Z").SkippedReadOnly == 5 && !d.CanUndo);
});
Test("Scope null means all; empty means none; duplicates do not duplicate results", () => {
    var i = new GraphSearchIndex(Graph()); Check(i.Find("alpha", scope: []).Matches.IsEmpty); Check(i.Find("alpha", scope: ["la", "la", "missing"]).Matches.Length == 1);
});
Test("Results cap distinguishes exact limit from truncated search", () => {
    var i = new GraphSearchIndex(Graph()); Check(!i.Find("Beta", new(MaximumResults: 1)).IsTruncated);
    var r = i.Find("alpha", new(MaximumResults: 2)); Check(r.IsTruncated && r.Matches.Length == 2);
});
Test("Truncated Replace All rejects before mutation but one result remains usable", () => {
    var d = Document(); var r = new GraphSearchIndex(d.Snapshot).Find("alpha", new(MaximumResults: 2)); var before = d.Snapshot;
    Throws<InvalidOperationException>(() => r.Replace(d, "Z")); Check(ReferenceEquals(before, d.Snapshot)); Check(r.Replace(d, "Z", 0).ChangedItems == 1);
});
Test("Replace All is one transaction with exact undo and redo", () => {
    var d = Document(); var before = d.Snapshot; var r = new GraphSearchIndex(before).Find("alpha", new(WholeWord: true));
    var replaced = r.Replace(d, "Z"); Check(replaced.ChangedItems == 3 && replaced.ReplacedOccurrences == 4); Check(d.Snapshot.Nodes[0].Text == "Z Z alphabet");
    var after = d.Snapshot; d.Undo(); Check(ReferenceEquals(before, d.Snapshot) && !d.CanUndo); d.Redo(); Check(ReferenceEquals(after, d.Snapshot));
});
Test("Replacement text is literal, including dollar signs and backslashes", () => {
    var d = Document(); new GraphSearchIndex(d.Snapshot).Find("Beta").Replace(d, "$1\\n"); Check(d.Snapshot.Nodes[1].Text == "$1\\n");
});
Test("Single replacement uses the chosen occurrence offsets", () => {
    var d = Document(); new GraphSearchIndex(d.Snapshot).Find("alpha").Replace(d, "X", 1); Check(d.Snapshot.Nodes[0].Text == "Alpha X alphabet");
});
Test("Empty replacement deletes matches and nonoverlapping semantics are stable", () => {
    var d = Document(Graph() with { Nodes = [new("a", new(0, 0, 100, 60), "aaaaa")], Edges = [], Ports = [], Labels = [] });
    var r = new GraphSearchIndex(d.Snapshot).Find("aa"); Check(r.Matches.Length == 2); r.Replace(d, ""); Check(d.Snapshot.Nodes[0].Text == "a");
});
Test("Same-value replacement creates no history", () => {
    var d = Document(); var before = d.Snapshot; var r = new GraphSearchIndex(before).Find("Beta"); var n = r.Replace(d, "Beta");
    Check(n.ChangedItems == 0 && n.ReplacedOccurrences == 0 && ReferenceEquals(before, d.Snapshot));
});
Test("Stale snapshot and revision leases reject replacement", () => {
    var d = Document(); var revision = d.Revision; var before = d.Snapshot; var r = new GraphSearchIndex(before).Find("Alpha");
    d.Edit("changed", e => e.Update(e.Node("b") with { Text = "new" })); Throws<InvalidOperationException>(() => r.Replace(d, "X"));
    d.Undo(); Throws<InvalidOperationException>(() => r.Replace(d, "X", expectedRevision: revision)); Check(ReferenceEquals(before, d.Snapshot));
});
Test("Search and replacement cancellation leave the graph unchanged", () => {
    var d = Document(); var r = new GraphSearchIndex(d.Snapshot); using var c = new CancellationTokenSource(); c.Cancel();
    Throws<OperationCanceledException>(() => r.Find("alpha", cancellationToken: c.Token)); var result = r.Find("alpha");
    Throws<OperationCanceledException>(() => result.Replace(d, "X", cancellationToken: c.Token)); Check(!d.CanUndo);
});
Test("Invalid options, indices and budgets are diagnosed", () => {
    var d = Document(); var i = new GraphSearchIndex(d.Snapshot);
    Throws<ArgumentOutOfRangeException>(() => i.Find("x", new(Kinds: (SelectionKinds)128)));
    Throws<ArgumentOutOfRangeException>(() => i.Find("x", new(Fields: (GraphSearchFields)8)));
    Throws<ArgumentOutOfRangeException>(() => i.Find("x", new(MaximumResults: 0)));
    Throws<InvalidOperationException>(() => i.Find("x", new(MaximumCharacters: 1)));
    Throws<ArgumentOutOfRangeException>(() => i.Find("Beta").Replace(d, "x", -1));
    Throws<ArgumentOutOfRangeException>(() => i.Find("Beta").Replace(d, "x", 1)); Check(!d.CanUndo);
});
Test("Replacement expansion budget rejects the entire transaction", () => {
    var d = Document(Graph() with { Nodes = [new("a", new(0, 0, 100, 60), new string('a', 100))], Edges = [], Ports = [], Labels = [] });
    var r = new GraphSearchIndex(d.Snapshot).Find("a"); Throws<InvalidOperationException>(() => r.Replace(d, new string('b', 1024 * 1024))); Check(!d.CanUndo);
});
Test("Empty query is inert and empty graph is searchable", () => {
    Check(new GraphSearchIndex(Graph()).Find("").Matches.IsEmpty); Check(new GraphSearchIndex(GraphSnapshot.Empty).Find("a").Matches.IsEmpty);
});
Test("Independent searches on one immutable registry are thread-safe", () => {
    var index = new GraphSearchIndex(Graph()); Parallel.For(0, 50, n => Check(index.Find("alpha").Matches.Length == 5));
});
Test("Ten thousand text fields have bounded scan and result counts", () => {
    var g = new GraphSnapshot(Enumerable.Range(0, 10000).Select(n => new GraphNode("n" + n, new(n * 200, 0, 100, 60), "Node " + n)).ToImmutableArray(), [], [], []);
    var index = new GraphSearchIndex(g); var r = index.Find("9999"); Check(r.Matches.Length == 1 && r.ExaminedItems == 10000 && r.ExaminedCharacters < 100000);
});
ColdIndexTests.Run(Test);
Directory.CreateDirectory("artifacts/validation");
File.WriteAllText("artifacts/validation/search.xml", new XElement("testsuite", new XAttribute("name", "Search and cold index"),
    new XAttribute("tests", tests.Count), new XAttribute("failures", tests.Count(t => t.Error is not null)), tests.Select(t =>
    new XElement("testcase", new XAttribute("name", t.Name), t.Error is null ? null : new XElement("failure", t.Error)))).ToString());
Console.WriteLine($"{tests.Count(t => t.Error is null)}/{tests.Count} passed"); return tests.Any(t => t.Error is not null) ? 1 : 0;
