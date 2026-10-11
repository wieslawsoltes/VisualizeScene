using System.Collections.Immutable;
using System.Diagnostics;
using System.Text.Json;
using System.Xml.Linq;
using SkiaSharp;
using VisualizeScene.Core;
using VisualizeScene.Editing;
using VisualizeScene.Rendering.Skia;

var results = new List<(string Name, string? Error)>();
void Test(string name, Action action) { try { action(); results.Add((name, null)); Console.WriteLine("PASS " + name); } catch (Exception e) { results.Add((name, e.ToString())); Console.WriteLine("FAIL " + name + "\n" + e); } }
void Check(bool condition) { if (!condition) throw new Exception("Assertion failed"); }
void Near(double a, double b) { if (!double.IsFinite(a) || Math.Abs(a - b) > 1e-7) throw new Exception($"Expected {b}, got {a}"); }
void Throws<T>(Action a) where T : Exception { try { a(); } catch (T) { return; } throw new Exception("Expected " + typeof(T).Name); }
GraphSnapshot Small() => new([new("a", new(0, 0, 100, 60)), new("b", new(200, 100, 100, 60))], [new("ab", "a", "b")], [], []);
GraphSnapshot Tree() => new([new("b", new(0, 0, 100, 60), ParentId: "g"), new("g", new(0, 0, 300, 300), IsGroup: true), new("h", new(100, 100, 100, 100), ParentId: "g", IsGroup: true), new("c", new(100, 100, 50, 50), ParentId: "h"), new("z", new(500, 0, 100, 60))], [], [], []);
using var surface = SKSurface.Create(new SKImageInfo(440, 320))!;
byte[] Draw(GraphOverviewRenderer renderer, GraphSnapshot graph, ViewportState? camera = null, double width = 220, double height = 160)
{
    surface.Canvas.ResetMatrix(); renderer.Render(surface.Canvas, graph, GraphScene.Bounds(graph), width, height, camera ?? new(new(), 1), 800, 400);
    using var image = surface.Snapshot(); using var bitmap = SKBitmap.FromImage(image); return bitmap.Bytes;
}
Test("Overview caches the scene while its viewport marker moves", () => {
    using var renderer = new GraphOverviewRenderer(); var graph = Small(); Draw(renderer, graph); var before = renderer.Statistics;
    for (var i = 1; i <= 20; i++) Draw(renderer, graph, new(new(i, i * 2), 1));
    Check(renderer.Statistics.SceneRecordings == before.SceneRecordings && renderer.Statistics.Frames == before.Frames + 20);
});
Test("Overview replay equals a fresh cache at each camera", () => {
    using var cached = new GraphOverviewRenderer(); var graph = Small();
    foreach (var zoom in new[] { .05, .7, 1, 8 }) {
        var view = new ViewportState(new(40.25, -80.5), zoom); var a = Draw(cached, graph, view);
        using var fresh = new GraphOverviewRenderer(); Check(a.SequenceEqual(Draw(fresh, graph, view)));
    }
});
Test("Overview invalidates changed geometry, graph identity and size", () => {
    using var renderer = new GraphOverviewRenderer(); var graph = Small(); Draw(renderer, graph);
    Draw(renderer, graph, width: 210); Check(renderer.Statistics.SceneRecordings == 2);
    var edit = new GraphEdit(graph); edit.Move(["b"], new(100, 100)); Draw(renderer, edit.Build()); Check(renderer.Statistics.SceneRecordings == 3);
    Draw(renderer, graph); Check(renderer.Statistics.SceneRecordings == 4);
});
Test("Overview fits the full 10000-node scene below the editor zoom floor", () => {
    var graph = new GraphSnapshot(Enumerable.Range(0, 10000).Select(i => new GraphNode("n" + i, new(i % 100 * 300, i / 100 * 250, 100, 60))).ToImmutableArray(), [], [], []);
    using var renderer = new GraphOverviewRenderer(); Draw(renderer, graph); var bounds = GraphScene.Bounds(graph);
    var top = renderer.ToScreen(new(bounds.X, bounds.Y)); var bottom = renderer.ToScreen(new(bounds.Right, bounds.Bottom));
    Check(top.X >= 9 && top.Y >= 9 && bottom.X <= 211 && bottom.Y <= 151);
    var point = new PointD(17893.5, 23000.125); var roundtrip = renderer.ToWorld(renderer.ToScreen(point)); Near(roundtrip.X, point.X); Near(roundtrip.Y, point.Y);
});
Test("Overview increases its cache pixel density with the caller canvas", () => {
    using var renderer = new GraphOverviewRenderer(); var graph = Small(); Draw(renderer, graph); Check(renderer.CachedPixels == 220 * 160);
    surface.Canvas.ResetMatrix(); surface.Canvas.Scale(2);
    renderer.Render(surface.Canvas, graph, GraphScene.Bounds(graph), 220, 160, new(new(), 1), 800, 400);
    Check(renderer.CachedPixels == 440 * 320 && renderer.Statistics.SceneRecordings == 2);
    surface.Canvas.ResetMatrix();
});
Test("Overview rejects malformed dimensions before changing its cache", () => {
    using var renderer = new GraphOverviewRenderer(); var graph = Small(); Draw(renderer, graph); var before = renderer.Statistics;
    Throws<ArgumentOutOfRangeException>(() => Draw(renderer, graph, width: double.NaN));
    Throws<ArgumentOutOfRangeException>(() => Draw(renderer, graph, width: 5000));
    Throws<ArgumentOutOfRangeException>(() => Draw(renderer, graph, new(new(), 0)));
    Check(renderer.Statistics == before);
});
Test("Overview zero dimensions are inert and clear releases its pixels", () => {
    using var renderer = new GraphOverviewRenderer(); var graph = Small(); Draw(renderer, graph, width: 0); Check(!renderer.Statistics.HasPicture);
    Draw(renderer, graph); renderer.Clear(); Check(!renderer.Statistics.HasPicture && renderer.CachedPixels == 0);
    Draw(renderer, graph); Check(renderer.Statistics.SceneRecordings == 2);
});
Test("Overview disposal is idempotent and rejects subsequent operations", () => {
    var renderer = new GraphOverviewRenderer(); Draw(renderer, Small()); renderer.Dispose(); renderer.Dispose(); Check(renderer.CachedPixels == 0);
    Throws<ObjectDisposedException>(() => Draw(renderer, Small())); Throws<ObjectDisposedException>(() => renderer.ToScreen(new()));
});
Test("Overview hidden group content changes rebuild correctly", () => {
    var graph = Tree(); using var cached = new GraphOverviewRenderer(); Draw(cached, graph);
    var edit = new GraphEdit(graph); edit.Update(edit.Node("g") with { Collapsed = true }); var folded = edit.Build();
    var image = Draw(cached, folded); using var fresh = new GraphOverviewRenderer(); Check(image.SequenceEqual(Draw(fresh, folded)));
});
Test("Structure is preorder even when child records precede parent records", () => {
    var index = new GraphStructureIndex(Tree()); Check(index.GetRows().Select(i => i.Id).SequenceEqual(new[] { "g", "b", "h", "c", "z" }));
    Check(index.GetRows().Select(i => i.Depth).SequenceEqual(new[] { 0, 1, 1, 2, 0 }));
});
Test("Structure expansion hides complete subtrees without graph edits", () => {
    var graph = Tree(); var index = new GraphStructureIndex(graph); Check(index.GetRows(["g"]).Select(i => i.Id).SequenceEqual(new[] { "g", "z" }));
    Check(index.GetRows(["h"]).Select(i => i.Id).SequenceEqual(new[] { "g", "b", "h", "z" })); Check(ReferenceEquals(index.Snapshot, graph));
});
Test("Structure view expansion is independent of graph folding", () => {
    var edit = new GraphEdit(Tree()); edit.Update(edit.Node("g") with { Collapsed = true }); var index = new GraphStructureIndex(edit.Build());
    Check(index.Count == 5 && index.GetRows().Single(i => i.Id == "c").IsVisible == false && index.GetRows().Single(i => i.Id == "g").IsVisible);
});
Test("Structure handles empty, duplicate and unknown expansion filters", () => {
    Check(new GraphStructureIndex(GraphSnapshot.Empty).GetRows().IsEmpty);
    var index = new GraphStructureIndex(Tree()); Check(index.GetRows(["h", "h", "unknown"]).Length == 4 && index.GetRows(["b"]).Length == 5);
});
Test("Structure supports deep groups without recursive call-stack growth", () => {
    var graph = new GraphSnapshot(Enumerable.Range(0, 10000).Select(i => new GraphNode("n" + i, new(0, 0, 100, 60), IsGroup: true, ParentId: i == 0 ? null : "n" + (i - 1))).Reverse().ToImmutableArray(), [], [], []);
    var index = new GraphStructureIndex(graph); Check(index.GetRows()[^1].Depth == 9999 && index.GetRows(["n50"]).Length == 51);
});
Test("Structure registry is stable under concurrent reading and caller changes", () => {
    var index = new GraphStructureIndex(Tree()); var hidden = new List<string> { "g" }; var rows = index.GetRows(hidden); hidden.Clear();
    Check(rows.Length == 2); Parallel.For(0, 20, _ => Check(index.GetRows(["h"]).Length == 4));
});
Directory.CreateDirectory("artifacts/validation");
File.WriteAllText("artifacts/validation/workspace.xml", new XElement("testsuite", new XAttribute("name", "Overview and structure"), new XAttribute("tests", results.Count), new XAttribute("failures", results.Count(r => r.Error is not null)), results.Select(r => new XElement("testcase", new XAttribute("name", r.Name), r.Error is null ? null : new XElement("failure", r.Error)))).ToString());
Console.WriteLine($"{results.Count(r => r.Error is null)}/{results.Count} passed");
if (results.Any(r => r.Error is not null)) return 1;
// Same-process measurement: the former full scene draw versus the retained image camera path.
var large = new GraphSnapshot(Enumerable.Range(0, 10000).Select(i => new GraphNode("n" + i, new(i % 100 * 120, i / 100 * 100, 80, 50))).ToImmutableArray(), [], [], []);
using var baseline = new GraphRenderer(); using var optimized = new GraphOverviewRenderer(); var view = new Viewport(); var box = GraphScene.Bounds(large);
// Both paths fit the full graph. The baseline transform is scaled below the editor zoom floor too.
var fit = Math.Min(200 / box.Width, 140 / box.Height); var extra = fit / Viewport.MinimumZoom; view.Restore(new(box.Center - new PointD(220 / extra / .1, 160 / extra / .1), .05));
void Old() { surface.Canvas.ResetMatrix(); surface.Canvas.Scale((float)extra); baseline.Render(surface.Canvas, large, view, 220 / extra, 160 / extra, new(ShowGrid: false, Overview: true)); }
void New() { surface.Canvas.ResetMatrix(); optimized.Render(surface.Canvas, large, box, 220, 160, new(new(), 1), 800, 400); }
object Measure(string name, Action a)
{
    for (var i = 0; i < 10; i++) a(); GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
    var times = new double[50]; var bytes = new long[50];
    for (var i = 0; i < times.Length; i++) { var memory = GC.GetAllocatedBytesForCurrentThread(); var time = Stopwatch.GetTimestamp(); a(); times[i] = Stopwatch.GetElapsedTime(time).TotalMilliseconds; bytes[i] = GC.GetAllocatedBytesForCurrentThread() - memory; }
    Array.Sort(times); Array.Sort(bytes); return new { name, medianMilliseconds = times[25], p95Milliseconds = times[47], medianAllocatedBytes = bytes[25] };
}
var report = new { nodes = 10000, logicalWidth = 220, logicalHeight = 160, note = "Same-process warm CPU/raster comparison; old redraw excludes viewport marker, cached includes it. Not GPU/FPS measurement. Cache rebuild and memory tradeoffs tested separately.", results = new[] { Measure("full_scene_redraw", Old), Measure("cached_overview_frame", New) } };
Directory.CreateDirectory("artifacts/overview-benchmark"); File.WriteAllText("artifacts/overview-benchmark/report.json", JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
return 0;
