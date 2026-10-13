"""Caption fitting through actual Uno pointer/menu/file input. All diagnostics are read-only."""
import copy
import json
import math
from pathlib import Path
import sys
import traceback
from playwright.sync_api import sync_playwright
from native_menu import target

url = sys.argv[1] if len(sys.argv) > 1 else 'http://127.0.0.1:8080/VisualizeScene/?test=1'
out = Path('artifacts/browser-workspace/caption-sizing'); out.mkdir(parents=True, exist_ok=True)
shapes = ['Rectangle', 'RoundedRectangle', 'Ellipse', 'Diamond', 'Hexagon', 'Parallelogram']
texts = ['Order processing', 'Order processing', 'Multiple lines\nSecond line', 'Multiple lines\nSecond line', 'Καλημέρα é', 'Emoji 😀 caption']
g = {'Nodes': [], 'Edges': [], 'Ports': [], 'Labels': []}
for i, (shape, text) in enumerate(zip(shapes, texts)):
    g['Nodes'].append({'Id':f'n{i}','Bounds':{'X':(i%2)*420,'Y':(i//2)*220,'Width':40,'Height':24},
                       'Text':text,'Shape':shape,'Fill':'#FFCC66','Stroke':'#A87523'})
    # Independent same-font annotations provide a separate measurement reference.
    g['Labels'].append({'Id':f'l{i}','OwnerId':f'n{i}','Text':text,'Offset':{'X':0,'Y':130},'FontSize':13})
g['Nodes'] += [{'Id':'locked','Bounds':{'X':900,'Y':0,'Width':40,'Height':24},'Text':'Locked caption','Locked':True},
               {'Id':'group','Bounds':{'X':900,'Y':260,'Width':180,'Height':100},'Text':'Folder','IsGroup':True,'Collapsed':True},
               {'Id':'hidden','Bounds':{'X':5000,'Y':5000,'Width':40,'Height':24},'Text':'Hidden caption','ParentId':'group'}]
g['Edges'] = [{'Id':'e','SourceId':'n0','TargetId':'n1','Routing':'Polyline','Bends':[{'X':220,'Y':60}]}]
g['Ports'] = [{'Id':'p','NodeId':'n0','RelativeLocation':{'X':1,'Y':.5}}]
fixture = out/'fixture.json'; fixture.write_text(json.dumps({'Version':1,'Graph':g}))
results, errors, trace = [], [], []
with sync_playwright() as p:
    browser = p.chromium.launch(headless=True,args=['--enable-unsafe-swiftshader','--use-angle=swiftshader','--disable-dev-shm-usage'])
    page = browser.new_page(viewport={'width':1440,'height':1050},accept_downloads=True)
    page.add_init_script("Object.defineProperty(window,'showOpenFilePicker',{value:undefined,configurable:true});")
    page.on('pageerror',lambda e:errors.append(str(e)))
    def state(): return page.evaluate('globalThis.visualizeSceneState')
    def stats(): return page.evaluate('globalThis.visualizeSceneCaptionSizing')
    def wait(expression):
        page.wait_for_function('() => {const v=globalThis.visualizeSceneState,w=globalThis.visualizeSceneWorkspace,c=globalThis.visualizeSceneCaptionSizing;return v&&w&&c&&('+expression+');}',timeout=45000)
    def click(name):
        wait('v.controls['+json.dumps(name)+']?.width>0')
        b=target(page,name,trace); x,y=b['x']+b['width']/2,b['y']+b['height']/2
        assert 0<=x<page.viewport_size['width'] and 0<=y<page.viewport_size['height'],(name,b)
        page.mouse.click(x,y); page.wait_for_timeout(180)
    def select_nodes():
        click('ModeSelect');click('MenuSelection');click('SelectNodes');wait('v.selection.length===7 && w.enabled.ArrangeFitNodeText')
    def undo(expected):
        rev=state()['revision']; click('Undo');wait(f'v.revision==={rev+1}'); assert state()['model']==expected
    def record(name):
        assert not state()['error'],state()['error'];results.append({'name':name,'passed':True});print('PASS',name,flush=True)
    try:
        page.goto(url,wait_until='domcontentloaded',timeout=120000);wait('v.ready')
        with page.expect_file_chooser() as chooser:click('Open')
        chooser.value.set_files(str(fixture));wait("v.status==='Graph imported.' && v.model.Graph.Nodes.length===9")
        baseline=state()['model'];wait('!w.enabled.ArrangeFitNodeText');select_nodes()
        boxes=state()['labelBounds'];expected=copy.deepcopy(baseline)
        for i, shape in enumerate(shapes):
            label=boxes[f'l{i}'];width=label['width']-6
            height=max((texts[i].count('\n')+1)*16.25,label['height']-4)+2
            w,h=width+16,height+12
            sw,sh=(w,h)
            if shape=='RoundedRectangle':sw,sh=w+12,h+12
            elif shape=='Ellipse':sw,sh=w*math.sqrt(2),h*math.sqrt(2)
            elif shape=='Diamond':sw,sh=w*2,h*2
            elif shape=='Hexagon':sw=w*2
            elif shape=='Parallelogram':sw=w/.6
            b=expected['Graph']['Nodes'][i]['Bounds'];b['Width']=max(30,math.ceil(max(sw,w/(.6 if shape=='Diamond' else .82))))
            b['Height']=max(24,math.ceil(max(sh,height+10)))
        camera=(state()['origin'],state()['zoom']);rev=state()['revision'];selection=state()['selection'];measured=stats()['measurements']
        page.screenshot(path=str(out/'before-fitting.png'))
        click('ArrangeFitNodeText');wait(f'v.revision==={rev+1} && c.resized===6')
        assert state()['model']==expected
        assert (state()['origin'],state()['zoom'])==camera and state()['selection']==selection
        assert stats()['measurements']-measured==len(set(texts))
        page.screenshot(path=str(out/'shape-aware-captions.png'))
        record('All six shapes fit measured same-font text in one transaction without changing positions or other data')
        record('Locked nodes, hidden children and group frames remain unchanged')
        record('Equal captions share native measurement across different node shapes')
        count=stats()['measurements'];click('ArrangeFitNodeText');wait('c.resized===0 && c.examined===0')
        assert state()['revision']==rev+1 and stats()['measurements']==count
        undo(baseline);click('Redo');wait(f'v.revision==={rev+3}');assert state()['model']==expected;undo(baseline)
        record('Reapplying performs no reshaping or history edit; undo and redo restore the whole graph exactly')
        select_nodes();rev=state()['revision'];click('MenuArrange');click('MenuArrangeFitNodeText')
        wait(f'v.revision==={rev+1} && v.graphFocused');assert state()['model']==expected;undo(baseline)
        record('Arrange menu and compact toolbar use identical sizing and restore keyboard focus')
        select_nodes();page.wait_for_timeout(250);builds=stats()['measurements'];click('ZoomIn');click('ZoomOut')
        assert stats()['measurements']==builds and state()['model']==baseline
        click('MenuSelection');click('SelectGroups');wait('v.selection.length===1 && !w.enabled.ArrangeFitNodeText')
        click('MenuSelection');click('SelectEdges');wait('v.selection.length===1 && !w.enabled.ArrangeFitNodeText')
        record('Camera changes do not reshape captions and non-node selections disable the command')
        select_nodes();page.set_viewport_size({'width':640,'height':900});wait('!w.leftVisible && !w.rightVisible');page.wait_for_timeout(250)
        rev=state()['revision'];click('ArrangeFitNodeText');wait(f'v.revision==={rev+1}');assert state()['model']==expected
        page.screenshot(path=str(out/'narrow-fitting.png'))
        with page.expect_download() as pending:click('Save')
        saved=out/'fitted.json';pending.value.save_as(str(saved));assert json.loads(saved.read_text())==expected
        wait("!w.documentTitle.endsWith('*')")
        with page.expect_file_chooser() as chooser:click('Open')
        chooser.value.set_files(str(saved));wait("v.status==='Graph imported.' && v.selection.length===0 && !w.enabled.ArrangeFitNodeText")
        assert state()['model']==expected
        record('Narrow-workspace sizing and actual JSON save/import preserve fitted geometry and dependent records')
        assert not errors,errors
    except Exception:
        traceback.print_exc();errors.append(traceback.format_exc())
        try:page.screenshot(path=str(out/'failure.png'))
        except Exception:pass
    finally:
        try:(out/'state.json').write_text(json.dumps(state(),indent=2))
        except Exception:pass
        (out/'report.json').write_text(json.dumps({'results':results,'errors':errors,'inputTargets':trace,'note':'Compiled Uno native input; independent same-font label bounds; SwiftShader, not physical GPU throughput.'},indent=2))
        browser.close()
sys.exit(1 if errors else 0)
