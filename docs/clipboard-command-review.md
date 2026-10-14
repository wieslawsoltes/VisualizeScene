# Clipboard command and nested-copy review

## User-facing behavior

Edit and the canvas context menu expose Cut, Copy, Paste, Paste in place, and
Duplicate through the same reusable GraphControl command path as the compact
Cut/Copy/Paste toolbar. Commands disable when they have no valid source or
paste destination. Native menu closure restores graph keyboard focus. Copy
updates command availability without editing the graph or waiting for a frame.

Graph-focused shortcuts are Ctrl+C / Ctrl+Insert, Ctrl+X / Shift+Delete,
Ctrl+V / Shift+Insert, Ctrl+Shift+V for Paste in place, and Ctrl+D for Duplicate.
Native text inputs and modal label editing retain their text-editing behavior.
The existing original vector toolbar icons are retained; no extra toolbar row
or proprietary icon assets are added.

A new Copy or Cut resets ordinary paste placement. Successive implicit pastes
use offsets (30,30), (60,60), and so on from the copied coordinates. Undo/redo
restore exact graph snapshots but do not rewind this clipboard sequence.
Paste in place uses the original graph coordinates and does not advance the
sequence. Duplicate uses one (30,30) offset and does not replace the clipboard
or advance its next paste. Empty Copy/Duplicate are inert; empty Copy does not
clear previously copied data. Explicit `EditorSession.Paste(data)` preserves
the former one-offset (30,30) behavior and does not advance the session sequence.

Independent-label-only Copy remains independent: a single selected owner is
the destination. A selected pasted label resolves to its owner, so subsequent
pastes do not jump back to the original owner. With no single destination,
original owners must still be visible. In-place labels retain their copied
**relative offsets**, not a fixed world location when their owner changes.

The clipboard is session-local, not the operating-system clipboard and not a
shared cross-control clipboard service. Import into the same document/session
retains it; replacing GraphControl.Document creates a new EditorSession and
therefore an empty clipboard. No browser clipboard permission is requested.
Arbitrary paste-at-cursor placement is not implemented by this command batch.

## Correctness and publication

Cut captures the selected subgraph and removal result before one document
transaction. Copy includes selected owners, group descendants, internal edges,
ports and independent labels in original drawing order. New IDs remap parent,
endpoint, port and label relationships; stored bends translate once. Position
locks retain the preceding deletion semantics; this change adds no new delete
permission policy.

The portable Paste helper preflights finite translated node bounds and stored
bends before mutating a caller-owned GraphEdit. Session pastes build and validate
an isolated proposed snapshot before publication. Failed pre-commit operations
do not consume a progressive offset. Post-commit subscriber exceptions do not
silently undo completed cuts. Reentrant document or selection changes are not
overwritten with stale pasted IDs. Clipboard state and its cached CanPaste value
are invalidated by immutable selection/snapshot identity and clipboard revision.
These UI-owned sessions are not a concurrent transaction service.

## Performance

Previously, selecting nested groups repeatedly traversed shared descendants.
Copy now uses a queue and an ordinal membership set: each included node's
children are expanded once, and each source collection is scanned once while
preserving order. With prepared validation/index caches, traversal is linear in
selection plus included descendants plus source records, with set/queue/output
storage proportional to included items. Cold index/validation costs remain;
copying and subsequent immutable graph publication are not allocation-free.

The workflow runs identical benchmark sources against pinned PR23 recovery
head `520c24fa3cad465bc56dc1c2ba1738c5ee5a313e` and this implementation on
one runner. It adds all-selected 1,000-deep groups and a 10,000-node graph with
9,900 edges, 10,000 ports and 10,000 labels. Graph/caption/default-grid pixel
hash gates and all prior workloads remain. Timings and current-thread managed
allocations are descriptive, not physical GPU/FPS or input-latency results.

## Validation

Eighteen portable cases include a frozen independent legacy-copy oracle across
200 shuffled selections, a 10,000-deep hierarchy, cancellation, exact metadata
and undo/redo, progressive and in-place placement, label destinations, preflight
overflow, nested-transaction rejection and reentrant/throwing subscribers.

Eight compiled-Uno browser scenarios use real native pointer, keyboard, menu,
file-chooser and download input. They check disabled commands, immediate copy
availability, sequence/undo, in-place and Duplicate, alternate shortcuts,
context commands, label ownership and the 640-pixel Edit menu. The diagnostics
are read-only; they do not implement or invoke the commands. Isolated package
consumers execute the portable API and compile the Uno API on desktop/browser.
These are authored gates; final pass counts and run IDs are recorded in the PR
only after inspecting the corresponding artifacts.

Local Python syntax, native-menu observer and whitespace checks are available.
No local .NET/browser execution is claimed in this environment. CI uses compiled
Uno and software-rendered Chromium, not physical GPU or native desktop GUI input.

## Reference and boundaries

Public interaction references, not implementation source:
- https://www.yfiles.com/the-yfiles-sdk/dotnet/yfiles-wpf/changelog
- https://docs.yworks.com/yfiles-html/dguide/interaction-support/
- https://docs.yworks.com/yfiles-html/dguide/_location_of_pasted_items

This is original behavior-oriented implementation, not full yFiles API/template,
layout, docking, printing or accessibility equivalence. The historical PR20
platform-blocked edge-toolbar supplement is separate and remains unpublished.
