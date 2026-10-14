"""Clipboard commands through the compiled Uno menu, toolbar, context menu and keyboard."""
import json
from pathlib import Path
import sys
import traceback
from playwright.sync_api import sync_playwright
from native_menu import target
from native_context import target as context_target

url = sys.argv[1] if len(sys.argv)>1 else 'http://127.0.0.1:8080/VisualizeScene/?test=1'
out=Path('artifacts/browser-clipboard');out.mkdir(parents=True,exist_ok=True)
fixture=out/'fixture.json'
fixture.write_text(json.dumps({'Version':1,'Graph':{
    'Nodes':[{'Id':'a','Text':'Alpha','Bounds':{'X':0,'Y':0,'Width':100,'Height':60}},
             {'Id':'b','Text':'Beta','Bounds':{'X':400,'Y':200,'Width':120,'Height':80}}],
    'Edges':[{'Id':'ab','SourceId':'a','TargetId':'b','SourcePortId':'pa','Text':'Link','Routing':'Polyline','Bends':[{'X':230,'Y':140}]}],
    'Ports':[{'Id':'pa','NodeId':'a','RelativeLocation':{'X':1,'Y':.5}}],
    'Labels':[{'Id':'la','OwnerId':'a','Text':'Annotation','Offset':{'X':2,'Y':90},'FontSize':13}]
}}))
results,errors,trace=[],[],[]
with sync_playwright() as p:
    browser=p.chromium.launch(headless=True,args=['--enable-unsafe-swiftshader','--use-angle=swiftshader','--disable-dev-shm-usage'])
    page=browser.new_page(viewport={'width':1440,'height':1050},accept_downloads=True)
    page.add_init_script("Object.defineProperty(window,'showOpenFilePicker',{value:undefined,configurable:true});")
    page.on('pageerror',lambda e:errors.append(str(e)))
    def state():return page.evaluate('globalThis.visualizeSceneState')
    def wait(expression):page.wait_for_function('()=>{const v=globalThis.visualizeSceneState,w=globalThis.visualizeSceneWorkspace;return v&&w&&('+expression+');}',timeout=45000)
    def click(name):
        wait('v.controls['+json.dumps(name)+']?.width>0')
        b=context_target(page,name,trace) if name.startswith('Context') else target(page,name,trace)
        x,y=b['x']+b['width']/2,b['y']+b['height']/2
        assert 0<=x<page.viewport_size['width'] and 0<=y<page.viewport_size['height'],(name,b)
        page.mouse.click(x,y);page.wait_for_timeout(180)
    def menu(name):
        click('MenuEdit');click(name);wait('!v.controls['+json.dumps(name)+'] && v.graphFocused')
    def load():
        with page.expect_file_chooser() as chooser:click('Open')
        chooser.value.set_files(str(fixture));wait("v.status==='Graph imported.' && v.model.Graph.Nodes.length===2 && v.selection.length===0")
        click('ModeSelect')
    def point(ident):
        v=state();b=next(n['Bounds'] for n in v['model']['Graph']['Nodes'] if n['Id']==ident)
        return (v['canvas']['x']+(b['X']+b['Width']/2-v['origin']['x'])*v['zoom'],v['canvas']['y']+(b['Y']+b['Height']/2-v['origin']['y'])*v['zoom'])
    def select(ident):
        click('ModeSelect');page.mouse.click(*point(ident));wait('v.selection.length===1 && v.selection[0]==='+json.dumps(ident))
    def undo(expected):
        revision=state()['revision'];click('Undo');wait('v.revision==='+str(revision+1));assert state()['model']==expected
    def last_node(text,x,y):
        node=state()['model']['Graph']['Nodes'][-1];assert node['Text']==text and node['Bounds']['X']==x and node['Bounds']['Y']==y,node
    def record(name):
        assert not state()['error'],state()['error'];results.append({'name':name,'passed':True});print('PASS',name,flush=True)
    try:
        page.goto(url,wait_until='domcontentloaded',timeout=120000);wait('v.ready');load();baseline=state()['model']
        wait('!w.enabled.Paste && !w.enabled.EditPaste && !w.enabled.EditPasteInPlace && !w.enabled.EditCut')
        select('a');before=state();menu('EditCopy');wait('w.enabled.Paste && w.enabled.EditPaste && w.enabled.EditPasteInPlace')
        assert state()['model']==baseline and state()['revision']==before['revision']
        record('Empty clipboard disables native Paste; native Copy immediately updates commands without a graph edit')
        click('Paste');wait('v.model.Graph.Nodes.length===3');last_node('Alpha',30,30);first=state()['model']
        copied=first['Graph'];assert copied['Ports'][-1]['NodeId']==copied['Nodes'][-1]['Id'] and copied['Labels'][-1]['OwnerId']==copied['Nodes'][-1]['Id']
        page.keyboard.press('Control+v');wait('v.model.Graph.Nodes.length===4');last_node('Alpha',60,60);second=state()['model']
        assert state()['zoom']==before['zoom'] and state()['origin']==before['origin']
        undo(first);click('Redo');wait('v.model.Graph.Nodes.length===4');assert state()['model']==second
        record('Toolbar then keyboard Paste use successive offsets, preserve dependents and support exact undo/redo')
        load();menu('EditPasteInPlace');wait('v.model.Graph.Nodes.length===3');last_node('Alpha',0,0);undo(baseline)
        page.keyboard.press('Control+Shift+v');wait('v.model.Graph.Nodes.length===3');last_node('Alpha',0,0);undo(baseline)
        page.keyboard.press('Control+v');wait('v.model.Graph.Nodes.length===3');last_node('Alpha',90,90)
        record('Native and keyboard Paste in place keep original coordinates without advancing the regular sequence')
        load();select('b');menu('EditDuplicate');wait('v.model.Graph.Nodes.length===3');last_node('Beta',430,230)
        page.keyboard.press('Control+v');wait('v.model.Graph.Nodes.length===4');last_node('Alpha',120,120)
        record('Native Duplicate leaves the prior clipboard and its next paste offset unchanged')
        load();select('a');menu('EditCut');wait('v.model.Graph.Nodes.length===1');cut=state()['model']['Graph']
        assert not cut['Edges'] and not cut['Ports'] and not cut['Labels'];undo(baseline)
        select('a');page.keyboard.press('Shift+Delete');wait('v.model.Graph.Nodes.length===1');undo(baseline)
        select('b');page.keyboard.press('Control+Insert');page.keyboard.press('Shift+Insert');wait('v.model.Graph.Nodes.length===3');last_node('Beta',430,230)
        record('Cut is one undoable operation and Ctrl+Insert, Shift+Delete and Shift+Insert use the same commands')
        load();select('b');page.mouse.click(*point('b'),button='right');wait('w.contextOpen');click('ContextCopy');wait('!w.contextOpen')
        page.mouse.click(*point('a'),button='right');wait('w.contextOpen && w.enabled.ContextPasteInPlace');page.screenshot(path=str(out/'native-context-clipboard.png'))
        click('ContextPasteInPlace');wait('!w.contextOpen && v.model.Graph.Nodes.length===3');last_node('Beta',400,200);undo(baseline)
        record('Native context Copy and Paste in place share the graph command path and exact undo')
        load();click('MenuSelection');click('SelectLabels');wait("v.selection.length===1 && v.selection[0]==='la' && v.graphFocused");menu('EditCopy');select('b')
        click('Paste');wait('v.model.Graph.Labels.length===2');page.keyboard.press('Control+v');wait('v.model.Graph.Labels.length===3')
        labels=state()['model']['Graph']['Labels'];assert [l['OwnerId'] for l in labels[-2:]]==['b','b']
        assert labels[-2]['Offset']=={'X':32,'Y':120} and labels[-1]['Offset']=={'X':62,'Y':150}
        menu('EditPasteInPlace');wait('v.model.Graph.Labels.length===4');label=state()['model']['Graph']['Labels'][-1]
        assert label['OwnerId']=='b' and label['Offset']=={'X':2,'Y':90} and state()['model']['Graph']['Nodes']==baseline['Graph']['Nodes']
        record('Repeated independent-label pastes stay on the chosen owner, including native Paste in place')
        load();select('a');menu('EditCopy');page.set_viewport_size({'width':640,'height':900});wait('!w.leftVisible && !w.rightVisible')
        click('MenuEdit');wait('v.controls.EditPasteInPlace?.width>0');page.screenshot(path=str(out/'narrow-native-edit-menu.png'))
        click('EditPasteInPlace');wait('v.model.Graph.Nodes.length===3 && v.graphFocused');last_node('Alpha',0,0)
        model=state()['model']
        with page.expect_download() as pending:click('Save')
        saved=out/'saved.json';pending.value.save_as(str(saved));assert json.loads(saved.read_text())==model
        record('Compact Edit menu remains usable at 640 pixels and native save preserves the complete pasted document')
        assert not errors,errors
    except Exception:
        traceback.print_exc();errors.append(traceback.format_exc())
        try:page.screenshot(path=str(out/'failure.png'))
        except Exception:pass
    finally:
        try:(out/'state.json').write_text(json.dumps(state(),indent=2))
        except Exception:pass
        (out/'report.json').write_text(json.dumps({'results':results,'errors':errors,'inputTargets':trace,'note':'Compiled Uno with real native input and read-only diagnostics. SwiftShader, not a physical-GPU or desktop-GUI certification.'},indent=2))
        browser.close()
sys.exit(1 if errors else 0)
