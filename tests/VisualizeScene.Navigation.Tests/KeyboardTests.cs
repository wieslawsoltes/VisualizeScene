using System.Collections.Immutable;
using VisualizeScene.Core;
using VisualizeScene.Editing;

static class KeyboardTests
{
    static void Check(bool condition) { if (!condition) throw new Exception("Assertion failed"); }
    static GraphDocument Document()
    {
        var d = new GraphDocument();
        d.Replace(new([new("a", new(0, 0, 100, 60)), new("b", new(300, 0, 100, 60)), new("c", new(300, 200, 100, 60))],
            [new("ab", "a", "b")], [new("ap", "a", new(1, .5))], [new("la", "a", "Annotation", new(0, 100))]), clearHistory: true); return d;
    }
    public static void Run(Action<string, Action> test)
    {
        test("Keyboard: cardinal directions navigate rather than edit graph", () => {
            var d = Document(); var before = d.Snapshot; using var s = new EditorSession(d); s.Select(["a"]);
            Check(s.Navigate(NavigationDirection.Right) && s.FocusedItemId == "b");
            Check(s.Navigate(NavigationDirection.Down) && s.FocusedItemId == "c");
            Check(s.Navigate(NavigationDirection.Up) && s.FocusedItemId == "b");
            Check(s.Navigate(NavigationDirection.Left) && s.FocusedItemId == "a"); Check(ReferenceEquals(before, d.Snapshot) && !d.CanUndo);
        });
        test("Keyboard: shift extension advances its focus while retaining selection", () => {
            using var s = new EditorSession(Document()); s.Select(["a"]); s.Navigate(NavigationDirection.Right, true); s.Navigate(NavigationDirection.Down, true);
            Check(s.Selection.SetEquals(["a", "b", "c"]) && s.FocusedItemId == "c");
        });
        test("Keyboard: absent directional target preserves focus and selection", () => {
            using var s = new EditorSession(Document()); s.Select(["a"]); Check(!s.Navigate(NavigationDirection.Left)); Check(s.FocusedItemId == "a" && s.Selection.SetEquals(["a"]));
        });
        test("Keyboard: focus can change independently from selection", () => {
            using var s = new EditorSession(Document()); s.Select(["a"]); s.FocusItem("b"); Check(s.Selection.SetEquals(["a"]));
            Check(!s.FocusItem("unknown") && s.FocusedItemId == "b"); s.FocusItem(null); Check(s.FocusedItemId is null);
        });
        test("Keyboard: document deletion clears orphaned focus", () => {
            var d = Document(); using var s = new EditorSession(d); s.FocusItem("la"); d.Edit("Remove", e => e.Remove("a")); Check(s.FocusedItemId is null);
        });
        test("Keyboard: folding maps child label focus to visible group", () => {
            var d = Document(); using var s = new EditorSession(d); string g = ""; d.Edit("Group", e => g = e.Group(["a"])); s.FocusItem("la");
            d.Edit("Fold", e => e.Update(e.Node(g) with { Collapsed = true })); Check(s.FocusedItemId == g);
        });
        test("Keyboard: reading-order boundary traversal is deterministic", () => {
            using var s = new EditorSession(Document()); s.NavigateBoundary(); Check(s.FocusedItemId == "a"); s.NavigateBoundary(true); Check(s.FocusedItemId == "c");
        });
        test("Keyboard: item masks allow measured label navigation", () => {
            using var s = new EditorSession(Document()) { NavigationKinds = SelectionKinds.Labels, LabelSizeProvider = _ => new(100, 20) };
            Check(s.NavigateBoundary() && s.FocusedItemId == "la" && s.FocusedItemBounds!.Value.Width == 100);
            s.Viewport.ZoomAt(new(), .1); Check(!s.NavigateBoundary());
        });
        test("Keyboard: label metrics changes invalidate cached bounds", () => {
            using var s = new EditorSession(Document()) { NavigationKinds = SelectionKinds.Labels }; s.NavigateBoundary();
            s.LabelSizeProvider = _ => new(222, 30); Check(s.FocusedItemBounds!.Value.Width == 222);
        });
        test("Keyboard: explicit focus bounds include ports, edges and group headers", () => {
            using var s = new EditorSession(Document()); Check(s.GetItemBounds("ap")!.Value.Width == 7); Check(s.GetItemBounds("ab")!.Value.Width > 100);
            Check(s.GetItemBounds("missing") is null);
        });
        test("Keyboard: reentrant deletion during selection cannot leave stale focus", () => {
            var d = Document(); using var s = new EditorSession(d); s.Select(["a"]);
            s.SelectionChanged += (_, _) => { if (s.Selection.Contains("b")) d.Edit("Remove", e => e.Remove("b")); };
            Check(!s.Navigate(NavigationDirection.Right)); Check(s.FocusedItemId is null);
        });
        test("Keyboard: invalid direction or masks do not mutate selection", () => {
            using var s = new EditorSession(Document()); s.Select(["a"]); var caught = 0;
            try { s.Navigate((NavigationDirection)99); } catch (ArgumentOutOfRangeException) { caught++; }
            try { s.NavigationKinds = (SelectionKinds)128; } catch (ArgumentOutOfRangeException) { caught++; }
            Check(caught == 2 && s.Selection.SetEquals(["a"]));
        });
        test("Navigation index: beam alignment beats a closer diagonal", () => {
            var index = new GraphNavigationIndex([new("a", new(0, 0, 100, 60)), new("aligned", new(500, 0, 100, 60)), new("diagonal", new(120, 100, 100, 60))]);
            Check(index.Next(NavigationDirection.Right, "a", new()) == "aligned");
        });
        test("Navigation index: stable ID breaks geometric ties", () => {
            NavigationTarget[] targets = [new("origin", new(0, 0, 100, 60)), new("z", new(300, 0, 100, 60)), new("a", new(300, 0, 100, 60))];
            Check(new GraphNavigationIndex(targets).Next(NavigationDirection.Right, "origin", new()) == "a");
            Check(new GraphNavigationIndex(targets.Reverse()).Next(NavigationDirection.Right, "origin", new()) == "a");
        });
        test("Navigation index: empty and overlapping centers terminate", () => {
            Check(new GraphNavigationIndex([]).Boundary() is null);
            var index = new GraphNavigationIndex([new("a", new(0, 0, 100, 60)), new("b", new(0, 0, 100, 60))]);
            Check(index.Next(NavigationDirection.Right, "a", new()) is null);
        });
        test("Navigation index: input is copied, validated and immutable", () => {
            var items = new List<NavigationTarget> { new("a", new(0, 0, 10, 10)) }; var index = new GraphNavigationIndex(items); items.Clear(); Check(index.Boundary() == "a");
            var caught = 0; try { _ = new GraphNavigationIndex([new("a", new()), new("a", new())]); } catch (ArgumentException) { caught++; }
            try { _ = new GraphNavigationIndex([new("a", new(double.NaN, 0, 10, 10))]); } catch (ArgumentException) { caught++; }
            Check(caught == 2);
        });
        test("Navigation index: 10000 nodes navigate to adjacent row and column", () => {
            var index = new GraphNavigationIndex(Enumerable.Range(0, 10000).Select(i => new NavigationTarget("n" + i, new(i % 100 * 200, i / 100 * 200, 100, 60))));
            Check(index.Next(NavigationDirection.Right, "n0", new()) == "n1"); Check(index.Next(NavigationDirection.Down, "n0", new()) == "n100");
        });
    }
}
