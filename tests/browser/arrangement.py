"""Arrangement toolbar and menu in the compiled Uno application. All graph changes use native input."""
import json
from pathlib import Path
import sys
import traceback
from playwright.sync_api import sync_playwright

url = sys.argv[1] if len(sys.argv) > 1 else 'http://127.0.0.1:8080/VisualizeScene/?test=1'
out = Path('artifacts/browser-workspace/arrangement'); out.mkdir(parents=True, exist_ok=True)
g = {'Nodes': [], 'Edges': [], 'Ports': [], 'Labels': []}
for ident, x, y, width, height, extra in [
    ('a', 0, 0, 80, 50, {}), ('b', 250, 130, 150, 90, {}), ('c', 580, 270, 110, 65, {}),
    ('locked', 780, 60, 70, 60, {'Locked': True}), ('group', 0, 450, 300, 200, {'IsGroup': True}),
    ('child', 60, 520, 80, 40, {'ParentId': 'group'})]:
    g['Nodes'].append({'Id': ident, 'Bounds': {'X': x, 'Y': y, 'Width': width, 'Height': height},
        'Text': ident, 'Shape': 'RoundedRectangle', 'Fill': '#FFCC66', 'Stroke': '#A87523', **extra})
g['Edges'] = [{'Id': 'e', 'SourceId': 'a', 'TargetId': 'b', 'Routing': 'Polyline', 'Bends': [{'X': 180, 'Y': 100}]}]
g['Ports'] = [{'Id': 'p', 'NodeId': 'b', 'RelativeLocation': {'X': 1, 'Y': .5}}]
g['Labels'] = [{'Id': 'l', 'OwnerId': 'b', 'Text': 'Annotation', 'Offset': {'X': 0, 'Y': 95}, 'FontSize': 12}]
fixture = out / 'fixture.json'; fixture.write_text(json.dumps({'Version': 1, 'Graph': g}))
empty = out / 'empty.json'; empty.write_text(json.dumps({'Version': 1, 'Graph': {key: [] for key in g}}))
results, errors = [], []
commands = ['AlignLeft', 'AlignHorizontalCenter', 'AlignRight', 'AlignTop', 'AlignVerticalCenter', 'AlignBottom',
            'DistributeHorizontal', 'DistributeVertical', 'SameWidth', 'SameHeight', 'SameSize']
with sync_playwright() as p:
    browser = p.chromium.launch(headless=True, args=['--enable-unsafe-swiftshader', '--use-angle=swiftshader', '--disable-dev-shm-usage'])
    page = browser.new_page(viewport={'width': 1440, 'height': 1050})
    page.add_init_script("Object.defineProperty(window,'showOpenFilePicker',{value:undefined,configurable:true});")
    page.on('pageerror', lambda e: errors.append(str(e)))
    def state(): return page.evaluate('globalThis.visualizeSceneState')
    def arrange(): return page.evaluate('globalThis.visualizeSceneArrange')
    def wait(expression):
        page.wait_for_function('() => {const v=globalThis.visualizeSceneState,w=globalThis.visualizeSceneWorkspace,a=globalThis.visualizeSceneArrange;return v&&w&&a&&(' + expression + ');}', timeout=45000)
    def click(name):
        key = json.dumps(name); wait(f'v.controls[{key}]?.width>0 && v.controls[{key}]?.height>0')
        b = state()['controls'][name]; x, y = b['x']+b['width']/2, b['y']+b['height']/2
        size = page.viewport_size
        assert 0 <= x < size['width'] and 0 <= y < size['height'], (name, b)
        page.mouse.click(x, y); page.wait_for_timeout(180)
    def point(ident):
        v = state(); b = next(n['Bounds'] for n in v['model']['Graph']['Nodes'] if n['Id'] == ident)
        return v['canvas']['x']+(b['X']+b['Width']/2-v['origin']['x'])*v['zoom'], v['canvas']['y']+(b['Y']+b['Height']/2-v['origin']['y'])*v['zoom']
    def select(*ids):
        click('ModeSelect'); page.mouse.click(*point(ids[0]))
        if len(ids) > 1:
            page.keyboard.down('Shift')
            for ident in ids[1:]: page.mouse.click(*point(ident))
            page.keyboard.up('Shift')
        wait('v.selection.length===' + str(len(ids)) + ' && ' + json.dumps(ids) + '.every(id=>v.selection.includes(id))')
    def nodes(): return {n['Id']: n for n in state()['model']['Graph']['Nodes']}
    def undo(expected):
        revision = state()['revision']; click('Undo'); wait(f'v.revision==={revision+1}')
        assert state()['model'] == expected
    def record(name):
        assert not state()['error'], state()['error']
        results.append({'name': name, 'passed': True}); print('PASS', name, flush=True)
    try:
        page.goto(url, wait_until='domcontentloaded', timeout=120000); wait('v.ready')
        with page.expect_file_chooser() as chooser: click('Open')
        chooser.value.set_files(str(fixture)); wait("v.status==='Graph imported.' && v.model.Graph.Nodes.length===6")
        baseline = state()['model']; original = nodes()
        assert all(not page.evaluate('globalThis.visualizeSceneWorkspace.enabled['+json.dumps('Arrange'+c)+']') for c in commands)
        select('a', 'b'); wait('w.enabled.ArrangeAlignLeft && !w.enabled.ArrangeDistributeHorizontal && w.enabled.ArrangeSameWidth')
        for command in commands:
            b = state()['controls']['Arrange'+command]
            assert b['width'] <= 28 and b['height'] <= 28
        page.screenshot(path=str(out/'compact-arrangement-toolbar.png'))
        record('Compact grouped arrangement buttons enable only applicable commands for the selection')
        for command in commands[:6]:
            select('a', 'b', 'c'); revision = state()['revision']; camera = (state()['origin'], state()['zoom'])
            click('Arrange'+command); wait(f'v.revision==={revision+1}')
            after = nodes(); bounds = [after[id]['Bounds'] for id in ['a', 'b', 'c']]
            axis = 'X' if command in commands[:3] else 'Y'; dim = 'Width' if axis == 'X' else 'Height'
            if command in ('AlignLeft', 'AlignTop'): values = [b[axis] for b in bounds]
            elif command in ('AlignRight', 'AlignBottom'): values = [b[axis]+b[dim] for b in bounds]
            else: values = [b[axis]+b[dim]/2 for b in bounds]
            assert max(values)-min(values) < 1e-8
            for ident in ['a', 'b', 'c']:
                assert after[ident]['Bounds']['Width'] == original[ident]['Bounds']['Width']
                assert after[ident]['Bounds']['Height'] == original[ident]['Bounds']['Height']
            for ident in ['locked', 'group', 'child']: assert after[ident] == original[ident]
            for collection in ['Edges', 'Ports', 'Labels']: assert state()['model']['Graph'][collection] == baseline['Graph'][collection]
            assert (state()['origin'], state()['zoom']) == camera and set(state()['selection']) == {'a', 'b', 'c'}
            undo(baseline); record(command + ' uses one undoable transaction without a camera or size change')
        for command, axis, dim in [('DistributeHorizontal', 'X', 'Width'), ('DistributeVertical', 'Y', 'Height')]:
            select('a', 'b', 'c'); revision = state()['revision']; click('Arrange'+command); wait(f'v.revision==={revision+1}')
            boxes = sorted([nodes()[id]['Bounds'] for id in ['a', 'b', 'c']], key=lambda b: b[axis])
            assert abs((boxes[1][axis]-boxes[0][axis]-boxes[0][dim])-(boxes[2][axis]-boxes[1][axis]-boxes[1][dim])) < 1e-8
            undo(baseline); record(command + ' equalizes gaps and preserves exact undo')
        for command in ['SameWidth', 'SameHeight', 'SameSize']:
            select('a', 'b', 'c', 'locked'); wait("a.reference==='a'"); revision = state()['revision']
            click('Arrange'+command); wait(f'v.revision==={revision+1}')
            for ident in ['b', 'c']:
                b = nodes()[ident]['Bounds']; old = original[ident]['Bounds']
                assert b['X'] == old['X'] and b['Y'] == old['Y']
                assert b['Width'] == (old['Width'] if command == 'SameHeight' else 80)
                assert b['Height'] == (old['Height'] if command == 'SameWidth' else 50)
            assert nodes()['locked'] == original['locked']
            unchanged_revision = state()['revision']; click('Arrange'+command); page.wait_for_timeout(250)
            assert state()['revision'] == unchanged_revision
            undo(baseline); record(command + ' matches the reference while preserving locked nodes and no-op history')
        select('locked', 'a', 'b'); wait("a.reference==='locked'"); revision = state()['revision']; click('ArrangeSameSize'); wait(f'v.revision==={revision+1}')
        assert all(nodes()[id]['Bounds']['Width'] == 70 and nodes()[id]['Bounds']['Height'] == 60 for id in ['a', 'b'])
        assert nodes()['locked'] == original['locked']; undo(baseline)
        record('A locked selected reference supplies dimensions without being moved or resized')
        select('a', 'b'); revision = state()['revision']; click('MenuArrange'); click('MenuArrangeSameHeight')
        wait(f'v.revision==={revision+1} && !v.controls.MenuArrangeSameHeight'); assert nodes()['b']['Bounds']['Height'] == 50
        sized = state()['model']; wait('v.graphFocused'); page.keyboard.press('ArrowRight'); wait(f'v.revision==={revision+2}')
        assert nodes()['a']['Bounds']['X'] == 1 and nodes()['b']['Bounds']['X'] == 251
        undo(sized); undo(baseline)
        record('The Arrange menu executes the same size command as the toolbar')
        record('Closing the Arrange menu restores graph keyboard input without an extra pointer click')
        select('a', 'b', 'c'); page.wait_for_timeout(250); builds = arrange()['builds']; click('ZoomIn'); click('ZoomOut'); page.wait_for_timeout(250)
        assert arrange()['builds'] == builds and state()['model'] == baseline
        record('Camera changes do not rebuild selection-aware arrangement availability')
        select('a', 'b'); page.set_viewport_size({'width': 640, 'height': 900}); wait('!w.leftVisible && !w.rightVisible')
        page.wait_for_timeout(250); revision = state()['revision']; click('ArrangeSameWidth'); wait(f'v.revision==={revision+1}')
        assert nodes()['b']['Bounds']['Width'] == 80; undo(baseline)
        page.screenshot(path=str(out/'narrow-arrangement-toolbar.png'))
        page.set_viewport_size({'width': 1440, 'height': 1050}); wait('w.leftVisible && w.rightVisible')
        record('Arrangement remains usable when responsive side panes are hidden')
        with page.expect_file_chooser() as chooser: click('Open')
        chooser.value.set_files(str(empty)); wait('v.model.Graph.Nodes.length===0 && a.reference===null && a.roots===0')
        assert all(not page.evaluate('globalThis.visualizeSceneWorkspace.enabled['+json.dumps('Arrange'+c)+']') for c in commands)
        record('Replacing the document clears stale reference and disables all selection commands')
        assert not errors, errors
    except Exception:
        traceback.print_exc(); errors.append(traceback.format_exc())
        try: page.screenshot(path=str(out/'failure.png'))
        except Exception: pass
    finally:
        try: (out/'state.json').write_text(json.dumps(state(), indent=2))
        except Exception: pass
        (out/'report.json').write_text(json.dumps({'results':results,'errors':errors,'note':'Compiled Uno, native pointer/keyboard input. SwiftShader software rendering; not physical GPU evidence.'},indent=2))
        browser.close()
if errors:
    sys.exit(1)
import subprocess
sys.exit(subprocess.run([sys.executable, str(Path(__file__).with_name("group_sizing.py")), url], check=False).returncode)
