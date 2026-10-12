# Desktop canvas scrolling

The workbench now hosts its real GraphControl and search overlay inside the reusable
`GraphScrollViewer`. Native Uno horizontal and vertical scrollbars occupy 16-unit rails,
with a corner Fit button and a View menu toggle. This follows the desktop graph-editor
arrangement referenced in `classic-shell.md`; it is not a screenshot-equivalence claim.

## Portable geometry

`ViewportScrollModel.Calculate` converts cached content bounds and a viewport state into
two immutable normalized axes. Each native bar uses range 0..1, regardless of graph size
or negative coordinates. The world range includes both padded content and the current
camera, so opening a scrollbar while panned outside the diagram does not snap the view.
Small steps are 24 screen units and page steps are 85% of the current visible dimension.
The thumb proportion includes the visible extent. Zero-sized views disable their axis;
nonfinite/overflowing geometry rejects instead of contaminating the camera transform.

```csharp
var model = ViewportScrollModel.Calculate(contentBounds, viewport.State, 1200, 800);
var x = model.Horizontal.GetOrigin(normalizedThumbValue);
viewport.Restore(new ViewportState(viewport.Origin with { X = x }, viewport.Zoom));
```

The model takes already-computed bounds: it does not enumerate graph items, allocate
collections, serialize the document, or edit undo history. Its ranges are values that can
be captured for a gesture. Numeric precision remains limited by binary64 world coordinates;
this is not arbitrary precision or a claim that all input magnitudes are renderable by Skia.

## Control and interaction lifetime

```csharp
var scroll = new GraphScrollViewer { Target = graphControl, Overlay = searchControl };
// Add scroll to the owning view, rather than parenting graphControl separately.
scroll.ScrollBarsVisible = true;
// When the host closes, dispose scroll, then dispose its separately owned graph/overlay.
```

The target and overlay must be unparented and distinct. Replacing them removes old visual
children and event subscriptions without disposing caller-owned controls. Clearing or
disposing the wrapper releases retained snapshot references. The search overlay remains
inside the graph area rather than covering the scrollbar rails.

Thumb movement freezes its coordinate range until EndScroll. External camera changes,
document changes/replacement, resize, cancellation, focus loss or unload invalidate an
active drag. Late native thumb events cannot apply an obsolete coordinate frame. Pointer
presses focus the native bar; arrows, PageUp/PageDown, Home and End act on that bar rather
than nudging selected graph items. Tab is not trapped. The corner button invokes the
existing Fit command, and graph scroll commands preserve zoom and graph revision.

Selection-only frames reuse the model. Camera frames use the graph's cached ContentBounds;
font/bounds changes are checked even when snapshot identity stays the same. Document
changes may still incur normal full scene-bound/index preparation. The renderer remains
the existing Uno Skia canvas; this change does not introduce a separate HTML graph or
claim a new physical-GPU rendering backend.

## Validation and performance contracts

`ScrollTests` in the navigation executable covers negative/off-content ranges, fitted and
empty scenes, page/thumb geometry, zoom limits, clamping, invalid/overflow input, 10,000
randomized ranges and zero warm managed allocation in 10,000 model calculations. It does
not replace the platform interaction tests.

`tests/browser/scrolling.py` uploads an 800-node fixture through a real file input and
operates native Uno bars via pointer/keyboard input. It checks both axes, exact line/page
steps, frozen thumb ranges, invalidation on resize, scrollbar show/hide, the corner Fit
button, responsive rails and empty-document replacement. Every viewport-only scenario
asserts unchanged graph data/revision. Rendering/serialization counters check that scrolling
reuses document preparation and selecting all nodes does not rebuild scrollbar geometry.
The existing workspace suite invokes this test after all prior batch/order/inspector tests;
its failure status fails the workflow and its report/screenshots are retained.

The read-only `?test=1` diagnostics expose current ranges and native thumb bounds only.
They contain no graph or viewport mutation functions. Package-only consumers compile the
wrapper APIs for desktop and browser. Only the exact successful Actions revision counts
as tested. Chromium SwiftShader is software rendering, not physical-GPU throughput,
physical touchscreen or native desktop GUI certification. No FPS or latency improvement
is inferred from the allocation/work counters.

## Reference and remaining boundaries

Native ScrollBar event and viewport-size contracts:
- https://learn.microsoft.com/en-us/windows/windows-app-sdk/api/winrt/microsoft.ui.xaml.controls.primitives.scrollbar.scroll
- https://learn.microsoft.com/en-us/windows/windows-app-sdk/api/winrt/microsoft.ui.xaml.controls.primitives.scrollbar.viewportsize

The toolkit still lacks full yFiles WPF API/template/style/layout-quality parity,
complete graph accessibility peers, full docking/multiple documents, printing and all
GraphML extensions. This is a focused interaction/lifetime/performance review with the
surrounding regression gates retained, not a guarantee that the whole toolkit is bug-free.
