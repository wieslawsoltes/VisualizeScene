# Drawing order, renderer reuse and desktop editor review

## Integrated UI

The workbench now has an original four-button vector toolbar beside the document title,
Arrange menu commands and context-menu commands for Bring to front, Send to back, Raise
one step and Lower one step. The overview is beneath the right-side property/tool pane,
closer to the arrangement of the published yWorks WPF editor reference. The property and
layout controls retain compact spacing, and drawing-order controls remain available when
narrow windows hide the side panes. The reused GraphOrderToolbar is independent of the
workbench. Icons are original Skia drawing code, not copied yWorks assets.

Reference (historical WPF 2.5 editor screenshot, not a current API specification):
https://marketplace.visualstudio.com/items?itemName=yWorks.yFilesWPF

This is a closer arrangement and additional editing behavior, not screenshot, proprietary
style/template or yFiles API parity. The software is an independent Uno toolkit.

## Drawing-order contract

GraphDrawingOrder.Calculate produces an immutable snapshot with reordered arrays. Every
item retains its identity, geometry, style, parent, ports and route points. Selected and
unselected items each retain their relative order. Raise/Lower crosses one neighboring
unselected item; Front/Back partitions the whole eligible layer. The operation uses
linear auxiliary storage and linear passes, rather than sorting by every selected ID.
No-op results return the original snapshot. Session/control commands cancel pending input,
lease document and selection state, and commit one undoable operation. Reentrant callbacks
cannot quietly retarget an operation to another selection or document.

Paint layers are deliberate: expanded group backgrounds (hierarchy depth first), edges,
ordinary nodes and collapsed groups, independent labels, then fixed-priority port handles.
An order command does not move an edge above a node, a group above its child, or a port
below a label. Expanded groups reorder only against groups at the same depth. Hidden
items and ports are not reordered; absent IDs and invalid operations are rejected.
Locked positions do not forbid changing drawing order. ExaminedItems counts collection
entries classified into layers, not all hash probes, validation or shared-index work.

```csharp
var ordered = GraphDrawingOrder.Calculate(document.Snapshot, selectedIds,
    DrawingOrder.BringToFront);
document.Replace(ordered.Graph, "Bring to front");
// Or use the integrated undoable session API:
session.ChangeDrawingOrder(DrawingOrder.SendToBack);
```

Nested group hit testing now uses the same depth-before-order rule as rendering. The
previous picker could choose a parent stored later in the node array even though its
nested child was painted on top. SVG group order uses cached index depths instead of
walking the parent chain separately for every group.

JSON naturally retains array order. Native GraphML adds optional node-order and port-order
ID arrays because XML grouping changes traversal order. The importer accepts these only
as complete permutations of existing records; missing, duplicate and unknown IDs reject.
Old files without these keys remain supported. These keys are VisualizeScene-specific;
external readers that ignore them are not guaranteed to retain our drawing order.

## Rendering performance and validation

The renderer distinguishes paint metadata from geometry. Node color/text/lock/order
changes update paint ranks and pictures without rebuilding spatial indexes, routes or
independent-label bounds. Edge caption/color/direction/order changes retain route geometry.
Shape, position, parenting, folding, endpoints, ports and bend changes still invalidate
corresponding geometry. Label reordering still rebuilds annotation order/index data.
Classification of changed node/edge arrays remains O(N/E), and new model validation,
editor indexes, history and overview rebuilds are not eliminated by this optimization.

The same-runner comparison pins PR #8 main 24b8b909369c950033c09abfeeb825f4d660840c.
New workloads alternate between two preconstructed, warmed 10,000-node snapshots, first
recoloring and then reversing node order, and measure Render preparation plus drawing.
Model construction and first validation/index preparation are outside these warm samples.
Both revisions use identical harness sources, runtime and raster/native libraries. All
previous workloads, including cold rendering and new-snapshot indexing, remain in the
report. These are CPU/raster and current-thread managed-allocation measurements, not
hardware GPU timestamps, browser FPS, input latency or whole-application edit latency.

Tests compare cached output byte-for-byte with fresh renderers at fractional transforms,
verify index/path reuse counters, and alternate style, order and geometry changes. Portable
checks cover all four order operations, stable randomized partitions, 10,000 items, exact
undo, hidden/group layers, invalid inputs, reentrant selection and GraphML permutations.
Actual compiled Uno browser tests verify overlap pixels, picking, menus, context commands,
color editing, GraphML download/import and narrow layout. Their diagnostics are read-only.

## Post-merge failure follow-up

PR #8's post-merge build 38178505046 failed its workspace Cut scenario; Pages deployment
38179036148 was skipped. The preserved state had a selected node, unchanged graph and no
reported application error. Toolbar availability previously updated during FrameRendered,
while selection was published before that frame. This PR updates command availability
synchronously on selection/document events, removes the per-frame availability work, and
adds twelve immediate selection-to-Cut/undo cycles without waiting for a rendered frame.
The exact timing of the earlier failure is not proven by its snapshot; the source ordering
defect and its correction are explicit. No old assertion or gate was removed.

The wider regression suites remain required. This review does not certify a bug-free
toolkit. Full docking/multiple documents, specialized styles, all layout families, complete
GraphML interchange, printing, graph accessibility peers and physical-GPU/touch/native-GUI
certification remain additional work.
