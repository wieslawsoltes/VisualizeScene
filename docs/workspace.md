# Overview performance and reusable workspace controls

## Published scope

`GraphOverviewRenderer` retains one raster image for the miniature diagram. The moving
camera rectangle is a separate overlay. Camera changes no longer invoke all managed
node/edge drawing operations, and `GraphOverview` does not invalidate for selection-only
main-canvas frames. Document identity, content bounds, overview dimensions and pixel
density are cache inputs. The cache is released when the target is cleared or disposed.

The overview has its own fit transform below the main editor's 5% zoom floor. Very large
diagrams can therefore be seen in their entirety, instead of being clipped to a tiny
region in the minimap. `ToWorld` and `ToScreen` share that transform for navigation.
Input capture tracks one contact and ends on release, cancellation, target replacement
or disposal. Repeated disposal is harmless.

The cached image is built at the caller canvas density (bounded to 1–4) and is drawn to
the caller-owned Skia canvas. Its backing image uses a maximum of 16 million pixels;
logical dimensions are limited to 4096 per side. A graph/size/density change incurs a
full rebuild. The small CPU-raster image is reusable on a GPU canvas, but this is not a
new direct-WebGPU renderer or proof of physical-GPU performance.

```csharp
using var overview = new GraphOverviewRenderer();
overview.Render(canvas, document.Snapshot, graphBounds, 220, 160,
    viewport.State, targetWidth: 1200, targetHeight: 800);
var pointInGraph = overview.ToWorld(new PointD(100, 80));
```

`GraphStructureIndex` is an immutable preorder registry of nodes and groups. It handles
arbitrarily ordered input and deeply nested groups without recursive traversal. Its
expansion filter belongs to the outline view; it does not change graph folding or undo
history. `GraphStructureView` provides a virtualized Uno ListView, independent expansion,
keyboard left/right expansion, and selection/reveal synchronization. Registry construction
occurs on graph revisions rather than selection or camera changes. Indentation is visually
capped for extremely deep trees while model depth remains exact.

`EditorWorkspace` is a reusable three-pane layout with pointer/keyboard splitters, bounded
preferred widths, responsive rails and reset/show/hide APIs. `EditorPane` supplies a compact
titled collapsible content area. `EditorIcon` supplies original vector command icons drawn
through Uno's Skia surface, not an external icon font. `NodePalette` supports list and tile
presentation and validates all templates before replacing its published list.

## Workbench integration

The classic shell recovered after PR #5 now connects these controls to the workbench.
The former local-only integration boundary is superseded by [classic shell](classic-shell.md).
Palette/Structure tabs, compact command icons, categorized tiles, splitters and pane
collapse are exercised by `tests/browser/workspace.py`. The original WPF screenshot is
a design reference, not copied assets or a screenshot-equivalence claim.

## Validation

`tests/VisualizeScene.Workspace.Tests` exercises image cache reuse, invalidation, geometry,
DPI, budgets, disposal, large-scene fitting, immutable outlines, independent expansion and
deep hierarchy. The workflow uploads XML results and a same-process comparison of full
managed scene redraw versus a warmed overview image. Both paths fit the same 10,000-node
graph. The baseline timing excludes the marker; the optimized timing includes it. Timings
are CPU/raster measurements, not GPU timestamps, presentation FPS or input latency.

The ordinary eight browser suites and the new classic workspace suite run against the compiled Uno workbench. New
workspace control APIs retain package-only desktop/browser compile coverage; their
workbench integration also has actual browser interaction coverage.
The original GraphOverview is exercised by existing navigation/large-document scenarios.

The review remains bounded: complete accessibility peers, specialized styles, additional layouts and GraphML extensions,
printing and proprietary API parity are not addressed by this batch.
