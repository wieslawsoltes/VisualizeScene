"""Drawing-order, fast command availability and retained-geometry checks on the compiled Uno app."""
import io
import json
from pathlib import Path
import sys
import traceback
from PIL import Image
from playwright.sync_api import sync_playwright

url = sys.argv[1] if len(sys.argv) > 1 else 'http://127.0.0.1:8080/VisualizeScene/?test=1'
out = Path('artifacts/browser-workspace/order'); out.mkdir(parents=True, exist_ok=True)
nodes = []
for ident, x, y, width, height, fill, extra in [
    ('red', 0, 0, 200, 130, '#F44336', {}), ('blue', 80, 35, 200, 130, '#4778A5', {}),
    ('green', 210, 85, 180, 120, '#5D9770', {}),
    ('childGroup', 0, 260, 240, 180, '#FFF1CC', {'IsGroup': True, 'ParentId': 'parent'}),
    ('parent', 0, 260, 280, 210, '#E9E2F3', {'IsGroup': True}),
    ('s', 340, 270, 60, 40, '#DDEBFA', {}), ('t', 600, 270, 60, 40, '#DDEFE3', {})]:
    nodes.append({'Id': ident, 'Bounds': {'X': x, 'Y': y, 'Width': width, 'Height': height}, 'Text': ident,
        'Shape': 'Rectangle', 'Fill': fill, 'Stroke': '#4778A5', **extra})
graph = {'Nodes': nodes, 'Edges': [
    {'Id': ident, 'SourceId': 's', 'TargetId': 't', 'Text': '', 'Weight': 1, 'Bends': [],
     'Stroke': color, 'Directed': False, 'Routing': 'Polyline'} for ident, color in [('e1', '#F44336'), ('e2', '#4778A5')]],
    'Ports': [{'Id': 'ps', 'NodeId': 's', 'RelativeLocation': {'X': .5, 'Y': 0}}],
    'Labels': [{'Id': ident, 'OwnerId': 's', 'Text': text, 'Offset': {'X': -30, 'Y': 120}, 'FontSize': 20}
        for ident, text in [('l1', 'UNDER'), ('l2', 'OVER')]]}
fixture = out / 'fixture.json'; fixture.write_text(json.dumps({'Version': 1, 'Graph': graph}))
results, errors = [], []
with sync_playwright() as p:
    browser = p.chromium.launch(headless=True, args=['--enable-unsafe-swiftshader', '--use-angle=swiftshader', '--disable-dev-shm-usage'])
    page = browser.new_page(viewport={'width': 1440, 'height': 1050}, device_scale_factor=1)
    page.add_init_script("Object.defineProperty(window,'showOpenFilePicker',{value:undefined,configurable:true});")
    page.on('pageerror', lambda error: errors.append(str(error)))
    # Bounded, read-only event evidence distinguishes a canvas click from a dismissing menu overlay.
    page.add_init_script("""globalThis.orderInputEvents=[];
        for(const type of ['pointerdown','pointerup','contextmenu']) addEventListener(type,e=>{
            const target=e.target, v=globalThis.visualizeSceneState;
            orderInputEvents.push({type,button:e.button,x:e.clientX,y:e.clientY,
                tag:target?.tagName,classes:String(target?.className??''),
                revision:v?.revision,focused:v?.graphFocused,context:globalThis.visualizeSceneWorkspace?.contextOpen});
            if(orderInputEvents.length>80)orderInputEvents.shift();
        },true);""")
    def state(): return page.evaluate('globalThis.visualizeSceneState')
    def ws(): return page.evaluate('globalThis.visualizeSceneWorkspace')
    def wait(expression):
        page.wait_for_function('() => {const v=globalThis.visualizeSceneState,w=globalThis.visualizeSceneWorkspace;return v&&w&&('+expression+');}', timeout=45000)
    def control(name):
        wait('v.controls['+json.dumps(name)+']?.width>0')
        b = state()['controls'][name]; return b['x'] + b['width']/2, b['y'] + b['height']/2
    def click(name): page.mouse.click(*control(name)); page.wait_for_timeout(150)
    def screen(x, y):
        v = state(); return v['canvas']['x'] + (x-v['origin']['x'])*v['zoom'], v['canvas']['y'] + (y-v['origin']['y'])*v['zoom']
    def pick(x, y, ident):
        page.mouse.click(*screen(x, y)); wait('v.selection.length===1 && v.selection[0]==='+json.dumps(ident))
    def menu(operation):
        name = 'MenuOrder'+operation
        click('MenuArrange'); click(name)
        # A command can publish the new graph before its native flyout finishes dismissing.
        # Wait for the actual menu item to unload, not an arbitrary extra sleep or a repeated click.
        wait('!v.controls['+json.dumps(name)+']')
    def undo(expected):
        revision = state()['revision']; click('Undo'); wait(f'v.revision==={revision+1}')
        assert state()['model'] == expected
    def record(name):
        assert not state()['error'], state()['error']; results.append({'name': name, 'passed': True}); print('PASS', name, flush=True)
    def pixel(x, y, color):
        page.wait_for_timeout(150)
        image = Image.open(io.BytesIO(page.screenshot())).convert('RGB'); xy = tuple(round(v) for v in screen(x,y))
        actual = image.getpixel(xy); expected = tuple(int(color[i:i+2],16) for i in (1,3,5))
        assert max(abs(a-b) for a,b in zip(actual,expected)) <= 8, (xy, actual, expected)
    try:
        page.goto(url, wait_until='domcontentloaded', timeout=120000); wait('v.ready')
        with page.expect_file_chooser() as chooser: click('Open')
        chooser.value.set_files(str(fixture)); wait("v.status==='Graph imported.' && v.model.Graph.Nodes.length===7")
        baseline = state()['model']; revision = state()['revision']
        wait('w.overview.bounds.x>=v.canvas.x+v.canvas.width')
        assert not ws()['enabled']['OrderBringToFront']
        page.screenshot(path=str(out/'reference-style-workspace.png'))
        record('Drawing-order icons and right-side overview load in the compact editor shell')
        pick(125,105,'blue'); pixel(125,105,'#4778A5')
        pick(30,60,'red'); before = state()['performance']
        click('OrderBringToFront'); wait(f'v.revision==={revision+1} && v.model.Graph.Nodes.at(-1).Id==="red"')
        pick(125,105,'red'); pixel(125,105,'#F44336')
        for key in ('nodeIndexBuilds','edgeIndexBuilds','annotationIndexBuilds','createdPaths'):
            assert state()['performance'][key] == before[key], key
        page.screenshot(path=str(out/'brought-to-front.png'))
        undo(baseline)
        record('Bring-to-front changes both overlap pixels and picking without rebuilding geometry')
        pick(30,60,'red'); revision = state()['revision']; menu('Raise')
        wait(f'v.revision==={revision+1} && v.model.Graph.Nodes[1].Id==="red"')
        menu('Lower'); wait(f'v.revision==={revision+2}'); assert state()['model'] == baseline
        record('Arrange menu raises and lowers one layer while preserving every item record')
        menu('BringToFront'); wait('v.model.Graph.Nodes.at(-1).Id==="red"')
        page.mouse.click(*screen(30,60), button='right'); wait('w.contextOpen')
        page.screenshot(path=str(out/'drawing-order-context.png'))
        click('ContextOrderSendToBack'); wait('v.model.Graph.Nodes[0].Id==="red" && !w.contextOpen')
        assert state()['model'] == baseline
        record('Context-menu send-to-back targets the selected item and restores its layer position')
        pick(500,290,'e2'); revision = state()['revision']; before = state()['performance']; menu('SendToBack')
        wait(f'v.revision==={revision+1} && v.model.Graph.Edges[0].Id==="e2"')
        pick(500,290,'e1')
        for key in ('edgeIndexBuilds','annotationIndexBuilds','createdPaths'): assert state()['performance'][key] == before[key], key
        undo(baseline)
        record('Overlapping edges change drawing and hit order while native paths remain cached')
        pick(340,410,'l2'); revision = state()['revision']; menu('SendToBack')
        wait(f'v.revision==={revision+1} && v.model.Graph.Labels[0].Id==="l2"'); pick(340,410,'l1'); undo(baseline)
        record('Independent labels reorder within their annotation layer and undo exactly')
        pick(50,275,'childGroup'); revision = state()['revision']; click('OrderBringToFront')
        assert state()['revision'] == revision and state()['model'] == baseline
        record('Nested group picking honors hierarchy depth and cannot reorder above a different depth')
        pick(370,270,'ps'); wait('!w.enabled.OrderBringToFront')
        assert state()['model'] == baseline
        record('Fixed-priority port handles do not expose a misleading drawing-order command')
        pick(30,60,'red'); revision = state()['revision']; before = state()['performance']
        click('PropertyFill'); page.keyboard.press('Control+a'); page.keyboard.insert_text('#FFCC66'); click('PropertyApply')
        wait(f'v.revision==={revision+1}')
        for key in ('nodeIndexBuilds','edgeIndexBuilds','annotationIndexBuilds','createdPaths'): assert state()['performance'][key] == before[key], key
        undo(baseline)
        record('Actual property recoloring reuses node, edge and annotation geometry')
        pick(30,60,'red'); click('OrderBringToFront'); wait('v.model.Graph.Nodes.at(-1).Id==="red"'); ordered = state()['model']
        click('MenuFile')
        with page.expect_download() as download: click('SaveGraphML')
        saved = out / 'drawing-order.graphml'; download.value.save_as(str(saved)); wait('!w.documentTitle.endsWith("*")')
        with page.expect_file_chooser() as chooser: click('Open')
        chooser.value.set_files(str(saved)); wait("v.status==='Graph imported.'")
        assert state()['model'] == ordered
        record('GraphML export and real file import preserve drawing order across nested groups and ports')
        # Reproduce the old selection -> Cut ordering without sleeping or waiting for a render after selection.
        cut = control('Cut'); restore = state()['model']
        for iteration in range(12):
            click('ModeSelect'); page.mouse.click(*screen(350,160)); wait('v.selection.length===1 && v.selection[0]==="green"')
            revision = state()['revision']; page.mouse.click(*cut)
            wait(f'v.revision==={revision+1} && v.model.Graph.Nodes.length===6')
            undo(restore)
        record('Twelve immediate selection-to-Cut cycles execute exactly once and undo without waiting for paint')
        page.set_viewport_size({'width':700,'height':900}); wait('!w.leftVisible && !w.rightVisible && v.canvas.width>650')
        x,y = control('OrderBringToFront'); assert 0<x<700 and 0<y<900
        page.screenshot(path=str(out/'narrow-order-toolbar.png'))
        record('Drawing-order commands remain reachable when narrow windows hide the side panes')
        assert not errors, errors
    except Exception as error:
        traceback.print_exc(); errors.append(str(error))
        try: page.screenshot(path=str(out/'failure.png'))
        except Exception: pass
    finally:
        for name, fn in [('state',state),('workspace',ws),('input-events',lambda: page.evaluate('globalThis.orderInputEvents'))]:
            try: (out/(name+'.json')).write_text(json.dumps(fn(),indent=2))
            except Exception: pass
        (out/'report.json').write_text(json.dumps({'results':results,'errors':errors,'note':'Compiled Uno, actual input and screenshot pixels. SwiftShader is software rendering.'},indent=2))
        browser.close()
sys.exit(1 if errors else 0)
