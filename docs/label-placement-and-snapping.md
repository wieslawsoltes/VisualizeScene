# Automatic label placement and alignment guides

## Validation

Placement and snapping are integrated into the managed conformance, actual Uno browser and isolated NuGet consumer gates. Reports are produced by `Build and validate`; only a successful run for the source revision being used establishes that those tests passed. The tests cover VisualizeScene contracts, not complete yFiles parity. Chromium CI uses SwiftShader software graphics, not physical GPU benchmarking.

## Reusable label placement

`VisualizeScene.Layout.LabelPlacementEngine.Calculate` accepts an immutable graph snapshot, placement options, a label measurement callback, an optional selected-label ID set, a candidate provider and a cancellation token. It returns a graph snapshot, counts, conflict diagnostics and an evaluation counter. It never writes a document or alters node, edge or port records.

```csharp
var result = new LabelPlacementEngine().Calculate(
    document.Snapshot,
    new LabelPlacementOptions(Gap: 8),
    measure: graphControl.MeasureLabel);
document.Replace(result.Graph, "Place labels");
```

The UI implementation captures the revision before computation and rejects stale results. It measures labels on the UI thread, then gives the calculation captured text metrics rather than accessing the Skia renderer from a worker. The measurement callback also receives synthetic caption records; it should measure by text/font properties rather than assuming every supplied ID is an independent graph label.

The default candidate provider proposes exterior positions around node owners, radial positions around ports, and positions offset from sampled edge-path arc lengths. The original label location is always a candidate. Applications can supply an `ILabelCandidateProvider` to generate their own finite world-space centers.

A candidate is evaluated against node rectangles, expanded-group headers/borders, routed edge segments, explicit ports, captions and other independent labels. Static obstacles use the core spatial index. Moving label bounds use an updatable spatial hash with an overflow set for giant bounds. Axis-aligned boxes are conservative for nonrectangular nodes. Touching the configured clearance boundary is allowed.

The deterministic refinement prefers fewer conflicts, then less overlap area, then less movement from the input position. Only strict score improvements are accepted. Labels are processed in ordinal ID order. Unselected labels and labels owned by locked nodes, locked ancestors or edges with locked endpoints remain fixed obstacles. Folded child labels are excluded.

Pass, candidate and evaluation limits bound the search, and cancellation propagates without publishing a partial document. Candidate providers must cooperate by returning/yielding; arbitrary user code blocking inside a callback cannot be preempted by this engine. Remaining conflicts are reported, including conflicts on fixed labels; no conflict-free guarantee or global-optimum claim is made.

**Boundaries:** This implementation moves independent `GraphLabel.Offset` values. Built-in node and edge captions are obstacles rather than movable independent labels. It does not provide yFiles label-model API compatibility, rotated labels, integrated node/route layout, rich text, complete font fallback, or yFiles-equivalent placement quality.

## Magnetic alignment snapping

`VisualizeScene.Editing.ItemSnapIndex` builds sorted horizontal and vertical node-anchor arrays per graph revision. It resolves nearby left/center/right and top/center/bottom alignments using binary-searched ranges. The tolerance is configured in view coordinates and divided by the current zoom.

The editor uses the union of translating selected-node bounds. Selected descendants and ancestor containers are excluded as targets, as are hidden nodes and expanded group frames. A zero-size proposed rectangle represents an active resize corner. Grid snapping is the fallback independently on any axis without an item match. Equal-distance candidates have deterministic tie breaks.

`EditorSession.AlignmentGuides` contains immutable world-space line hints. The Skia renderer paints them on the same canvas as the graph, and clears them on release, cancellation, mode changes and stale-revision cancellation. They are interaction adorners, not graph data or exported content. Keyboard nudge remains exact.

`SnapToItems` is enabled by default and can be changed independently of `SnapToGrid`; `ItemSnapDistance` defaults to six view units. The workbench's **View → Alignment guides** and **View → Toggle grid snapping** commands expose both settings. **Layout → Place independent labels**, or the Layout panel's **Place labels** button, runs automatic placement. The panel also exposes clearance and selected-only placement.

**Boundaries:** Snapping currently covers node movement and resize corners. It does not implement spacing/equidistance guides, angular snapping, edge/bend/label-owner snap models, touch gestures or the complete yFiles input-mode/decorator API.

## Added validation entry points

```sh
dotnet run --project tests/VisualizeScene.Parity.Tests -c Release
python tests/browser/placement.py 'http://127.0.0.1:8080/VisualizeScene/?test=1'
```

The managed project contains 28 placement cases and 18 snapping cases. These cover collision-free fixtures, pinned/selected/locked labels, deterministic results, malformed input, cancellation, budget limits, serialization, undo, screen-space tolerance, selection exclusions, previews and a 1,000-label fixture. The browser suite adds seven scenarios that import a fixture through a real file input and use actual Uno pointer/menu/keyboard input. The package consumer references the new public APIs through NuGet packages. The same managed test project also runs [selection regressions](selection.md). All suites are wired into the build workflow, and failed cases prevent package-consumer validation and deployment.

## Capability references

Public descriptions consulted for behavioral targets; no proprietary code is included:

- yFiles WPF documentation: https://docs.yfiles.com/yfileswpf/
- yWorks generic labeling: https://docs.yfiles.com/yfiles-layout-reactflow/layouts/genericlabeling
- yWorks snapping demo: https://www.yfiles.com/demos/application-features/snapping/
- SnapContext view-coordinate distance: https://docs.yfiles.com/yfiles-html/api/SnapContext/
