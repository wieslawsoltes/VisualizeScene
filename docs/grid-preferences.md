# Grid preferences and lattice-correct rendering

**View → Grid settings…** opens a compact native settings dialog, using the reusable
`GraphGridEditor` property grid and a live native Skia preview. The fields stage
horizontal/vertical spacing, X/Y origin, five display patterns, #RRGGBB color,
0.25–4 screen-unit thickness, independent visibility/snapping, selected snap axes
and the node anchor. Apply validates and publishes the complete configuration;
Cancel/Escape discard it. Reset is also staged until Apply. Invalid input keeps
the dialog open. The editor parses decimal-point numbers using invariant culture.
The preview is at 100% zoom, not a duplicate graph editor. Preference rows and
preview scroll inside the reusable control; validation feedback and Reset remain
in a separate fixed footer. The dialog supplies a finite editor height and adjusts
it when the window changes, leaving the native Apply/Cancel row outside both.

The dialog restores focus after closing and rejects stale settings/session
replacement before applying. A native flyout closes before opening the modal
dialog. Configuration updates cancel any old drag/edge preview, but do not edit
the graph, selection, saved marker, camera or undo/redo history. Importing graph
data or replacing a GraphControl document preserves the control's grid settings.
They are session/view preferences, not graph JSON/GraphML fields or restart-persisted
application preferences. The historical default remains top-left node snapping;
center snapping is an explicit choice for creation and node translation. Resize
corners, bends, ports and label anchors keep their existing anchor conventions.
Magnetic item snapping remains a separate option and may override the grid.

## Reusable APIs

- Core: immutable `GraphGridSettings`, `GraphGrid.Snap`, `GraphGrid.Plan`,
  `GridAxisPlan`, `GraphGridPlan`, `GridPattern`, `GridSnapAxes`, `GridNodeAnchor`.
- Editing: `EditorSession.GridSettings`; existing `ShowGrid`/`SnapToGrid` forward
  to the configuration. `GridSize` sets both spacings; its legacy scalar getter
  returns horizontal spacing when using a rectangular grid.
- Rendering: standalone `GraphGridRenderer` draws onto caller-owned Skia canvases;
  `RenderOptions.GridSettings` configures the integrated graph renderer while
  preserving the original positional RenderOptions constructor. Null retains
  legacy `GridSize`; `ShowGrid=false` and overview rendering omit the grid.
- Uno: `GraphControl.GridSettings`, `GridStatistics`, and disposable staged
  `GraphGridEditor.LoadSettings` / `TryReadSettings` / `FindElement`.

PNG and SVG export remain diagram-only; editor grid preferences are not exported.
The five patterns are dots, crosses, full lines, horizontal lines and vertical
lines. Display pattern and snapping axes are deliberately independent.

## Correctness and performance

The old grid renderer halved large world spacing until marks were at most 45
screen units apart. At high zoom that invented points that were not on the
snapping lattice. The new planner **never subdivides** the configured spacing:
it only removes points at power-of-two intervals when too dense. Displayed marks
remain on the same origin-relative lattice. Zooming out may hide intermediate
snap points; it never changes the actual snapping interval.

Planning uses exponent arithmetic rather than unbounded scaling loops, and
reduced origin phases rather than subtracting opposite huge world coordinates.
Each axis has at most **256** sampled marks, hence at most **65,536** dots/crosses
or **512** full-grid line primitives. Planning requires no graph scan or per-frame
managed arrays. A single bounded native path is reused for unchanged sampled
geometry; color changes and hidden→visible transitions can reuse it. Pattern,
phase, relevant spacing, dimensions and dot diameter changes rebuild it. The main
graph's spatial indexes are not invalidated by a grid repaint.

The same-source benchmark is pinned to pre-grid main
`401df2f58d44440a1694a714b1c6871fa3710d98` and adds an empty 800×400
viewport grid-only workload, separately reporting warm, pan and cold paths. The
ordinary graph/caption pixel gates remain enforced. The zero-origin default-grid
pixel hash is also reported; high-zoom visual changes are intentional corrections.
The full report retains regressions as well as improvements. The app workflow's
timeout increases from 25 to 30 minutes to accommodate the additional native
browser suite without dropping existing tests.

These are explicit work/storage bounds, not a hardware GPU or frame-rate claim.
Native path storage, antialiasing and actual raster/GPU rendering still have a
cost. Binary64 rounding applies. Snapping keeps midpoint-to-even ties and throws
when the requested result cannot be represented finitely; it does not return
NaN/Infinity to graph mutations. Extremely large viewport/spacing combinations
that cannot yield a finite coarsened interval suppress that sampled axis and set
its `Suppressed` flag. Native screen dimensions must fit finite single precision.

## Validation

`tests/VisualizeScene.Grid.Tests` contains portable lattice/input tests, an
independent finite-range lattice oracle, large/subnormal-number work bounds,
configuration atomicity and stale-preview cancellation, exact undo, and native
Skia tests for all patterns, high-zoom marks, cached/fresh pixel equality, caller
matrix/clip preservation, invalidation and graph-index reuse. The existing suites
remain unchanged and enabled.

`tests/browser/grid.py` uses actual compiled Uno pointer, combo, dialog, keyboard
and file input. It verifies staged Cancel/Reset, invalid Apply, full configuration,
all patterns, node-center snapping, independent axes with a hidden grid, high-zoom
spacing, geometry reuse, native save/import and a 640-pixel workspace. Diagnostics
only observe native state; they do not invoke commands or alter graph data.
Package-only consumers execute the portable/session/Skia APIs and compile the
Uno APIs outside the source tree.

Local Python syntax, native-menu observation and whitespace checks are separate
from managed/native/browser execution in GitHub Actions. Exact final-head run
results and any failures are recorded in the PR rather than inferred from test
source. Local browser navigation restrictions are not bypassed.

## Design references and scope

This is original implementation code; no proprietary icons, templates or demo
source were copied. The compact categorized grid editor follows this project's
published WPF GraphEditor visual reference:
https://marketplace.visualstudio.com/items?itemName=yWorks.yFilesWPF

Public yWorks grid documentation describes independent visibility/snapping,
origin and separate spacings, display patterns and snap-axis configuration:
https://docs.yworks.com/yfiles-html/api/GridInfo/
https://docs.yworks.com/yfiles-html/api/GridStyle/
https://www.yworks.com/demos/application-features/grid-snapping/

Those references describe related yWorks behavior; this does **not** establish
WPF API/template/screenshot parity. Full docking, specialized styles, layout,
printing, accessibility and physical GPU/native GUI certification remain separate.
The previously platform-blocked PR20 edge-toolbar supplement is not included.

## Native dialog validation review

At head `65206b38ff9157a1cb96fab6223863f468d08eb3`, Build run
`38463526706` passed 654 XML cases and the downloaded workspace reports contain
135 passing existing browser scenarios, but the new grid suite failed after
seven passing scenarios. Its retained artifact
`11848193020` shows Reset partly clipped at the bottom of the outer 550-unit
ScrollViewer. The button's native PointerEntered never reached the requested
center, so the existing input observer correctly failed instead of clicking
through the clipping region. This was a real layout defect, not a timing allowance.

The recovery removes the outer scroller, gives the editor its own bounded body
scroller and pins feedback/Reset outside that body. Window-size changes adjust
the editor's finite viewport. Browser assertions retain all prior setting,
preview, exact-data and undo checks, additionally require footer containment and
stable actions during validation errors, and exercise resize/scroll/reset/cancel
at 640×480. Grid artifacts are uploaded immediately after that suite so later
workspace checks do not delay failure diagnosis. No failed-job retry or removed
native-input assertion is used. Final-head execution remains separately recorded
in the PR. Extreme standalone grid-planner tests do not expand the graph renderer’s
existing world-coordinate range.
