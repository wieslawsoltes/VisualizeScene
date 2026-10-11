using System.Collections.Immutable;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using SkiaSharp;
using VisualizeScene.Core;
using VisualizeScene.Editing;
using VisualizeScene.Rendering.Skia;
using VisualizeScene.Layout;

// Deliberately uses only pre-PR3 public APIs so the identical harness builds against both revisions.
if (args.Length != 2) throw new ArgumentException("Usage: benchmark output.json revision");
const int count = 10000;
var nodes = Enumerable.Range(0, count).Select(i => new GraphNode("n" + i, new(i % 100 * 300, i / 100 * 250, 100, 60), "Node")).ToImmutableArray();
var edges = Enumerable.Range(0, count).Where(i => i % 100 < 99).Select(i => new GraphEdge("e" + i, "n" + i, "n" + (i + 1))).ToImmutableArray();
var graph = new GraphSnapshot(nodes, edges,
    Enumerable.Range(0, count).Select(i => new GraphPort("p" + i, "n" + i, new(1, .5))).ToImmutableArray(),
    Enumerable.Range(0, count).Select(i => new GraphLabel("l" + i, "n" + i, "Label", new(0, 80))).ToImmutableArray());
var document = new GraphDocument(); document.Replace(graph, clearHistory: true);
using var editor = new EditorSession(document) { LabelSizeProvider = _ => new(80, 20) };
using var renderer = new GraphRenderer(); using var surface = SKSurface.Create(new SKImageInfo(800, 400)) ?? throw new Exception("No raster surface");
var viewport = new Viewport(); var options = new RenderOptions(ShowGrid: false);
var results = new List<object>(); object? sink = null;
void Measure(string name, Action action, int samples = 40, int batch = 1)
{
    for (var i = 0; i < 10; i++) action();
    var timings = new double[samples]; var allocations = new long[samples];
    for (var i = 0; i < samples; i++)
    {
        var memory = GC.GetAllocatedBytesForCurrentThread(); var start = Stopwatch.GetTimestamp();
        for (var j = 0; j < batch; j++) action();
        timings[i] = Stopwatch.GetElapsedTime(start).TotalMilliseconds / batch;
        allocations[i] = (GC.GetAllocatedBytesForCurrentThread() - memory) / batch;
    }
    Array.Sort(timings); Array.Sort(allocations);
    var p50 = timings[samples / 2]; var p95 = timings[Math.Min(samples - 1, (int)Math.Ceiling(samples * .95) - 1)];
    results.Add(new { name, samples, batch, medianMilliseconds = p50, p95Milliseconds = p95, medianAllocatedBytes = allocations[samples / 2] });
    Console.WriteLine($"{name}: p50 {p50:F6} ms; p95 {p95:F6} ms; {allocations[samples / 2]} bytes/op");
}
Measure("snapshot_index_warm", () => sink = graph.Index(), batch: 5);
Measure("snapshot_validation_warm", () => GraphValidation.Validate(graph), batch: 5);
Measure("hit_test_local", () => sink = editor.Hit(new(50, 30), handles: false), batch: 10);
Measure("connection_candidate_local", () => sink = editor.FindConnectionCandidate(new(100, 30)), batch: 10);
var defaultPolicy = new ConnectionPolicy(); var request = new ConnectionRequest(new("n0"), new("n1")); var graphIndex = graph.Index();
Measure("connection_policy_default", () => sink = defaultPolicy.Evaluate(graphIndex, request), batch: 10);
editor.SelectAll();
Measure("selected_handle_hit", () => sink = editor.Hit(new(50, 30)), batch: 10);
var selectedOptions = new RenderOptions(ShowGrid: false, Selection: editor.Selection);
Measure("render_selected_local", () => renderer.Render(surface.Canvas, graph, viewport, 800, 400, selectedOptions));
var deletionGraph = new GraphSnapshot(nodes.Take(1000).ToImmutableArray(), [], [], []);
Measure("delete_1000_nodes", () => { var d = new GraphDocument(); d.Replace(deletionGraph); using var s = new EditorSession(d); s.SelectAll(); s.DeleteSelection(); sink = d.Snapshot; }, samples: 12);
var layoutGraph = new GraphSnapshot(nodes, [], [], []);
Measure("grid_layout_10000", () => sink = new LayoutEngine().Calculate(layoutGraph, new(LayoutKind.Grid)), samples: 12);
Measure("label_edit_build", () => { var edit = new GraphEdit(graph); edit.Update(graph.Labels[0] with { Text = "Changed" }); sink = edit.Build(); });
editor.Select(["n0"]); editor.SnapToGrid = false; editor.SnapToItems = false;
Measure("node_drag_preview", () => { editor.PointerDown(new(50, 30)); editor.PointerMove(new(70, 45)); editor.CancelGesture(); });
Measure("render_warm_local", () => renderer.Render(surface.Canvas, graph, viewport, 800, 400, options));
// Renderer construction and first preparation are reported separately, including any native disposal overhead.
Measure("render_cold_local", () => { using var cold = new GraphRenderer(); cold.Render(surface.Canvas, graph, viewport, 800, 400, options); }, samples: 12);
Measure("index_new_snapshot", () => sink = (graph with { }).Index(), samples: 12);
renderer.Render(surface.Canvas, graph, viewport, 800, 400, options);
using var image = surface.Snapshot(); using var bitmap = SKBitmap.FromImage(image);
var report = new { revision = args[1], runtime = RuntimeInformation.FrameworkDescription, os = RuntimeInformation.OSDescription,
    processors = Environment.ProcessorCount, nodes = count, edges = edges.Length, ports = count, labels = count, viewport = new { width = 800, height = 400, zoom = 1 },
    note = "Same-source, same-runner CPU/raster comparison. Warm operation timings and current-thread managed allocation only; not hardware GPU/FPS/input-latency measurements. Cold render includes construction/disposal with shared model already warmed.",
    pixelSha256 = Convert.ToHexString(SHA256.HashData(bitmap.Bytes)), results };
Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(args[0]))!);
File.WriteAllText(args[0], JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
GC.KeepAlive(sink);
