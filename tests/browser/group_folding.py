"""Atomic toolbar/context folding and explicit expand/collapse in compiled Uno. Native input only."""
import json
from pathlib import Path
import sys
import traceback
from playwright.sync_api import sync_playwright
from native_menu import target as menu_target
from native_context import target as context_target

url=sys.argv[1] if len(sys.argv)>1 else 'http://127.0.0.1:8080/VisualizeScene/?test=1'
out=Path('artifacts/browser-workspace/group-folding');out.mkdir(parents=True,exist_ok=True)
g={'Nodes':[],'Edges':[],'Ports':[],'Labels':[]}
for ident,box,extra in [
    ('g',(-100,-100,700,700),{'IsGroup':True}),('h',(0,0,400,350),{'IsGroup':True,'ParentId':'g'}),
    ('a',(100,120,100,60),{'ParentId':'h'}),('k',(800,0,350,330),{'IsGroup':True,'Collapsed':True,'Locked':True}),
    ('b',(860,100,100,60),{'ParentId':'k'}),('z',(600,600,100,60),{})]:
    g['Nodes'].append({'Id':ident,'Text':ident,'Bounds':dict(zip(['X','Y','Width','Height'],box)),
                       'Fill':'#FFCC66','Stroke':'#A87523','Shape':'RoundedRectangle',**extra})
g['Edges']=[{'Id':'e','SourceId':'a','TargetId':'z','Routing':'Polyline','Bends':[{'X':700,'Y':500}]}]
g['Ports']=[{'Id':'p','NodeId':'a','RelativeLocation':{'X':1,'Y':.5}}]
g['Labels']=[{'Id':'l','OwnerId':'a','Text':'Note','Offset':{'X':0,'Y':90},'FontSize':12}]
fixture=out/'fixture.json';fixture.write_text(json.dumps({'Version':1,'Graph':g}))
results,errors,targets=[],[],[]
with sync_playwright() as p:
    browser=p.chromium.launch(headless=True,args=['--enable-unsafe-swiftshader','--use-angle=swiftshader','--disable-dev-shm-usage'])
    page=browser.new_page(viewport={'width':1440,'height':1050})
    page.add_init_script("Object.defineProperty(window,'showOpenFilePicker',{value:undefined,configurable:true});")
    page.on('pageerror',lambda e:errors.append(str(e)))
    def state():return page.evaluate('globalThis.visualizeSceneState')
    def wait(expression):
        page.wait_for_function('() => {const v=globalThis.visualizeSceneState,w=globalThis.visualizeSceneWorkspace;return v&&w&&('+expression+');}',timeout=45000)
    def click(name):
        key=json.dumps(name);wait(f'v.controls[{key}]?.width>0 && v.controls[{key}]?.height>0')
        box=context_target(page,name,targets) if name.startswith('Context') else menu_target(page,name,targets)
        page.mouse.click(box['x']+box['width']/2,box['y']+box['height']/2);page.wait_for_timeout(180)
    def nodes():return {n['Id']:n for n in state()['model']['Graph']['Nodes']}
    def point(ident):
        v=state();n=nodes()[ident];b=n['Bounds'];x=b['X']+(40 if n.get('IsGroup') else b['Width']/2);y=b['Y']+(12 if n.get('IsGroup') else b['Height']/2)
        return v['canvas']['x']+(x-v['origin']['x'])*v['zoom'],v['canvas']['y']+(y-v['origin']['y'])*v['zoom']
    def groups():
        click('ModeSelect');click('MenuSelection');click('SelectGroups');wait('v.selection.length===3 && w.enabled.Fold')
    def undo(expected):
        revision=state()['revision'];click('Undo');wait(f'v.revision==={revision+1}');assert state()['model']==expected
    def flags(expected):
        for ident,value in expected.items():assert nodes()[ident]['Collapsed']==value,(ident,nodes()[ident])
    def unchanged_except_flags():
        for ident,node in nodes().items():
            old=original[ident];assert {**node,'Collapsed':old['Collapsed']}==old,ident
        for key in ['Edges','Ports','Labels']:assert state()['model']['Graph'][key]==baseline['Graph'][key],key
    def record(name):
        assert not state()['error'],state()['error'];results.append({'name':name,'passed':True});print('PASS',name,flush=True)
    try:
        page.goto(url,wait_until='domcontentloaded',timeout=120000);wait('v.ready')
        with page.expect_file_chooser() as chooser:click('Open')
        chooser.value.set_files(str(fixture));wait("v.status==='Graph imported.' && v.model.Graph.Nodes.length===6")
        baseline=state()['model'];original=nodes();groups();revision=state()['revision'];camera=(state()['origin'],state()['zoom'])
        wait('w.enabled.ExpandSelectedGroups && w.enabled.CollapseSelectedGroups')
        click('Fold');wait(f'v.revision==={revision+1}')
        flags({'g':True,'h':True,'k':False});unchanged_except_flags()
        assert set(state()['selection'])=={'g','k'} and (state()['origin'],state()['zoom'])==camera
        folded=state()['model'];page.screenshot(path=str(out/'mixed-groups-folded.png'))
        undo(baseline);click('Redo');wait(f'v.revision==={revision+3}');assert state()['model']==folded;undo(baseline)
        record('One Fold toolbar click toggles all selected nested groups with one exact undo/redo')
        groups();revision=state()['revision'];click('MenuArrange');click('CollapseSelectedGroups')
        wait(f'v.revision==={revision+1} && v.graphFocused');flags({'g':True,'h':True,'k':True});unchanged_except_flags()
        collapsed=state()['model'];wait('!w.enabled.CollapseSelectedGroups && w.enabled.ExpandSelectedGroups')
        record('Explicit Collapse selected groups is one transaction and disables when no selected group can collapse')
        revision=state()['revision'];click('MenuArrange');click('ExpandSelectedGroups')
        wait(f'v.revision==={revision+1} && v.graphFocused');flags({'g':False,'h':True,'k':False});unchanged_except_flags()
        undo(collapsed);undo(baseline)
        record('Expanding selected parents preserves a hidden unselected child folding state')
        groups();revision=state()['revision'];click('MenuArrange');click('ExpandSelectedGroups')
        wait(f'v.revision==={revision+1} && !w.enabled.ExpandSelectedGroups');flags({'g':False,'h':False,'k':False});unchanged_except_flags()
        undo(baseline)
        record('Explicit Expand changes only collapsed groups, including position-locked frames')
        click('ModeSelect');page.mouse.click(*point('k'));wait("v.selection.length===1 && v.selection[0]==='k'")
        page.mouse.click(*point('k'),button='right');wait('w.contextOpen && w.enabled.ContextFold')
        revision=state()['revision'];click('ContextFold');wait(f'v.revision==={revision+1} && !w.contextOpen')
        flags({'g':False,'h':False,'k':False});unchanged_except_flags();undo(baseline)
        record('The context-menu fold command shares atomic behavior without changing a position lock')
        click('ModeSelect');page.mouse.click(*point('a'));wait("v.selection.length===1 && v.selection[0]==='a'")
        wait('!w.enabled.Fold && !w.enabled.ExpandSelectedGroups && !w.enabled.CollapseSelectedGroups')
        assert state()['model']==baseline
        record('Leaf-only selection disables group folding controls')
        groups();page.wait_for_timeout(200);builds=page.evaluate('globalThis.visualizeSceneArrange.builds')
        click('ZoomIn');click('ZoomOut');page.wait_for_timeout(200)
        assert page.evaluate('globalThis.visualizeSceneArrange.builds')==builds
        page.set_viewport_size({'width':640,'height':900});wait('!w.leftVisible && !w.rightVisible')
        revision=state()['revision'];click('Fold');wait(f'v.revision==={revision+1}');flags({'g':True,'h':True,'k':False})
        page.screenshot(path=str(out/'narrow-group-folding.png'));undo(baseline)
        record('Folding availability stays cached on zoom and the toolbar works in a narrow workspace')
        assert not errors,errors
    except Exception:
        traceback.print_exc();errors.append(traceback.format_exc())
        try:page.screenshot(path=str(out/'failure.png'))
        except Exception:pass
    finally:
        try:(out/'state.json').write_text(json.dumps(state(),indent=2))
        except Exception:pass
        (out/'report.json').write_text(json.dumps({'results':results,'errors':errors,'targets':targets,'note':'Compiled Uno native input; SwiftShader, not physical GPU evidence.'},indent=2))
        browser.close()
sys.exit(1 if errors else 0)
