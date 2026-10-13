using System.Collections.Immutable;
using System.Diagnostics;
using RuntimeInformation = System.Runtime.InteropServices.RuntimeInformation;
using System.Security.Cryptography;
using System.Text.Json;
using SkiaSharp;
using VisualizeScene.Core;
using VisualizeScene.Editing;
using VisualizeScene.Rendering.Skia;
using VisualizeScene.Layout;
using VisualizeScene.Algorithms;

// Use APIs shared with the pinned baseline so both runs compile identical harness sources.
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
    // Isolate retained garbage from preceding workloads; same collection policy for both revisions.
    GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
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
Func<GraphLabel, PointD> fixedMetrics = _ => new(80, 20);
// One fitted group amid unrelated annotations. Both revisions keep exact no-op geometry.
var scopeGraph = graph with
{
    Nodes = graph.Nodes.Add(new("scopeGroup", new(-28, -48, 156, 136), IsGroup: true))
        .Add(new("scopeChild", new(0, 0, 100, 60), ParentId: "scopeGroup")),
    Labels = graph.Labels.Add(new("scopeLabel", "scopeChild", "Inside", new()))
};
string[] scopeIds = ["scopeGroup"];
if (!ReferenceEquals(GraphGroupSizing.Calculate(scopeGraph, scopeIds, measure: fixedMetrics).Graph, scopeGraph))
    throw new InvalidOperationException("The group annotation scope benchmark must be a no-op.");
Measure("group_fit_scope_10000", () => sink = GraphGroupSizing.Calculate(scopeGraph, scopeIds, measure: fixedMetrics));
Measure("scene_bounds_10000", () => sink = GraphScene.Bounds(graph, fixedMetrics));
var spatialItems = nodes.Select(n => (n.Bounds, n.Id)).ToArray();
Measure("spatial_build_10000", () => sink = new SpatialIndex<string>(spatialItems));
var spatial = new SpatialIndex<string>(spatialItems); var candidates = new List<string>(64);
Measure("spatial_query_warm", () => { candidates.Clear(); _ = spatial.Query(new RectD(0, 0, 800, 400), candidates); }, batch: 100);
Measure("pagerank_10000", () => sink = GraphAlgorithms.PageRank(graph, iterations: 80, tolerance: 1e-20), samples: 20);
Measure("snapshot_index_warm", () => sink = graph.Index(), batch: 5);
Measure("snapshot_validation_warm", () => GraphValidation.Validate(graph), batch: 5);
Measure("hit_test_local", () => sink = editor.Hit(new(50, 30), handles: false), batch: 10);
Measure("connection_candidate_local", () => sink = editor.FindConnectionCandidate(new(100, 30)), batch: 10);
var defaultPolicy = new ConnectionPolicy(); var request = new ConnectionRequest(new("n0"), new("n1")); var graphIndex = graph.Index();
Measure("connection_policy_default", () => sink = defaultPolicy.Evaluate(graphIndex, request), batch: 10);
// Compare identical legacy-edge marquee semantics in both revisions; construction is outside this warmed query.
var edgeArea = new RectD(-10, -10, 30000, 25000);
if (!editor.QueryArea(edgeArea, kinds: SelectionKinds.Edges).SequenceEqual(edges.Select(e => e.Id).Order(StringComparer.Ordinal)))
    throw new InvalidOperationException("The edge-area benchmark returned incorrect items.");
Measure("edge_area_9900", () => sink = editor.QueryArea(edgeArea, kinds: SelectionKinds.Edges));
editor.SelectAll();
Measure("selected_handle_hit", () => sink = editor.Hit(new(50, 30)), batch: 10);
var selectedOptions = new RenderOptions(ShowGrid: false, Selection: editor.Selection);
Measure("render_selected_local", () => renderer.Render(surface.Canvas, graph, viewport, 800, 400, selectedOptions));
var deletionGraph = new GraphSnapshot(nodes.Take(1000).ToImmutableArray(), [], [], []);
Measure("delete_1000_nodes", () => { var d = new GraphDocument(); d.Replace(deletionGraph); using var s = new EditorSession(d); s.SelectAll(); s.DeleteSelection(); sink = d.Snapshot; }, samples: 12);
Measure("align_1000_nodes", () => {
    var d = new GraphDocument(); d.Replace(deletionGraph); using var session = new EditorSession(d); session.SelectAll();
    session.Align(Alignment.HorizontalCenter); sink = d.Snapshot;
}, samples: 20);
var alignedGraph = deletionGraph with { Nodes = deletionGraph.Nodes.Select(n => n with { Bounds = n.Bounds with { X = 0 } }).ToImmutableArray() };
var alignedIds = alignedGraph.Nodes.Select(n => n.Id).ToArray();
Measure("align_noop_1000", () => sink = GraphAlignment.Calculate(alignedGraph, alignedIds, Alignment.Left));
var layoutGraph = new GraphSnapshot(nodes, [], [], []);
Measure("grid_layout_10000", () => sink = new LayoutEngine().Calculate(layoutGraph, new(LayoutKind.Grid)), samples: 12);
Measure("label_edit_build", () => { var edit = new GraphEdit(graph); edit.Update(graph.Labels[0] with { Text = "Changed" }); sink = edit.Build(); });
editor.Select(["n0"]); editor.SnapToGrid = false; editor.SnapToItems = false;
Measure("node_drag_preview", () => { editor.PointerDown(new(50, 30)); editor.PointerMove(new(70, 45)); editor.CancelGesture(); });
var recolored = graph with { Nodes = graph.Nodes.Select(n => n with { Fill = "#FFCC66" }).ToImmutableArray() };
var reordered = graph with { Nodes = graph.Nodes.Reverse().ToImmutableArray() };
var toggle = false;
Measure("style_revision_render_10000", () => {
    toggle = !toggle; renderer.Render(surface.Canvas, toggle ? recolored : graph, viewport, 800, 400, options);
}, samples: 20);
Measure("order_revision_render_10000", () => {
    toggle = !toggle; renderer.Render(surface.Canvas, toggle ? reordered : graph, viewport, 800, 400, options);
}, samples: 20);
Measure("render_warm_local", () => renderer.Render(surface.Canvas, graph, viewport, 800, 400, options));
// Renderer construction and first preparation are reported separately, including any native disposal overhead.
Measure("render_cold_local", () => { using var cold = new GraphRenderer(); cold.Render(surface.Canvas, graph, viewport, 800, 400, options); }, samples: 12);
Measure("index_new_snapshot", () => sink = (graph with { }).Index(), samples: 12);
renderer.Render(surface.Canvas, graph, viewport, 800, 400, options);
using var image = surface.Snapshot(); using var bitmap = SKBitmap.FromImage(image);
var report = new { revision = args[1], runtime = RuntimeInformation.FrameworkDescription, os = RuntimeInformation.OSDescription,
    processors = Environment.ProcessorCount, nodes = count, edges = edges.Length, ports = count, labels = count, viewport = new { width = 800, height = 400, zoom = 1 },
    note = "Same-source, same-runner CPU/raster comparison. Full collection after warm-up before each workload in both revisions. Warm operation timings and current-thread managed allocation only; not hardware GPU/FPS/input-latency measurements. Cold render includes construction/disposal with shared model already warmed. Scene bounds use fixed 80x20 label metrics; its allocation includes boxing the rectangle result into the sink.",
    pixelSha256 = Convert.ToHexString(SHA256.HashData(bitmap.Bytes)), results };
Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(args[0]))!);
File.WriteAllText(args[0], JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
GC.KeepAlive(sink);
