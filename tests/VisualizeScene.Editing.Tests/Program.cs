using System.Xml.Linq;
using VisualizeScene.Core;
using VisualizeScene.Editing;

var results = new List<(string Name, string? Error)>();
void Check(bool condition) { if (!condition) throw new Exception("Assertion failed"); }
void Test(string name, Action body)
{
    try { body(); results.Add((name, null)); Console.WriteLine("PASS " + name); }
    catch (Exception ex) { results.Add((name, ex.ToString())); Console.WriteLine("FAIL " + name + ": " + ex); }
}
HitResult? Click(DoubleClickRecognizer r, long time, string id = "n", EditorMode mode = EditorMode.Select,
    InputModifiers keys = InputModifiers.None, long revision = 1, double x = 0)
{
    r.Press(new(HitKind.Node, id), mode, keys, new(x, 0), revision, time);
    return r.Release(new(x, 0), revision, time + 20);
}
Test("Two completed clicks activate exactly once", () =>
{
    var r = new DoubleClickRecognizer(); Check(Click(r, 0) is null); Check(Click(r, 100)?.Id == "n"); Check(Click(r, 200) is null);
});
Test("Create-edge contact cannot activate select-mode double click", () =>
{
    var r = new DoubleClickRecognizer(); Check(Click(r, 0, mode: EditorMode.CreateEdge) is null); Check(Click(r, 100) is null);
});
Test("Explicit mode reset breaks click sequence", () =>
{
    var r = new DoubleClickRecognizer(); Click(r, 0); r.Reset(); Check(Click(r, 100) is null);
});
Test("Different items do not form double click", () =>
{
    var r = new DoubleClickRecognizer(); Click(r, 0, "a"); Check(Click(r, 100, "b") is null);
});
Test("Revision change breaks double click", () =>
{
    var r = new DoubleClickRecognizer(); Click(r, 0); Check(Click(r, 100, revision: 2) is null);
});
Test("Modifier change preserves multi-select instead of label dialog", () =>
{
    var r = new DoubleClickRecognizer(); Click(r, 0); Check(Click(r, 100, keys: InputModifiers.Control) is null);
});
Test("Long delay or distance does not activate", () =>
{
    var r = new DoubleClickRecognizer(); Click(r, 0); Check(Click(r, 1000) is null); Check(Click(r, 1100, x: 10) is null);
});
Test("Out-and-back pointer excursion remains a drag", () =>
{
    var r = new DoubleClickRecognizer(); Click(r, 0); r.Press(new(HitKind.Node, "n"), EditorMode.Select, InputModifiers.None, new(), 1, 100);
    r.Move(new(50, 0)); r.Move(new()); Check(r.Release(new(), 1, 120) is null); Check(Click(r, 140) is null);
});
Test("Selection handles do not activate label editor", () =>
{
    var r = new DoubleClickRecognizer(); Click(r, 0); r.Press(new(HitKind.ResizeHandle, "n", 2), EditorMode.Select, InputModifiers.None, new(), 1, 100);
    Check(r.Release(new(), 1, 110) is null); Check(Click(r, 120) is null);
});
Test("Hold gesture and backward timestamp are rejected", () =>
{
    var r = new DoubleClickRecognizer(); r.Press(new(HitKind.Node, "n"), EditorMode.Select, InputModifiers.None, new(), 1, 100);
    Check(r.Release(new(), 1, 900) is null); Click(r, 1000); Check(Click(r, 0) is null);
});
Test("Stale drag is canceled without overwrite", () =>
{
    var d = new GraphDocument(); d.Edit("node", e => e.Add(new GraphNode("n", new(0, 0, 100, 50))));
    using var s = new EditorSession(d); s.PointerDown(new(50, 25)); s.PointerMove(new(100, 25));
    d.Edit("rename", e => e.Update(e.Node("n") with { Text = "remote" })); s.PointerUp(new(150, 25));
    Check(d.Snapshot.Nodes[0].Bounds.X == 0 && d.Snapshot.Nodes[0].Text == "remote");
});

ConnectionTests.Run(Test);
LabelTests.Run(Test);

Directory.CreateDirectory("artifacts/validation");
var failures = results.Count(r => r.Error is not null);
File.WriteAllText("artifacts/validation/editing.xml", new XElement("testsuite", new XAttribute("name", "Editing regressions"),
    new XAttribute("tests", results.Count), new XAttribute("failures", failures), results.Select(r => new XElement("testcase",
        new XAttribute("name", r.Name), r.Error is null ? null : new XElement("failure", r.Error)))).ToString());
Console.WriteLine($"{results.Count - failures}/{results.Count} passed");
return failures == 0 ? 0 : 1;
