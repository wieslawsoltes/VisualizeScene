"""Group-frame fitting in the actual compiled Uno editor. Input-only changes; diagnostics are read-only."""
import json
from pathlib import Path
import sys
import traceback
from playwright.sync_api import sync_playwright

url = sys.argv[1] if len(sys.argv)>1 else 'http://127.0.0.1:8080/VisualizeScene/?test=1'
out = Path('artifacts/browser-workspace/group-sizing'); out.mkdir(parents=True, exist_ok=True)
g = {'Nodes':[], 'Edges':[], 'Ports':[], 'Labels':[]}
for ident, box, extra in [
    ('g',(-100,-100,1000,850),{'IsGroup':True}),
    ('h',(0,0,600,500),{'ParentId':'g','IsGroup':True}),
    ('a',(100,100,100,60),{'ParentId':'h'}), ('b',(300,220,80,50),{'ParentId':'h'}),
    ('locked',(1050,0,300,400),{'IsGroup':True,'Locked':True}), ('lc',(1100,90,80,60),{'ParentId':'locked'}),
    ('folded',(1050,500,200,120),{'IsGroup':True,'Collapsed':True}), ('hidden',(5000,5000,100,60),{'ParentId':'folded'}),
    ('empty',(50,800,130,60),{'IsGroup':True})]:
    g['Nodes'].append({'Id':ident,'Text':ident,'Bounds':dict(zip(['X','Y','Width','Height'],box)),
                       'Fill':'#FFCC66','Stroke':'#A87523','Shape':'RoundedRectangle',**extra})
g['Edges']=[{'Id':'e','SourceId':'a','TargetId':'b','Routing':'Polyline','Bends':[{'X':650,'Y':350}]}]
g['Ports']=[{'Id':'p','NodeId':'b','RelativeLocation':{'X':1,'Y':.5}}]
g['Labels']=[{'Id':'la','OwnerId':'a','Text':'Node annotation','Offset':{'X':0,'Y':160},'FontSize':12},
             {'Id':'lp','OwnerId':'p','Text':'Port annotation','Offset':{'X':80,'Y':80},'FontSize':12}]
fixture=out/'fixture.json';fixture.write_text(json.dumps({'Version':1,'Graph':g}))
results,errors=[],[]
with sync_playwright() as p:
    browser=p.chromium.launch(headless=True,args=['--enable-unsafe-swiftshader','--use-angle=swiftshader','--disable-dev-shm-usage'])
    page=browser.new_page(viewport={'width':1440,'height':1050},accept_downloads=True)
    page.add_init_script("Object.defineProperty(window,'showOpenFilePicker',{value:undefined,configurable:true});")
    page.on('pageerror',lambda e:errors.append(str(e)))
    def state():return page.evaluate('globalThis.visualizeSceneState')
    def wait(expression):
        page.wait_for_function('() => {const v=globalThis.visualizeSceneState,w=globalThis.visualizeSceneWorkspace;return v&&w&&('+expression+');}',timeout=45000)
    def click(name):
        key=json.dumps(name);wait(f'v.controls[{key}]?.width>0 && v.controls[{key}]?.height>0')
        b=state()['controls'][name];x,y=b['x']+b['width']/2,b['y']+b['height']/2
        assert 0<=x<page.viewport_size['width'] and 0<=y<page.viewport_size['height'],(name,b)
        page.mouse.click(x,y);page.wait_for_timeout(180)
    def nodes():return {n['Id']:n for n in state()['model']['Graph']['Nodes']}
    def point(ident):
        v=state();n=nodes()[ident];b=n['Bounds'];x=b['X']+(40 if n.get('IsGroup') else b['Width']/2);y=b['Y']+(12 if n.get('IsGroup') else b['Height']/2)
        return v['canvas']['x']+(x-v['origin']['x'])*v['zoom'],v['canvas']['y']+(y-v['origin']['y'])*v['zoom']
    def select(ident):
        click('ModeSelect');page.mouse.click(*point(ident));wait('v.selection.length===1 && v.selection[0]==='+json.dumps(ident))
    def groups():click('ModeSelect');click('MenuSelection');click('SelectGroups');wait('v.selection.length===5 && w.enabled.ArrangeFitGroups')
    def undo(expected):
        revision=state()['revision'];click('Undo');wait(f'v.revision==={revision+1}');assert state()['model']==expected
    def fitted(boxes):
        left=min(b[0] for b in boxes);top=min(b[1] for b in boxes);right=max(b[0]+b[2] for b in boxes);bottom=max(b[1]+b[3] for b in boxes)
        return {'X':left-28,'Y':top-48,'Width':max(80,right-left+56),'Height':max(60,bottom-top+76)}
    def close_box(a,b):assert all(abs(a[k]-b[k])<1e-7 for k in ['X','Y','Width','Height']),(a,b)
    def record(name):
        assert not state()['error'],state()['error'];results.append({'name':name,'passed':True});print('PASS',name,flush=True)
    try:
        page.goto(url,wait_until='domcontentloaded',timeout=120000);wait('v.ready')
        with page.expect_file_chooser() as chooser:click('Open')
        chooser.value.set_files(str(fixture));wait("v.status==='Graph imported.' && v.model.Graph.Nodes.length===9")
        baseline=state()['model'];original=nodes();select('a');wait('!w.enabled.ArrangeFitGroups')
        groups();page.screenshot(path=str(out/'groups-before-fit.png'))
        record('The new compact group-fit button enables for expanded groups, not leaf nodes')
        label_bounds=state()['labelBounds'];boxes=[]
        for id in ['a','b']:
            b=original[id]['Bounds'];boxes.append(tuple(b[k] for k in ['X','Y','Width','Height']))
        for id in ['la','lp']:
            b=label_bounds[id];boxes.append(tuple(b[k] for k in ['x','y','width','height']))
        expected_inner=fitted(boxes);expected_outer=fitted([tuple(expected_inner[k] for k in ['X','Y','Width','Height'])])
        revision=state()['revision'];camera=(state()['origin'],state()['zoom']);selection=set(state()['selection'])
        click('ArrangeFitGroups');wait(f'v.revision==={revision+1}')
        close_box(nodes()['h']['Bounds'],expected_inner);close_box(nodes()['g']['Bounds'],expected_outer)
        for id in ['a','b','lc','locked','folded','hidden','empty']:assert nodes()[id]==original[id],id
        for key in ['Edges','Ports','Labels']:assert state()['model']['Graph'][key]==baseline['Graph'][key],key
        assert (state()['origin'],state()['zoom'])==camera and set(state()['selection'])==selection
        fitted_model=state()['model'];page.screenshot(path=str(out/'nested-groups-fitted.png'))
        record('Nested frames fit bottom-up around actual measured annotations without moving children or camera')
        click('ArrangeFitGroups');page.wait_for_timeout(250);assert state()['revision']==revision+1 and state()['model']==fitted_model
        undo(baseline);click('Redo');wait(f'v.revision==={revision+3}');assert state()['model']==fitted_model
        undo(baseline)
        record('Repeated fitting is a no-op and one undo/redo restores the entire nested operation exactly')
        groups();revision=state()['revision'];click('MenuArrange');click('MenuArrangeFitGroups');wait(f'v.revision==={revision+1} && v.graphFocused')
        assert state()['model']==fitted_model;undo(baseline)
        record('Arrange-menu group fitting uses the same operation and restores graph keyboard focus')
        select('h');page.mouse.click(*point('h'),button='right');wait('w.contextOpen && w.enabled.ContextFitGroups')
        revision=state()['revision'];click('ContextFitGroups');wait(f'v.revision==={revision+1} && !w.contextOpen')
        close_box(nodes()['h']['Bounds'],expected_inner);assert nodes()['g']==original['g']
        undo(baseline)
        record('The context command fits only its selected group and leaves an unselected ancestor untouched')
        for id in ['locked','folded','empty']:
            select(id);wait('!w.enabled.ArrangeFitGroups');page.mouse.click(*point(id),button='right');wait('w.contextOpen && !w.enabled.ContextFitGroups')
            page.keyboard.press('Escape');wait('!w.contextOpen');assert state()['model']==baseline
        record('Locked, collapsed and empty groups disable fitting in both toolbar and context menu')
        groups();page.wait_for_timeout(250);builds=page.evaluate('globalThis.visualizeSceneArrange.builds')
        click('ZoomIn');click('ZoomOut');page.wait_for_timeout(250)
        assert page.evaluate('globalThis.visualizeSceneArrange.builds')==builds
        record('Group-fit availability reuses its cached selection state across camera-only changes')
        page.set_viewport_size({'width':640,'height':900});wait('!w.leftVisible && !w.rightVisible');page.wait_for_timeout(250)
        revision=state()['revision'];click('ArrangeFitGroups');wait(f'v.revision==={revision+1}');assert state()['model']==fitted_model
        page.screenshot(path=str(out/'narrow-group-fitting.png'))
        with page.expect_download() as pending:click('Save')
        saved=out/'fitted.json';pending.value.save_as(str(saved));assert json.loads(saved.read_text())==fitted_model
        wait("!w.documentTitle.endsWith('*')")
        with page.expect_file_chooser() as chooser:click('Open')
        chooser.value.set_files(str(saved));wait("v.status==='Graph imported.' && v.selection.length===0 && !w.enabled.ArrangeFitGroups")
        assert state()['model']==fitted_model
        record('Narrow-window fitting and the real JSON save/import preserve every adjusted frame and dependent item')
        assert not errors,errors
    except Exception:
        traceback.print_exc();errors.append(traceback.format_exc())
        try:page.screenshot(path=str(out/'failure.png'))
        except Exception:pass
    finally:
        try:(out/'state.json').write_text(json.dumps(state(),indent=2))
        except Exception:pass
        (out/'report.json').write_text(json.dumps({'results':results,'errors':errors,'note':'Compiled Uno pointer, keyboard and file input. SwiftShader; no physical-GPU claim.'},indent=2))
        browser.close()
sys.exit(1 if errors else 0)
