# Atomic folding and explicit group commands

The workbench's Fold toolbar button previously looped over the selected groups and
called `ToggleGroup` for each. Every call published a separate revision, rebuilt the
editor indexes, and selected that one group. Multiple selected groups therefore did
not act like one user command: one Undo restored only the last change, and intermediate
visibility/selection changes affected the rest of the operation.

The toolbar and graph context menu now share `GraphControl.FoldGroups`. **Arrange →
Expand selected groups** and **Collapse selected groups** supply explicit operations
for mixed selections. All three operations capture visible selected groups before
changing any flags and publish one complete snapshot. A selected child group is still
included when its selected parent collapses in that same operation. Already hidden
groups are excluded; expanding a parent does not recursively reset a hidden child's
own folding flag. Lock position does not prohibit hierarchy expansion. Empty groups
can fold, unlike the separate fit-to-children command.

```csharp
var result = GraphGroupFolding.Calculate(snapshot, selectedIds, GroupFoldOperation.Collapse);
editorSession.FoldGroups(GroupFoldOperation.Toggle);
graphControl.FoldGroups(GroupFoldOperation.Expand);
```

Only `GraphNode.Collapsed` changes. Geometry, child records, ports, labels, edge routes
and styles retain their records. Existing projection determines which edges are visible
and how hidden endpoints attach to a visible group. Selection is pruned only after the
single commit, and keyboard item focus is moved to the visible representative as usual.
The camera is not fitted or reset. Undo/redo restores the entire graph transaction;
selection itself is not part of snapshot history.

Unknown IDs, invalid enum values, null arguments and cancellation are diagnosed before
publication. No-op calls return the input snapshot and do not create history or discard
Redo. Revision/snapshot/selection checks reject stale session calculations. A reentrant
post-publication edit is retained and invalidates the last-operation result instead of
being overwritten. The control cancels unfinished pointer/touch previews before editing.

## Performance and UI state

The kernel stores only selected IDs and changed records. A changed batch copies the
node collection once and performs normal full-graph validation/publication, rather
than publishing one graph per group. `ExaminedNodes` counts the copy pass, not all work
inside document indexes or validation. A warmed no-op with 10,000 unrelated nodes must
use no more than 4 KiB extra scratch memory versus a small graph. No timing speedup is
claimed from that allocation contract.

Expandable/collapsible counts are computed in the existing single selected-item pass
of GraphArrangeState. The workbench uses that cached state for the new menu commands
and Fold toolbar button. Camera-only changes do not recompute availability. A leaf-only
selection disables all folding actions. Explicit menu commands restore graph keyboard
focus after native dismissal.

## Validation

Twelve portable regressions cover captured nested/mixed selections, duplicates, locks,
empty and hidden groups, no-op/redo preservation, exact one-transaction undo/redo,
cancellation, invalid input, availability, allocation and reentrant publication. Seven
compiled Uno browser scenarios drive the existing toolbar, the two new menu entries,
and the context command with real pointer/keyboard input. They preserve original model
and camera assertions and cover narrow UI access. Package-only consumers execute the
portable transaction and compile the control surface for both Uno targets.

The public yWorks grouping/folding tutorials establish the requested interaction concept,
not this implementation's WPF API compatibility:
https://www.yfiles.com/demos/tutorial-yfiles-basic-features/08-grouping/
Full yFiles folding-manager/proxy APIs, group navigation, alternate collapsed dimensions,
WPF style/template equivalence and accessibility parity remain separate work.
