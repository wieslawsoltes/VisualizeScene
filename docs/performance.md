# Performance contracts and reproducible measurement

## Implemented changes

`GraphSnapshot.Index()` shares a lazily constructed, immutable `GraphIndex` by snapshot
**identity**, not record equality. The cache uses weak keys, so it does not itself retain
retired document revisions. Validation stamps are also identity-keyed and appear only
after successful validation. A `with` copy is independently validated. Read-only dictionary
wrappers, frozen adjacency arrays and frozen route lists prevent a consumer from
corrupting another consumer's shared index. Publication is thread-safe; graph documents
and editors remain single-writer objects.

The index resolves depth and the outermost collapsed ancestor in a breadth-first pass.
Subsequent visibility/depth queries avoid repeated ancestry walks. Routes for the exact
edge records in the snapshot are memoized. A different edge record sharing an ID, such
as a reconnection preview, does not receive a stale cached route.

`GraphEdit` creates mutable dictionaries only when a collection is changed. Build retains
untouched immutable arrays and returns the original snapshot for an empty edit. Previously
built snapshots remain immutable. Simple node drags avoid the two intermediate snapshots
and indexes previously created by combined node/port/label movement. Mixed selections
still compensate label offsets for changed owner anchors.

`SpatialIndex.Query(rectangle, destination)` appends to caller-owned storage with a
nonallocating balanced traversal after buffer warmup. The original iterator API remains.
`SpatialQueryStatistics` reports branches visited, leaf entries examined and results.
The editor uses this API for nodes, edges, ports and independent labels during pointer
hit testing and connection-candidate queries. Original painter order and port precedence
are retained; `LastHitStatistics` exposes examined/candidate counts.

The Skia renderer indexes measured independent-label bounds and port positions. Frames
query those indexes instead of scanning all annotations. Visible items retain their
established paint order. Label-only changes reuse the node/edge spatial indexes; unchanged
edge point sequences retain their owned native `SKPath` even when style metadata changes.
Reordering edges invalidates the ordering-dependent edge index. `RenderStatistics` and
`PreparationStatistics` separate viewport work from document preparation.

The optional `?test=1` diagnostics serialize the immutable document, routes and measured
label bounds only when the graph snapshot or text-measurement provider changes. Camera,
selection and touch state remain per-frame. Normal application mode does not enable this
diagnostic bridge, and there are no test-only graph mutation functions.

## Complexity and remaining costs

These are focused optimizations, not a claim that every edit is O(changed items).
Cold graph indexing/validation traverses the graph. Spatial index construction still
sorts partitions. Editing a touched collection still copies that collection, and changed
node geometry can rebuild scene indexes. Selection-handle hit testing still traverses
selected items. Keyboard directional navigation scans its cached candidate registry once.

Annotation indexes retain per-label geometry for a revision, increasing retained state in
exchange for reduced frame work. Node pictures are retained by node ID and released on
removal/disposal; there is no native-memory-byte budget or independent-label picture LRU
in this revision. Large histories retain their snapshots intentionally and remain bounded
by entry count, not bytes. Route-cache lookups still have some delegate allocation. Cold
preparation can trade time/memory for faster warm interactions and must be reported separately.

## Correctness gates

`tests/VisualizeScene.Performance.Tests` verifies shared-index identity, concurrent reads,
mutation rejection, weak-key collection, independent validation of changed snapshots,
a 10,000-level hierarchy, copy-on-write snapshots, spatial-query ordering/allocation,
10,000-item hit-test broad phases, annotation culling and renderer invalidation.
Cached renderer outputs are compared byte-for-byte with a fresh renderer after edits,
including route styles, ports, label metrics, folding, order changes, deletion and undo.

The Linux executable supplies matching SkiaSharp/HarfBuzz native assets. It is part of
`Build and validate`, so failures prevent the package-consumer gate and Pages deployment.
The browser large-document suite uses actual input and checks work counters on the Uno
WebAssembly application, not an alternative JavaScript graph implementation.

## Same-runner benchmark

`Navigation and performance` checks out baseline commit
`894cd8e8a0600a73cd4efff9fcac2c5df052a22a` and the current revision on the same runner.
It copies the **same** benchmark sources to two isolated project directories, references
each source tree, and runs both with the same .NET version and native dependencies.
Tiered compilation is disabled for both. Builds/restores are outside measured sections.

The fixture contains 10,000 nodes, 9,900 short local edges, 10,000 relative ports and
10,000 independent labels. The raster viewport is 800 × 400 at zoom 1. Operations cover
warm snapshot indexing, warm validation, local hit testing, connection candidates,
label edit/build, node drag previews, warm rendering, cold renderer preparation and
indexing a new snapshot. Every workload is warmed, sampled repeatedly and reported with
median/p95 elapsed time and median current-thread managed allocation per operation.

Cold renderer timings include renderer construction, first preparation and disposal,
with the model already warmed. Index-new-snapshot timings include validation as required
by the revision under test. The report also records a raster pixel hash for the common
fixture. Timing ratios are descriptive; deterministic correctness/complexity assertions
are the gates, not fragile wall-clock thresholds.

Artifacts contain `baseline.json`, `current.json`, `comparison.json` and `comparison.md`.
Results must be attributed to the run and revision that produced them. Do not extrapolate
one viewport/fixture to all graph densities, label lengths or layout workloads.

These are **CPU/raster and managed-allocation measurements**, not GPU timestamps, browser
presentation FPS, input latency, power consumption, physical touchscreen or native
Windows/macOS GUI certification. Skia's interactive backend is still selected by Uno.
