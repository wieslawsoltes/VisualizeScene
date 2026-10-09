# Architecture

VisualizeScene is an independent implementation of graph visualization capabilities, not a redistribution or wrapper of yFiles. All project source is MIT licensed. Third-party packages retain their own licenses.

## Package boundaries

| Package | Responsibility | Dependencies |
|---|---|---|
| VisualizeScene.Core | Geometry, immutable graph data, validated transactions, undo/redo, hierarchy/folding, spatial index | .NET only |
| VisualizeScene.Algorithms | Traversal, connectivity, paths, spanning forests, ranking, coloring, flow | Core |
| VisualizeScene.Layout | Compound graph layouts and obstacle-aware routing | Core, Algorithms |
| VisualizeScene.Editing | Pointer gesture state machine, selection, clipboard, snapping, grouping, alignment | Core |
| VisualizeScene.Serialization | Versioned JSON, GraphML subset, standalone SVG | Core |
| VisualizeScene.Rendering.Skia | Retained node pictures, shaped labels, paths, culling, adorners, PNG export | Core, SkiaSharp, SkiaSharp.HarfBuzz |
| VisualizeScene.Controls.Uno | GraphControl, GraphOverview, NodePalette, GraphPropertyInspector | Core, Editing, Rendering.Skia, Uno Platform |

The workbench is an application, not a required dependency of any package. The graph algorithms and editing session run without a UI, graphics context, Uno runtime, file service, or network connection.

## Data and threading

A GraphSnapshot contains immutable arrays of immutable records. Readers can retain a snapshot across document changes. GraphDocument is a single-writer object; applications marshal edits to their UI or owner thread. GraphEdit provides an isolated transaction. Validation happens before publication, so invalid endpoints, port owners, hierarchy cycles, geometry or style data do not partially mutate the document.

Undo history stores before/after snapshots and is bounded by entry count. A move gesture uses a preview snapshot until pointer release and creates one history entry. Cancellation does not alter the document. Revision leases reject stale asynchronous layout results. This is not a collaborative CRDT or a memory-byte-bounded persistent tree; large-document history should be configured appropriately.

## Rendering

The graph control uses Uno.WinUI.Graphics2DSK.SKCanvasElement. Its renderer draws into the caller's Skia canvas rather than allocating an interactive bitmap surface. Uno owns the native backend and swapchain. GPU acceleration is available where the Uno host uses a supported accelerated Skia backend; software drivers or fallback hosts can still be used. VisualizeScene does not claim that a browser capability probe proves the app is running on a physical GPU.

The renderer caches node pictures in local node coordinates, so translations can reuse recorded text and geometry. Node/edge spatial indexes reject offscreen elements. Shapes, group backgrounds, edges, labels and selection adorners have explicit paint order. Low zoom levels omit detailed node labels. Edges are clipped at endpoints and folded graph edges use visible ancestor representatives.

Text is shaped with HarfBuzz through SkiaSharp and recorded into node pictures. This does not yet supply complete multilingual font fallback, rich text, bidirectional paragraph layout, or yFiles label-placement models. CPU draw duration measures synchronous renderer submission only; it is not a GPU timestamp, frame rate or end-to-end latency metric.

Raster export uses a separate CPU surface deliberately, with explicit allocation limits. SVG export remains vector-based. Neither export currently supplies print pagination or poster tiling.

## Layout

Hierarchical layout condenses strongly connected components, assigns ranks and performs alternating barycenter sweeps. Tree layout computes a spanning forest and subtree widths. Circular, radial and grid layouts are independent implementations. Organic layout uses a seeded Barnes-Hut quadtree and spatial overlap broad phase. Groups are laid out bottom-up and translated with their descendants. Locked nodes retain their positions, but routing/layout around arbitrary locked geometry is not guaranteed collision-free.

Orthogonal routing uses rectilinear A* on an implicit Hanan grid, with clearance, bend costs and a visited-state budget. Failed routes return diagnostics and remain unchanged. Obstacle-aware self-loop routing is not implemented. No algorithm is represented as equivalent in output quality or configuration coverage to yFiles.

## Interchange and safety

JSON is source-generated and versioned. GraphML parsing disables DTDs and external entities, checks input size and limits nesting. Native VisualizeScene GraphML data retains its model properties. Plain topology and selected legacy yWorks geometry/style elements can be imported. Unknown or specialized extensions are reported, not advertised as supported. SVG uses XML escaping; graph text is never inserted as executable markup.

The workbench is local-first. Graph documents are not sent to a server. Browser file operations require user gestures; downloads use browser Blob URLs. The optional `?test=1` diagnostic snapshot is read-only and is used by Playwright tests that modify the application exclusively through real input.

## References

- yFiles WPF product and capabilities: https://www.yworks.com/products/yfiles-wpf
- Uno SKCanvasElement integration: https://platform.uno/docs/articles/controls/SKCanvasElement.html
- Uno Skia renderer: https://platform.uno/docs/articles/features/using-skia-renderer.html
- SkiaSharp shaped text API: https://learn.microsoft.com/dotnet/api/skiasharp.harfbuzz.canvasextensions

These sources describe interoperability targets and dependencies, not validation of VisualizeScene's implementation.
