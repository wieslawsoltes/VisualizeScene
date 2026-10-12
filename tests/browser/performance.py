"""Large-document regression on the real Uno application, using input only for graph/view changes.
Counters verify bounded work and reuse. This is not an FPS or hardware-GPU benchmark.
"""
import json
from pathlib import Path
import sys
import traceback
from playwright.sync_api import sync_playwright

url = sys.argv[1] if len(sys.argv) > 1 else "http://127.0.0.1:8080/VisualizeScene/?test=1"
out = Path("artifacts/browser-performance")
out.mkdir(parents=True, exist_ok=True)
g = {"Nodes": [], "Edges": [], "Ports": [], "Labels": []}
for i in range(10000):
    g["Nodes"].append({"Id": f"n{i}", "Bounds": {"X": i % 100 * 300, "Y": i // 100 * 250, "Width": 100, "Height": 60},
                       "Text": "Node", "Shape": "RoundedRectangle", "Fill": "#DDEBFA", "Stroke": "#4778A5"})
    g["Ports"].append({"Id": f"p{i}", "NodeId": f"n{i}", "RelativeLocation": {"X": 1, "Y": .5}})
    g["Labels"].append({"Id": f"l{i}", "OwnerId": f"n{i}", "Text": "Label", "Offset": {"X": 0, "Y": 80}, "FontSize": 12})
    if i % 100 < 99:
        g["Edges"].append({"Id": f"e{i}", "SourceId": f"n{i}", "TargetId": f"n{i+1}", "Text": "", "Weight": 1,
                           "Bends": [], "Stroke": "#607D99", "Directed": True, "Routing": "Orthogonal"})
fixture = out / "fixture.json"
fixture.write_text(json.dumps({"Version": 1, "Graph": g}))
results, errors = [], []
with sync_playwright() as p:
    browser = p.chromium.launch(headless=True, args=["--enable-unsafe-swiftshader", "--use-angle=swiftshader", "--disable-dev-shm-usage"])
    page = browser.new_page(viewport={"width": 1440, "height": 1050})
    page.add_init_script("Object.defineProperty(window,'showOpenFilePicker',{value:undefined,configurable:true});")
    page.on("pageerror", lambda e: errors.append(str(e)))
    def wait(expression):
        page.wait_for_function("() => {const v=globalThis.visualizeSceneState; return v && (" + expression + ");}", timeout=120000)
    def state():
        return page.evaluate("""() => {const v=globalThis.visualizeSceneState; return {
            revision:v.revision,frames:v.frames,zoom:v.zoom,origin:v.origin,canvas:v.canvas,
            performance:v.performance,error:v.error,nodes:v.model.Graph.Nodes.length,edges:v.model.Graph.Edges.length};}""")
    def click(name):
        key = json.dumps(name)
        wait(f"v.controls[{key}]?.width > 0")
        b = page.evaluate(f"globalThis.visualizeSceneState.controls[{key}]")
        page.mouse.click(b["x"]+b["width"]/2, b["y"]+b["height"]/2)
        page.wait_for_timeout(180)
    def record(name):
        assert not state()["error"], state()["error"]
        results.append({"name": name, "passed": True, "state": state()})
        print("PASS", name, flush=True)
    try:
        page.goto(url, wait_until="domcontentloaded", timeout=120000)
        wait("v.ready && v.frames > 0")
        with page.expect_file_chooser() as pending:
            click("Open")
        pending.value.set_files(str(fixture))
        wait("v.status === 'Graph imported.' && v.model.Graph.Nodes.length === 10000 && v.performance.visibleNodes > 100")
        before = state()
        page.evaluate("globalThis.performanceBaseline = JSON.stringify(globalThis.visualizeSceneState.model)")
        record("Import 10000 nodes, 9900 edges, 10000 ports and labels through the real picker")
        while state()["zoom"] < 1:
            click("ZoomIn")
        wait("v.performance.visibleLabels > 0 && v.performance.examinedItems < 1000")
        after = state()
        for counter in ["documentBuilds", "sceneBuilds", "nodeIndexBuilds", "edgeIndexBuilds", "annotationIndexBuilds", "createdPaths"]:
            assert after["performance"][counter] == before["performance"][counter], counter
        assert after["revision"] == before["revision"]
        page.screenshot(path=str(out / "large-graph-culled.png"))
        record("Zoom reuses document preparation and culls annotation work to the viewport")
        c = after["canvas"]
        x, y = c["x"]+c["width"]*.5, c["y"]+c["height"]*.5
        page.mouse.move(x, y)
        page.mouse.down(button="middle")
        page.mouse.move(x+80, y+40, steps=16)
        page.mouse.up(button="middle")
        wait(f"v.frames > {after['frames']} && v.origin.x !== {after['origin']['x']}")
        after = state()
        assert after["performance"]["sceneBuilds"] == before["performance"]["sceneBuilds"]
        assert after["performance"]["documentBuilds"] == before["performance"]["documentBuilds"]
        assert after["performance"]["examinedItems"] < 1000
        record("Panning avoids full graph serialization and scene rebuilding")
        page.keyboard.press("Control+Home")
        wait("v.navigationFocus === 'n0'")
        before_edit = state()
        page.keyboard.press("ArrowRight")
        wait(f"v.revision === {before_edit['revision']+1} && v.performance.sceneBuilds > {before_edit['performance']['sceneBuilds']}")
        assert state()["performance"]["documentBuilds"] == before_edit["performance"]["documentBuilds"]+1
        page.keyboard.press("Control+z")
        wait(f"v.revision === {before_edit['revision']+2}")
        assert page.evaluate("JSON.stringify(globalThis.visualizeSceneState.model) === globalThis.performanceBaseline")
        record("An actual edit invalidates the cached document once and undo restores all graph data")
        # Large-selection adorners and handles must remain viewport-bounded.
        page.keyboard.press("Control+a")
        wait("v.selection.length > 30000 && v.performance.selectionAdorners > 0 && v.performance.selectionAdorners < 256")
        assert state()["performance"]["retainedPictures"] <= state()["performance"]["pictureCapacity"]
        page.screenshot(path=str(out / "large-selection.png"))
        record("Select-all rendering is viewport-bounded with a finite native picture budget")
        page.wait_for_function("() => globalThis.visualizeSceneWorkspace?.inspector.batchCount === 39900", timeout=45000)
        properties = page.evaluate("globalThis.visualizeSceneWorkspace.inspector")
        assert properties["batchExamined"] == 39900 and properties["count"] == 15
        record("Mixed property summary examines each selected item once with a fixed number of editors")
        page.keyboard.press("Delete")
        wait("v.model.Graph.Nodes.length === 0 && v.performance.retainedPictures === 0")
        page.keyboard.press("Control+z")
        wait("v.model.Graph.Nodes.length === 10000")
        assert page.evaluate("JSON.stringify(globalThis.visualizeSceneState.model) === globalThis.performanceBaseline")
        record("Bulk deletion and undo preserve all 39900 graph items")
        page.keyboard.press("Control+Home")
        wait("v.selection.length === 1 && v.selection[0] === 'n0'")
        page.keyboard.press("Control+Shift+ArrowRight")
        wait("v.selection.length === 2 && v.selection.includes('n1')")
        click("Group")
        wait("v.selection.length === 1 && v.model.Graph.Nodes.some(n => n.Id === v.selection[0] && n.IsGroup)")
        original = page.evaluate("""() => {const v=globalThis.visualizeSceneState; return {
            group: v.model.Graph.Nodes.find(n => n.Id === v.selection[0]), child: v.model.Graph.Nodes.find(n => n.Id === 'n0'), revision:v.revision};}""")
        target_x = original["group"]["Bounds"]["X"] + .123456789
        click("PropertyX")
        page.keyboard.press("Control+a")
        page.keyboard.type(str(target_x), delay=10)
        click("PropertyApply")
        wait(f"v.revision === {original['revision']+1}")
        child_x = page.evaluate("globalThis.visualizeSceneState.model.Graph.Nodes.find(n => n.Id === 'n0').Bounds.X")
        assert abs(child_x - original["child"]["Bounds"]["X"] - .123456789) < 1e-8
        record("Property-panel group movement translates its children with full precision")
        revision_after = state()["revision"]
        click("PropertyApply")
        page.wait_for_timeout(350)
        assert state()["revision"] == revision_after
        record("Reapplying unchanged fractional properties neither rounds values nor creates history")
        click("Undo")
        click("Undo")
        wait("v.model.Graph.Nodes.length === 10000 && !v.model.Graph.Nodes.some(n => n.IsGroup)")
        assert page.evaluate("JSON.stringify(globalThis.visualizeSceneState.model) === globalThis.performanceBaseline")
        record("Property and group operations restore exactly through undo")
        assert not errors, errors
    except Exception as error:
        traceback.print_exc()
        errors.append(str(error))
        try:
            page.screenshot(path=str(out / "failure.png"))
        except Exception:
            pass
    finally:
        (out / "report.json").write_text(json.dumps({"results": results, "errors": errors,
            "note": "Actual Uno/Chromium/SwiftShader input. Structural counters, not FPS or physical GPU throughput."}, indent=2))
        browser.close()
sys.exit(1 if errors else 0)
