# VisualizeScene

An independent, modular graph visualization and editing toolkit, with an **Uno Platform desktop and WebAssembly workbench**. The graph model, editing engine, layout algorithms, analysis, interchange and rendering can each be reused independently of the sample application.

> **Status:** Initial implementation. This is not yFiles, is not affiliated with yWorks, and does **not** provide full yFiles WPF API, algorithm-quality or visual parity. No proprietary yFiles implementation or assets are included. See the [capability matrix](docs/capabilities.md) for implemented features and explicit boundaries.

## Workbench

The workbench brings together a graph editor, node palette, overview, property inspector, layout controls and graph analysis. It uses Uno's shared Skia rendering surface rather than a separate HTML/JavaScript graph engine. Desktop and browser hosts reference the same control library and core packages.

The browser demo is available at **https://wieslawsoltes.github.io/VisualizeScene/**. The Pages workflow deploys the browser artifact only after the main-branch build, browser scenarios and isolated package consumers succeed.

### Editing

Select and drag nodes; use corner handles to resize. Drag empty canvas for marquee selection, Shift+click to add items, and Ctrl+click to toggle them. Choose a palette shape and click the canvas to create it. In Edge mode, click a source node or port, then a target. Double-click labels to edit them, or Ctrl+double-click an edge to insert a bend. Group headers can be folded and unfolded.

Drag the circular handles on a selected edge to reconnect its source or target. Invalid drops and Escape leave the original edge unchanged. Add ports through the Edit menu, then drag them or edit their relative coordinates in the inspector. Connection rules are supplied through the reusable `IConnectionPolicy`.

Use **Edit → Add independent label** to annotate a node, edge or port. Independent labels have measured hit areas and can be dragged, edited with F2, duplicated, deleted and nudged. Copy a label, select another owner and paste to retarget it without duplicating the graph. Selecting a label together with its owner moves both once, rather than applying the movement twice.

Mouse-wheel zoom is anchored at the pointer. Middle-button drag or Pan mode moves the viewport. The overview navigates the graph. Small viewports hide side panels so the editor remains usable.

| Shortcut | Action |
|---|---|
| Ctrl+Z / Ctrl+Y | Undo / redo |
| Ctrl+C / Ctrl+V / Ctrl+D | Internal graph clipboard / paste / duplicate |
| Ctrl+G / Ctrl+Shift+G | Group / ungroup |
| F2 / Delete | Edit label / remove selection |
| Arrow keys / Shift+arrow | Move by 1 / 10 units |
| Home / Escape | Fit graph / cancel gesture and select mode |

### Placement, snapping and selection

Use **Layout → Place independent labels** or the Layout panel to place independent annotations around graph elements. The panel exposes clearance, selected-only placement and remaining conflict counts. Placement changes label offsets only, commits once and supports undo; it does not guarantee a collision-free result.

Node movement and resize corners snap to nearby node boundaries or centers, with guides drawn in the existing Skia canvas. **View → Alignment guides** controls magnetic snapping independently of grid snapping.

The **Selection** menu filters nodes, edges, ports, labels or groups, inverts/clears the selection and expands incoming/outgoing neighbors. Marquee modes are intersecting, fully contained, or directional: drag right for a containment window and left for crossing selection. A solid blue rectangle indicates containment; a dashed green rectangle indicates crossing. Dragging previews highlights without changing the committed selection until release. Escape restores the original selection. Shift adds, Control toggles and Control+Shift subtracts. Ctrl+I inverts; Ctrl+Shift+A clears.

### Graph Find and Replace

Use **Edit → Find graph text / Replace graph text**, or Ctrl+F / Ctrl+H while the graph
has focus. The modeless panel searches captions, annotations and optional IDs, with
case/whole-word, hidden-item, kind and captured-selection filters. F3 / Shift+F3 cycles
occurrences; Replace All is one undoable operation and skips IDs and locked owners.
Truncated or stale results cannot silently produce a partial Replace All.

The reusable `GraphSearchIndex` engine and `GraphSearchControl` do not depend on the
workbench. [Search contracts and limits](docs/search.md) describe Unicode boundaries,
cancellation, literal replacement, revision guards and bounded output. Regex is not supported.

### Touch, spatial navigation and performance

**View → Touch input** offers Navigate, Edit and Disabled modes. One finger can pan
or edit; two fingers navigate with centroid-preserving pinch zoom. A second contact
cancels an edit preview before taking over navigation. Node/edge creation waits for
a completed tap. Ctrl+Arrow navigates graph items; Ctrl+Shift+Arrow extends selection;
Ctrl+Home/End visits reading-order boundaries. Page keys pan without editing.

The implementation shares immutable graph indexes across consumers, memoizes visible
group ancestry and routes, uses copy-on-write edit collections, and spatially queries
pointer hits, visible labels and ports. Unchanged renderer paths and indexes are
reused where their inputs did not change. The test diagnostics cache document data
separately from per-frame view state.

[Navigation contracts](docs/navigation.md) and [performance methodology](docs/performance.md)
describe the APIs, validation and limits. Same-runner benchmark reports compare the
current implementation with pinned previous-main baseline commit `36154e0f`; they are CPU/raster
measurements, not physical-GPU throughput or a promise of fixed frame rates.

## Reusable packages

| Package | Contents |
|---|---|
| `VisualizeScene.Core` | Geometry, immutable graph snapshots, validation, atomic edits, undo/redo, groups, ports, labels, spatial index |
| `VisualizeScene.Algorithms` | Traversal, connectivity, shortest paths, spanning forests, PageRank, betweenness, coloring, maximum flow |
| `VisualizeScene.Layout` | Six graph layouts, obstacle-aware orthogonal routing and automatic independent-label placement |
| `VisualizeScene.Editing` | Platform-neutral gestures, connection policies, reconnection, port/label editing, indexed selection, clipboard, magnetic snapping, grouping and alignment |
| `VisualizeScene.Serialization` | Versioned JSON, GraphML interchange subset, standalone SVG |
| `VisualizeScene.Rendering.Skia` | Retained node pictures, shaped text, culling, selection adorners and PNG export |
| `VisualizeScene.Controls.Uno` | `GraphControl`, `GraphOverview`, `NodePalette`, `GraphPropertyInspector`, `GraphSearchControl` |

The first five packages require no UI or graphics runtime. The Skia renderer accepts a caller-owned canvas. Uno owns the native graphics backend, so hardware acceleration depends on the host, browser, driver and fallback configuration. A software SwiftShader CI run validates rendering behavior, not physical GPU throughput.

### Core example

```csharp
using VisualizeScene.Core;
using VisualizeScene.Layout;
using VisualizeScene.Serialization;

var document = new GraphDocument();
document.Edit("Create graph", edit =>
{
    edit.Add(new GraphNode("start", new RectD(0, 0, 120, 54), "Start"));
    edit.Add(new GraphNode("finish", new RectD(200, 0, 120, 54), "Finish"));
    edit.Add(new GraphEdge("connection", "start", "finish"));
});

var result = new LayoutEngine().Calculate(document.Snapshot,
    new LayoutOptions(LayoutKind.Hierarchical));
document.Replace(result.Graph, "Hierarchical layout");
var json = GraphIO.ToJson(document.Snapshot);
var svg = SvgExporter.Export(document.Snapshot);
document.Undo();
```

### Uno integration

```csharp
using VisualizeScene.Controls.Uno;

var graphControl = new GraphControl { Document = document };
var overview = new GraphOverview { Target = graphControl };
var palette = new NodePalette { Target = graphControl };
var inspector = new GraphPropertyInspector { Target = graphControl };
// Add controls to your layout. Call graphControl.Fit() after it is arranged.
// Dispose the graph control, overview and inspector when the owning view closes.
```

## Build and run

Requirements: .NET 10 SDK, the WebAssembly workload, and the native requirements for your chosen Uno desktop host. `global.json` pins Uno.Sdk 6.7.30 and permits the latest .NET 10 feature band.

```sh
dotnet workload install wasm-tools
dotnet run --project tests/VisualizeScene.Tests -c Release
dotnet run --project tests/VisualizeScene.Editing.Tests -c Release
dotnet run --project tests/VisualizeScene.Rendering.Tests -c Release
dotnet run --project tests/VisualizeScene.Parity.Tests -c Release
dotnet run --project tests/VisualizeScene.Navigation.Tests -c Release
dotnet run --project tests/VisualizeScene.Search.Tests -c Release
# Linux native Skia/HarfBuzz assets are supplied by the performance test project:
dotnet run --project tests/VisualizeScene.Performance.Tests -c Release

dotnet run --project samples/VisualizeScene.Workbench -f net10.0-desktop
dotnet publish samples/VisualizeScene.Workbench -c Release -f net10.0-browserwasm
```

For a project-site deployment, publish with `-p:WasmShellWebAppBasePath=/VisualizeScene/`. Serve the resulting browser distribution through HTTP; do not open WebAssembly output using `file://`.

```sh
dotnet pack src/VisualizeScene.Core -c Release -o artifacts/packages
dotnet pack src/VisualizeScene.Controls.Uno -c Release -o artifacts/packages
```

CI builds all seven package projects and uploads NuGet artifacts. The `package-consumers` job restores them into a temporary directory with a fresh package cache and **no source project references**. It runs the model/editing/layout/analysis/interchange/Skia consumer and compiles a controls consumer for desktop and WebAssembly. Logs and package hashes are uploaded as `package-validation`. Packages are not published to nuget.org.

A standalone Linux rendering host must provide the matching SkiaSharp and HarfBuzz native assets. The package-consumer example supplies `SkiaSharp.NativeAssets.Linux` 3.119.2 and `HarfBuzzSharp.NativeAssets.Linux` 8.3.1.1; Uno applications obtain their platform runtime through Uno. The renderer does not bundle application fonts.

## Validation and deployment

`Build and validate` runs core conformance, editing/geometry, placement/snapping/selection and bounded text-layout regressions, builds desktop, publishes the actual Uno WebAssembly application, packages the controls, and runs Playwright pointer/keyboard tests against that application. Artifacts include source, NuGet packages, browser distribution, test reports and screenshots.

The browser test's optional `?test=1` snapshot is read-only. Tests create, move, resize, duplicate, delete, connect and group graph items through actual input; they do not call a parallel model or mutation API. Eight browser suites cover baseline interactions; ports, reconnection and independent labels; automatic placement and snapping; advanced selection; literal Find/Replace; touch/keyboard navigation; a 10,000-node performance fixture; and layouts, analysis, exports, file round trips and all sample graphs. File-import automation exercises Uno’s upload-picker fallback; the native operating-system file dialog is not automated. See [browser validation](tests/browser/README.md). The core tests exercise this project's contracts, not the whole yFiles commercial SDK.

## Documentation

- [Architecture and package contracts](docs/architecture.md)
- [Capability matrix and remaining parity work](docs/capabilities.md)
- [Label placement and snapping](docs/label-placement-and-snapping.md)
- [Selection contracts](docs/selection.md)

## License

MIT for this repository's original source. Uno Platform, SkiaSharp and HarfBuzz retain their respective licenses. yFiles and yWorks names identify the requested compatibility target only.
