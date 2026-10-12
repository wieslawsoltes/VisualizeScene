# Connection palette and edge appearance workflow

The original-style Connections category now presents six visual presets, drawn with the
same reusable Skia edge painter used by the graph. Selecting a preset configures future
connections; it does not change existing graph items. The default orthogonal preset
preserves legacy null-style appearance. Other presets demonstrate dashed/open arrows,
plain connections, hollow source diamonds and dotted circle endpoints. Source and target
markers are appearance only and never silently change graph algorithm direction.

`EdgePalette` is a reusable Uno control with a validated, read-only template list.
`EdgeCreationOptions` belongs to the portable editing package and includes route, color
and optional immutable `EdgeStyle`. Assigning changed defaults cancels a half-complete
two-contact connection; validation happens before cancellation or publication. Palette
previews use temporary owned painter resources, not a process-wide native cache.

```csharp
graph.Session.EdgeDefaults = new EdgeCreationOptions(
    EdgeRouting.Polyline, "#607D99", new EdgeStyle(2, EdgeDash.Dash));
var palette = new EdgePalette { Target = graph };
palette.SetItems([new EdgeTemplate("Dashed dependency", graph.Session.EdgeDefaults)]);
```

The property inspector exposes stroke width, five dash patterns, and independent source
and target marker choices for single edges and mixed selections. Sparse batch edits retain
unmodified fields and graph direction. Applying unchanged single-edge controls preserves
the legacy null style; invalid widths/enums roll back the complete transaction. The four
new properties increase the fixed maximum editor count from eleven to fifteen, not one
set per selected edge. Apply/Reset commands are pinned outside the scrollable property
rows so long mixed selections cannot scroll the commit actions out of view.

## Rendering review

Caption rectangles now have an independent spatial index. Changing caption text updates
that index without rebuilding unchanged route geometry or native paths. Width or explicit
marker changes can rebuild route visual bounds, while dash/color-only changes retain them.
Both candidate sets are deduplicated and sorted in original edge painter order. A wrapped
caption intersecting the viewport remains visible even with an offscreen route. The extra
caption index costs retained per-caption bounds and revision-time measurement; this is not
a claim of zero allocation for shaping or all scene preparation. No speedup or FPS figure
is inferred from the deterministic cache/work counters.

The recovered branch originally failed a route-index invalidation assertion and two
browser interactions. The route/caption coupling was fixed in source, with the existing
assertion kept. The prior batch input and layout timeouts passed in the subsequent run,
but their causes were not conclusively established. Follow-up diagnostics refresh all
current property references before publishing control bounds, and batch input tests
verify the actual focused text field and entered value before applying. This does not
replace native pointer input or add a graph mutation hook.

## Editor integration and clear-text correction

Review found that the portable stroke/marker hit helpers were not yet called by the
editor's own spatial broad phase and selection commands. Actual pointer picking,
marquee crossing/containment and keyboard focus now use these appearance bounds and
predicates. The original null-style centerline behavior is preserved; styled dash gaps
are deliberately selectable as continuous strokes. The edge index remains spatial: an
actual EditorSession regression with 10,000 styled edges checks fewer than 100 examined
entries for the local probe. Unstyled bounds stream route points without per-path LINQ
iterators, and area predicates no longer create a Range/Any closure per candidate.

The `edge_area_9900` benchmark compares the same public QueryArea call on the same 9,900
unstyled edges, checking exact returned IDs before sampling. Both revisions use identical
harness code, warmed indexes and fixed geometry; baseline is PR #12 merge
`9bf42c7a953b28b1aaf79133513919102b1ae38d`. Timing/allocation include result collection and
sorting, not graph setup. This does not compare against the earlier incorrect behavior
for styled edges, nor claim allocation-free result construction. All other workloads,
including slower ones and the common raster hash, remain in the report.

The first PR #13 browser run stopped at explicit Clear selected text on a mixed
selection. An empty placeholder and an intentional clear used the same text value;
repeated value notifications could remove the pending clear. `GraphTextPropertyDraft`
now retains that intent independently, distinguishes null (unchanged) from empty
(clear), and preserves no-op/reset semantics. Portable duplicate-notification and exact
undo regressions accompany the unchanged native clear/undo browser assertion. The
failed artifact is retained. The subsequent native run still stopped at Clear, so the
draft fix is not claimed to have resolved that browser failure by itself. Review of the
pinned footer also found that variable-height feedback moved its button targets: a short
"Properties applied" message and a multi-line validation/help message shifted the rows
by more than a button height. Feedback now reserves a fixed 70-unit area. The browser
checks that the same action rectangles survive changed staging feedback and requires
explicit clear intent to remain staged before Apply; it neither retries the click nor
removes exact-undo assertions. Successful workflow results, not this inventory, establish
which revisions passed.

## Validation

Portable tests exercise preset creation, cancellation, validation, sparse style batches
and exact undo. Renderer tests compare cached/fresh output, caption-only changes, offscreen
routes, independent invalidation and removal. The existing native dash cache remains
bounded to 32 entries and releases evicted/native objects. Existing graph, layout, analysis,
interchange, navigation and text tests remain in the build.

The compiled Uno browser suite creates each preset with actual pointer input, edits its
width/pattern/markers, rejects invalid staged text plus appearance, performs mixed-edge
updates, and checks JSON, native GraphML and SVG output. Its report is under
`browser-workspace/edge-styles`. The original 39,900-item fixture still checks one-pass
selection summaries, exact undo and a fixed fifteen property fields. Package-only consumers
compile the new palette/style APIs and execute their existing isolation scenarios.

The UI is inspired by the public WPF graph-editor reference in `classic-shell.md`; no
proprietary assets/code are copied. Full yFiles API/template/screenshot/layout-quality
parity, custom/curved/bus styles, full docking, printing and accessibility remain outside
this batch. Stroke widths are 0.25–64 world units; dash gaps remain continuously selectable,
hollow markers use a white fill, and geometry remains binary64/Skia float based. Hardware
GPU throughput, physical touch and native desktop GUI interaction are not certified.
