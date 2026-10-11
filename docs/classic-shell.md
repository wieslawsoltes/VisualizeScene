# Classic workbench integration and recovered numeric fixes

## Recovered local work

The three-file classic-shell patch left outside PR #5 is now part of the workbench source.
It uses the already packaged `EditorWorkspace`, `EditorPane`, `EditorIcon` and
`GraphStructureView` rather than replacing the graph engine with a browser-only mock.
The original numeric-review patch is also included: nonfinite PageRank options reject,
positive capacities below 1e-12 are no longer discarded, overflowing accumulated flow
is diagnosed and graph validation runs before adjacency/flow construction.

This supersedes the unshipped integration boundary recorded in PR #5. The recovered
patches are tested together through this repository's managed and compiled Uno browser
workflows. Results belong to the exact tested revision, not merely this source inventory.

## Original-style UI

The design reference is yWorks' published WPF graph-editor screenshot:
https://marketplace.visualstudio.com/items?itemName=yWorks.yFilesWPF

The workbench now connects a compact, grouped vector-icon toolbar; amber flowchart tiles;
Palette/Structure tabs; collapsible palette categories and overview; resizable left/right
rails; a diagram document header with dirty indication; and exact percentage zoom.
Existing Properties/Layout/Analysis panels, menus, search, export and editing paths remain.
All icons are original code in the reusable control package, not extracted proprietary
assets. The diagram data and six graph layouts are unchanged by the shell integration.

Pane resizing/hiding, outline expansion and graph viewport navigation are view operations,
not graph transactions. A selected palette template feeds the existing undoable node
creation path. Undo/redo and selection commands have enabled-state feedback. Narrow
windows automatically hide rails while preserving the main editor and scrollable toolbar.

This is closer in arrangement and density, not a pixel-identical replica. Native OS chrome,
full docking/floating windows, multi-document tabs, every original style family and the
complete WPF API are not implemented. The graph peer accessibility tree and physical
GPU/touch behavior remain separate validation work.

## Validation entry points

The existing eight browser suites remain enabled. `tests/browser/workspace.py` adds real
pointer/keyboard input for splitters, pane commands, collapse/restore, the integrated
structure view, tile creation, zoom presets, search, cut/undo and responsive restoration.
Read-only workspace diagnostics are available only with `?test=1`; no mutation API exists.
CI retains screenshots and failure state under `browser-workspace`.

Numeric regressions are in `tests/VisualizeScene.Parity.Tests/ReviewTests.cs`. They are
small targeted correctness cases, not a proof of all floating-point conditioning or
numeric-analysis parity. Full yFiles parity remains tracked in `capabilities.md`.
