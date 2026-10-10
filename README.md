# VisualizeScene

An independent, modular graph visualization and editing toolkit, with an **Uno Platform desktop and WebAssembly workbench**. The graph model, editing engine, layout algorithms, analysis, interchange and rendering can each be reused independently of the sample application.

> **Status:** Initial implementation. This is not yFiles, is not affiliated with yWorks, and does **not** provide full yFiles WPF API, algorithm-quality or visual parity. No proprietary yFiles implementation or assets are included. See the [capability matrix](docs/capabilities.md) for implemented features and explicit boundaries.

## Workbench

The workbench brings together a graph editor, node palette, overview, property inspector, layout controls and graph analysis. It uses Uno's shared Skia rendering surface rather than a separate HTML/JavaScript graph engine. Desktop and browser hosts reference the same control library and core packages.

The GitHub Pages workflow deploys the browser artifact only after the main-branch build and browser tests succeed. The intended app address is **https://wieslawsoltes.github.io/VisualizeScene/**. A successful deployment must be confirmed in the repository's Actions and Pages environments; the presence of this link is not proof of a deployment.

### Editing

Select and drag nodes; use corner handles to resize. Drag empty canvas for marquee selection, and Ctrl+click for additive selection. Choose a palette shape and click the canvas to create it. In Edge mode, click a source node or port, then a target. Double-click labels to edit them, or Ctrl+double-click an edge to insert a bend. Group headers can be folded and unfolded.

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

## Reusable packages

| Package | Contents |
|---|---|
| `VisualizeScene.Core` | Geometry, immutable graph snapshots, validation, atomic edits, undo/redo, groups, ports, labels, spatial index |
| `VisualizeScene.Algorithms` | Traversal, connectivity, shortest paths, spanning forests, PageRank, betweenness, coloring, maximum flow |
| `VisualizeScene.Layout` | Hierarchical, organic, tree, radial, circular and grid layouts; obstacle-aware orthogonal routing |
| `VisualizeScene.Editing` | Platform-neutral gestures, connection policies, reconnection, port/label editing, selection, clipboard, snapping, grouping and alignment |
| `VisualizeScene.Serialization` | Versioned JSON, GraphML interchange subset, standalone SVG |
| `VisualizeScene.Rendering.Skia` | Retained node pictures, shaped text, culling, selection adorners and PNG export |
| `VisualizeScene.Controls.Uno` | `GraphControl`, `GraphOverview`, `NodePalette`, `GraphPropertyInspector` |

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

`Build and validate` runs the core conformance, editing/geometry and bounded text-layout regressions, builds desktop, publishes the actual Uno WebAssembly application, packages the controls, and runs Playwright pointer/keyboard tests against that application. Artifacts include source, NuGet packages, browser distribution, test reports and screenshots.

The browser test's optional `?test=1` snapshot is read-only. Tests create, move, resize, duplicate, delete, connect and group graph items through actual input; they do not call a parallel model or mutation API. The three browser suites cover baseline interactions; ports, reconnection and independent labels; and layouts, analysis, exports, file round trips and all sample graphs. File-import automation exercises Uno’s upload-picker fallback; the native operating-system file dialog is not automated. See [browser validation](tests/browser/README.md). The core tests exercise this project's contracts, not the whole yFiles commercial SDK.

## Documentation

- [Architecture and package contracts](docs/architecture.md)
- [Capability matrix and remaining parity work](docs/capabilities.md)

## License

MIT for this repository's original source. Uno Platform, SkiaSharp and HarfBuzz retain their respective licenses. yFiles and yWorks names identify the requested compatibility target only.
