using System.Xml.Linq;
using VisualizeScene.Core;
using VisualizeScene.Editing;

var results = new List<(string Name, string? Error)>();
void Test(string name, Action body)
{
    try { body(); results.Add((name, null)); Console.WriteLine("PASS " + name); }
    catch (Exception ex) { results.Add((name, ex.ToString())); Console.WriteLine("FAIL " + name + ": " + ex); }
}
void Check(bool value) { if (!value) throw new Exception("Assertion failed"); }
void Near(double actual, double expected) { if (!double.IsFinite(actual) || Math.Abs(actual - expected) > 1e-7) throw new Exception($"Expected {expected}, got {actual}"); }
void Point(PointD actual, PointD expected) { Near(actual.X, expected.X); Near(actual.Y, expected.Y); }
void Throws(Action body) { try { body(); } catch (ArgumentException) { return; } throw new Exception("Expected invalid argument rejection"); }
Test("Viewport: transform preserves moving anchor", () => {
    var v = new Viewport(); v.Restore(new(new(-200, 70), 2)); var world = v.ToWorld(new(50, 60));
    v.Transform(new(50, 60), new(150, 100), 1.5); Point(v.ToScreen(world), new(150, 100)); Near(v.Zoom, 3);
});
Test("Viewport: clipping zoom still preserves the pointer anchor", () => {
    var v = new Viewport(); var world = v.ToWorld(new(300, 200));
    v.ZoomAt(new(300, 200), 100); Near(v.Zoom, 8); Point(v.ToScreen(world), new(300, 200));
    v.ZoomAt(new(300, 200), .00001); Near(v.Zoom, .05); Point(v.ToScreen(world), new(300, 200));
});
Test("Viewport: unchanged operations do not change revision", () => {
    var v = new Viewport(); v.Pan(new()); v.Restore(v.State); v.ZoomAt(new(50, 20), 1); Check(v.Revision == 0);
});
Test("Viewport: invalid operations cannot poison the view", () => {
    var v = new Viewport(); var before = v.State;
    foreach (var action in new Action[] { () => v.Pan(new(double.NaN, 0)), () => v.Restore(new(new(), 0)),
        () => v.ZoomAt(new(), double.PositiveInfinity), () => v.Center(new(), -1, 10),
        () => v.Fit(new(0, 0, 10, 10), 100, 100, -1), () => v.EnsureVisible(new(double.NaN, 0, 1, 1), 100, 100) }) Throws(action);
    Check(v.State == before && v.Revision == 0);
});
Test("Viewport: ensure visible uses minimal pan and preserves zoom", () => {
    var v = new Viewport(); Check(!v.EnsureVisible(new(30, 30, 50, 50), 200, 200, 20));
    Check(v.EnsureVisible(new(180, 180, 50, 50), 200, 200, 20)); Point(v.Origin, new(50, 50)); Near(v.Zoom, 1);
});
Test("Viewport: oversized target is centered and small view is ignored", () => {
    var v = new Viewport(); v.EnsureVisible(new(200, 200, 500, 500), 200, 200, 20); Point(v.Origin, new(350, 350));
    Check(!v.EnsureVisible(new(0, 0, 1, 1), 20, 20, 20));
});
Test("Touch: tap and jitter do not move the viewport", () => {
    var v = new Viewport(); var t = new TouchNavigator(v); t.Press(1, new(50, 50), 0); t.Move(1, new(52, 51));
    Check(t.Release(1, new(50, 50), 100).IsTap && v.Revision == 0 && t.ContactCount == 0);
});
Test("Touch: one-finger pan is measured in screen coordinates", () => {
    var v = new Viewport(); v.Restore(new(new(), 2)); var t = new TouchNavigator(v); t.Press(1, new(50, 50), 0);
    t.Move(1, new(90, 70)); Point(v.Origin, new(-20, -10)); Check(t.Phase == TouchNavigationPhase.Panning);
    Check(!t.Release(1, new(90, 70), 100).IsTap);
});
Test("Touch: final release position is applied even without a move event", () => {
    var v = new Viewport(); var t = new TouchNavigator(v); t.Press(1, new(10, 10), 0);
    Check(!t.Release(1, new(30, 40), 20).IsTap); Point(v.Origin, new(-20, -30));
});
Test("Touch: two-finger pinch preserves world centroid", () => {
    var v = new Viewport(); var t = new TouchNavigator(v); t.Press(1, new(100, 100), 0); t.Press(2, new(200, 100), 1);
    t.Move(1, new(50, 120)); t.Move(2, new(250, 120)); Near(v.Zoom, 2); Point(v.ToScreen(new(150, 100)), new(150, 120));
});
Test("Touch: pinch to one-finger transition does not jump", () => {
    var v = new Viewport(); var t = new TouchNavigator(v); t.Press(1, new(100, 100), 0); t.Press(2, new(200, 100), 1);
    t.Move(2, new(300, 100)); var before = v.State; t.Release(1, new(100, 100), 10); Check(v.State == before);
    t.Move(2, new(320, 100)); Near(v.Zoom, before.Zoom); Near(v.Origin.X, before.Origin.X - 20 / v.Zoom);
    Check(!t.Release(2, new(320, 100), 20).IsTap);
});
Test("Touch: third contact cannot steal the active pair", () => {
    var v = new Viewport(); var t = new TouchNavigator(v); t.Press(8, new(100, 100), 0); t.Press(9, new(200, 100), 1);
    t.Press(1, new(500, 500), 2); var before = v.State; t.Move(1, new(800, 800)); Check(v.State == before);
    t.Release(8, new(100, 100), 10); before = v.State; t.Move(9, new(200, 100)); Check(v.State == before);
});
Test("Touch: coincident points and crossing contacts stay finite", () => {
    var v = new Viewport(); var t = new TouchNavigator(v); t.Press(1, new(100, 100), 0); t.Press(2, new(100, 100), 1);
    t.Move(2, new(200, 100)); Near(v.Zoom, 1); t.Move(1, new(250, 100)); Check(v.Origin.IsFinite && double.IsFinite(v.Zoom));
});
Test("Touch: external viewport changes rebase instead of being overwritten", () => {
    var v = new Viewport(); var t = new TouchNavigator(v); t.Press(1, new(100, 100), 0); t.Move(1, new(140, 100));
    v.Restore(new(new(1000, 500), 4)); t.Move(1, new(160, 100)); Point(v.Origin, new(995, 500)); Near(v.Zoom, 4);
});
Test("Touch: edit-mode single contact does not navigate; pinch still does", () => {
    var v = new Viewport(); var t = new TouchNavigator(v) { SingleFingerPanning = false }; t.Press(1, new(100, 100), 0);
    t.Move(1, new(140, 100)); Check(v.Revision == 0 && t.HasMoved); t.Press(2, new(240, 100), 1);
    t.Move(2, new(340, 100)); Near(v.Zoom, 2); t.Release(2, new(340, 100), 10);
    var before = v.State; t.Move(1, new(160, 100)); Check(v.State != before);
});
Test("Touch: cancel retains view and ignores late pointer events", () => {
    var v = new Viewport(); var t = new TouchNavigator(v); t.Press(1, new(100, 100), 0); t.Move(1, new(140, 100));
    var before = v.State; t.Cancel(); Check(!t.Move(1, new(200, 100))); Check(!t.Release(1, new(), 10).Tracked); Check(v.State == before);
});
Test("Touch: duplicate IDs, unknown releases and contact count are bounded", () => {
    var t = new TouchNavigator(new()); Check(t.Press(1, new(), 0)); Check(!t.Press(1, new(), 0));
    Check(!t.Release(42, new(), 0).Tracked); for (uint i = 2; i <= 16; i++) Check(t.Press(i, new(), 0));
    Check(!t.Press(17, new(), 0) && t.ContactCount == 16);
});
Test("Touch: malformed coordinates are rejected before mutation", () => {
    var t = new TouchNavigator(new()); Throws(() => t.Press(1, new(double.NaN, 0), 0)); Check(t.ContactCount == 0);
    t.Press(1, new(), 0); Throws(() => t.Move(1, new(1e10, 0))); Check(t.ContactCount == 1);
});
Test("Touch: holds and backward timestamps never activate taps", () => {
    var t = new TouchNavigator(new()); t.Press(1, new(), 100); Check(!t.Release(1, new(), 90).IsTap);
    t.Press(2, new(), 100); Check(!t.Release(2, new(), 900).IsTap);
});
Test("Touch: out-and-back excursion is not a tap", () => {
    var v = new Viewport(); var t = new TouchNavigator(v); t.Press(1, new(), 0); t.Move(1, new(40, 10));
    Check(!t.Release(1, new(), 100).IsTap); Point(v.Origin, new());
});
Test("Touch: two contacts without movement never produce a tap", () => {
    var t = new TouchNavigator(new()); t.Press(1, new(100, 100), 0); t.Press(2, new(200, 100), 0);
    Check(!t.Release(1, new(100, 100), 10).IsTap && !t.Release(2, new(200, 100), 20).IsTap);
});
Test("Touch: reversing at the maximum zoom has no dead zone", () => {
    var v = new Viewport(); v.Restore(new(new(), 7)); var t = new TouchNavigator(v);
    t.Press(1, new(100, 100), 0); t.Press(2, new(200, 100), 1); t.Move(2, new(400, 100)); Near(v.Zoom, 8);
    t.Move(2, new(390, 100)); Check(v.Zoom < 8);
});
Test("Touch: reversing at the minimum zoom has no dead zone", () => {
    var v = new Viewport(); v.Restore(new(new(), .06)); var t = new TouchNavigator(v);
    t.Press(1, new(100, 100), 0); t.Press(2, new(300, 100), 1); t.Move(2, new(200, 100)); Near(v.Zoom, .05);
    t.Move(2, new(210, 100)); Check(v.Zoom > .05);
});
Test("Touch: an externally moved pending view cannot turn into a stale tap", () => {
    var v = new Viewport(); var t = new TouchNavigator(v); t.Press(1, new(100, 100), 0);
    v.Pan(new(500, 500)); Check(!t.Release(1, new(100, 100), 100).IsTap);
});
Test("Viewport: overflow during a transform cannot partially apply", () => {
    var v = new Viewport(); v.Restore(new(new(), .05)); var before = v.State; var revision = v.Revision;
    Throws(() => v.Pan(new(double.MaxValue, 0))); Check(v.State == before && v.Revision == revision);
});
ScrollTests.Run(Test);
KeyboardTests.Run(Test);
Directory.CreateDirectory("artifacts/validation");
File.WriteAllText("artifacts/validation/navigation.xml", new XElement("testsuite", new XAttribute("name", "Navigation"),
    new XAttribute("tests", results.Count), new XAttribute("failures", results.Count(r => r.Error is not null)),
    results.Select(r => new XElement("testcase", new XAttribute("name", r.Name), r.Error is null ? null : new XElement("failure", r.Error)))).ToString());
Console.WriteLine($"{results.Count(r => r.Error is null)}/{results.Count} passed");
return results.Any(r => r.Error is not null) ? 1 : 0;
