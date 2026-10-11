"""Selection contracts exercised through the actual compiled Uno control.
The fixture enters through the upload picker. Diagnostics are read-only.
"""
import json
import pathlib
import sys
import traceback
from playwright.sync_api import sync_playwright

url = sys.argv[1] if len(sys.argv) > 1 else "http://127.0.0.1:8080/VisualizeScene/?test=1"
output = pathlib.Path("artifacts/browser-selection")
output.mkdir(parents=True, exist_ok=True)
fixture = {"Version": 1, "Graph": {"Nodes": [], "Edges": [], "Ports": [], "Labels": []}}
for name, x, y in [("a", 0, 0), ("b", 300, 0), ("c", 300, 200)]:
    fixture["Graph"]["Nodes"].append({"Id": name, "Bounds": {"X": x, "Y": y, "Width": 100, "Height": 60},
        "Text": name.upper(), "Shape": "RoundedRectangle", "Fill": "#DDEBFA", "Stroke": "#4778A5",
        "IsGroup": False, "Collapsed": False, "Locked": False})
for name, source, target in [("ab", "a", "b"), ("bc", "b", "c")]:
    fixture["Graph"]["Edges"].append({"Id": name, "SourceId": source, "TargetId": target, "Text": "", "Weight": 1,
        "Bends": [], "Stroke": "#607D99", "Directed": True, "Routing": "Polyline"})
fixture["Graph"]["Ports"] = [{"Id": "ap", "NodeId": "a", "RelativeLocation": {"X": 1, "Y": .5}}]
fixture["Graph"]["Labels"] = [{"Id": "la", "OwnerId": "a", "Text": "Annotation", "Offset": {"X": 0, "Y": 100}, "FontSize": 12}]
fixture_path = output / "fixture.json"
fixture_path.write_text(json.dumps(fixture), encoding="utf-8")
results, errors, console = [], [], []

with sync_playwright() as p:
    browser = p.chromium.launch(headless=True, args=["--enable-unsafe-swiftshader", "--use-angle=swiftshader", "--disable-dev-shm-usage"])
    page = browser.new_page(viewport={"width": 1440, "height": 1050})
    page.add_init_script("Object.defineProperty(window,'showOpenFilePicker',{value:undefined,configurable:true});")
    page.on("pageerror", lambda error: errors.append(str(error)))
    page.on("console", lambda message: console.append({"type": message.type, "text": message.text}))

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

    def command(name):
        click("MenuSelection")
        click(name)
        wait("v.graphFocused")

    def screen(x, y):
        s = state()
        return s["canvas"]["x"]+(x-s["origin"]["x"])*s["zoom"], s["canvas"]["y"]+(y-s["origin"]["y"])*s["zoom"]

    def selection(*expected, display=False):
        key = "displaySelection" if display else "selection"
        encoded = json.dumps(sorted(expected))
        wait(f"JSON.stringify([...v.{key}].sort()) === JSON.stringify({encoded})")

    def marquee(start, end, keys=(), release=True):
        for key in keys:
            page.keyboard.down(key)
        page.mouse.move(*screen(*start))
        page.mouse.down()
        page.mouse.move(*screen(*end), steps=12)
        wait("v.marquee !== null")
        if release:
            page.mouse.up()
            wait("v.marquee === null")
            for key in reversed(keys):
                page.keyboard.up(key)

    def record(name, action):
        action()
        s = state()
        assert s["model"] == before, "Selection changed graph content"
        assert s["revision"] == revision, "Selection created a graph history entry"
        assert not s["error"], s["error"]
        results.append({"name": name, "passed": True})
        print("PASS", name, flush=True)

    try:
        page.goto(url, wait_until="domcontentloaded", timeout=120000)
        wait("v.ready && v.frames > 0")
        with page.expect_file_chooser() as pending:
            click("Open")
        pending.value.set_files(str(fixture_path))
        wait("v.status === 'Graph imported.' && v.model.Graph.Labels.length === 1")
        before, revision = state()["model"], state()["revision"]
        lb = state()["labelBounds"]["la"]
        first = (lb["x"]-12, lb["y"]-12)
        last = (lb["x"]+lb["width"]+12, lb["y"]+lb["height"]+12)

        def kinds():
            for name, ids in [("SelectNodes", ["a", "b", "c"]), ("SelectEdges", ["ab", "bc"]),
                              ("SelectPorts", ["ap"]), ("SelectLabels", ["la"]), ("SelectGroups", [])]:
                command(name)
                selection(*ids)
        record("Selection menu filters nodes, edges, ports, labels and groups", kinds)

        def invert_and_clear():
            command("SelectLabels")
            command("InvertSelection")
            selection("a", "b", "c", "ab", "bc", "ap")
            page.keyboard.press("Control+i")
            selection("la")
            page.keyboard.press("Control+Shift+a")
            selection()
            page.keyboard.press("Control+a")
            selection("a", "b", "c", "ab", "bc", "ap", "la")
        record("Invert, clear and select-all keyboard commands match menu behavior", invert_and_clear)

        def preview_cancel():
            page.mouse.click(*screen(350, 30))
            selection("b")
            marquee(first, last, release=False)
            selection("b")
            selection("la", display=True)
            page.screenshot(path=str(output / "selection-preview.png"))
            page.keyboard.press("Escape")
            page.mouse.up()
            wait("v.marquee === null")
            selection("b", display=True)
            selection("b")
        record("Marquee previews painted labels without committing; Escape restores selection", preview_cancel)

        def release():
            marquee(first, last)
            selection("la")
        record("Releasing a marquee commits its preview without graph edits", release)

        def modifiers():
            page.mouse.click(*screen(350, 30))
            selection("b")
            marquee(first, last, ("Shift",))
            selection("b", "la")
            marquee(first, last, ("Control",))
            selection("b")
            marquee(first, last, ("Shift",))
            selection("b", "la")
            marquee(first, last, ("Control", "Shift"))
            selection("b")
        record("Shift adds, Control toggles and Control+Shift subtracts area selections", modifiers)

        def directional():
            command("MarqueeDirectional")
            wait("v.marqueeMode === 'Directional'")
            middle_x = lb["x"]+lb["width"]/2
            marquee(first, (middle_x, last[1]), release=False)
            wait("v.marqueeContains")
            selection(display=True)
            page.screenshot(path=str(output / "window-selection.png"))
            page.mouse.up()
            wait("v.marquee === null")
            selection()
            marquee((middle_x, first[1]), (first[0], last[1]), release=False)
            wait("!v.marqueeContains")
            selection("la", display=True)
            page.screenshot(path=str(output / "crossing-selection.png"))
            page.mouse.up()
            wait("v.marquee === null")
            selection("la")
        record("Directional drag uses window containment to the right and crossing to the left", directional)

        def contains():
            command("MarqueeContains")
            wait("v.marqueeMode === 'Contains'")
            marquee(first, last)
            selection("la")
            marquee((190, 18), (210, 42))
            selection()
        record("Containment includes whole labels but excludes partially enclosed edges", contains)

        def crossing_edge():
            command("MarqueeIntersects")
            marquee((190, 18), (210, 42))
            selection("ab")
        record("Crossing selects the actual edge path with both endpoints outside", crossing_edge)

        def port():
            marquee((90, 70), (110, 20))
            assert "ap" in state()["selection"]
        record("Port hit geometry participates in an area selection", port)

        def filters():
            command("MarqueeLabelsOnly")
            wait("v.marqueeKinds === 8")
            marquee((-8, -8), (408, 268))
            selection("la")
            command("MarqueeNodesOnly")
            wait("v.marqueeKinds === 1")
            marquee((-8, -8), (408, 268))
            selection("a", "b", "c")
            command("MarqueeAllKinds")
            wait("v.marqueeKinds === 15")
        record("Marquee item filters apply independently of its geometric mode", filters)

        def neighbors():
            page.mouse.click(*screen(50, 30))
            selection("a")
            command("SelectNeighborsOutgoing")
            selection("a", "b", "ab")
            command("SelectNeighborsOutgoing")
            selection("a", "b", "c", "ab", "bc")
            page.mouse.click(*screen(350, 230))
            selection("c")
            command("SelectNeighborsIncoming")
            selection("b", "c", "bc")
        record("Topology expansion follows incoming and outgoing edge directions", neighbors)

        def out_and_back():
            page.mouse.click(*screen(350, 30))
            selection("b")
            marquee(first, last, release=False)
            page.mouse.move(*screen(*first), steps=10)
            wait("v.marquee === null")
            page.mouse.up()
            selection("b")
            selection("b", display=True)
        record("Out-and-back marquee preserves the original selection", out_and_back)
        page.screenshot(path=str(output / "selection.png"))
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
            "note": "Actual Uno control with read-only diagnostics; pointer/keyboard selection, SwiftShader rendering."}, indent=2), encoding="utf-8")
        (output / "console.json").write_text(json.dumps(console, indent=2), encoding="utf-8")
        browser.close()
sys.exit(1 if errors else 0)
