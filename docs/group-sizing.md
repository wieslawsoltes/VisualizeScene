# Fitting group frames to children

**Arrange → Fit groups to children**, the compact frame icon beside the size tools,
and the graph context menu invoke the same reusable command. Select nested groups to
fit their frames from the inside out. Child positions are never translated or scaled.
Only selected, visible, expanded, unlocked, nonempty group frames can change. Empty,
collapsed and locked frames remain unchanged. An unselected ancestor is not implicitly
resized; select it too when it needs adjustment.

This is a separate operation from Make same size, which copies reference-node dimensions.
The public yWorks grouping tutorial describes adjusting a group after changing its children;
the API reference describes empty groups as inert and nested adjustment from inside out.
Those behavior references were consulted, not copied implementations or proprietary assets:
https://www.yfiles.com/demos/tutorial-yfiles-basic-features/08-grouping/
https://docs.yfiles.com/yfiles-html/api/Graph/#Graph-method-adjustGroupNodeLayout
They document the grouping concept in yFiles for HTML, not a WPF parity certification.

## Portable and Uno APIs

```csharp
var options = new GroupSizingOptions(
    Padding: 28, HeaderHeight: 20, MinimumWidth: 80, MinimumHeight: 60,
    IncludeLabels: true, EnlargeOnly: false);
var result = GraphGroupSizing.Calculate(snapshot, selectedIds, options, measureLabel);
// A headless call without measureLabel uses GraphScene.EstimateLabelSize.
editorSession.FitGroups(options); // uses the session's current label-size provider
// Same command through the reusable Uno control:
graphControl.ArrangeNodes(GraphArrangeCommand.FitGroups);
```

The result reports eligible/resized groups, examined nodes/labels, measured labels and
maximum explicit stack depth. Source snapshots and input collections are never modified.
No-op results retain the original snapshot identity. The session checks document revision,
snapshot, immutable selection identity and font-measurement provider before publishing one
undo entry. Callback-triggered selection, document or font changes reject stale results.
Cancellation and malformed geometry cannot partially publish a frame update.

## Geometry contract

The fitted rectangle encloses visible descendant node frames and, by default, their
independent **node and port annotations**, using the actual host's measured text size.
The normal padding applies on all four sides; HeaderHeight is extra space above the
contents. Minimum dimensions expand to the right/bottom. EnlargeOnly unions the fitted
rectangle with the previous group frame. The selected group's own annotations are excluded
from its calculation to avoid self-dependent growth.

Selected nested frames are updated before their enclosing frames. Node- and port-owned
annotations on those nested groups use the updated frame's center/relative port position.
Collapsed groups contribute their displayed frame and own annotations, never hidden
children. Unselected nested groups retain their existing rectangle; visible descendants
outside that rectangle still contribute to a selected ancestor's envelope.

This is **node-envelope fitting**, not edge routing or full-scene export bounds: arbitrary
edge bends, edge captions/annotations, stroke decorations, selection adorners and the
fitted group's own labels do not expand its frame. Edges, ports, label offsets, child
records, topology and styles are retained. Group-owned relative ports and annotations
naturally follow the changed frame. No automatic layout, ancestor resize or camera Fit is
performed. The browser's node/port font metrics are used even below their drawing LOD.

## Performance and validation

Traversal roots are disjoint. Ancestor decisions are memoized, and explicit stack frames
accumulate child geometry bottom-up. Every visited visible node is finalized once, even
with thousands of selected nested groups. Each participating annotation is measured once.
Only the active ancestor stack and changed group records are retained during traversal,
not a rectangle list for each group. Optional annotation indexing scans the document's
labels once. A changed result still copies the immutable node array and undergoes normal
whole-document validation and editor/renderer invalidation; these costs are not constant.

Twenty portable regressions cover fixed positions, nested order, updated annotation anchors,
locked/collapsed/empty groups, unselected ancestors, margins/minima, no-op history, invalid
IDs/metrics, overflow, cancellation and reentrant changes. A straightforward repeated-index
oracle checks independent nested results. A 200-level allocation contract compares against
that oracle (not a previously shipped group-fit implementation). The 5,000-level test
requires 5,001 finalized nodes and one annotation measurement without recursive calls.

`tests/browser/group_sizing.py` runs after the existing arrangement suite and propagates
failure. It uses real pointer, keyboard, dropdown and file input; it compares nested frames
to measured annotation bounds, checks all three command surfaces, disabled states, exact
undo/redo, unchanged camera/content, cached availability and narrow-window save/import.
Package-only consumers execute the portable API and compile the control surface for both
Uno targets. Inventory is not test evidence: successful runs must be attributed to the
specific head tested. CPU work/allocation counts do not certify physical GPU throughput.

Full yFiles WPF API, styles, layout quality, docking/multi-document, printing, GraphML
extensions and accessibility parity remain ongoing work. No source/binary/screenshot
equivalence, physical touchscreen test or native desktop GUI certification is claimed.
