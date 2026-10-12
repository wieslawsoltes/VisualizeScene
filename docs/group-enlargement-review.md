# Enlarge-only groups and scoped annotation storage

The compact frame tools, Arrange menu and graph context menu now offer **Enlarge groups
to children** in addition to Fit. Both share the portable group-fitting implementation;
Enlarge sets `GroupSizingOptions.EnlargeOnly` and unions the fitted frame with its old
rectangle. Existing space remains. Eligible groups are still visible, expanded, unlocked
and nonempty. Selected nested groups are processed bottom-up. Child records, ports,
annotations and bends are not rewritten, and an unselected ancestor remains unchanged.

The new original vector icon adds outward diagonals to distinguish enlargement from
fitting. Its name/tooltip and menu caption are explicit rather than relying on the icon.
There is no new renderer or alternate HTML editor. The reusable API is
`graphControl.ArrangeNodes(GraphArrangeCommand.EnlargeGroups)`; direct editing-package
clients can continue to call `editor.FitGroups(new(EnlargeOnly: true))`.

## Scoped annotation allocation

The preceding implementation measured only relevant labels, but first allocated a
bucket/list for every visible node's annotations, including unrelated graphs. The new
filter stores only annotations on strict descendants of eligible selected groups.
Ancestor membership reuses the iterative, memoized root-discovery paths. Root-owned
annotations are excluded; an annotation owned by a selected nested group still counts
for its selected ancestor. Unrelated top-level label owners require no memo entries.

This still scans all document labels once. It is an allocation reduction, not a
sublinear label query or allocation-free operation. Deep unrelated ancestor paths can
still require memo entries. Optional font shaping, immutable result publication and
normal document indexes retain their own costs.

A warmed regression compares one fitted group alone and the same group beside 10,000
unrelated node annotations: scratch allocation may differ by at most 4 KiB. Both calls
must return the original snapshot, visit exactly two selected-subtree nodes, measure
one annotation, and report the actual full label scan. Separate tests cover unrelated
nested/root annotations, selected nested-group annotation anchors, and enlarge-only
union/no-op behavior. Existing 5,000-deep traversal and independent-oracle tests remain.

The identical-harness benchmark pins PR #15 head `5899c3d901cd263a375a6082bcc4aeba165ddb3d`
and adds `group_fit_scope_10000`. Both sides use the same fixed metrics and already-warmed
indexes, with an exact no-op identity check before measuring. Timings and allocation
include the returned result record. The fixture has 10,002 nodes, 9,900 edges, 10,000
ports and 10,001 labels; only one child annotation participates. Full reports preserve
all other workloads and the common raster hash. No GPU/FPS or general latency claim.

Five new compiled Uno browser scenarios execute toolbar/menu/context enlargement,
compare explicit nested rectangles, preserve camera/data, test exact undo/redo and
no-op reapplication, check disabled selections and exercise the compact control at
640-pixel width. They run after the original group suite and propagate failures.
Package-only Uno consumers compile the new public enum and command. Passing CI must
be attributed to the exact tested revision; test inventory alone is not evidence.

These are original implementations of the group-adjustment concept described in the
yWorks grouping tutorial and `adjustGroupNodeLayout` API, not WPF source/API/screenshot
parity: https://www.yfiles.com/demos/tutorial-yfiles-basic-features/08-grouping/ and
https://docs.yfiles.com/yfiles-html/api/Graph/#Graph-method-adjustGroupNodeLayout.
