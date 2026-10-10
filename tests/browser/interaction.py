"""End-to-end tests against the actual compiled Uno WebAssembly application.

The opt-in test state is read-only. All modifications below use browser pointer
and keyboard input; no test-side graph engine or mutation bridge is used.
"""
import json
import pathlib
import sys
import time
import traceback
from playwright.sync_api import sync_playwright

url = sys.argv[1] if len(sys.argv) > 1 else "http://127.0.0.1:8080/VisualizeScene/?test=1"
output = pathlib.Path("artifacts/browser-validation")
output.mkdir(parents=True, exist_ok=True)
results = []
errors = []
console = []
started = time.time()

with sync_playwright() as p:
    browser = p.chromium.launch(headless=True, args=["--enable-unsafe-swiftshader", "--use-angle=swiftshader", "--disable-dev-shm-usage"])
    page = browser.new_page(viewport={"width": 1440, "height": 1050}, device_scale_factor=1)
    page.on("pageerror", lambda error: errors.append(str(error)))
    page.on("console", lambda message: console.append({"type": message.type, "text": message.text}))
    page.set_default_timeout(30000)

    def state():
        return page.evaluate("globalThis.visualizeSceneState")

    def wait(predicate):
        page.wait_for_function("s => { const v = globalThis.visualizeSceneState; return v && (" + predicate + "); }", timeout=30000)
        return state()

    def click_control(name):
        current = state()
        bounds = current["controls"][name]
        assert bounds and bounds["width"] > 0 and bounds["height"] > 0, f"Control {name} is not laid out: {bounds}"
        page.mouse.click(bounds["x"] + bounds["width"] / 2, bounds["y"] + bounds["height"] / 2)
        page.wait_for_timeout(100)

    def node_point(node_id, corner=False):
        current = state()
        node = next(n for n in current["model"]["Graph"]["Nodes"] if n["Id"] == node_id)
        b = node["Bounds"]
        x = b["X"] + (b["Width"] if corner else b["Width"] / 2)
        y = b["Y"] + (b["Height"] if corner else b["Height"] / 2)
        return current["canvas"]["x"] + (x - current["origin"]["x"]) * current["zoom"], current["canvas"]["y"] + (y - current["origin"]["y"]) * current["zoom"]

    def record(name, action):
        start = time.time()
        try:
            action()
            results.append({"name": name, "passed": True, "seconds": time.time() - start})
            print("PASS", name, flush=True)
        except Exception as error:
            results.append({"name": name, "passed": False, "seconds": time.time() - start, "error": str(error)})
            raise

    try:
        page.goto(url, wait_until="domcontentloaded", timeout=120000)
        page.wait_for_function("globalThis.visualizeSceneState?.ready && globalThis.visualizeSceneState.frames > 0", timeout=180000)
        page.wait_for_timeout(1000)
        page.screenshot(path=str(output / "initial.png"), full_page=True)
        current = state()
        assert len(current["model"]["Graph"]["Nodes"]) == 10
        assert current["canvas"]["width"] > 500 and current["canvas"]["height"] > 500
        results.append({"name": "Uno application loads and paints initial graph", "passed": True})
        initial_nodes = len(current["model"]["Graph"]["Nodes"])
        initial_edges = len(current["model"]["Graph"]["Edges"])

        def create():
            click_control("ModeNode")
            wait("v.mode === 'CreateNode'")
            c = state()["canvas"]
            page.mouse.click(c["x"] + c["width"] * .84, c["y"] + c["height"] * .15)
            wait(f"v.model.Graph.Nodes.length === {initial_nodes + 1}")
        record("Create node with toolbar and canvas pointer", create)
        added = state()["model"]["Graph"]["Nodes"][-1]["Id"]

        def drag():
            click_control("ModeSelect")
            before = state()["model"]["Graph"]["Nodes"][-1]["Bounds"].copy()
            x, y = node_point(added)
            revision = state()["revision"]
            page.mouse.move(x, y)
            page.mouse.down()
            page.mouse.move(x - 60, y + 65, steps=8)
            page.mouse.up()
            wait(f"v.revision > {revision}")
            after = state()["model"]["Graph"]["Nodes"][-1]["Bounds"]
            assert after["X"] != before["X"] and after["Y"] != before["Y"]
            click_control("Undo")
            wait(f"v.model.Graph.Nodes.at(-1).Bounds.X === {before['X']}")
            click_control("Redo")
            wait(f"v.model.Graph.Nodes.at(-1).Bounds.X === {after['X']}")
        record("Drag node, undo and redo through real controls", drag)

        def duplicate():
            page.mouse.click(*node_point(added))
            page.keyboard.press("Control+d")
            wait(f"v.model.Graph.Nodes.length === {initial_nodes + 2}")
            page.keyboard.press("Delete")
            wait(f"v.model.Graph.Nodes.length === {initial_nodes + 1}")
        record("Keyboard duplicate and delete", duplicate)

        def edge():
            click_control("ModeEdge")
            page.mouse.click(*node_point("start"))
            page.mouse.click(*node_point(added))
            wait(f"v.model.Graph.Edges.length === {initial_edges + 1}")
            e = state()["model"]["Graph"]["Edges"][-1]
            assert e["SourceId"] == "start" and e["TargetId"] == added
        record("Two-click edge creation survives pointer release", edge)

        def resize():
            click_control("ModeSelect")
            page.mouse.click(*node_point(added))
            before = next(n for n in state()["model"]["Graph"]["Nodes"] if n["Id"] == added)["Bounds"]
            x, y = node_point(added, True)
            revision = state()["revision"]
            page.mouse.move(x, y)
            page.mouse.down()
            page.mouse.move(x + 30, y + 25, steps=5)
            page.mouse.up()
            wait(f"v.revision > {revision}")
            after = next(n for n in state()["model"]["Graph"]["Nodes"] if n["Id"] == added)["Bounds"]
            assert after["Width"] > before["Width"] and after["Height"] > before["Height"]
        record("Resize using selected node corner", resize)

        def group():
            page.mouse.click(*node_point("start"))
            page.keyboard.down("Control")
            page.mouse.click(*node_point("validate"))
            page.keyboard.up("Control")
            click_control("Group")
            wait("v.model.Graph.Nodes.some(n => n.IsGroup)")
            click_control("Fold")
            wait("v.model.Graph.Nodes.some(n => n.IsGroup && n.Collapsed)")
            click_control("Fold")
            wait("v.model.Graph.Nodes.some(n => n.IsGroup && !n.Collapsed)")
        record("Group, fold and unfold", group)

        def zoom():
            before = state()["zoom"]
            click_control("ZoomIn")
            wait(f"v.zoom > {before}")
            click_control("ZoomOut")
            click_control("Fit")
            assert state()["zoom"] > 0
        record("Zoom and fit controls", zoom)

        def responsive():
            page.set_viewport_size({"width": 700, "height": 900})
            page.wait_for_timeout(500)
            click_control("Fit")
            assert state()["canvas"]["width"] > 650
            page.screenshot(path=str(output / "mobile.png"), full_page=True)
            page.set_viewport_size({"width": 1440, "height": 1050})
            page.wait_for_timeout(400)
            click_control("Fit")
        record("Responsive narrow viewport preserves graph editor", responsive)
        page.screenshot(path=str(output / "final.png"), full_page=True)
        assert not state()["error"], state()["error"]
        assert not errors, "JavaScript errors: " + repr(errors)
        results.append({"name": "No unhandled browser or application errors", "passed": True})
    except Exception as error:
        traceback.print_exc()
        errors.append(str(error))
        try:
            page.screenshot(path=str(output / "failure.png"), full_page=True)
        except Exception:
            pass
    finally:
        try:
            (output / "state.json").write_text(json.dumps(state(), indent=2), encoding="utf-8")
            gpu = page.evaluate("""() => {const c = document.createElement('canvas'); const gl = c.getContext('webgl2'); if(!gl) return {available:false}; const e=gl.getExtension('WEBGL_debug_renderer_info'); return {available:true, renderer:e ? gl.getParameter(e.UNMASKED_RENDERER_WEBGL):gl.getParameter(gl.RENDERER), vendor:e?gl.getParameter(e.UNMASKED_VENDOR_WEBGL):gl.getParameter(gl.VENDOR)};}""")
        except Exception:
            gpu = {"available": False, "reason": "Application did not initialize"}
        (output / "console.json").write_text(json.dumps(console, indent=2), encoding="utf-8")
        report = {"url": url, "seconds": time.time() - started, "results": results, "errors": errors, "webglProbe": gpu, "note": "CI uses software SwiftShader. A WebGL probe is capability evidence, not proof of physical GPU rendering or GPU performance."}
        (output / "report.json").write_text(json.dumps(report, indent=2), encoding="utf-8")
        browser.close()

sys.exit(1 if errors or any(not r["passed"] for r in results) else 0)
