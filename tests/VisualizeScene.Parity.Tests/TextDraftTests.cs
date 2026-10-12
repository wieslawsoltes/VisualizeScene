using VisualizeScene.Core;
using VisualizeScene.Editing;

static class TextDraftTests
{
    static void Check(bool v) { if (!v) throw new Exception("Assertion failed"); }
    public static void Run(Action<string, Action> test)
    {
        test("Text draft: mixed placeholder is untouched until explicitly cleared", () => {
            var draft = new GraphTextPropertyDraft(new(2, true, "Alpha"));
            Check(draft.Text == "" && draft.Replacement is null);
            draft.Clear(); draft.SetText(""); draft.SetText("");
            Check(draft.Replacement == "");
        });
        test("Text draft: explicit clear survives duplicate notifications after nonempty staging", () => {
            var draft = new GraphTextPropertyDraft(new(2, true, "Alpha"));
            draft.SetText("replacement"); draft.Clear(); draft.SetText("");
            Check(draft.Replacement == ""); draft.Reset(); draft.SetText("");
            Check(draft.Replacement is null);
        });
        test("Text draft: common values, reverted input and repeated input retain no-op semantics", () => {
            var draft = new GraphTextPropertyDraft(new(2, false, "Same"));
            draft.SetText("Same"); Check(draft.Replacement is null);
            draft.SetText("Other"); Check(draft.Replacement == "Other");
            draft.SetText("Same"); Check(draft.Replacement is null);
            draft.Clear(); draft.SetText(""); Check(draft.Replacement == "");
            var empty = new GraphTextPropertyDraft(new(2, false, "")); empty.Clear(); Check(empty.Replacement is null);
        });
        test("Text draft: clearing different labels applies one exact undo transaction", () => {
            var d = new GraphDocument(); d.Replace(new([new("a", new(0,0,100,60), "Alpha"),new("b", new(200,0,100,60), "Beta")],[],[],[]),clearHistory:true);
            var original = d.Snapshot; var selected = new GraphPropertySelection(original, ["a", "b"]);
            var draft = new GraphTextPropertyDraft(selected.Text); draft.Clear(); draft.SetText("");
            Check(selected.Apply(d, new(Text: draft.Replacement)).ChangedItems == 2);
            Check(d.Snapshot.Nodes.All(n => n.Text == "")); d.Undo(); Check(ReferenceEquals(d.Snapshot, original) && !d.CanUndo);
        });
    }
}
