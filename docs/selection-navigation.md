# Zoom to selection and streaming scene bounds

**View → Zoom to selection** fits the currently selected visible items without changing
selection, graph data, dirty state or undo history. The command is enabled synchronously
with selection changes. Its reusable implementation is `GraphControl.FitSelection`.
It cancels an unfinished edit preview before measuring, leases the document/session,
revision and immutable selection, then uses the same viewport fit limits as Fit graph.
No visible selection is an inert operation. The graph's existing 5% minimum and 200%
fit-command maximum zoom still apply; very large selections can exceed the minimum-zoom
viewport, so this is not a guarantee that arbitrary coordinate spans fit onscreen.

```csharp
var bounds = GraphScene.BoundsForItems(document.Snapshot, selectedIds, measureLabel);
graphControl.FitSelection();
```

Selected nodes contribute their full rectangle, including expanded groups rather than
just their header hit area. Selected edges contribute route points and measured captions.
Ports contribute their exact owner-relative anchor. Independent annotations contribute
measured bounds when explicitly selected. Unselected owner annotations and unrelated
nodes are not implicitly added. Hidden items are skipped; unknown IDs reject. Empty
results are null, distinct from a zero-sized selected port. Selection iteration order is
preserved; duplicate IDs are repeated contributions rather than new editing operations.
These contracts are independent of the workbench and use no native GUI dependencies.

The full-scene bounds path previously constructed a List of every node rectangle,
edge path point and independent-label rectangle. It now streams through a value
accumulator. Sequential union arithmetic and visible-item order remain unchanged,
including the legacy 1×1 empty-scene extent. Nonfinite label geometry or an overflowing
union now raises a diagnostic instead of publishing an invalid camera/export extent.
Per-label font measurement and caption records still have their own costs; this does
not claim all text shaping is allocation-free. The no-temporary-list contract is tested
with a cached graph index, blank edge captions and a fixed, allocation-free measurer.

`SceneBoundsTests` compares exact results with the previous list-based implementation
across movement and folding, and covers explicit-item scope, measured captions, full
groups, ports, hidden items, bad IDs, cancellation, overflow and warmed allocation.
The native Uno scrolling suite additionally operates the View command at display
pixel densities 1 and 2, checks camera centering for single/multiple selections and
asserts unchanged graph revision/data. A package-only consumer exercises the portable
query and compiles the reusable control API for desktop and browser.

The same-source, same-runner benchmark adds `scene_bounds_10000`, using the standard
10,000-node, 9,900-edge and 10,000-label fixture with fixed 80×20 label metrics. Both
revisions have identical harnesses and already-warmed indexes. Reported current-thread
managed bytes include boxing the returned rectangle into the benchmark sink. Timings
measure CPU traversal, not GPU throughput, presentation FPS, physical input latency,
or all export/text workloads. All other workloads and raster hash checks are retained;
slower samples must remain in the complete report.

This extends the original desktop-style navigation rather than claiming yFiles API or
screenshot equivalence. Specialized style/layout families, full GraphML compatibility,
docking/multiple documents, printing and complete accessibility remain outstanding.
