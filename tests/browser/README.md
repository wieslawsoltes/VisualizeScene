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
```

CI uses Chromium with the SwiftShader software graphics driver. It records initial/final/mobile screenshots, console messages, exceptions, model state and a WebGL capability probe. That environment cannot certify physical GPU throughput or native Windows/macOS behavior.

The suite checks initialization, node creation, dragging, undo/redo, duplicate/delete shortcuts, edge creation across two contacts, corner resizing, grouping/folding, viewport commands, responsive layout and unhandled errors. Additional feature and parity tests remain necessary; this is not validation of every advertised yFiles capability.
