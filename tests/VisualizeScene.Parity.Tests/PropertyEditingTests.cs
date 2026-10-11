using VisualizeScene.Core;
using VisualizeScene.Editing;

static class PropertyEditingTests
{
    private static void Check(bool value) { if (!value) throw new Exception("Assertion failed"); }
    private static void Throws<T>(Action action) where T : Exception { try { action(); } catch (T) { return; } throw new Exception("Expected " + typeof(T).Name); }
    private static GraphDocument Document()
    {
        var document = new GraphDocument(); document.Replace(new([new("n", new(1e15, 1e15, 130, 60))], [],
            [new("p", "n", new(.5, .5))], [new("l", "n", "Text", new(0, 30))]), clearHistory: true); return document;
    }
    public static void Run(Action<string, Action> test)
    {
        test("Properties: relative port precision does not depend on world-coordinate magnitude", () => {
            var d = Document(); using var s = new EditorSession(d); var relative = new PointD(.123456789012345, .765432109876543);
            s.SetPortLocation("p", relative); Check(d.Snapshot.Ports[0].RelativeLocation == relative); d.Undo(); Check(d.Snapshot.Ports[0].RelativeLocation == new PointD(.5, .5));
        });
        test("Properties: immovable ports and invalid relatives are rejected atomically", () => {
            var d = Document(); using var s = new EditorSession(d);
            Throws<ArgumentOutOfRangeException>(() => s.SetPortLocation("p", new(-.1, .5))); Check(!d.CanUndo);
            s.ConnectionPolicy = new ConnectionPolicy { Ports = new Dictionary<string, PortConstraint> { ["p"] = new(Movable: false) } };
            Throws<InvalidOperationException>(() => s.SetPortLocation("p", new(.1, .5))); Check(!d.CanUndo);
        });
        test("Properties: label fields update together and preserve one-step undo", () => {
            var d = Document(); var original = d.Snapshot; using var s = new EditorSession(d);
            s.SetLabelProperties("l", "Changed", new(11.25, -15.125), 23); Check(d.Snapshot.Labels[0].Text == "Changed" && d.Snapshot.Labels[0].FontSize == 23);
            d.Undo(); Check(ReferenceEquals(d.Snapshot, original) && !d.CanUndo);
        });
        test("Properties: label offset editing respects owner position locks", () => {
            var d = Document(); d.Edit("Lock", edit => edit.Update(edit.Node("n") with { Locked = true })); var original = d.Snapshot;
            using var s = new EditorSession(d); Throws<InvalidOperationException>(() => s.SetLabelProperties("l", "Changed", new(0, 50), 12));
            Check(ReferenceEquals(d.Snapshot, original)); s.SetLabelProperties("l", "Renamed", new(0, 30), 12); Check(d.Snapshot.Labels[0].Text == "Renamed");
        });
        test("Properties: invalid label changes preserve all original fields", () => {
            var d = Document(); var original = d.Snapshot; using var s = new EditorSession(d);
            Throws<ArgumentException>(() => s.SetLabelProperties("l", "Changed", new(0, 40), 0));
            Check(ReferenceEquals(d.Snapshot, original) && !d.CanUndo);
        });
        test("Save point: undo and redo can return to clean and dirty snapshots", () => {
            var d = Document(); var saved = new GraphSavePoint(d); Check(!saved.IsDirty);
            d.Edit("Rename", edit => edit.Update(edit.Node("n") with { Text = "New" })); Check(saved.IsDirty);
            d.Undo(); Check(!saved.IsDirty); d.Redo(); Check(saved.IsDirty); saved.MarkSaved(); Check(!saved.IsDirty);
            d.Undo(); Check(saved.IsDirty); d.Redo(); Check(!saved.IsDirty);
        });
        test("Save point: a completed older asynchronous save does not mark current edits clean", () => {
            var d = Document(); var saved = new GraphSavePoint(d); var writing = d.Snapshot;
            d.Edit("Rename", edit => edit.Update(edit.Node("n") with { Text = "New" })); saved.MarkSaved(writing); Check(saved.IsDirty);
            d.Undo(); Check(!saved.IsDirty);
        });
        test("Save point: equal no-op edits retain the saved identity", () => {
            var d = Document(); var saved = new GraphSavePoint(d); d.Edit("No-op", edit => edit.Update(edit.Node("n") with { })); Check(!saved.IsDirty);
            Throws<ArgumentNullException>(() => new GraphSavePoint(null!));
        });
    }
}
