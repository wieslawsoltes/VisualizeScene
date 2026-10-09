"""Exercise layouts, analysis, exports and samples in the compiled Uno browser UI.
No test-only mutation APIs: every operation uses pointer/keyboard/file chooser input.
"""
import json
import pathlib
import sys
import time
import traceback
import xml.etree.ElementTree as ET
from playwright.sync_api import sync_playwright

url = sys.argv[1] if len(sys.argv) > 1 else "http://127.0.0.1:8080/VisualizeScene/?test=1"
output = pathlib.Path("artifacts/browser-features")
output.mkdir(parents=True, exist_ok=True)
results, errors, console = [], [], []

with sync_playwright() as p:
    browser = p.chromium.launch(headless=True, args=["--enable-unsafe-swiftshader", "--use-angle=swiftshader", "--disable-dev-shm-usage"])
    page = browser.new_page(viewport={"width": 1440, "height": 1050}, device_scale_factor=1, accept_downloads=True)
    page.on("pageerror", lambda error: errors.append(str(error)))
    page.on("console", lambda message: console.append({"type": message.type, "text": message.text}))
    page.set_default_timeout(30000)

    def state():
        return page.evaluate("globalThis.visualizeSceneState")

    def wait(expression):
        page.wait_for_function("() => { const v = globalThis.visualizeSceneState; return v && (" + expression + "); }", timeout=45000)
        return state()

    def click(name):
        key = json.dumps(name)
        wait(f"v.controls[{key}] && v.controls[{key}].width > 0 && v.controls[{key}].height > 0")
        b = state()["controls"][name]
        page.mouse.click(b["x"] + b["width"] / 2, b["y"] + b["height"] / 2)
        page.wait_for_timeout(150)

    def choose(name, index):
        click(name)
        page.keyboard.press("Home")
        for _ in range(index):
            page.keyboard.press("ArrowDown")
        page.keyboard.press("Enter")
        page.wait_for_timeout(250)

    def node_point(node_id):
        s = state()
        n = next(n for n in s["model"]["Graph"]["Nodes"] if n["Id"] == node_id)
        b = n["Bounds"]
        return (s["canvas"]["x"] + (b["X"] + b["Width"] / 2 - s["origin"]["x"]) * s["zoom"],
                s["canvas"]["y"] + (b["Y"] + b["Height"] / 2 - s["origin"]["y"]) * s["zoom"])

    def select_terminals():
        click("ModeSelect")
        page.mouse.click(*node_point("start"))
        page.keyboard.down("Control")
        page.mouse.click(*node_point("end"))
        page.keyboard.up("Control")
        wait("v.selection.length === 2")

    def record(name, action):
        started = time.time()
        try:
            action()
            assert not state()["error"], state()["error"]
            results.append({"name": name, "passed": True, "seconds": time.time() - started})
            print("PASS", name, flush=True)
        except Exception as error:
            results.append({"name": name, "passed": False, "seconds": time.time() - started, "error": str(error)})
            raise

    def download(control, extension, menu=False):
        if menu:
            click("MenuFile")
        with page.expect_download(timeout=30000) as pending:
            click(control)
        path = output / ("export." + extension)
        pending.value.save_as(str(path))
        assert path.stat().st_size > 50
        return path

    try:
        page.goto(url, wait_until="domcontentloaded", timeout=120000)
        wait("v.ready && v.frames > 0")
        page.wait_for_timeout(400)
        page.screenshot(path=str(output / "workbench.png"), full_page=True)
        assert not state()["error"], state()["error"]
        click("TabLayout")
        for i, kind in enumerate(["Hierarchical", "Organic", "Tree", "Circular", "Radial", "Grid"]):
            def apply_layout(i=i, kind=kind):
                choose("LayoutKind", i)
                revision = state()["revision"]
                click("ApplyLayout")
                wait(f"v.revision > {revision} && v.status === {json.dumps(kind + ' layout complete.')}")
                assert len(state()["model"]["Graph"]["Nodes"]) == 10
                assert len(state()["model"]["Graph"]["Edges"]) == 10
            record("Execute " + kind + " layout through the layout panel", apply_layout)
        page.screenshot(path=str(output / "layout-panel.png"), full_page=True)

        def route():
            before = state()["model"]
            revision = state()["revision"]
            click("RouteEdges")
            wait(f"v.revision > {revision}")
            assert any(e.get("Bends") for e in state()["model"]["Graph"]["Edges"])
            click("Undo")
            wait(f"v.revision > {revision + 1}")
            assert state()["model"] == before
        record("Obstacle routing and exact undo", route)

        click("TabAnalysis")
        for control, expected in [("AnalyzeConnectedcomponents", "1 connected components"),
                                  ("AnalyzeStrongcomponents", "10 strongly connected components"),
                                  ("AnalyzePageRank", "PageRank"), ("AnalyzeBetweenness", "Betweenness")]:
            def analyze(control=control, expected=expected):
                click(control)
                wait("v.analysis.startsWith(" + json.dumps(expected) + ")")
            record("Analysis: " + expected, analyze)

        def spanning():
            click("AnalyzeMinimumspanningforest")
            wait("v.analysis.startsWith('9 edges') && v.selection.length === 9")
        record("Minimum spanning forest selects nine edges", spanning)

        def color():
            click("AnalyzeColorgraph")
            wait("v.analysis.startsWith('DSATUR used')")
            g = state()["model"]["Graph"]
            colors = {n["Id"]: n["Fill"] for n in g["Nodes"]}
            assert all(colors[e["SourceId"]] != colors[e["TargetId"]] for e in g["Edges"])
        record("Graph coloring paints adjacent nodes differently", color)

        def shortest():
            select_terminals()
            click("AnalyzeShortestpath")
            wait("v.analysis.startsWith('Cost:') && v.selection.length > 2")
        record("Selected-terminal shortest path", shortest)

        def flow():
            select_terminals()
            click("AnalyzeMaximumflow")
            wait("v.analysis.startsWith('Maximum flow: 1')")
        record("Selected-terminal maximum flow", flow)
        page.screenshot(path=str(output / "analysis-panel.png"), full_page=True)

        def json_export():
            path = download("Save", "json")
            assert json.loads(path.read_text(encoding="utf-8")) == state()["model"]
        record("Download native JSON and compare the complete model", json_export)

        def graphml_export():
            root = ET.parse(download("SaveGraphML", "graphml", True)).getroot()
            ns = {"g": "http://graphml.graphdrawing.org/xmlns"}
            assert len(root.findall(".//g:node", ns)) == 10
            assert len(root.findall(".//g:edge", ns)) == 10
        record("Download GraphML topology", graphml_export)

        def svg_export():
            root = ET.parse(download("ExportSvg", "svg", True)).getroot()
            assert root.tag == "{http://www.w3.org/2000/svg}svg"
            assert len(root.findall(".//{http://www.w3.org/2000/svg}text")) >= 10
        record("Download vector SVG with labels", svg_export)

        def png_export():
            data = download("ExportPng", "png", True).read_bytes()
            assert data[:8] == b"\x89PNG\r\n\x1a\n"
            assert len(data) > 5000
        record("Download rendered PNG", png_export)

        def open_roundtrip():
            with page.expect_file_chooser(timeout=30000) as chooser:
                click("Open")
            chooser.value.set_files(str(output / "export.json"))
            wait("v.status === 'Graph imported.'")
            assert state()["model"] == json.loads((output / "export.json").read_text(encoding="utf-8"))
        record("Open downloaded JSON through the browser file chooser", open_roundtrip)

        # Opening a file marks the document saved; each sample replacement also starts clean.
        for i, expected in enumerate([10, 17, 40, 14, 5, 1000]):
            def sample(i=i, expected=expected):
                frames = state()["frames"]
                choose("Samples", i)
                wait(f"v.model.Graph.Nodes.length === {expected}")
                click("Fit")
                wait(f"v.frames > {frames}")
                assert state()["canvas"]["width"] > 500
            record(f"Load and render sample {i + 1} ({expected} nodes)", sample)
        page.screenshot(path=str(output / "thousand-nodes.png"), full_page=True)
        assert not errors, errors
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
        except Exception:
            pass
        (output / "report.json").write_text(json.dumps({"url": url, "results": results, "errors": errors, "note": "Actual Uno WebAssembly input tests on Chromium/SwiftShader; not hardware GPU benchmarking."}, indent=2), encoding="utf-8")
        (output / "console.json").write_text(json.dumps(console, indent=2), encoding="utf-8")
        browser.close()

sys.exit(1 if errors or any(not r["passed"] for r in results) else 0)
