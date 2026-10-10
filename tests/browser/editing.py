"""Real Uno pointer/keyboard regression tests for ports, reconnection and modal labels.
Diagnostics provide read-only model, geometry and control bounds; no mutation bridge is used.
"""
import json
import os
import pathlib
import sys
import time
import traceback
from playwright.sync_api import sync_playwright

url = sys.argv[1] if len(sys.argv) > 1 else "http://127.0.0.1:8080/VisualizeScene/?test=1"
output = pathlib.Path("artifacts/browser-editing")
output.mkdir(parents=True, exist_ok=True)
results, errors, console = [], [], []

with sync_playwright() as p:
    browser = p.chromium.launch(executable_path=os.environ.get("VISUALIZESCENE_CHROMIUM"), headless=True,
        args=["--enable-unsafe-swiftshader", "--use-angle=swiftshader", "--disable-dev-shm-usage"])
    page = browser.new_page(viewport={"width": 1440, "height": 1050})
    page.on("pageerror", lambda e: errors.append(str(e)))
    page.on("console", lambda m: console.append({"type": m.type, "text": m.text}))
    page.set_default_timeout(30000)

    def state():
        return page.evaluate("globalThis.visualizeSceneState")

    def wait(expression):
        page.wait_for_function("() => { const v=globalThis.visualizeSceneState; return v && (" + expression + "); }", timeout=45000)
        return state()

    def click(name):
        key = json.dumps(name)
        wait(f"v.controls[{key}] && v.controls[{key}].width > 0 && v.controls[{key}].height > 0")
        b = state()["controls"][name]
        assert b and b["width"] > 0 and b["height"] > 0, name
        page.mouse.click(b["x"] + b["width"] / 2, b["y"] + b["height"] / 2)
        page.wait_for_timeout(150)

    def screen(x, y):
        s = state()
        return s["canvas"]["x"] + (x-s["origin"]["x"])*s["zoom"], s["canvas"]["y"] + (y-s["origin"]["y"])*s["zoom"]

    def node(node_id):
        return next(n for n in state()["model"]["Graph"]["Nodes"] if n["Id"] == node_id)

    def node_point(node_id):
        b = node(node_id)["Bounds"]
        return screen(b["X"] + b["Width"]/2, b["Y"] + b["Height"]/2)

    def port_point(port_id):
        port = next(p for p in state()["model"]["Graph"]["Ports"] if p["Id"] == port_id)
        b = node(port["NodeId"])["Bounds"]
        return screen(b["X"] + b["Width"]*port["RelativeLocation"]["X"], b["Y"] + b["Height"]*port["RelativeLocation"]["Y"])

    def endpoint(edge_id, end):
        point = state()["edgePaths"][edge_id][0 if end == "source" else -1]
        return screen(point["x"], point["y"])

    def select_edge(edge_id):
        path = state()["edgePaths"][edge_id]
        pairs = list(zip(path, path[1:]))
        a, b = max(pairs, key=lambda ab: (ab[0]["x"]-ab[1]["x"])**2 + (ab[0]["y"]-ab[1]["y"])**2)
        page.mouse.click(*screen((a["x"]+b["x"])/2, (a["y"]+b["y"])/2))
        wait(json.dumps(edge_id) + " === v.selection[0]")

    def drag(start, end, cancel=False):
        page.mouse.move(*start)
        page.mouse.down()
        page.mouse.move(*end, steps=10)
        if cancel:
            page.keyboard.press("Escape")
        page.mouse.up()
        page.wait_for_timeout(250)

    def type_label(text):
        # Wait for the real dialog's Opened/focus transition, not a guessed animation duration.
        wait("v.labelEditor.ready")
        page.wait_for_function("() => ['TEXTAREA','INPUT'].includes(document.activeElement?.tagName)")
        page.keyboard.press("Control+a")
        page.keyboard.type(text, delay=20)
        wait("v.labelEditor.text === " + json.dumps(text))

    def record(name, action):
        started = time.time()
        try:
            action()
            assert not state()["error"], state()["error"]
            results.append({"name": name, "passed": True, "seconds": time.time()-started})
            print("PASS", name, flush=True)
        except Exception as error:
            (output/"focus.json").write_text(json.dumps(page.evaluate("""() => ({
                active: document.activeElement?.outerHTML,
                inputs: [...document.querySelectorAll('input,textarea')].map(e => ({html:e.outerHTML,value:e.value}))
            })"""), indent=2))
            results.append({"name": name, "passed": False, "error": str(error)})
            raise

    try:
        page.goto(url, wait_until="domcontentloaded", timeout=120000)
        wait("v.ready && v.frames > 0")
        page.wait_for_timeout(500)
        click("New")
        wait("v.model.Graph.Nodes.length === 0")
        click("ModeNode")
        c = state()["canvas"]
        for x, y in [(0.22, 0.28), (0.70, 0.28), (0.70, 0.65)]:
            page.mouse.click(c["x"]+c["width"]*x, c["y"]+c["height"]*y)
            page.wait_for_timeout(180)
        wait("v.model.Graph.Nodes.length === 3")
        ids = [n["Id"] for n in state()["model"]["Graph"]["Nodes"]]
        click("ModeSelect")

        def add_ports():
            for node_id in ids[:2]:
                page.mouse.click(*node_point(node_id))
                click("MenuEdit")
                click("AddPort")
            wait("v.model.Graph.Ports.length === 2")
            assert len(state()["model"]["Graph"]["Nodes"]) == 3
        record("Add explicit ports through the Edit menu", add_ports)
        ports = [p["Id"] for p in state()["model"]["Graph"]["Ports"]]

        def relocate_port():
            b = node(ids[1])["Bounds"]
            revision = state()["revision"]
            drag(port_point(ports[1]), screen(b["X"], b["Y"]+b["Height"]/2))
            wait(f"v.revision === {revision+1}")
            p = state()["model"]["Graph"]["Ports"][1]
            assert p["RelativeLocation"]["X"] == 0
            click("Undo")
            wait("v.model.Graph.Ports[1].RelativeLocation.X === 1")
            click("Redo")
            wait("v.model.Graph.Ports[1].RelativeLocation.X === 0")
        record("Relocate a port by dragging and undo/redo atomically", relocate_port)

        def connect():
            click("ModeEdge")
            page.mouse.click(*port_point(ports[0]))
            page.mouse.click(*port_point(ports[1]))
            wait("v.model.Graph.Edges.length === 1")
            e = state()["model"]["Graph"]["Edges"][0]
            assert e["SourcePortId"] == ports[0] and e["TargetPortId"] == ports[1]
        record("Create an edge between explicit ports", connect)
        edge_id = state()["model"]["Graph"]["Edges"][0]["Id"]
        click("ModeSelect")

        def reconnect_target():
            revision = state()["revision"]
            drag(endpoint(edge_id, "target"), node_point(ids[2]))
            wait(f"v.revision === {revision+1}")
            e = state()["model"]["Graph"]["Edges"][0]
            assert e["TargetId"] == ids[2] and e.get("TargetPortId") is None and e["SourcePortId"] == ports[0]
            click("Undo")
            wait("v.model.Graph.Edges[0].TargetPortId === " + json.dumps(ports[1]))
        record("Reconnect target handle to another node and restore its old port", reconnect_target)

        def reject_drop():
            revision = state()["revision"]
            c = state()["canvas"]
            drag(endpoint(edge_id, "target"), (c["x"]+40, c["y"]+c["height"]-70))
            assert state()["revision"] == revision
            assert state()["model"]["Graph"]["Edges"][0]["TargetPortId"] == ports[1]
        record("Dropping a reconnect handle on empty space does not mutate", reject_drop)

        def cancel_reconnect():
            revision = state()["revision"]
            drag(endpoint(edge_id, "source"), node_point(ids[2]), cancel=True)
            assert state()["revision"] == revision
            assert state()["model"]["Graph"]["Edges"][0]["SourcePortId"] == ports[0]
        record("Escape cancels endpoint preview without a history entry", cancel_reconnect)

        def move_connected_port():
            c = state()["canvas"]
            page.mouse.click(c["x"]+20, c["y"]+30)
            b = node(ids[0])["Bounds"]
            revision = state()["revision"]
            drag(port_point(ports[0]), screen(b["X"]+b["Width"]/2, b["Y"]))
            wait(f"v.revision === {revision+1}")
            point = state()["edgePaths"][edge_id][0]
            assert point["y"] == b["Y"]
            click("Undo")
            wait("v.model.Graph.Ports[0].RelativeLocation.X === 1")
        record("Moving a connected port updates its edge endpoint", move_connected_port)

        def edit_label():
            page.mouse.click(*node_point(ids[0]))
            page.keyboard.press("F2")
            type_label("Source system")
            page.keyboard.press("Tab")
            page.keyboard.press("Enter")
            wait("v.model.Graph.Nodes[0].Text === 'Source system'")
            assert len(state()["model"]["Graph"]["Nodes"]) == 3
        record("F2 label editor applies text without graph shortcut leakage", edit_label)

        def double_click_label():
            page.mouse.dblclick(*node_point(ids[0]), delay=90)
            type_label("Double-click editor")
            page.keyboard.press("Tab")
            page.keyboard.press("Enter")
            wait("v.model.Graph.Nodes[0].Text === 'Double-click editor'")
        record("A genuine double click still opens the label editor", double_click_label)

        def cancel_label():
            revision = state()["revision"]
            page.keyboard.press("F2")
            type_label("Discard this text")
            page.keyboard.press("Escape")
            page.wait_for_timeout(300)
            assert state()["revision"] == revision
            assert node(ids[0])["Text"] == "Double-click editor"
        record("Canceling the label dialog preserves the document", cancel_label)
        def independent_label():
            click("MenuEdit")
            click("AddLabel")
            wait("v.model.Graph.Labels.length === 1")
            assert state()["model"]["Graph"]["Labels"][0]["OwnerId"] == ids[0]
        record("Add an independent label to the selected node", independent_label)
        label_id = state()["model"]["Graph"]["Labels"][0]["Id"]

        def move_label():
            s = state()
            b = s["labelBounds"][label_id]
            before = s["model"]["Graph"]["Labels"][0]["Offset"].copy()
            center = (b["x"]+b["width"]/2, b["y"]+b["height"]/2)
            drag(screen(*center), screen(center[0]+40, center[1]+30))
            wait(f"v.revision === {s['revision']+1}")
            after = state()["model"]["Graph"]["Labels"][0]["Offset"]
            assert after != before and len(state()["model"]["Graph"]["Nodes"]) == 3
            click("Undo")
            wait("v.model.Graph.Labels[0].Offset.X === " + str(before["X"]))
        record("Drag an independent label using measured bounds and undo", move_label)

        def rename_label():
            # Undo toolbar has keyboard focus; click the restored label before F2.
            b = state()["labelBounds"][label_id]
            page.mouse.click(*screen(b["x"]+b["width"]/2, b["y"]+b["height"]/2))
            page.keyboard.press("F2")
            type_label("Independent annotation")
            page.keyboard.press("Tab")
            page.keyboard.press("Enter")
            wait("v.model.Graph.Labels[0].Text === 'Independent annotation'")
        record("Edit independent label text with F2", rename_label)

        def duplicate_label():
            page.keyboard.press("Control+d")
            wait("v.model.Graph.Labels.length === 2")
            assert len(state()["model"]["Graph"]["Nodes"]) == 3
            assert state()["model"]["Graph"]["Labels"][1]["OwnerId"] == ids[0]
        record("Duplicate only a label without duplicating its node", duplicate_label)

        def retarget_label():
            page.keyboard.press("Control+c")
            page.mouse.click(*node_point(ids[2]))
            page.keyboard.press("Control+v")
            wait("v.model.Graph.Labels.length === 3")
            assert state()["model"]["Graph"]["Labels"][-1]["OwnerId"] == ids[2]
            page.keyboard.press("Delete")
            wait("v.model.Graph.Labels.length === 2")
            page.keyboard.press("Control+z")
            wait("v.model.Graph.Labels.length === 3")
        record("Paste a label onto another owner, delete and restore it", retarget_label)
        page.screenshot(path=str(output/"editing.png"), full_page=True)
        assert not errors, errors
    except Exception as error:
        traceback.print_exc()
        errors.append(str(error))
        try:
            page.screenshot(path=str(output/"failure.png"), full_page=True)
        except Exception:
            pass
    finally:
        try:
            (output/"state.json").write_text(json.dumps(state(), indent=2))
        except Exception:
            pass
        (output/"report.json").write_text(json.dumps({"results": results, "errors": errors,
            "note": "Actual compiled Uno app; software SwiftShader; pointer and keyboard mutations only."}, indent=2))
        (output/"console.json").write_text(json.dumps(console, indent=2))
        browser.close()
sys.exit(1 if errors or any(not r["passed"] for r in results) else 0)
