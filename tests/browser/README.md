# Browser validation

These tests launch the actual published Uno WebAssembly workbench. The optional `?test=1` diagnostic object contains graph snapshots and laid-out control coordinates; it has no mutation functions. All editing assertions use Playwright pointer and keyboard input.

Publish with the real Uno bootstrap property, **WasmShellWebAppBasePath**, not WasmShellBasePath:

```sh
dotnet publish samples/VisualizeScene.Workbench -c Release -f net10.0-browserwasm \
  -p:WasmShellWebAppBasePath=/VisualizeScene/ -o artifacts/browser-publish
```

The project defaults to `./`, which also works when the app is served under a project subdirectory. Copy the directory containing the generated `index.html` into `artifacts/site/VisualizeScene`, then:

```sh
python -m pip install playwright
python -m playwright install chromium
python -m http.server 8080 --directory artifacts/site
# In another terminal:
python tests/browser/interaction.py 'http://127.0.0.1:8080/VisualizeScene/?test=1'
python tests/browser/editing.py 'http://127.0.0.1:8080/VisualizeScene/?test=1'
python tests/browser/features.py 'http://127.0.0.1:8080/VisualizeScene/?test=1'
```

CI uses Chromium with the SwiftShader software graphics driver. It records initial/final/mobile screenshots, console messages, exceptions, model state and a WebGL capability probe. That environment cannot certify physical GPU throughput or native Windows/macOS behavior.

`interaction.py` covers initialization, node creation, dragging, undo/redo, duplicate/delete shortcuts, two-contact edge creation, resizing, grouping/folding, viewport commands and responsive layout.

`editing.py` covers port creation and relocation, explicit-port connections, endpoint reconnection, rejected/canceled drops, connected-port movement, F2 and double-click label dialogs, cancellation, independent-label movement, label-only duplication and owner-retargeting paste. Label coordinates come from the actual shaped text bounds, not a parallel JavaScript layout.

`features.py` exercises all six layouts, routing undo, eight analysis commands, JSON/GraphML/SVG/PNG downloads, a native-format import round trip and all six samples including the 1,000-node graph. The test disables the optional `showOpenFilePicker` capability before initialization to exercise Uno's standard upload-input fallback. Playwright cannot intercept Chromium's native OS file picker. The file bytes still enter through the real browser file input; no graph mutation API or fake import implementation is used. Native OS picker execution remains unverified.

Reports, read-only model state, console messages and screenshots are written to `artifacts/browser-validation`, `artifacts/browser-editing` and `artifacts/browser-features`. A test count is evidence for this project's contracts only; it is not full yFiles parity validation.

## Placement, snapping and selection

`placement.py` imports its fixture through a real upload input, then checks automatic placement, selected-only behavior, exact undo/redo, magnetic node/corner snapping, cancellation and independent snapping toggles. Screenshots include overlapping and placed labels and active alignment guides.

`selection.py` exercises the Selection menu, item filters, inversion/clear keyboard commands, nonmutating marquee previews, Escape, commit-on-release, modifier combinations, directional window/crossing behavior, port and edge geometry, incoming/outgoing neighbors and out-and-back cancellation. Every scenario asserts the entire graph model and its revision remain unchanged. Neither suite has a graph mutation bridge.

```sh
python tests/browser/placement.py 'http://127.0.0.1:8080/VisualizeScene/?test=1'
python tests/browser/selection.py 'http://127.0.0.1:8080/VisualizeScene/?test=1'
```

Their evidence is uploaded as `browser-placement` and `browser-selection`. A passing source build is not a substitute for these actual input tests.

## Navigation and large-document checks

`navigation.py` drives Chromium's native touch-input dispatch and actual keyboard/menu
commands against the compiled Uno app. It verifies pan/pinch, tap selection, deferred
creation, edit arbitration, cancellation and spatial focus/reveal. This is input
emulation, not physical touchscreen certification.

`performance.py` uploads 10,000 nodes, 9,900 edges, 10,000 ports and 10,000 labels through
the real file picker. It checks that zoom/pan retain graph data and preparation,
that annotation queries inspect a small viewport subset, and that an actual edit
invalidates the diagnostic document cache exactly once. It uses structural counters,
not an FPS claim or hardware-GPU benchmark. Reports are uploaded as
`browser-navigation` and `browser-performance`.

```sh
python tests/browser/navigation.py 'http://127.0.0.1:8080/VisualizeScene/?test=1'
python tests/browser/performance.py 'http://127.0.0.1:8080/VisualizeScene/?test=1'
```
