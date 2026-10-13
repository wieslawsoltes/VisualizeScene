# Rendering-picture budgets and edge-caption reuse

## Changes and UI

The native **View → Rendering cache** submenu offers Off, 4 MiB, 16 MiB
(default), and 64 MiB **estimated picture** presets. Check marks are mutually
exclusive even when the active preset is clicked again. The preference belongs
to this workbench session, survives document import and asynchronous font
replacement, and is not serialized into graph JSON/GraphML. It is not persisted
across restarting the app. Switching presets does not edit history, selection,
camera, graph styles, or the actual text. The compact submenu follows the existing
native navigation/touch menu pattern; no extra toolbar row is added.

`GraphRenderer.PictureCacheByteBudget` and `GraphControl.PictureCacheByteBudget`
set a nonnegative estimated-byte budget alongside `PictureCacheCapacity`. Both
limits apply to **one LRU** containing node, independent-label and edge-caption
pictures. Zero in either limit disables retention. Reducing either limit evicts
immediately. A picture that individually exceeds the byte budget is replayed
before disposal and never entered into the LRU. Admission makes room before
registering the incoming entry; old entries cannot cause a new picture to be
released before it is drawn. Replacements, removals and disposal update accounting.

`PictureCacheStatistics` keeps its original five positional members and adds
`EstimatedRetainedBytes`, `ByteBudget`, `OversizedBypasses`,
`EdgeCaptionRecordings`, and `RetainedEdgeCaptions`. Counters are lifetime totals;
retained counts/bytes describe the current renderer. Font replacement starts a
new renderer but preserves configured budgets. The control is UI-thread-owned,
not a concurrent rendering service.

Previously, unchanged edge captions were shaped on every frame. The renderer now
records their exact world-coordinate text/background commands and replays them.
Text or route-center changes invalidate the affected picture; camera, selection,
line styling and identical route-center changes do not. A retained unchanged
route also reuses its midpoint instead of walking every bend each frame. Removed
edges, empty captions and hidden internal edges are pruned when preparing the
next graph snapshot. Overview/low-detail paths still omit captions. Original
wrapping, painter order and raster export behavior are preserved, not re-designed.

## Important memory and performance limits

The default is **16 MiB estimated retained picture cost**, not 16 MiB of total
memory. Accounting sums `SKPicture.ApproximateBytesUsed` (at least one byte per
picture). Skia excludes large referenced objects from this estimate. It is not a
hard native/GPU/font/image/process-memory cap and does not include graph indexes,
managed dictionary overhead, other caches or transient recording allocations.
One temporary picture can exceed the budget while recording/replaying. Disabling
retention trades lower retained picture cost for repeated drawing/recording work.

Primary API references:
- https://learn.microsoft.com/en-us/dotnet/api/skiasharp.skpicture.approximatebytesused
- https://api.skia.org/classSkPicture.html

Warm caption reuse is the targeted improvement. Cold preparation still measures
captions and creates graph indexes; edited snapshots can perform graph-sized
validation/preparation. Cache misses still incur shaping and native recording.
Timings must therefore distinguish warm replay, panning and cold render rather
than claiming an across-the-board speedup.

## Executable validation

Nineteen new native Skia cases cover exact raster replay at fractional cameras,
PNG export, replacement/undo/route/folding/deletion, count and byte limits,
exact-fit admission, oversized bypass, immediate trimming, cost accounting and
disposal. Existing conformance and bounded-work tests remain enabled.

`tests/browser/rendering_cache.py` drives the compiled Uno app using actual
pointer/menu/file input. It checks all native presets, check marks, unchanged
document/camera/history/selection, live caption reuse, batch clearing and undo,
JSON import retention, and a 640-pixel workspace. Its diagnostics only observe
native state. Package-only consumers exercise the renderer API and compile the
public Uno API without source-project references.

The performance workflow compiles the **identical benchmark source** against
merged baseline `ea4a1e966e86ddc7dea2ce89f2715d848146a44c` and the current
revision on one runner. In addition to the existing 10,000-node fixture, a fully
visible 20-node/10-caption fixture measures warmed replay, panning and cold render.
Both fixture pixel hashes must match; timing differences are descriptive, not a
substitute for correctness. These are CPU/raster and current-thread managed
allocation measurements, not physical GPU throughput or end-to-end input latency.

Local Python syntax checks, the existing native-menu observer unit test and
`git diff --check` are available. This environment has no .NET SDK and local
browser navigation is administrator-blocked; managed/browser execution evidence
must come from the matching GitHub Actions head, not inferred from authored tests.
See the PR for final run IDs, results and measured values.

Full yFiles WPF API, templates, native docking, layout-engine, printing and
accessibility parity is not established. The previously platform-blocked PR20
edge-toolbar supplement is separate and is not republished by this work.
