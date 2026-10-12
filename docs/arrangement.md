# Arrangement toolbar and reference-node sizing

The document header now contains original compact vector icons for six alignments,
horizontal/vertical distribution, and matching width, height or both dimensions. The
Arrange menu uses the same control commands and readable captions. Native focus, tooltips,
keyboard button activation and enabled-state visuals remain; the command strip can scroll
horizontally when the document area is narrow. This follows the compact desktop-editor
presentation of the yWorks public WPF sample, not its proprietary assets or source:
https://marketplace.visualstudio.com/items?itemName=yWorks.yFilesWPF

`GraphArrangeToolbar` is reusable independently of the workbench. It subscribes to
selection, node-collection, document replacement and keyboard-focus changes, and releases
those subscriptions on reassignment/disposal. Camera frames never refresh its state.
Edits that retain node records and selection/focus reuse the availability result. Ancestor
lookup is iterative and memoized rather than recursive or repeated for every deep path.
The graph's shared index is still a preparation cost, and a large selected set still costs
linear enumeration; no constant-time arbitrary-selection claim is made.

```csharp
var tools = new GraphArrangeToolbar { Target = graphControl };
var state = GraphArrangeState.Calculate(document.Snapshot, selectedIds, preferredReferenceId);
graphControl.ArrangeNodes(GraphArrangeCommand.SameWidth);
// Dispose tools when its owning view is permanently closed.
```

## Command semantics

Alignment requires two independently movable roots; distribution requires three in the
UI. Selecting an ancestor and its descendant does not create two movement roots. Locked
nodes are excluded, while an unlocked descendant of a locked selected ancestor remains
independently movable. Existing alignment geometry, internal group bends and cross-root
bend behavior are preserved. Distribution uses equal gaps, including negative gaps for
an already overlapping selection; it is not automatic non-overlapping layout.

The three new size commands use the selected keyboard-focus node as their reference, or
the first selected visible node when the focus is not a selected node. Tooltips identify
the reference ID. An explicit portable-API reference must be selected and visible.
A locked reference can supply dimensions; locked target nodes are never resized.
Node top-left positions stay fixed. Unselected descendants, edge bend records, relative
ports and label offsets are not scaled or translated. Their world anchors can of course
follow an owner's new dimensions. Groups are resized in place rather than auto-fitting or
scaling their children. This is intentional and distinct from graph layout.

`GraphNodeSizing.Calculate` returns an immutable result, preserves snapshot identity for
no-op requests and validates the complete result. `EditorSession.MatchNodeSize` publishes
one revision-guarded undo entry. `GraphControl.ArrangeNodes` cancels unfinished gestures
and validates document/session/revision/selection identity before editing. These APIs
never perform a Fit or change zoom. Malformed dimensions/overflow cannot partially commit.

## Alignment performance review

The existing alignment implementation unnecessarily sorted roots for all six non-distribution
commands. It also allocated hierarchy ownership and traversal structures when every selected
root was a plain top-level node. Those operations now skip this work. Distribution keeps its
original ordinal tie ordering and accumulation order; hierarchical/group cases keep the
same preorder ownership semantics. A no-op returns before cloning the entire node array.

The retained test-only previous implementation is compared against exact serialized output
on 80 fractional-coordinate/grouped fixtures and all eight alignment modes. A warmed
allocation comparison checks no-op temporary storage. The external same-runner benchmark
pins the PR #13 merge and runs identical harnesses, retains the existing complete workloads,
and adds `align_noop_1000`. That workload measures the already-aligned kernel, while the
existing `align_1000_nodes` includes temporary document/editor construction, selection and
publication. Full model validation and spatial-index refresh remain graph-sized costs.
Timing reports are CPU/raster and current-thread managed allocations, not GPU/FPS/input
latency measurements. Slower unchanged workloads must remain in the full comparison.

## Validation and boundaries

The portable suite covers all size modes, reference choice, locked nodes, selected groups,
hidden nodes, no-op identity, cancellation, overflow, one-step undo/redo, deep ancestry and
exact comparisons with the previous alignment implementation. Package-only consumers
execute sizing/state queries and compile the toolbar/control APIs for desktop and browser.
The new independent `tests/browser/arrangement.py` gate operates actual native toolbar and
menu items, checks all eleven commands, exact undo, preserved camera/data, locked reference,
no-op reapplication, responsive controls, state reuse on zoom and empty-document replacement.
No diagnostic object can mutate the graph or camera. CI counts and screenshots must be
attributed to the exact tested commit; inventory alone is not a passing-test claim.

This batch does not complete yFiles WPF screenshot/API/template/layout-quality equivalence.
Specialized graph styles, full GraphML extensions, docking/multiple documents, printing and
complete graph accessibility remain outstanding. Native desktop GUI, physical GPU and
physical touchscreen behavior are not certified by software-rendered Chromium tests.
