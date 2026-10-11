"""Automatic label placement and magnetic guides in the compiled Uno workbench.
No graph mutation bridge: fixtures enter through the real browser upload input.
"""
import json
import pathlib
import sys
import traceback
from playwright.sync_api import sync_playwright

url = sys.argv[1] if len(sys.argv) > 1 else "http://127.0.0.1:8080/VisualizeScene/?test=1"
output = pathlib.Path("artifacts/browser-placement")
output.mkdir(parents=True, exist_ok=True)
results, errors, messages = [], [], []
fixture = {"Version": 1, "Graph": {"Nodes": [], "Edges": [], "Ports": [], "Labels": []}}
for node_id, x, y in [("a", 0, 0), ("b", 300, 200)]:
    fixture["Graph"]["Nodes"].append({"Id": node_id, "Bounds": {"X": x, "Y": y, "Width": 100, "Height": 60},
        "Text": node_id.upper(), "Shape": "RoundedRectangle", "Fill": "#DDEBFA", "Stroke": "#4778A5",
        "IsGroup": False, "Collapsed": False, "Locked": False})
for i in range(4):
    fixture["Graph"]["Labels"].append({"Id": "l"+str(i), "OwnerId": "a", "Text": "Label "+str(i),
        "Offset": {"X": 0, "Y": 0}, "FontSize": 12})
fixture_path = output / "fixture.json"
fixture_path.write_text(json.dumps(fixture), encoding="utf-8")

with sync_playwright() as p:
    browser = p.chromium.launch(headless=True, args=["--enable-unsafe-swiftshader", "--use-angle=swiftshader", "--disable-dev-shm-usage"])
    page = browser.new_page(viewport={"width": 1440, "height": 1050}, accept_downloads=True)
    page.add_init_script("Object.defineProperty(window, 'showOpenFilePicker', {value: undefined, configurable: true});")
    page.on("pageerror", lambda error: errors.append(str(error)))
    page.on("console", lambda message: messages.append({"type": message.type, "text": message.text}))

    def state():
        return page.evaluate("globalThis.visualizeSceneState")

    def wait(expression):
        page.wait_for_function("() => {const v=globalThis.visualizeSceneState;return v && ("+expression+");}", timeout=45000)
        return state()

    def click(name):
        key = json.dumps(name)
        wait(f"v.controls[{key}]?.width > 0 && v.controls[{key}]?.height > 0")
        b = state()["controls"][name]
        page.mouse.click(b["x"]+b["width"]/2, b["y"]+b["height"]/2)
        page.wait_for_timeout(180)

    def screen(x, y):
        s = state()
        return (s["canvas"]["x"]+(x-s["origin"]["x"])*s["zoom"], s["canvas"]["y"]+(y-s["origin"]["y"])*s["zoom"])

    def drag_preview(start, end):
        page.mouse.move(*screen(*start))
        page.mouse.down()
        page.mouse.move(*screen(*end), steps=12)

    def record(name, action):
        action()
        assert not state()["error"], state()["error"]
        results.append({"name": name, "passed": True})
        print("PASS", name, flush=True)

    try:
        page.goto(url, wait_until="domcontentloaded", timeout=120000)
        wait("v.ready && v.frames > 0")
        with page.expect_file_chooser() as pending:
            click("Open")
        pending.value.set_files(str(fixture_path))
        wait("v.status === 'Graph imported.' && v.model.Graph.Labels.length === 4")
        before = state()["model"]
        page.screenshot(path=str(output / "overlapping-labels.png"))

        def placement():
            click("TabLayout")
            click("PlaceLabels")
            wait("v.labelPlacement?.eligible === 4 && v.labelPlacement.moved === 4 && v.labelPlacement.conflicts === 0")
            assert state()["model"]["Graph"]["Nodes"] == before["Graph"]["Nodes"]
            assert state()["model"]["Graph"]["Edges"] == before["Graph"]["Edges"]
            page.screenshot(path=str(output / "placed-labels.png"))
        record("Place four overlapping labels without moving nodes or routes", placement)
        after = state()["model"]

        def history():
            click("Undo")
            wait("v.model.Graph.Labels.every(l => l.Offset.X === 0 && l.Offset.Y === 0)")
            assert state()["model"] == before
            assert state()["labelPlacement"] is None, "Undo left stale placement diagnostics"
            click("Redo")
            wait("v.model.Graph.Labels.some(l => l.Offset.X !== 0 || l.Offset.Y !== 0)")
            assert state()["model"] == after
        record("Placement is one exact undo/redo operation", history)

        def selected_only():
            b = state()["labelBounds"]["l0"]
            page.mouse.click(*screen(b["x"]+b["width"]/2, b["y"]+b["height"]/2))
            wait("v.selection.length === 1 && v.selection[0] === 'l0'")
            click("SelectedLabelsOnly")
            click("PlaceLabels")
            wait("v.labelPlacement?.eligible === 1 && v.labelPlacement.moved === 0")
            assert state()["model"] == after
            click("SelectedLabelsOnly")
        record("Selected-only placement respects the selection and preserves valid positions", selected_only)

        def snap_move():
            revision = state()["revision"]
            end = (350-3/state()["zoom"], 118)
            drag_preview((50, 30), end)
            wait("v.guides.length > 0")
            assert state()["revision"] == revision
            page.screenshot(path=str(output / "alignment-guides.png"))
            page.mouse.up()
            wait(f"v.revision === {revision+1} && v.guides.length === 0")
            assert abs(state()["model"]["Graph"]["Nodes"][0]["Bounds"]["X"]-300) < 1e-6
            click("Undo")
            wait("v.model.Graph.Nodes[0].Bounds.X === 0")
        record("Node movement displays magnetic guides and commits once", snap_move)

        def cancel_snap():
            revision = state()["revision"]
            drag_preview((50, 30), (350-3/state()["zoom"], 118))
            wait("v.guides.length > 0")
            page.keyboard.press("Escape")
            page.mouse.up()
            wait("v.guides.length === 0")
            assert state()["revision"] == revision
            assert state()["model"] == after
        record("Escape clears guides and cancels the preview without mutation", cancel_snap)

        def snap_resize():
            page.mouse.click(*screen(50, 30))
            wait("v.selection[0] === 'a'")
            revision = state()["revision"]
            drag_preview((100, 60), (400-3/state()["zoom"], 138))
            wait("v.guides.length > 0")
            page.mouse.up()
            wait(f"v.revision === {revision+1}")
            b = state()["model"]["Graph"]["Nodes"][0]["Bounds"]
            assert abs(b["X"]+b["Width"]-400) < 1e-6
            click("Undo")
            wait("v.model.Graph.Nodes[0].Bounds.Width === 100")
        record("Resize corner snaps to another node edge and is undoable", snap_resize)

        def disabled():
            for control in ["ToggleItemSnap", "ToggleGridSnap"]:
                click("MenuView")
                click(control)
            wait("!v.snapToItems && !v.snapToGrid")
            revision = state()["revision"]
            expected_x = 300-3/state()["zoom"]
            drag_preview((50, 30), (expected_x+50, 118))
            assert not state()["guides"]
            page.mouse.up()
            wait(f"v.revision === {revision+1}")
            assert abs(state()["model"]["Graph"]["Nodes"][0]["Bounds"]["X"]-expected_x) < .01
        record("View menu can disable magnetic and grid snapping independently", disabled)
        assert not errors, errors
    except Exception as error:
        traceback.print_exc()
        errors.append(str(error))
        results.append({"name": "Interrupted scenario", "passed": False, "error": str(error)})
        try:
            page.screenshot(path=str(output / "failure.png"))
        except Exception:
            pass
    finally:
        try:
            (output / "state.json").write_text(json.dumps(state(), indent=2), encoding="utf-8")
        except Exception:
            pass
        (output / "report.json").write_text(json.dumps({"results": results, "errors": errors,
            "note": "Real Uno browser input with SwiftShader; no hardware GPU benchmark."}, indent=2), encoding="utf-8")
        (output / "console.json").write_text(json.dumps(messages, indent=2), encoding="utf-8")
        browser.close()
sys.exit(1 if errors else 0)
