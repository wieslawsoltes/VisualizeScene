# Mixed property editing, context commands and alignment review

## Reusable batch properties

`GraphPropertySelection` captures unique item IDs and an immutable graph snapshot. One
pass reports common/mixed values and applicable counts for text, node shape/fill/size/
position lock, node/edge stroke, edge routing/direction/weight, and independent-label
font size. Node groups remain nodes for these property counts. Ports have no batch
location field because their coordinates belong to different owner frames.

`GraphPropertyPatch` is sparse: null means leave a field unchanged, while empty text,
false and zero are explicit values. Apply checks the snapshot and optional revision,
validates the patch, and publishes one document transaction. An error on any selected
item rolls back the whole edit. Resizing an already position-locked node is rejected;
unlocking and resizing requires separate transactions. Style/text changes remain
allowed on position-locked nodes. Groups and selected descendants are resized in
place, not translated or scaled. Unselected descendants are not silently edited.

```csharp
var selection = new GraphPropertySelection(document.Snapshot, selectedIds);
var fillIsMixed = selection.Fill.Count > 0 && selection.Fill.IsMixed;
selection.Apply(document, new GraphPropertyPatch(Fill: "#FFCC66"), document.Revision);
document.Undo();
```

The existing Uno property inspector now uses this model for multi-selection. It creates
a fixed set of property rows instead of one set per selected item. Mixed values have
placeholders, indeterminate checkboxes and a distinct mixed-color preview. Only fields
edited by the user are staged. Initialization callbacks do not turn mixed placeholders
into edits. Reset discards staged fields; Clear selected text explicitly stages empty
text. Apply preserves unrelated fields and collections. Both selection identity and
document revision are leased by command handlers so an old editor cannot target a new
selection. These are original controls styled to match the existing compact shell,
not a yFiles WPF API or screenshot-equivalence layer.

## Context-menu corrections

Right-clicking an unselected graph item selects it before the menu is constructed.
Right-clicking a selected item retains the whole multiselection. Blank-canvas requests
clear old selection and disable inapplicable item commands. Shift+F10 and the application
menu key retain the keyboard selection. Opening the menu cancels unfinished editing
previews; create-here uses the menu's graph position. Folding multiple selected groups
uses one transaction. The menu retains a document/session/revision/selection lease,
and closes on selection changes, graph status changes, document replacement or unload.
Stale commands reject instead of applying to a different document or target.

The structure view no longer leaves a stale node highlight for an edge-only selection.
Its node/hierarchy registry and row list are reused for edits that retain the immutable
node collection, including label/port/edge-only changes. Node changes rebuild the rows;
this is not incremental per-node virtualization of the hierarchy model.

## Alignment performance

Previously, Align called GraphEdit.Move for every selected root; each call scanned node
hierarchy and edges. GraphAlignment now discovers selected roots in one hierarchy pass,
computes their translations, applies node changes in one pass and visits edges once.
Selected ancestors own descendant movement, so descendants are not moved twice. Locked
nodes retain positions. Internal bends move only when both endpoints move under the
same selected root; cross-root bends retain the prior command semantics. Equal-axis
sort ties use ordinal ID for deterministic results. No-op results retain snapshot identity.

The work counters cover the alignment traversal, excluding shared index construction,
validation, selection enumeration and the O(R log R) root sort. Full graph indexing,
validation and editor/renderer reindexing are still costs of a changed document. These
changes do not make every graph edit proportional only to changed items.

The same-source/same-runner benchmark pins main `b5999bc6419aad120e53994b6fece8b3f30ebdcf`
as the baseline and adds `align_1000_nodes`. It includes constructing a temporary document
and editor, selecting 1,000 nodes, aligning their horizontal centers and publishing the
result. Every other workload is retained, including cold preparation and raster hash
comparison. Measurements are CPU/raster and managed allocation, not physical GPU/FPS or
input-latency measurements. Use the run's full report, including slower samples, rather
than extrapolating a single fixture to every hierarchy or edge density.

## Validation and remaining scope

BatchPropertyTests cover common/mixed values, applicable fields, sparse edits, explicit
empty/false/zero values, lock rollback, ancestor/child sizes, stale leases, invalid input,
cancellation, exact undo and 10,000-item selections. ContextAlignmentTests exercise
context targeting and cancellation, all eight alignment commands, group ownership,
locked descendants, no-ops and bounded visits on 10,000 roots.

The workspace browser entry point runs the existing shell/inspector tests, then launches
`tests/browser/batch.py` against the same compiled Uno site in a fresh browser. The new
fixture tests actual property inputs, menu actions, rollback and undo. Its reports and
screenshots live under `browser-workspace/batch`; a failing child test fails the workflow.
The existing 39,900-item browser fixture additionally verifies the fixed-size inspector
and unique-item work count. Neither diagnostic object has a graph mutation function.
The package-only consumers execute the new portable APIs and compile the control APIs.

The changes were reviewed in the context of model, editing, controls, rendering,
interchange, layout and workflow contracts. This is not an exhaustive proof of a bug-free
toolkit. Full yFiles API/template/layout-quality parity, specialized styles, complete
GraphML extensions, full docking/multiple documents, printing, graph accessibility peers,
and physical touch/GPU/native desktop GUI certification remain outstanding.

Visual reference: https://marketplace.visualstudio.com/items?itemName=yWorks.yFilesWPF
WinUI context behavior reference: https://learn.microsoft.com/en-us/windows/windows-app-sdk/api/winrt/microsoft.ui.xaml.uielement.contextrequested
