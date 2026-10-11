# Selection contracts

## Reusable API

Selection belongs to `EditorSession`, not `GraphSnapshot`. It does not modify graph content, dirty state or undo history. The same session implementation handles the Uno desktop control, the WebAssembly control and headless tests.

`Selection` is an immutable published snapshot: retaining it across later changes is safe, and it cannot be downcast to a mutable set. `ApplySelection` materializes its input before changing membership, making aliases such as `session.Select(session.Selection)` and lazy filtered selections safe. Duplicate IDs toggle once. Unknown IDs and items hidden by folding are ignored. Enumeration errors or a document revision changed during enumeration reject the operation before selection publication. Unchanged membership does not raise redundant selection events.

```csharp
session.ApplySelection(ids, SelectionOperation.Add);
session.SelectAll(SelectionKinds.Labels);
session.InvertSelection(SelectionKinds.Nodes);
session.SelectArea(new RectD(0, 0, 400, 300),
    AreaSelectionMode.Contains,
    SelectionKinds.Nodes | SelectionKinds.Labels);
var hits = session.QueryArea(rectangle, AreaSelectionMode.Intersects);
session.SelectNeighbors(NeighborDirection.Outgoing, depth: 2, includeEdges: true);
```

Masks independently distinguish nodes, groups, edges, ports and independent labels. Inversion toggles only the requested kinds, preserving selected items of other kinds. `SelectAll()` includes structurally visible ports and labels. Built-in node/edge captions are part of their owner, not independent selectable objects.

## Area geometry

Queries use the existing node, edge and measured-label spatial indexes plus a port index. Node intersections use actual filled shapes rather than a bounding-box-only test. Containment requires the entire node box. Expanded groups match their header and border in intersection mode; selecting an empty part of a group's interior does not select the group frame. Folded child nodes, ports, labels and internal edges are excluded.

Edge intersection tests traverse the actual polyline segments, rejecting empty portions of a route's bounding box. Containment requires every path vertex inside the rectangle. This is centerline geometry, not a pixel mask for arrowheads or stroke thickness. Ports use their painted circle radius converted from view units through the current zoom. Independent labels use the same measured bounds supplied to the editor by the Skia renderer; marquee queries omit labels below the renderer's detail threshold.

The public geometry helpers validate finite coordinates and extents, handle tangency and degenerate segments, and normalize extreme finite segments to prevent subtraction overflow. Query results are deterministically sorted by item ID. `QueryArea` accepts an explicit intersection or containment mode; direction is resolved only by a pointer drag.

## Gesture lifecycle

Pressing empty canvas starts a selection gesture without clearing the committed selection. Pointer movement calculates `DisplaySelection`, which the Uno renderer highlights. `Selection` remains unchanged until release. Each preview is computed against the original selection, not accumulated across intermediate rectangles.

Shift adds, Control toggles and Control+Shift subtracts. An unmodified marquee replaces selection. The default mode is intersection and includes nodes, edges, ports and labels; group selection is available through the API and Select groups command. Directional mode uses containment when dragging right and crossing when dragging left. Containment is drawn with a solid blue outline; crossing uses a dashed green outline.

Release publishes the final preview once. Escape, pointer cancellation, document changes and interaction-mode changes discard it. Returning to the initial pointer position after dragging restores the original preview and does not clear selection on release. An unmodified stationary empty-canvas click clears selection. Deselecting the drag anchor with a modifier cannot move another selected item.

## Topology expansion

Incoming, outgoing and bidirectional expansion use visible graph topology with a configurable breadth-first depth. Undirected edges work in either traversal direction. Selected independent labels and ports contribute their owners as seeds; selected edges contribute both endpoints. Folded external edges connect their visible group representatives. A visited set terminates cycles, and callers can omit traversed edges from the result. Existing selection is retained.

## Workbench and validation

The Selection menu exposes type filters, inversion, clearing, neighbor expansion and marquee mode/filter presets. Ctrl+A selects all, Ctrl+I inverts and Ctrl+Shift+A clears. These graph shortcuts do not intercept typing inside the label editor.

`SelectionTests` covers aliasing, failed/stale enumeration, immutable event snapshots, masks, geometry, folded graphs, previews, modifiers, cancellation, topology and a 10,000-node indexed fixture. `tests/browser/selection.py` imports a real file and drives the compiled Uno control through pointer, menu and keyboard input. Each scenario asserts graph content and revision remain unchanged. The package-only consumer also exercises alias replacement, measured area selection and topology expansion through NuGet references.

The implementation does not add lasso selection, arbitrary style hit-test adapters, keyboard spatial traversal, full accessibility graph peers or the complete yFiles input-mode/decorator API. These remain separate parity work.
