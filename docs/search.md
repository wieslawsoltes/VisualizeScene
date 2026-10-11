# Graph Find and Replace

`VisualizeScene.Editing.GraphSearchIndex` is a reusable immutable registry over one
snapshot. It searches node and edge captions, independent annotations, and optionally
item IDs (including ports). `GraphSearchControl` in Controls.Uno is a modeless control
that binds this engine to a `GraphControl`. It is used by the actual Uno workbench,
not a parallel JavaScript implementation.

## Behavior

Open **Edit → Find graph text** / **Replace graph text**, or focus the graph and press
Ctrl+F / Ctrl+H. Enter or F3 finds the next occurrence; Shift+F3 goes backward;
Escape closes the panel and returns graph focus. Navigation wraps. Result selection
reveals the item without editing graph data. Hidden matches show their visible folded
representative rather than expanding groups. Independent-label navigation raises a
low-detail viewport to zoom 1 so the selected annotation is actually painted.

Literal text matching uses ordinal comparison, optionally case-sensitive or whole-word.
Whole-word boundaries recognize Unicode letters, digits, combining marks and connector
punctuation. UTF-16 surrogate pairs are never split; match offsets are UTF-16 indices.
This is not Unicode normalization, locale collation, regex search or full grapheme-aware
substring matching. Replacement strings are literal, including dollar signs/backslashes.

The panel can search hidden items, IDs and selected item kinds. Captured selection scope
is copied when enabled or explicitly recaptured; result navigation does not silently
change the search scope. IDs are read-only. Text owned by locked nodes/ancestors, or
edges touching locked owners, is also read-only and reported as skipped. Hidden text is
replaceable only when explicitly included and not locked. Other editing commands retain
their existing lock policies; this is the search command's documented policy.

Search follows document collection order: nodes/groups, edges, ports, annotations; text
before ID within each item. Repeated matches are non-overlapping. Query length is capped
at 4,096 UTF-16 units. Default limits are 10,000 results and 16 x 1,048,576 scanned UTF-16 units;
the engine supports 100,000 results / 64 x 1,048,576 UTF-16 units with explicit options. Exact result limits
are not reported as truncated unless another eligible match exists. Replace All rejects
truncated results; replacing one listed result is still permitted. Character-budget
exhaustion is diagnosed, not silently treated as complete.

Replacement validates exact snapshot identity and an optional revision lease. It builds
all new text before one document transaction, preserving topology/geometry and exact
undo/redo. Same-value replacements do not create history. A single replacement string
is limited to 1 x 1,048,576 UTF-16 units, each rewritten field to 16 x 1,048,576,
and total rewritten output to 64 x 1,048,576. These are code-unit, not byte budgets.
Cancellation, invalid options, stale results or output-budget errors do not partially
edit the graph. Registry creation is graph-sized; each query scans eligible text. This
is not a token-based inverted index or a sublinear full-text database.

## Reuse

```csharp
var index = new GraphSearchIndex(document.Snapshot);
var results = index.Find("Start", new GraphSearchOptions(WholeWord: true));
var revision = document.Revision;
var changes = results.Replace(document, "Begin", expectedRevision: revision);
// One undo entry for all actual changes. IDs and locked owners are skipped.
```

```csharp
var search = new GraphSearchControl { Target = graphControl };
// Add search as a sibling overlay, not a child inside the graph input control.
search.Open(showReplacement: true);
// Dispose when the owning view closes; Target can be rebound independently.
```

The Uno panel debounces requests by 200 ms and uses cancellation plus generation,
snapshot and revision checks to reject superseded completions. Textbox shortcuts remain
in the panel rather than leaking into graph delete/nudge commands. A virtualized list
shows bounded previews. On WebAssembly without threading, Task.Run is not a guarantee
of background CPU execution; large searches can still occupy the browser UI thread.

## Validation

The Search.Tests executable covers scope, Unicode, locks, hidden owners, IDs, limits,
no-op/stale/cancelled replacements, concurrency, exact undo and 10,000 text fields. Cold
validation/index tests share that executable. `tests/browser/search.py` drives the
compiled Uno app through real menu, keyboard, pointer and upload-input operations;
diagnostics are read-only. Package-only consumers exercise both engine and control APIs.
Read the final Actions run for the exact tested revision and outcomes. Browser tests use
Chromium/SwiftShader, not physical touchscreen or hardware-GPU certification.
