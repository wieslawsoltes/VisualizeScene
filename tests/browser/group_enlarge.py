"""Enlarge-only group commands on the compiled Uno application; mutations use native input."""
import json
from pathlib import Path
import sys
import traceback
from playwright.sync_api import sync_playwright
from native_context import target as context_target

url = sys.argv[1] if len(sys.argv)>1 else 'http://127.0.0.1:8080/VisualizeScene/?test=1'
out = Path('artifacts/browser-workspace/group-enlarge'); out.mkdir(parents=True, exist_ok=True)
g = {'Nodes': [], 'Edges': [], 'Ports': [], 'Labels': []}
for ident, box, extra in [
    ('g', (-100,-100,500,400), {'IsGroup': True}),
    ('h', (0,0,180,130), {'IsGroup': True, 'ParentId': 'g'}),
    ('a', (450,320,100,60), {'ParentId': 'h'}),
    ('empty', (700,0,100,60), {'IsGroup': True}),
    ('locked', (700,200,140,140), {'IsGroup': True, 'Locked': True}),
    ('lc', (750,230,80,60), {'ParentId': 'locked'})]:
    g['Nodes'].append({'Id': ident, 'Text': ident, 'Bounds': dict(zip(['X','Y','Width','Height'], box)),
                       'Fill': '#FFCC66', 'Stroke': '#A87523', 'Shape': 'RoundedRectangle', **extra})
fixture=out/'fixture.json'; fixture.write_text(json.dumps({'Version':1,'Graph':g}))
results,errors,context_targets=[],[],[]
with sync_playwright() as p:
    browser=p.chromium.launch(headless=True,args=['--enable-unsafe-swiftshader','--use-angle=swiftshader','--disable-dev-shm-usage'])
    page=browser.new_page(viewport={'width':1440,'height':1050})
    page.add_init_script("Object.defineProperty(window,'showOpenFilePicker',{value:undefined,configurable:true});")
    page.on('pageerror',lambda e:errors.append(str(e)))
    def state(): return page.evaluate('globalThis.visualizeSceneState')
    def wait(expression):
        page.wait_for_function('() => {const v=globalThis.visualizeSceneState,w=globalThis.visualizeSceneWorkspace;return v&&w&&('+expression+');}',timeout=45000)
    def click(name):
        key=json.dumps(name);wait(f'v.controls[{key}]?.width>0 && v.controls[{key}]?.height>0')
        b=context_target(page,name,context_targets) if name.startswith('Context') else state()['controls'][name]
        x,y=b['x']+b['width']/2,b['y']+b['height']/2
        assert 0<=x<page.viewport_size['width'] and 0<=y<page.viewport_size['height'],(name,b)
        page.mouse.click(x,y);page.wait_for_timeout(180)
    def nodes(): return {n['Id']:n for n in state()['model']['Graph']['Nodes']}
    def point(ident):
        v=state();n=nodes()[ident];b=n['Bounds']
        x=b['X']+(40 if n.get('IsGroup') else b['Width']/2);y=b['Y']+(12 if n.get('IsGroup') else b['Height']/2)
        return v['canvas']['x']+(x-v['origin']['x'])*v['zoom'],v['canvas']['y']+(y-v['origin']['y'])*v['zoom']
    def groups():
        click('ModeSelect');click('MenuSelection');click('SelectGroups')
        wait('v.selection.length===4 && w.enabled.ArrangeEnlargeGroups')
    def undo(expected):
        rev=state()['revision'];click('Undo');wait(f'v.revision==={rev+1}');assert state()['model']==expected
    def record(name):
        assert not state()['error'],state()['error'];results.append({'name':name,'passed':True});print('PASS',name,flush=True)
    try:
        page.goto(url,wait_until='domcontentloaded',timeout=120000);wait('v.ready')
        with page.expect_file_chooser() as chooser:click('Open')
        chooser.value.set_files(str(fixture));wait("v.status==='Graph imported.' && v.model.Graph.Nodes.length===6")
        original=nodes();baseline=state()['model'];groups()
        rev=state()['revision'];camera=(state()['origin'],state()['zoom']);selected=set(state()['selection'])
        click('ArrangeEnlargeGroups');wait(f'v.revision==={rev+1}')
        assert nodes()['h']['Bounds']=={'X':0,'Y':0,'Width':578,'Height':408}
        assert nodes()['g']['Bounds']=={'X':-100,'Y':-100,'Width':706,'Height':536}
        for ident in ['a','empty','locked','lc']:assert nodes()[ident]==original[ident]
        assert (state()['origin'],state()['zoom'])==camera and set(state()['selection'])==selected
        enlarged=state()['model'];page.screenshot(path=str(out/'enlarged-groups.png'))
        click('ArrangeEnlargeGroups');page.wait_for_timeout(250);assert state()['revision']==rev+1
        undo(baseline);click('Redo');wait(f'v.revision==={rev+3}');assert state()['model']==enlarged;undo(baseline)
        record('Enlarge-only toolbar preserves existing blank space, child data, camera and exact no-op undo/redo')
        groups();rev=state()['revision'];click('MenuArrange');click('MenuArrangeEnlargeGroups')
        wait(f'v.revision==={rev+1} && v.graphFocused');assert state()['model']==enlarged;undo(baseline)
        record('The Arrange menu uses identical enlarge-only geometry and restores keyboard focus')
        click('ModeSelect');page.mouse.click(*point('h'));wait("v.selection.length===1 && v.selection[0]==='h'")
        page.mouse.click(*point('h'),button='right');wait('w.contextOpen && w.enabled.ContextEnlargeGroups')
        rev=state()['revision'];click('ContextEnlargeGroups');wait(f'v.revision==={rev+1} && !w.contextOpen')
        assert nodes()['g']==original['g'] and nodes()['h']['Bounds']=={'X':0,'Y':0,'Width':578,'Height':408}
        undo(baseline)
        record('Context enlargement changes only the selected frame, not unselected ancestors')
        for ident in ['empty','locked','a']:
            click('ModeSelect');page.mouse.click(*point(ident));wait('v.selection.length===1 && v.selection[0]==='+json.dumps(ident))
            wait('!w.enabled.ArrangeEnlargeGroups');assert state()['model']==baseline
        record('Empty, locked and leaf nodes cannot enable enlargement')
        groups();page.set_viewport_size({'width':640,'height':900});wait('!w.leftVisible && !w.rightVisible');page.wait_for_timeout(250)
        rev=state()['revision'];click('ArrangeEnlargeGroups');wait(f'v.revision==={rev+1}');assert state()['model']==enlarged
        page.screenshot(path=str(out/'narrow-enlargement.png'));undo(baseline)
        record('The new compact enlargement command remains reachable in the narrow workspace')
        assert not errors,errors
    except Exception:
        traceback.print_exc();errors.append(traceback.format_exc())
        try:page.screenshot(path=str(out/'failure.png'))
        except Exception:pass
    finally:
        try:(out/'state.json').write_text(json.dumps(state(),indent=2))
        except Exception:pass
        (out/'report.json').write_text(json.dumps({'results':results,'errors':errors,'contextTargets':context_targets,'note':'Compiled Uno with native pointer, keyboard and file input; SwiftShader software rendering.'},indent=2))
        browser.close()
sys.exit(1 if errors else 0)
