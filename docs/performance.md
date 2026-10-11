# Performance contracts and reproducible measurement

## Shared immutable graph preparation

`GraphSnapshot.Index()` shares an immutable `GraphIndex` by snapshot identity, not
record equality. Validation publishes four read-only identity maps through a
`ConditionalWeakTable<GraphSnapshot, Lazy<...>>`; the index reuses them instead of
rebuilding dictionaries. Both registries use weak keys, so they do not themselves
retain retired revisions. Concurrent callers share one completed validation result.
A changed `with` copy is independently validated, including null records, global ID
collisions, port ownership, label ownership, geometry and hierarchy cycles.

Three-state hierarchy validation uses one reusable path instead of a HashSet per
node. Maps are pre-sized; adjacency uses private small list buckets behind read-only
wrappers. GraphIndex resolves depth and outermost collapsed ancestors breadth-first;
subsequent visibility queries do not repeatedly walk ancestry. Exact snapshot edge
records reuse frozen routes. Replacement edge records sharing an ID, such as a
reconnection preview, bypass stored routes. Warm lookups and color validation avoid
per-call closures/enumerators. GraphDocument and view/editor objects remain single-writer.

Validation-only users now retain four identity maps while a snapshot lives. This is
a memory tradeoff, not free caching. Immutable snapshots intentionally retained by
history remain alive; history limits entries, not bytes. Failed immutable snapshots
remain invalid and publish no completed maps. No mutable dictionaries or adjacency
lists are exposed through the shared public API.

## Editing and input

GraphEdit uses copy-on-write dictionaries: only touched collections are copied,
untouched immutable arrays are retained, and empty/same-value edits preserve identity.
Equivalent bend values do not create no-op history entries. Simple node dragging avoids
intermediate graph snapshots used by compound node/port/label movement; compound
selections still compensate labels for changed owner anchors.

RemoveRange materializes input before mutation, discovers subtree/edge/port/label
cascades once and detaches deleted ports from surviving edges. Deleting 10,000 nodes,
10,000 ports and 10,000 labels examines 30,000 items rather than repeating a full graph
scan for every selected ID. DeleteSelection commits one atomic batch with exact undo.
Default connection rules inspect no edge list; optional degree/parallel/capacity rules
use endpoint adjacency, and cycle prevention traverses reachable topology.

Pointer hits and selected handles query nearby node/edge/port/label candidates.
Selected-handle priority ranks are cached per immutable selection. Node painter order
and port precedence are retained. Edge picking uses reverse document paint order rather
than an accidental spatial-tree order. Keyboard directional navigation still scans its
cached candidate registry once; it is not a logarithmic nearest-neighbor query.

## Packed spatial hierarchy

SpatialIndex stores entries and branches in packed struct arrays. Leaves reference
ranges in shared entry storage instead of owning separate entry arrays. Balanced
half splits bound tree depth. Three-way median partitioning replaces a full sort at
every recursive node; an introspective sort fallback bounds pathological partition
sequences. Equal-coordinate inputs terminate. Invalid and overflowing extents reject.

Query(rectangle, destination) appends into caller-owned storage with no warm iterator,
stack or leaf-array allocation. Statistics report visited branches, examined leaf
entries and results. The iterator overload validates arguments eagerly and returns the
same spatial traversal order; that order is deliberately not a paint-order contract.
Randomized queries are compared with exhaustive rectangle intersection. Dense or
heavily overlapping queries can still return the whole graph. Construction still scans
bounds at each balanced level and is not claimed universally linear or constant-time.

## Rendering and controls

The Skia renderer indexes nodes, routes, measured independent-label bounds and port
positions. It draws only viewport candidates, preserving explicit document edge order
regardless of BVH partitioning. Selection adorners use visible candidates and constant-
time membership tests, not the whole selection. Label-only changes retain node/edge
indexes; unchanged point sequences retain owned SKPath objects even when styles change.
Geometry revisions can still rebuild graph-sized indexes.

Node and independent-label pictures share an LRU entry budget, default 4,096 and
configurable from zero to 65,536. Zero disables retention. Eviction, budget reduction,
removal, folding and disposal release native pictures. Independent labels record the
original world-coordinate commands, avoiding extra float translation/rounding at replay.
Cached and uncached output is compared byte-for-byte at fractional camera transforms.
The budget limits entries, not native bytes; complex text can make entries vary in size.

Normal application mode does not enable the optional test diagnostics. With ?test=1,
the immutable document, routes and measured labels are serialized only when snapshot
identity or font measurement changes; camera, selection and touch state remain current.
There are no test-only graph mutation functions. The new modeless search control uses
its own revision-bound registry, bounded previews and debounced, cancellable requests.
Queries scan eligible text; Task.Run is not a guarantee of off-thread CPU work on
WebAssembly without threading. See search.md for query and replacement budgets.

Layout result mapping uses indexed node lookup, grid columns compute maxima in one
pass, and compound layout uses per-parent child lookup. Compound edge projection still
runs per group; whole-graph layout is not generally linear. Property-panel group moves
translate descendants/internal bends once, preserve numeric precision, and reapplying
unchanged values does not create history. Asynchronous file replacement has a revision
lease; saving an older revision no longer marks newer edits clean.

## Correctness gates

Performance.Tests verifies immutable index identity and concurrent reads, mutation
rejection, weak-key collection, deep hierarchy traversal, copy-on-write snapshots,
bounded broad phases, allocation budgets, cache lifetimes and renderer invalidation.
Search.Tests adds validation-map reuse, null/cycle/ownership checks, no warm route
allocation, query/replacement contracts and document replacement revision leases.

Cached renderer output is compared with a fresh renderer after route styles, port
motion, label metrics, folding, reorder, deletion and undo. The packed-index suite
checks geometry against exhaustive queries and matches topmost pick with a colored
rendered overlap. The existing 10,000-node browser performance suite drives the compiled
Uno app through actual input, including Select All, bulk deletion, undo and fractional
property editing. Matching native Skia/HarfBuzz assets are supplied on Linux.

The main build runs these gates, actual browser scenarios and fresh package-only
consumers before Pages deployment. Physical touch devices and native desktop GUI
interaction require independent testing; compilation is not execution evidence.

## Same-runner comparison

Navigation and performance checks out pinned baseline main
`36154e0f5bcfea41a2d0f42889b3d27fc86dc438` (PR #3) and the current revision. The same
benchmark source is copied into two isolated project directories referencing each
revision, with the same .NET/native dependencies. Tiered compilation is disabled.
Build and restore time is excluded. Both versions receive a full managed collection
after warm-up and before each measured workload, isolating garbage from preceding cases.

The primary fixture contains 10,000 nodes, 9,900 short local edges, 10,000 relative
ports and 10,000 independent labels. The raster viewport is 800 x 400 at zoom 1.
Operations cover spatial build/query, warm index/validation, local hit/connection rules,
selected handles/rendering, label edit/build, node drag preview, warm and cold rendering,
new-snapshot indexing, 10,000-node grid layout, and deletion of 1,000 nodes. Deletion
includes temporary document/editor construction. Cold render includes construction,
first preparation and disposal with the shared model already warmed. New-snapshot
indexing includes validation when required. No workload is presented as isolated GPU time.

Each workload is warmed and sampled repeatedly, reporting median/p95 elapsed time and
median current-thread managed allocation. A raster hash checks the common fixture.
Baseline/current JSON and comparison JSON/Markdown are uploaded. Zero elapsed samples
are below timer resolution; no artificial ratio is assigned. Timings are descriptive,
not pass/fail gates, and must be attributed to their precise run/revision. Regressions
and retention tradeoffs must be reported alongside improvements.

These are CPU/raster and managed-allocation measurements, not presentation FPS, physical
GPU throughput, input latency, energy use or touchscreen certification. Uno chooses the
interactive rendering backend. One fixture cannot establish performance for every graph
density, label length, zoom level or layout workload.
