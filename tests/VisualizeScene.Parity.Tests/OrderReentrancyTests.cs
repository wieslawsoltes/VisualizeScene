using VisualizeScene.Core;
using VisualizeScene.Editing;

static class OrderReentrancyTests
{
    public static void Run(Action<string, Action> test)
    {
        test("Order: reentrant selection cannot retarget a prepared command", () =>
        {
            var graph = new GraphSnapshot([new("a", new(0, 0, 100, 60)), new("b", new(20, 0, 100, 60))], [], [], []);
            var document = new GraphDocument(); document.Replace(graph, clearHistory: true); using var session = new EditorSession(document); session.Select(["a"]);
            var invalidations = 0; session.Invalidated += (_, _) => { if (++invalidations == 1) session.Select(["b"]); };
            var rejected = false;
            try { session.ChangeDrawingOrder(DrawingOrder.BringToFront); } catch (InvalidOperationException) { rejected = true; }
            if (!rejected || !ReferenceEquals(document.Snapshot, graph) || document.CanUndo || !session.Selection.SetEquals(["b"]))
                throw new Exception("Reentrant selection must abort drawing order without changing the graph.");
        });
    }
}
