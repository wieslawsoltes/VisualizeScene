"""Find/Replace on the real Uno app: menu, keyboard, pointer and file-input only."""
import json
from pathlib import Path
import sys
import traceback
from playwright.sync_api import sync_playwright

url = sys.argv[1] if len(sys.argv) > 1 else "http://127.0.0.1:8080/VisualizeScene/?test=1"
out = Path("artifacts/browser-search")
out.mkdir(parents=True, exist_ok=True)
nodes = []
for ident, x, y, text, extra in [
    ("a", 0, 0, "Alpha alpha alphabet", {}), ("b", 300, 0, "Beta", {}),
    ("g", 0, 300, "Folder", {"IsGroup": True, "Collapsed": True}),
    ("hidden", 40, 340, "Alpha secret", {"ParentId": "g"}),
    ("locked", 600, 0, "Alpha locked", {"Locked": True}),
    ("unicode", 300, 300, "Καλημέρα é 😀", {})]:
    nodes.append({"Id": ident, "Bounds": {"X": x, "Y": y, "Width": 180, "Height": 70}, "Text": text,
                  "Shape": "RoundedRectangle", "Fill": "#DDEBFA", "Stroke": "#4778A5", **extra})
model = {"Version": 1, "Graph": {"Nodes": nodes,
    "Edges": [{"Id": "ab", "SourceId": "a", "TargetId": "b", "Text": "Alpha route", "Weight": 1,
               "Bends": [], "Stroke": "#607D99", "Directed": True, "Routing": "Orthogonal"}],
    "Ports": [{"Id": "ap", "NodeId": "a", "RelativeLocation": {"X": 1, "Y": .5}}],
    "Labels": [{"Id": "la", "OwnerId": "a", "Text": "Alpha note", "Offset": {"X": 0, "Y": 120}, "FontSize": 12}]}}
fixture = out / "fixture.json"
fixture.write_text(json.dumps(model), encoding="utf-8")
empty = out / "empty.json"
empty.write_text(json.dumps({"Version": 1, "Graph": {k: [] for k in ["Nodes", "Edges", "Ports", "Labels"]}}))
results, errors = [], []
with sync_playwright() as p:
    browser = p.chromium.launch(headless=True, args=["--enable-unsafe-swiftshader", "--use-angle=swiftshader", "--disable-dev-shm-usage"])
    page = browser.new_page(viewport={"width": 1440, "height": 1050})
    page.add_init_script("Object.defineProperty(window,'showOpenFilePicker',{value:undefined,configurable:true});")
    page.on("pageerror", lambda error: errors.append(str(error)))
    def state(): return page.evaluate("globalThis.visualizeSceneState")
    def wait(expression):
        page.wait_for_function("() => {const v=globalThis.visualizeSceneState;return v && ("+expression+");}", timeout=45000)
    def click(name):
        key = json.dumps(name)
        wait(f"v.controls[{key}]?.width > 0 && v.controls[{key}]?.height > 0")
        b = state()["controls"][name]
        page.mouse.click(b["x"]+b["width"]/2, b["y"]+b["height"]/2)
        page.wait_for_timeout(150)
    def fill(name, text):
        click(name)
        page.keyboard.press("Control+a")
        if text:
            page.keyboard.insert_text(text)
        else:
            page.keyboard.press("Backspace")
    def find(text, count):
        fill("SearchQuery", text)
        wait("v.search.ready && v.search.query === "+json.dumps(text)+f" && v.search.count === {count}")
    def choose_kind(index):
        click("SearchKind"); page.keyboard.press("Home")
        for _ in range(index): page.keyboard.press("ArrowDown")
        page.keyboard.press("Enter")
        page.wait_for_timeout(300)
    def open_replace():
        click("MenuEdit"); click("ReplaceGraph"); wait("v.search.open")
    def import_file(file):
        with page.expect_file_chooser() as chooser: click("Open")
        chooser.value.set_files(str(file))
    def node_point(ident):
        s=state(); b=next(n["Bounds"] for n in s["model"]["Graph"]["Nodes"] if n["Id"]==ident)
        return s["canvas"]["x"]+(b["X"]+b["Width"]/2-s["origin"]["x"])*s["zoom"], s["canvas"]["y"]+(b["Y"]+b["Height"]/2-s["origin"]["y"])*s["zoom"]
    def record(name):
        assert not state()["error"], state()["error"]
        assert not state()["search"]["error"], state()["search"]["error"]
        results.append({"name": name, "passed": True})
        print("PASS", name, flush=True)
    try:
        page.goto(url, wait_until="domcontentloaded", timeout=120000)
        wait("v.ready && v.frames > 0")
        import_file(fixture); wait("v.status === 'Graph imported.' && v.model.Graph.Nodes.length === 6")
        before=state()["model"]; revision=state()["revision"]
        open_replace(); find("alpha", 6)
        wait("v.selection.length === 1 && v.selection[0] === 'a'")
        assert state()["revision"]==revision
        page.screenshot(path=str(out/"search-panel.png"))
        record("Find spans captions, edges and independent labels without modifying the document")
        click("SearchNext"); wait("v.search.current === 1")
        page.keyboard.press("F3"); wait("v.search.current === 2")
        page.keyboard.press("Shift+F3"); wait("v.search.current === 1")
        click("SearchPrevious"); wait("v.search.current === 0")
        click("SearchPrevious"); wait("v.search.current === 5")
        assert state()["model"]==before
        record("Next, previous and F3 navigation wrap through occurrences")
        click("SearchCase"); find("Alpha",4)
        click("SearchWord"); find("alpha",1)
        click("SearchCase"); wait("v.search.ready && v.search.count === 5")
        click("SearchWord"); wait("v.search.ready && v.search.count === 6")
        record("Case and whole-word options change actual result counts")
        fill("SearchReplacement","Z")
        click("SearchReplace")
        wait(f"v.revision === {revision+1} && v.search.ready && v.search.count === 5")
        assert next(n for n in state()["model"]["Graph"]["Nodes"] if n["Id"]=="a")["Text"]=="Z alpha alphabet"
        click("Undo"); wait(f"v.revision === {revision+2} && v.search.ready && v.search.count === 6")
        assert state()["model"]==before
        record("Single occurrence replacement preserves offsets and exact undo")
        revision=state()["revision"]
        click("SearchReplaceAll"); wait(f"v.revision === {revision+1} && v.search.ready && v.search.count === 1")
        assert state()["search"]["replaced"]==5 and state()["search"]["skipped"]==1
        assert next(n for n in state()["model"]["Graph"]["Nodes"] if n["Id"]=="locked")["Text"]=="Alpha locked"
        click("Undo"); wait(f"v.revision === {revision+2} && v.search.ready && v.search.count === 6")
        assert state()["model"]==before
        record("Replace All is one undo entry and skips locked owners")
        find("secret",0); click("SearchHidden"); wait("v.search.ready && v.search.count === 1 && v.selection[0] === 'g'")
        assert state()["model"]==before and next(n for n in before["Graph"]["Nodes"] if n["Id"]=="g")["Collapsed"]
        record("Hidden results reveal the folded representative without expanding or editing the group")
        click("SearchHidden"); click("SearchIds"); choose_kind(5); find("ap",1)
        wait("v.selection[0] === 'ap'")
        record("Port IDs are searchable as read-only results")
        choose_kind(0); click("SearchIds"); find("",0); click("SearchClose")
        page.mouse.click(*node_point("a")); open_replace(); click("SearchSelection"); find("alpha",3)
        click("SearchNext"); wait("v.search.current === 1 && v.search.count === 3")
        record("Captured-selection scope is stable while navigating results")
        click("SearchSelection"); choose_kind(3)
        for _ in range(8): click("ZoomOut")
        wait("v.zoom < .25")
        find("Alpha",1)
        wait("v.selection[0] === 'la' && v.zoom >= .25")
        record("Item-kind filter isolates and reveals labels above the low-detail threshold")
        choose_kind(0); find("😀",1); wait("v.selection[0] === 'unicode'")
        find("Καλημέρα",1)
        assert state()["model"]==before
        record("Unicode search locates supplementary characters and non-Latin text")
        fill("SearchQuery","Alpha"); fill("SearchQuery","Beta"); wait("v.search.ready && v.search.query === 'Beta' && v.search.count === 1 && v.selection[0] === 'b'")
        record("Rapid query changes publish only the current result set")
        click("SearchClose"); wait("!v.search.open && v.graphFocused")
        page.keyboard.press("Control+f"); wait("v.search.open")
        find("Alpha",6); page.keyboard.press("Escape"); wait("!v.search.open && v.graphFocused")
        assert state()["model"]==before
        record("Ctrl+F and Escape open and close modeless search without leaking graph shortcuts")
        open_replace(); find("Alpha",6)
        with page.expect_download() as saved: click("Save")
        saved.value.save_as(str(out/"before-reload.json"))
        import_file(empty); wait("v.model.Graph.Nodes.length === 0 && v.search.ready && v.search.count === 0")
        record("Document replacement invalidates old search results before they can be reused")
        import_file(fixture); wait("v.model.Graph.Nodes.length === 6 && v.search.ready && v.search.count === 6")
        page.set_viewport_size({"width":640,"height":900}); page.wait_for_timeout(500)
        find("Beta",1); page.screenshot(path=str(out/"narrow-search.png"))
        b=state()["controls"]["SearchQuery"]; assert b["x"]>=0 and b["x"]+b["width"]<=640
        record("Search remains usable with the workbench side rails collapsed")
        assert not errors,errors
    except Exception as error:
        traceback.print_exc(); errors.append(str(error))
        try: page.screenshot(path=str(out/"failure.png"))
        except Exception: pass
    finally:
        try: (out/"state.json").write_text(json.dumps(state(),indent=2),encoding="utf-8")
        except Exception: pass
        (out/"report.json").write_text(json.dumps({"results":results,"errors":errors,"note":"Actual compiled Uno input scenarios; Chromium SwiftShader is software rendering."},indent=2),encoding="utf-8")
        browser.close()
sys.exit(1 if errors else 0)
