# Compact property editing and numeric review

## Workbench and reusable UI

The recovered classic shell is integrated, not a separate preview. `GraphPropertyInspector`
now uses an original two-column property grid, compact category headers and expandable
Label/Appearance/Geometry/Data groups. `EditorPropertyGrid` and `EditorColorField` are
reusable Uno controls with no graph dependency. Category expansion survives inspector
row rebuilds. Color palettes are created on demand, not once for every selection refresh.
A swatch changes the editor's literal `#RRGGBB` text; only **Apply properties** changes the
graph. Invalid style or geometry values reject the entire transaction. The inspector
retains the document/revision guard, round-trip numeric formatting and exact undo.

The design follows the density and arrangement of the yWorks WPF graph-editor reference
linked in `classic-shell.md`. All controls and icons are original. This is not screenshot,
XAML template or WPF API equivalence. The grid handles this editor's small property sets,
not a general reflection-driven or virtualized thousands-of-properties browser.

```csharp
var grid = new EditorPropertyGrid();
grid.AddGroup("Appearance");
var fill = new EditorColorField { Value = "#FFCC66" };
grid.AddProperty("Fill", fill);
grid.SetGroupExpanded("Appearance", false);
```

`EditorSession.SetPortLocation` sets exact relative coordinates, avoiding precision loss
from converting to large world coordinates and back. It obeys immovable-port and owner
position locks. `SetLabelProperties` updates text, offsets and font size together; offset
changes respect the same owner locks as pointer movement. Node position locks do not
prevent text/style edits. Label offsets, font size and relative port coordinates have
separate editor identities, rather than ambiguous IDs derived from the first display word.

## Saved document state

`GraphSavePoint` records the immutable snapshot actually persisted. The workbench dirty
marker and discard prompt compare the current snapshot with it. Undo/redo can return to
the saved state, and a save completing for an earlier snapshot cannot incorrectly mark
newer edits clean. The host marks new/loaded documents explicitly; asynchronous font
initialization does not mark subsequent user edits as saved.

```csharp
var saved = new GraphSavePoint(document);
var writing = document.Snapshot;
// Persist writing successfully, possibly asynchronously.
saved.MarkSaved(writing);
var hasUnsavedChanges = saved.IsDirty;
```

The save point intentionally retains one snapshot and uses identity, not serialized
content equality. A separately constructed equivalent snapshot is not automatically
recognized as a persisted one. This keeps camera/status checks constant-time and avoids
per-frame graph serialization. Failed or canceled saves do not advance the save point.
The current input mode caption also follows Escape-driven mode changes.

## Numeric correctness and performance

PageRank now uses a compressed adjacency array and two reusable rank buffers rather than
rebuilding string dictionaries every iteration. It preserves ordinal IDs, parallel arcs,
undirected-edge semantics and dangling mass. It remains unweighted, with the existing
convergence tolerance and iteration limit. Cancellation is checked during setup and the
iteration loops; checked arc counts diagnose array-range overflow.

Maximum flow reuses queue, predecessor and visited storage between augmentations.
Each original edge owns its own forward/reverse residual pair. Net edge flow is read from
the reverse residual rather than `capacity - remaining`, which loses a small flow when
capacity is much larger (for example, a flow of 1 through a capacity of 1e25). Parallel and
antiparallel edges remain independent, and self-loops carry no augmenting flow. Tiny
positive and subnormal capacities are retained. Accumulated flow overflow is diagnosed.
These algorithms still use binary64 arithmetic; arbitrary precision and all possible
ill-conditioned networks are not claimed.

The added numeric tests compare PageRank with an independent dense transition-matrix
oracle on 80 mixed multigraph fixtures. Maximum flow is compared against exhaustive
source/sink cuts on 100 small random graphs, checking bounds and conservation as well as
the total. Additional tests cover cancellation, nonfinite inputs, independent results,
subnormal capacities, huge-capacity bottlenecks and iteration-independent allocation.

## Measurement and validation

The same-runner benchmark now pins PR #6 merge
`3e674fe8a27e5495abb06ab50f36cb77df7862b6` as its baseline and builds exactly the same
harness against both revisions. It adds PageRank for the 10,000-node / 9,900-edge fixture,
80 maximum iterations and tolerance 1e-20. It retains all existing graph, spatial,
rendering, editing and cold-preparation workloads. Reports include median/p95 wall-clock
CPU timings and current-thread managed allocation. A renderer pixel hash covers the
shared raster fixture. Timing differences in unchanged workloads are runner variation,
not evidence that the property grid accelerates the main canvas. Physical GPU/FPS/input
latency measurements are not part of this test.

The real Uno workspace browser suite imports no alternative graph engine. Its inspector
scenarios use actual pointer/keyboard input for category expansion, atomic property edits,
invalid-color rollback, color swatches, independent-label and port fields, save/undo/redo
state and the Escape caption. Screenshots include the grid and the palette flyout.
Package-only consumers exercise the new save-point API and compile the reusable controls
on desktop and WebAssembly. Passing results must be attributed to the exact final head.

## Review boundary

This batch reviews and changes the numeric, property-editor and saved-state paths above;
existing regression gates still cover the other modules. It is not an exhaustive proof
of a bug-free toolkit. Full yFiles API/layout-quality parity, additional specialized styles,
GraphML extensions, printing, docking/multiple documents and complete graph accessibility
remain outstanding. Other algorithms' extreme-value arithmetic and native desktop GUI,
physical touch and hardware-GPU behavior require additional work and validation.
