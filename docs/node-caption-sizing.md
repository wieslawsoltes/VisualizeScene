# Fit nodes to text

Select nodes and use **Arrange → Fit nodes to text** or the compact framed-T button
beside the existing sizing tools. The command measures complete explicit caption lines
with the current graph renderer's font and adjusts node width/height in one undoable
transaction. It is a manual operation, not an automatic resize after every keystroke.

Only selected visible, unlocked, non-group nodes participate. Group headers, collapsed
folder frames, hidden descendants and independent annotations are not sizing targets.
Top-left positions, captions, node styles, topology, relative ports, label offsets and
stored bends retain their values. Relative anchors and clipped edge endpoints naturally
follow resized nodes; their absolute positions need not remain unchanged. Other nodes
are not rearranged to avoid overlaps, and the viewport is never fitted or zoomed implicitly.

## Reusable contracts

```csharp
using VisualizeScene.Core;
using VisualizeScene.Editing;
using VisualizeScene.Rendering.Skia;

using var renderer = new GraphRenderer();
var result = GraphNodeCaptionSizing.Calculate(snapshot, selectedIds,
    renderer.MeasureNodeCaption,
    new NodeCaptionSizingOptions(HorizontalPadding: 8, VerticalPadding: 6));

editor.NodeCaptionSizeProvider = renderer.MeasureNodeCaption;
editor.FitNodeCaptions();
// An optional programmatic variant only enlarges existing dimensions:
editor.FitNodeCaptions(new(EnlargeOnly: true));
// The reusable Uno graph supplies/replaces native measurements with its typeface:
graphControl.ArrangeNodes(GraphArrangeCommand.FitNodeText);
```

`GraphNodeCaptionSizing` materializes and validates all IDs before invoking application
callbacks. Known non-node items are ignored; unknown IDs, invalid sizes and coordinate
overflow are diagnosed. Callback failure, cancellation and stale document, selection or
measurement-provider changes cannot publish partial dimensions. No-op results retain
snapshot identity and redo history. The session requires a measurement provider rather
than silently guessing from string length. Custom providers may depend on node identity;
only the standard renderer uses text-only cache keys.

`NodeCaptionLayout` shares the plain-node font size, line height and drawing-width fraction
with the renderer. Fitting reserves its complete-line budget and two units of measured
rounding slack. Rectangular text envelopes are conservatively contained by every existing
shape: rectangular/rounded, ellipse, diamond, hexagon and parallelogram. Whole-unit fitted
sizes avoid wrapping-boundary roundoff. These frames are deliberately conservative, not
minimum-area geometric solutions. Custom padding/minimum sizes and enlarge-only behavior
are available through the editing API. Rich text, rotated text, automatic font fallback
and custom node templates are not added by this command.

## Performance and lifetime

The renderer caches up to 2,048 plain-caption measurements, shared across equal text on
different node IDs/shapes. Native shaping is not repeated on a cache hit. A full cache is
cleared before admitting another distinct caption. It is entry-bounded, not byte-bounded;
large strings and first-time shaping retain their existing costs. Typeface replacement
creates a fresh renderer/cache, and disposal clears measurements and native resources.

The fitting kernel examines selected items and measures each eligible node once. A no-op
returns before copying/scanning the node array. Publishing changed dimensions still copies
the immutable node collection and invokes whole-graph validation/index invalidation.
The warmed one-selection regression compares small and 10,000-unrelated-node graphs;
this is not a constant-time cold-index or arbitrary-large-selection claim. Toolbar
availability extends the existing event-driven selection state rather than adding work
to camera frames. No FPS or physical-GPU performance figure is asserted.

## Validation inventory

Twenty portable regressions cover all six shape interiors, extreme aspect ratios, data
preservation, eligibility, duplicate/unknown IDs, exact no-op/undo/redo, custom minima,
overflow, cancellation and reentrant callbacks. Six native Skia tests cover explicit
lines, CRLF/Unicode metrics, cache reuse/capacity/disposal, group rejection and fresh versus
retained rendering. Seven browser scenarios drive actual toolbar/menu/file input, compare
sizes against independent same-font annotation measurements, and check one transaction,
unchanged data/camera, disabled selections, cache reuse and a 640-unit workspace.

The package-only core consumer executes shaped fitting and undo. The isolated Uno consumer
compiles the control and toolbar APIs on both targets. Existing test suites remain, with
the caption browser suite an additional independent gate. These are authored regression
cases, not passing-test claims; successful results must be attributed to the tested commit.

## Reference and remaining scope

The general node-to-label sizing concept is documented by yWorks:
https://kb.yworks.com/article/85/How-to-automatically-adjust-node-bounds-to-the-nodes-label
This implementation uses the repository's original immutable model, Uno controls and
Skia geometry, not proprietary code or artwork. It does not establish full yFiles WPF
API, template, screenshot, layout-engine, docking, printing or accessibility parity.
Software-rendered Chromium tests do not certify physical GPUs or native desktop input.
The previously blocked edge-toolbar supplement is separate and is not included here.
