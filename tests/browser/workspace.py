"""Classic workspace UI on compiled Uno. All mutations use real keyboard/pointer input."""
import json
from pathlib import Path
import sys
import traceback
import subprocess
from playwright.sync_api import sync_playwright
from native_zoom import choose as choose_zoom

url = sys.argv[1] if len(sys.argv) > 1 else 'http://127.0.0.1:8080/VisualizeScene/?test=1'
out = Path('artifacts/browser-workspace'); out.mkdir(parents=True, exist_ok=True)
results, errors = [], []
zoom_targets=[]
with sync_playwright() as p:
    browser = p.chromium.launch(headless=True, args=['--enable-unsafe-swiftshader', '--use-angle=swiftshader', '--disable-dev-shm-usage'])
    page = browser.new_page(viewport={'width':1440, 'height':1050})
    page.on('pageerror', lambda e: errors.append(str(e)))
    def state(): return page.evaluate('globalThis.visualizeSceneState')
    def ws(): return page.evaluate('globalThis.visualizeSceneWorkspace')
    def wait(expression):
        page.wait_for_function('() => {const v=globalThis.visualizeSceneState,w=globalThis.visualizeSceneWorkspace;return v&&w&&('+expression+');}', timeout=45000)
    def point(name):
        wait('v.controls['+json.dumps(name)+']?.width>0')
        b=state()['controls'][name]; return b['x']+b['width']/2, b['y']+b['height']/2
    def click(name): page.mouse.click(*point(name)); page.wait_for_timeout(180)
    def menu(name): click('MenuView'); click(name)
    def record(name):
        assert not state()['error'], state()['error']
        results.append({'name':name,'passed':True}); print('PASS',name,flush=True)
    try:
        page.goto(url, wait_until='domcontentloaded', timeout=120000); wait('v.ready && w.leftVisible && w.rightVisible')
        before=state()['model']; revision=state()['revision']
        assert not ws()['enabled']['Undo'] and not ws()['enabled']['Redo']
        assert state()['controls']['ModeNode']['width']<45
        page.screenshot(path=str(out/'classic-workspace.png'))
        record('Compact original-style toolbar and integrated resizable workspace load')
        x,y=point('ResizeLeftPane'); old=ws()['leftWidth']
        page.mouse.move(x,y);page.mouse.down();page.mouse.move(x+35,y,steps=7);page.mouse.up()
        wait(f'w.leftWidth>{old+20}')
        record('Left splitter changes pane width through pointer capture')
        click('ResizeRightPane'); old=ws()['rightWidth'];page.keyboard.press('ArrowLeft')
        wait(f'w.rightWidth>{old}')
        page.keyboard.press('Home');wait('w.rightWidth===280')
        record('Splitter supports keyboard resizing and reset')
        menu('ToggleLeftPane');wait('!w.leftVisible');menu('ToggleLeftPane');wait('w.leftVisible')
        menu('ToggleRightPane');wait('!w.rightVisible');menu('ToggleRightPane');wait('w.rightVisible')
        menu('ResetWorkspace');wait('w.leftWidth===210 && w.rightWidth===280')
        assert state()['model']==before and state()['revision']==revision
        record('Hide, show and reset panes preserve graph data and revision')
        click('LeftPaneCollapse');wait('w.leftCollapsed');click('LeftPaneCollapse');wait('!w.leftCollapsed')
        click('OverviewCollapse');wait('w.overviewCollapsed');click('OverviewCollapse');wait('!w.overviewCollapsed')
        record('Pane collapse and restore are real workspace operations')
        click('TabStructure');wait("w.leftTab==='Structure' && w.structure.rows===10")
        click('StructureItems');page.keyboard.press('Home');page.keyboard.press('ArrowDown')
        wait('w.structure.selected!==null && v.selection.includes(w.structure.selected)')
        assert state()['revision']==revision
        old=ws()['structure']['builds'];click('Fit');click('ZoomIn');click('ZoomOut')
        wait(f'w.structure.builds==={old}')
        record('Virtualized structure selection reveals graph items without rebuilding on camera changes')
        click('TabPalette');wait("w.leftTab==='Palette'")
        click('FlowchartCollapse');click('FlowchartCollapse');click('Template1');wait("v.mode==='CreateNode'")
        c=state()['canvas'];page.mouse.click(c['x']+c['width']*.85,c['y']+c['height']*.15)
        wait(f'v.revision==={revision+1} && v.model.Graph.Nodes.length===11 && w.enabled.Undo')
        added=state()['model']['Graph']['Nodes'][-1];assert added['Shape']=='Diamond' and added['Fill']=='#FFCC66'
        assert ws()['documentTitle'].endswith('*')
        with page.expect_download() as download: click('Save')
        download.value.save_as(str(out/'palette-created.json'))
        wait("!w.documentTitle.endsWith('*')")
        assert state()['revision']==revision+1
        record('Saving clears the document header marker without another graph edit')
        click('Undo');wait('v.model.Graph.Nodes.length===10 && w.enabled.Redo')
        assert state()['model']==before
        record('Categorized amber tile palette creates the selected shape with exact undo')
        click('ModeSelect');choose_zoom(page,3,zoom_targets);wait('Math.abs(v.zoom-1)<1e-9')
        record('Zoom combo applies an exact percentage and returns focus to graph')
        click('QuickFind');wait('v.search.open');click('SearchClose');wait('!v.search.open')
        record('Compact Find button opens the existing modeless search control')
        click('Fit');click('ModeSelect')
        page.keyboard.press('Control+Home');wait('v.selection.length===1')
        click('Cut');wait('v.model.Graph.Nodes.length===9');click('Undo');wait('v.model.Graph.Nodes.length===10')
        assert state()['model']==before
        record('Cut toolbar executes graph clipboard and deletion with exact undo')
        page.set_viewport_size({'width':640,'height':900});wait('!w.leftVisible&&!w.rightVisible&&v.canvas.width>600')
        page.screenshot(path=str(out/'narrow-workspace.png'))
        page.set_viewport_size({'width':1440,'height':1050});wait('w.leftVisible&&w.rightVisible')
        assert state()['model']==before
        record('Responsive layout restores side rails without changing graph content')
        import inspector
        inspector.run(page, state, ws, wait, click, record, out)
        assert not errors, errors
    except Exception as e:
        traceback.print_exc();errors.append(str(e))
        try: page.screenshot(path=str(out/'failure.png'))
        except Exception: pass
    finally:
        for name, fn in [('state',state),('workspace',ws)]:
            try:(out/(name+'.json')).write_text(json.dumps(fn(),indent=2))
            except Exception:pass
        (out/'report.json').write_text(json.dumps({'results':results,'errors':errors,'zoomTargets':zoom_targets,'note':'Compiled Uno app, real input; SwiftShader is software rendering.'},indent=2))
        browser.close()
if errors:
    sys.exit(1)
# Use a fresh browser/fixture for batch and context scenarios, sharing the same compiled site.
batch_result = subprocess.run([sys.executable, str(Path(__file__).with_name("batch.py")), url], check=False).returncode
if batch_result:
    sys.exit(batch_result)
order_result = subprocess.run([sys.executable, str(Path(__file__).with_name("order.py")), url], check=False).returncode
if order_result:
    sys.exit(order_result)
scroll_result = subprocess.run([sys.executable, str(Path(__file__).with_name("scrolling.py")), url], check=False).returncode
if scroll_result:
    sys.exit(scroll_result)
sys.exit(subprocess.run([sys.executable, str(Path(__file__).with_name("edge_styles.py")), url], check=False).returncode)
