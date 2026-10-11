"""Actual Uno touch/keyboard scenarios, not a JavaScript model simulation.
Touch input uses Chromium's native CDP input path; diagnostics remain read-only.
Run only against a browser publish of the source revision under test.
"""
import json
import pathlib
import sys
import traceback
from playwright.sync_api import sync_playwright

url = sys.argv[1] if len(sys.argv) > 1 else "http://127.0.0.1:8080/VisualizeScene/?test=1"
out = pathlib.Path("artifacts/browser-navigation")
out.mkdir(parents=True, exist_ok=True)
fixture = {"Version": 1, "Graph": {"Nodes": [], "Edges": [], "Ports": [], "Labels": []}}
for name, x, y in [("a", 0, 0), ("b", 240, 0), ("c", 240, 180), ("d", 1400, 500)]:
    fixture["Graph"]["Nodes"].append({"Id": name, "Bounds": {"X": x, "Y": y, "Width": 100, "Height": 60},
        "Text": name.upper(), "Shape": "RoundedRectangle", "Fill": "#DDEBFA", "Stroke": "#4778A5",
        "IsGroup": False, "Collapsed": False, "Locked": False})
fixture_path = out / "fixture.json"
fixture_path.write_text(json.dumps(fixture), encoding="utf-8")
results, errors, console = [], [], []

with sync_playwright() as p:
    browser = p.chromium.launch(headless=True, args=["--enable-unsafe-swiftshader", "--use-angle=swiftshader", "--disable-dev-shm-usage"])
    context = browser.new_context(viewport={"width": 1440, "height": 1050}, has_touch=True)
    page = context.new_page()
    page.add_init_script("Object.defineProperty(window,'showOpenFilePicker',{value:undefined,configurable:true});")
    page.on("pageerror", lambda e: errors.append(str(e)))
    page.on("console", lambda m: console.append({"type": m.type, "text": m.text}))
    cdp = context.new_cdp_session(page)

    def state():
        return page.evaluate("globalThis.visualizeSceneState")

    def wait(expression):
        page.wait_for_function("() => {const v=globalThis.visualizeSceneState;return v && ("+expression+");}", timeout=45000)
        return state()

    def click(name):
        key = json.dumps(name)
        wait(f"v.controls[{key}]?.width > 0 && v.controls[{key}]?.height > 0")
        b = state()["controls"][name]
        x, y = b["x"]+b["width"]/2, b["y"]+b["height"]/2
        assert 0 <= x < 1440 and 0 <= y < 1050, (name, b)
        page.mouse.click(x, y)
        page.wait_for_timeout(180)

    def touch_mode(mode):
        click("MenuView")
        click("TouchMenu")
        click("Touch"+mode)
        wait("v.touchMode === "+json.dumps(mode))

    def screen(x, y):
        s = state()
        return s["canvas"]["x"]+(x-s["origin"]["x"])*s["zoom"], s["canvas"]["y"]+(y-s["origin"]["y"])*s["zoom"]

    def node_point(name):
        b = next(n["Bounds"] for n in state()["model"]["Graph"]["Nodes"] if n["Id"] == name)
        return screen(b["X"]+b["Width"]/2, b["Y"]+b["Height"]/2)

    def center():
        c = state()["canvas"]
        return c["x"]+c["width"]*.5, c["y"]+c["height"]*.55

    def send(kind, points):
        cdp.send("Input.dispatchTouchEvent", {"type": kind, "touchPoints": [
            {"id": i, "x": xy[0], "y": xy[1], "radiusX": 2, "radiusY": 2, "force": 1}
            for i, xy in points]})
        page.wait_for_timeout(30)

    def end():
        send("touchEnd", [])
        wait("v.touchContacts === 0")

    def model_unchanged(before):
        assert state()["model"] == before["model"]
        assert state()["revision"] == before["revision"]

    def record(name, action):
        try:
            action()
            assert not state()["error"], state()["error"]
            results.append({"name": name, "passed": True})
            print("PASS", name, flush=True)
        except Exception as error:
            results.append({"name": name, "passed": False, "error": str(error)})
            raise

    try:
        page.goto(url, wait_until="domcontentloaded", timeout=120000)
        wait("v.ready && v.frames > 0")
        with page.expect_file_chooser() as pending:
            click("Open")
        pending.value.set_files(str(fixture_path))
        wait("v.status === 'Graph imported.' && v.model.Graph.Nodes.length === 4")

        def pan():
            before = state()
            x, y = center()
            send("touchStart", [(1, (x, y))])
            wait("v.touchContacts === 1")
            for i in range(1, 9):
                send("touchMove", [(1, (x+i*8, y+i*4))])
            wait("v.touchPhase === 'Panning'")
            end()
            after = state()
            assert abs(after["origin"]["x"]-(before["origin"]["x"]-64/before["zoom"])) < .1
            assert abs(after["origin"]["y"]-(before["origin"]["y"]-32/before["zoom"])) < .1
            model_unchanged(before)
        record("One-finger pan changes only the graph viewport", pan)

        def pinch():
            before = state()
            scale = page.evaluate("window.visualViewport?.scale || 1")
            x, y = center()
            cx, cy = x-before["canvas"]["x"], y-before["canvas"]["y"]
            world = (before["origin"]["x"]+cx/before["zoom"], before["origin"]["y"]+cy/before["zoom"])
            send("touchStart", [(1, (x-80, y)), (2, (x+80, y))])
            wait("v.touchContacts === 2")
            for i in range(1, 9):
                send("touchMove", [(1, (x-80-i*5, y+i*2)), (2, (x+80+i*5, y+i*2))])
            wait("v.touchPhase === 'Pinching'")
            page.screenshot(path=str(out/"pinching.png"))
            end()
            after = state()
            assert abs(after["zoom"]-before["zoom"]*1.5) < .01
            assert abs((world[0]-after["origin"]["x"])*after["zoom"]-cx) < 1
            assert abs((world[1]-after["origin"]["y"])*after["zoom"]-(cy+16)) < 1
            assert abs(page.evaluate("window.visualViewport?.scale || 1")-scale) < .001
            model_unchanged(before)
        record("Two-finger pinch preserves its moving centroid without browser-page zoom", pinch)

        def tap():
            click("Fit")
            before = state()
            send("touchStart", [(1, node_point("b"))])
            end()
            wait("v.selection.length === 1 && v.selection[0] === 'b'")
            model_unchanged(before)
        record("Single touch tap selects without creating an undo entry", tap)

        def editing():
            touch_mode("Edit")
            for control in ["ToggleGridSnap", "ToggleItemSnap"]:
                click("MenuView")
                click(control)
            wait("!v.snapToGrid && !v.snapToItems")
            before = state()
            x, y = node_point("b")
            send("touchStart", [(1, (x, y))])
            for i in range(1, 9):
                send("touchMove", [(1, (x+i*5, y+i*3))])
            model_unchanged(before)
            end()
            wait(f"v.revision === {before['revision']+1}")
            assert state()["model"] != before["model"]
            click("Undo")
            wait(f"v.revision === {before['revision']+2}")
            assert state()["model"] == before["model"]
        record("Edit touch mode commits a one-finger drag once and undo restores it", editing)

        def arbitrate():
            before = state()
            x, y = node_point("b")
            send("touchStart", [(1, (x, y))])
            send("touchMove", [(1, (x+40, y+20))])
            model_unchanged(before)
            send("touchStart", [(1, (x+40, y+20)), (2, (x+160, y+20))])
            wait("v.touchContacts === 2")
            send("touchMove", [(1, (x+20, y+20)), (2, (x+180, y+20))])
            end()
            model_unchanged(before)
        record("A second finger cancels the edit preview before pinch navigation", arbitrate)

        def canceled():
            click("Fit")
            before = state()
            x, y = node_point("b")
            send("touchStart", [(1, (x, y))])
            send("touchMove", [(1, (x+30, y+30))])
            send("touchCancel", [])
            wait("v.touchContacts === 0")
            model_unchanged(before)
        record("Native touch cancellation cannot commit an edit preview", canceled)

        def escape():
            before = state()
            x, y = node_point("b")
            send("touchStart", [(1, (x, y))])
            send("touchMove", [(1, (x+30, y+30))])
            page.keyboard.press("Escape")
            wait("v.touchContacts === 0")
            end()
            model_unchanged(before)
        record("Escape cancels tracked touches and ignores their later releases", escape)

        def deferred_creation():
            click("ModeNode")
            before = state()
            x, y = center()
            send("touchStart", [(1, (x-60, y)), (2, (x+60, y))])
            send("touchMove", [(1, (x-80, y)), (2, (x+80, y))])
            end()
            model_unchanged(before)
            send("touchStart", [(1, (x, y))])
            model_unchanged(before)
            end()
            wait("v.model.Graph.Nodes.length === 5")
            click("Undo")
            wait("v.model.Graph.Nodes.length === 4")
            click("ModeSelect")
        record("Create-node mode defers tap creation and never creates nodes during pinch", deferred_creation)

        def disabled():
            touch_mode("Disabled")
            before = state()
            x, y = center()
            send("touchStart", [(1, (x-50, y)), (2, (x+50, y))])
            send("touchMove", [(1, (x-80, y)), (2, (x+80, y))])
            end()
            assert state()["zoom"] == before["zoom"] and state()["origin"] == before["origin"]
            model_unchanged(before)
            touch_mode("Navigate")
        record("Touch handling can be disabled without disabling mouse or keyboard", disabled)

        def keyboard():
            click("Fit")
            before = state()
            page.mouse.click(*node_point("a"))
            page.keyboard.press("Control+ArrowRight")
            wait("v.navigationFocus === 'b' && v.selection.length === 1")
            page.keyboard.press("Control+Shift+ArrowDown")
            wait("v.navigationFocus === 'c' && v.selection.length === 2")
            page.keyboard.press("Control+Home")
            wait("v.navigationFocus === 'a'")
            for _ in range(3):
                click("ZoomIn")
            # Focus the graph without altering selection via the navigation menu.
            click("MenuView")
            click("NavigationMenu")
            click("NavigateLast")
            wait("v.navigationFocus === 'd' && v.graphFocused")
            sx, sy = node_point("d")
            c = state()["canvas"]
            assert c["x"] <= sx <= c["x"]+c["width"] and c["y"] <= sy <= c["y"]+c["height"]
            page.screenshot(path=str(out/"keyboard-focus.png"))
            model_unchanged(before)
        record("Spatial keys, extended selection, boundary navigation and reveal preserve graph content", keyboard)

        def page_pan():
            before = state()
            page.keyboard.press("PageDown")
            wait(f"v.origin.y > {before['origin']['y']}")
            middle = state()
            page.keyboard.press("Shift+PageDown")
            wait(f"v.origin.x > {middle['origin']['x']}")
            model_unchanged(before)
        record("Page keys pan vertically or horizontally without moving graph items", page_pan)

        def text_isolation():
            click("MenuView")
            click("NavigationMenu")
            click("NavigateFirst")
            wait("v.navigationFocus === 'a' && v.graphFocused")
            before = state()
            page.keyboard.press("F2")
            wait("v.labelEditor.ready")
            page.keyboard.press("Control+ArrowRight")
            page.keyboard.press("Control+End")
            assert state()["navigationFocus"] == "a"
            page.keyboard.press("Escape")
            wait("!v.labelEditor.ready")
            model_unchanged(before)
        record("Navigation shortcuts cannot escape the label editor into the graph", text_isolation)
        assert not errors, errors
    except Exception as error:
        traceback.print_exc()
        errors.append(str(error))
        try:
            page.screenshot(path=str(out/"failure.png"))
        except Exception:
            pass
    finally:
        try:
            (out/"state.json").write_text(json.dumps(state(), indent=2), encoding="utf-8")
        except Exception:
            pass
        (out/"report.json").write_text(json.dumps({"results": results, "errors": errors,
            "note": "Compiled Uno input scenarios with native Chromium touch dispatch; SwiftShader is software rendering."}, indent=2), encoding="utf-8")
        (out/"console.json").write_text(json.dumps(console, indent=2), encoding="utf-8")
        browser.close()
sys.exit(1 if errors or any(not r["passed"] for r in results) else 0)
