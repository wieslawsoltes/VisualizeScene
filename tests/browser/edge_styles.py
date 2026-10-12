"""Visual connection presets and appearance editing on compiled Uno; graph mutation uses input only."""
import json
from pathlib import Path
import sys
import traceback
import xml.etree.ElementTree as ET
from playwright.sync_api import sync_playwright

url = sys.argv[1] if len(sys.argv) > 1 else 'http://127.0.0.1:8080/VisualizeScene/?test=1'
out = Path('artifacts/browser-workspace/edge-styles'); out.mkdir(parents=True, exist_ok=True)
g = {'Nodes': [], 'Edges': [], 'Ports': [], 'Labels': []}
for ident, x, y in [('a', 0, 0), ('b', 450, 0), ('c', 0, 300), ('d', 450, 300)]:
    g['Nodes'].append({'Id': ident, 'Bounds': {'X': x, 'Y': y, 'Width': 100, 'Height': 60},
                      'Text': ident.upper(), 'Shape': 'RoundedRectangle', 'Fill': '#FFCC66', 'Stroke': '#A87523'})
fixture = out / 'fixture.json'; fixture.write_text(json.dumps({'Version': 1, 'Graph': g}))
results, errors = [], []
with sync_playwright() as p:
    browser = p.chromium.launch(headless=True, args=['--enable-unsafe-swiftshader', '--use-angle=swiftshader', '--disable-dev-shm-usage'])
    page = browser.new_page(viewport={'width': 1440, 'height': 1050}, accept_downloads=True)
    page.add_init_script("Object.defineProperty(window,'showOpenFilePicker',{value:undefined,configurable:true});")
    page.on('pageerror', lambda e: errors.append(str(e)))
    def state(): return page.evaluate('globalThis.visualizeSceneState')
    def wait(expression):
        page.wait_for_function('() => {const v=globalThis.visualizeSceneState,w=globalThis.visualizeSceneWorkspace;return v&&w&&('+expression+');}', timeout=45000)
    def click(name):
        key = json.dumps(name); wait(f'v.controls[{key}]?.width>0 && v.controls[{key}]?.height>0')
        b = state()['controls'][name]; x, y = b['x']+b['width']/2, b['y']+b['height']/2
        assert 0 <= x < 1440 and 0 <= y < 1050, (name, b)
        page.mouse.click(x, y); page.wait_for_timeout(200)
    def point(ident):
        v = state(); n = next(n for n in v['model']['Graph']['Nodes'] if n['Id'] == ident); b = n['Bounds']
        return v['canvas']['x']+(b['X']+b['Width']/2-v['origin']['x'])*v['zoom'], v['canvas']['y']+(b['Y']+b['Height']/2-v['origin']['y'])*v['zoom']
    def fill(name, text):
        click('Property'+name); wait('w.inspector.focused==='+json.dumps(name))
        page.keyboard.press('Control+a'); page.keyboard.insert_text(text)
        wait('w.inspector.text['+json.dumps(name)+']==='+json.dumps(text))
    def choose(name, value):
        click('Property'+name); page.keyboard.press('Home')
        for _ in range(value): page.keyboard.press('ArrowDown')
        page.keyboard.press('Enter'); page.wait_for_timeout(250)
    def connect(template, source='a', target='b'):
        revision = state()['revision']; count = len(state()['model']['Graph']['Edges'])
        click('EdgeTemplate'+str(template)); wait("v.mode==='CreateEdge'")
        page.mouse.click(*point(source)); page.mouse.click(*point(target))
        wait(f'v.revision==={revision+1} && v.model.Graph.Edges.length==={count+1}')
        return state()['model']['Graph']['Edges'][-1]
    def undo(expected):
        revision = state()['revision']; click('Undo'); wait(f'v.revision==={revision+1}')
        assert state()['model'] == expected
    def download(control, extension, menu=False):
        if menu: click('MenuFile')
        with page.expect_download() as pending: click(control)
        path = out / ('styled.'+extension); pending.value.save_as(str(path)); return path
    def record(name):
        assert not state()['error'], state()['error']; results.append({'name': name, 'passed': True}); print('PASS', name, flush=True)
    try:
        page.goto(url, wait_until='domcontentloaded', timeout=120000); wait('v.ready')
        with page.expect_file_chooser() as chooser: click('Open')
        chooser.value.set_files(str(fixture)); wait("v.status==='Graph imported.' && v.model.Graph.Nodes.length===4")
        baseline = state()['model']; page.screenshot(path=str(out/'connection-palette.png'))
        expected = [None, {'Width':2,'Dash':'Dash','SourceArrow':'None','TargetArrow':'Automatic'},
                    {'Width':1.5,'Dash':'Solid','SourceArrow':'None','TargetArrow':'None'},
                    {'Width':1.5,'Dash':'Solid','SourceArrow':'None','TargetArrow':'Open'},
                    {'Width':1.5,'Dash':'Solid','SourceArrow':'HollowDiamond','TargetArrow':'None'},
                    {'Width':2,'Dash':'Dot','SourceArrow':'Circle','TargetArrow':'HollowCircle'}]
        for i, style in enumerate(expected):
            edge = connect(i); assert edge.get('Style') == style, edge
            assert edge['Directed'] and edge['SourceId']=='a' and edge['TargetId']=='b'
            assert edge['Routing'] == ('Orthogonal' if i==0 else 'Polyline')
            undo(baseline)
            record(f'Visual connection preset {i+1} creates its actual stroke and markers with exact undo')
        edge = connect(1); click('ModeSelect'); wait('w.inspector.count===9')
        before = state()['model']; revision = state()['revision']
        fill('LineWidth', '4.25'); choose('LinePattern', 4); choose('SourceArrow', 6); choose('TargetArrow', 3)
        click('PropertyApply'); wait(f'v.revision==={revision+1}')
        current = state()['model']['Graph']['Edges'][0]
        assert current['Style'] == {'Width':4.25,'Dash':'DashDotDot','SourceArrow':'HollowDiamond','TargetArrow':'Open'}
        assert current['Directed'] and current['Weight']==edge['Weight'] and current['Bends']==edge['Bends']
        assert state()['model']['Graph']['Nodes'] == baseline['Graph']['Nodes']
        page.screenshot(path=str(out/'styled-edge-inspector.png'))
        record('Width, dash and both endpoint markers apply in one transaction without changing topology')
        styled = state()['model']; revision = state()['revision']
        fill('LineWidth', 'NaN'); fill('Label', 'must not commit'); click('PropertyApply')
        wait('w.inspector.message.includes("finite")')
        assert state()['model']==styled and state()['revision']==revision
        record('Invalid edge width rolls back text and every other staged field')
        # Selection change resets staged editors without writing invalid values.
        click('ModeSelect'); page.mouse.click(*point('a')); click('MenuSelection'); click('SelectEdges'); wait("v.selection.length===1 && v.selection[0]===v.model.Graph.Edges[0].Id")
        with page.expect_download() as pending: click('Save')
        json_file=out/'styled.json'; pending.value.save_as(str(json_file)); assert json.loads(json_file.read_text())==styled
        xml_file=download('SaveGraphML','graphml',True); svg_file=download('ExportSvg','svg',True)
        svg=ET.parse(svg_file).getroot(); ns={'s':'http://www.w3.org/2000/svg'}
        line=svg.find("s:polyline[@data-edge-id]",ns); assert line is not None
        assert line.attrib['stroke-width']=='4.25' and line.attrib['stroke-dasharray']=='17 8.5 4.25 8.5 4.25 8.5'
        markers=svg.findall("*[@data-edge-marker]"); assert {m.attrib['data-edge-marker'] for m in markers}=={'source','target'}
        with page.expect_file_chooser() as chooser: click('Open')
        chooser.value.set_files(str(xml_file)); wait("v.status==='Graph imported.'")
        assert state()['model']==styled
        record('Native JSON, GraphML and SVG retain explicit appearance and both endpoint decorations')
        connect(5,'c','d'); click('ModeSelect'); click('MenuSelection'); click('SelectEdges')
        wait('v.selection.length===2 && w.inspector.batchCount===2')
        batch_before=state()['model']; revision=state()['revision']; fill('LineWidth','6'); click('PropertyApply'); wait(f'v.revision==={revision+1}')
        after=state()['model']['Graph']['Edges']
        assert all(e['Style']['Width']==6 for e in after)
        for i in range(2):
            assert after[i]['Style']['Dash']==batch_before['Graph']['Edges'][i]['Style']['Dash']
            assert after[i]['Directed']==batch_before['Graph']['Edges'][i]['Directed']
        undo(batch_before)
        record('Mixed edge selection changes width only and preserves each distinct line pattern')
        page.screenshot(path=str(out/'mixed-edge-styles.png'))
        assert not errors, errors
    except Exception:
        traceback.print_exc(); errors.append(traceback.format_exc())
        try: page.screenshot(path=str(out/'failure.png'))
        except Exception: pass
    finally:
        try: (out/'state.json').write_text(json.dumps(state(),indent=2))
        except Exception: pass
        (out/'report.json').write_text(json.dumps({'results':results,'errors':errors,'note':'Compiled Uno, native pointer/keyboard/file input; SwiftShader software rendering.'},indent=2))
        browser.close()
sys.exit(1 if errors else 0)
