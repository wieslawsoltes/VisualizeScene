"""Mixed property editing and context menus in the compiled Uno application. Input-only mutation."""
import json
from pathlib import Path
import sys
import traceback
from playwright.sync_api import sync_playwright

url = sys.argv[1] if len(sys.argv) > 1 else 'http://127.0.0.1:8080/VisualizeScene/?test=1'
out = Path('artifacts/browser-workspace/batch'); out.mkdir(parents=True, exist_ok=True)
g = {'Nodes': [], 'Edges': [], 'Ports': [], 'Labels': []}
for ident, x, y, width, height, text, fill, extra in [
    ('a', 0, 0, 100, 60, 'Alpha', '#FFCC66', {}),
    ('b', 300, 200, 150, 80, 'Beta', '#DDEBFA', {}),
    ('c', 600, 40, 120, 80, 'Locked node', '#DDEFE3', {'Locked': True}),
    ('g', 0, 450, 300, 220, 'Group', '#E9E2F3', {'IsGroup': True}),
    ('h', 40, 520, 100, 60, 'Child', '#FFCC66', {'ParentId': 'g'})]:
    g['Nodes'].append({'Id': ident, 'Bounds': {'X': x, 'Y': y, 'Width': width, 'Height': height}, 'Text': text,
        'Shape': 'RoundedRectangle', 'Fill': fill, 'Stroke': '#4778A5', **extra})
g['Edges'] = [{'Id':'ab','SourceId':'a','TargetId':'b','Text':'Flow','Weight':7,'Bends':[], 'Stroke':'#607D99','Directed':True,'Routing':'Polyline'}]
g['Ports'] = [{'Id':'ap','NodeId':'a','RelativeLocation':{'X':1,'Y':.5}}]
g['Labels'] = [{'Id':i,'OwnerId':owner,'Text':text,'Offset':{'X':0,'Y':90},'FontSize':12} for i,owner,text in [('la','a','A note'),('lb','b','B note')]]
fixture = out/'fixture.json'; fixture.write_text(json.dumps({'Version':1,'Graph':g}))
results, errors = [], []
with sync_playwright() as p:
    browser = p.chromium.launch(headless=True, args=['--enable-unsafe-swiftshader','--use-angle=swiftshader','--disable-dev-shm-usage'])
    page = browser.new_page(viewport={'width':1440,'height':1050})
    page.add_init_script("Object.defineProperty(window,'showOpenFilePicker',{value:undefined,configurable:true});")
    page.on('pageerror', lambda e: errors.append(str(e)))
    def state(): return page.evaluate('globalThis.visualizeSceneState')
    def workspace(): return page.evaluate('globalThis.visualizeSceneWorkspace')
    def wait(expression):
        page.wait_for_function('() => {const v=globalThis.visualizeSceneState,w=globalThis.visualizeSceneWorkspace;return v&&w&&('+expression+');}', timeout=45000)
    def click(name):
        wait('v.controls['+json.dumps(name)+']?.width>0 && v.controls['+json.dumps(name)+']?.height>0')
        b=state()['controls'][name]; x,y=b['x']+b['width']/2,b['y']+b['height']/2
        assert 0<=x<1440 and 0<=y<1050,(name,b)
        page.mouse.click(x,y); page.wait_for_timeout(180)
    def point(ident):
        s=state(); b=next(n['Bounds'] for n in s['model']['Graph']['Nodes'] if n['Id']==ident)
        return s['canvas']['x']+(b['X']+b['Width']/2-s['origin']['x'])*s['zoom'],s['canvas']['y']+(b['Y']+b['Height']/2-s['origin']['y'])*s['zoom']
    def select(*ids):
        click('ModeSelect'); page.mouse.click(*point(ids[0]))
        if len(ids)>1:
            page.keyboard.down('Shift')
            for ident in ids[1:]: page.mouse.click(*point(ident))
            page.keyboard.up('Shift')
        wait('v.selection.length==='+str(len(ids))+' && '+json.dumps(list(ids))+'.every(id=>v.selection.includes(id))')
        wait('w.inspector.batchCount==='+str(len(ids) if len(ids)>1 else 0))
    def fill(name,text):
        click('Property'+name)
        wait('w.inspector.focused==='+json.dumps(name))
        page.keyboard.press('Control+a'); page.keyboard.insert_text(text)
        wait('w.inspector.text['+json.dumps(name)+']==='+json.dumps(text))
    def undo(baseline):
        revision=state()['revision']; click('Undo'); wait(f'v.revision==={revision+1}')
        assert state()['model']==baseline
    def record(name):
        assert not state()['error'],state()['error']; results.append({'name':name,'passed':True}); print('PASS',name,flush=True)
    try:
        page.goto(url,wait_until='domcontentloaded',timeout=120000); wait('v.ready')
        with page.expect_file_chooser() as chooser: click('Open')
        chooser.value.set_files(str(fixture)); wait("v.status==='Graph imported.' && v.model.Graph.Nodes.length===5")
        baseline=state()['model']; revision=state()['revision']
        select('a','b'); wait("w.inspector.text.Fill==='' && w.inspector.text.Label==='' && w.inspector.text.Width===''")
        assert workspace()['inspector']['batchExamined']==2
        page.screenshot(path=str(out/'mixed-properties.png'))
        click('PropertyApply'); page.wait_for_timeout(250)
        assert state()['model']==baseline and state()['revision']==revision
        record('Mixed selection shows distinct values without silently applying an initial placeholder')
        fill('Fill','#DDEFE3'); fill('Width','175.125'); click('PropertyApply'); wait(f'v.revision==={revision+1}')
        after=state()['model']['Graph']; assert after['Nodes'][0]['Fill']==after['Nodes'][1]['Fill']=='#DDEFE3'
        assert after['Nodes'][0]['Bounds']['Width']==after['Nodes'][1]['Bounds']['Width']==175.125
        assert after['Nodes'][0]['Text']=='Alpha' and after['Nodes'][1]['Text']=='Beta'
        for key in ['Edges','Ports','Labels']: assert after[key]==baseline['Graph'][key]
        page.screenshot(path=str(out/'batch-style-applied.png')); undo(baseline)
        record('One sparse batch updates both selected nodes and preserves every unrelated collection')
        revision=state()['revision']; fill('Fill','#000000'); click('PropertyReset'); click('PropertyApply')
        assert state()['model']==baseline and state()['revision']==revision
        record('Reset discards staged values without creating a history entry')
        fill('Fill','#000000'); fill('Width','NaN'); click('PropertyApply'); wait('w.inspector.message.includes("finite")')
        assert state()['model']==baseline and state()['revision']==revision
        click('PropertyReset')
        record('Invalid geometry rolls back the entire batch, including valid staged styles')
        click('PropertyClearText'); assert state()['revision']==revision
        click('PropertyApply'); wait(f'v.revision==={revision+1}')
        assert all(n['Text']=='' for n in state()['model']['Graph']['Nodes'][:2]); undo(baseline)
        record('Explicit clear-text staging applies empty text to a mixed selection with one undo')
        select('a','c'); revision=state()['revision']; fill('Fill','#000000'); fill('Width','250'); click('PropertyApply')
        wait('w.inspector.message.includes("locked position")')
        assert state()['model']==baseline and state()['revision']==revision
        click('PropertyReset')
        record('A locked selected node rejects a resize and prevents partial style updates')
        click('ModeSelect'); page.keyboard.press('Control+a'); wait('w.inspector.batchCount===9 && w.inspector.count===15')
        click('TabStructure'); wait("w.leftTab==='Structure'")
        builds=workspace()['structure']['builds']; revision=state()['revision']
        fill('FontSize','22'); click('PropertyApply'); wait(f'v.revision==={revision+1}')
        assert all(l['FontSize']==22 for l in state()['model']['Graph']['Labels'])
        for key in ['Nodes','Edges','Ports']: assert state()['model']['Graph'][key]==baseline['Graph'][key]
        assert workspace()['structure']['builds']==builds
        undo(baseline); click('TabPalette')
        record('Cross-kind edits touch applicable items only and label edits reuse the structure index')
        select('a'); page.mouse.click(*point('b'),button='right'); wait("w.contextOpen && v.selection.length===1 && v.selection[0]==='b'")
        page.screenshot(path=str(out/'targeted-context-menu.png'))
        click('ContextDelete'); wait('v.model.Graph.Nodes.length===4 && !w.contextOpen')
        assert any(n['Id']=='a' for n in state()['model']['Graph']['Nodes']) and not any(n['Id']=='b' for n in state()['model']['Graph']['Nodes'])
        undo(baseline)
        record('Right-click Delete targets the clicked node rather than a previously selected node')
        select('a','b'); page.mouse.click(*point('a'),button='right'); wait('w.contextOpen && v.selection.length===2')
        click('ContextDelete'); wait('v.model.Graph.Nodes.length===3'); undo(baseline)
        record('Right-clicking a selected item preserves and acts on the full multiselection')
        select('a','b'); page.keyboard.press('Shift+F10'); wait('w.contextOpen && v.selection.length===2')
        page.keyboard.press('Escape'); wait('!w.contextOpen'); assert state()['model']==baseline
        record('Keyboard context menu retains the selection and Escape closes it without an edit')
        c=state()['canvas']; page.mouse.click(c['x']+c['width']*.95,c['y']+c['height']*.95,button='right')
        wait('w.contextOpen && v.selection.length===0 && !w.enabled.ContextDelete && !w.enabled.ContextGroup')
        click('ContextCreateNode'); wait('v.model.Graph.Nodes.length===6'); undo(baseline)
        record('Blank-canvas context has disabled selection actions and undoable create-here')
        # Selecting an edge must not leave the last selected node highlighted in the outline.
        click('TabStructure'); click('ModeSelect'); page.keyboard.press('Control+a')
        # Native menu filtering is the same path used by users.
        click('MenuSelection'); click('SelectEdges'); wait('v.selection.length===1 && v.selection[0]==="ab"')
        wait('w.structure.selected===null'); assert state()['model']==baseline
        record('An edge-only selection clears a stale node highlight in the structure view')
        assert not errors,errors
    except Exception as error:
        traceback.print_exc();errors.append(str(error))
        try:page.screenshot(path=str(out/'failure.png'))
        except Exception:pass
    finally:
        for name,fn in [('state',state),('workspace',workspace)]:
            try:(out/(name+'.json')).write_text(json.dumps(fn(),indent=2))
            except Exception:pass
        (out/'report.json').write_text(json.dumps({'results':results,'errors':errors,'note':'Compiled Uno, real input and read-only diagnostics. SwiftShader software rendering.'},indent=2))
        browser.close()
sys.exit(1 if errors else 0)
