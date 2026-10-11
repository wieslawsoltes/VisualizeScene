# Navigation/performance review

This review covers the model/history, editing, selection, navigation, rendering,
controls, layout, analysis, serialization and workflow/package boundaries. It is
not a certification that the entire toolkit has no remaining bugs or full yFiles parity.

## Implemented corrections

| Finding | Correction and evidence |
|---|---|
| Selecting many items made deletion repeatedly scan the full graph | One `RemoveRange` dependency pass; exact undo and 10,000-node work-count tests |
| Connection rules copied/scanned all edges on every candidate | Endpoint adjacency for optional constraints; default checks need no edge scan; randomized equivalence tests |
| Selecting everything made handle tests and adorners traverse offscreen items | Spatial candidate queries plus immutable selection ranks and membership checks |
| Independent labels were reshaped each frame; native pictures had no retention bound | Shared configurable node/label picture LRU, explicit disposal, fractional-transform cached/uncached pixel comparisons |
| Reconstructed equal routes created no-op history entries | Edge metadata plus bend-value equivalence, normalizing default/empty bends |
| Same-value edits cloned untouched data | COW setters preserve identity for equal records; no-op movement returns before cloning |
| Minimum-size corner resizing moved the supposedly fixed corner | Clamp the active corner against its original opposite corner |
| Aligning a group and its selected child moved the child twice | Only selected ancestor roots participate in alignment |
| Property-panel group movement left children behind | `SetNodeBounds` translates children/internal bends once before applying style and size |
| Applying property fields rounded fractional values | Round-trip invariant formatting; actual browser reapply verifies no revision change |
| Old property editors could apply after document changes | Captured document/revision guard; errors from clear-bends use the same protected command path |
| Copying an edge whose endpoint is a group omitted group contents | Resolve endpoint owners before expanding group descendants |
| Legacy GraphML group inherited its child's geometry/style | Inspect only the node's own data elements, not nested graph descendants |
| Grid/layout result mapping repeatedly scanned all nodes | Indexed identity lookup, one-pass column maxima and per-parent child lookup |

`tests/VisualizeScene.Performance.Tests`, `tests/VisualizeScene.Parity.Tests` and the
large-document browser suite contain the associated regressions. Package-only consumers
also exercise the added bulk-edit and cache APIs. A successful Actions run for the final
revision, not this inventory, determines which checks passed.

## Explicit remaining findings and boundaries

Historical PR #3 findings (the three named corrections below are now implemented by the recovered numeric patch; broader extreme-value coverage remains): PageRank's nonfinite option
validation, tiny flow capacities and accumulated flow overflow were identified during
review and were **not included in PR #3**. See `classic-shell.md` for their subsequent recovery. Additional arithmetic
range/conditioning cases remain for shortest paths, centrality and geometry.

Routing quality, specialized GraphML extensions/styles, accessibility graph peers,
physical GPU/touch testing, direct WebGPU rendering, all layout families, rich label
models and screenshot/API equivalence remain outside this change. The picture budget
limits entries rather than native bytes. Model snapshots, route caches and history still
retain graph-sized data; geometry edits can rebuild indexes. See the capability matrix
and performance methodology for the broader inventory and measurement limitations.
