"""Native Uno cache-menu input and observable rendering counters, not a JS renderer model."""
import json
from pathlib import Path
import sys
import traceback
from playwright.sync_api import sync_playwright
from native_menu import target

url = sys.argv[1] if len(sys.argv) > 1 else 'http://127.0.0.1:8080/VisualizeScene/?test=1'
out = Path('artifacts/browser-performance/rendering-cache'); out.mkdir(parents=True, exist_ok=True)
fixture = out/'fixture.json'
fixture.write_text(json.dumps({'Version':1,'Graph':{
    'Nodes':[{'Id':name,'Bounds':{'X':x,'Y':y,'Width':90,'Height':50},'Text':name.upper()}
             for name,x,y in [('a',0,0),('b',360,0),('c',0,180),('d',360,180)]],
    'Edges':[{'Id':'ab','SourceId':'a','TargetId':'b','Text':'A measured caption\nSecond line'},
             {'Id':'cd','SourceId':'c','TargetId':'d','Text':'Wrapped words with é and Ω'}],
    'Ports':[], 'Labels':[{'Id':'note','OwnerId':'a','Text':'Independent annotation','Offset':{'X':0,'Y':80},'FontSize':13}]
}}))
results, errors, trace = [], [], []
with sync_playwright() as p:
    browser = p.chromium.launch(headless=True,args=['--enable-unsafe-swiftshader','--use-angle=swiftshader','--disable-dev-shm-usage'])
    page = browser.new_page(viewport={'width':1440,'height':1050},accept_downloads=True)
    page.add_init_script("Object.defineProperty(window,'showOpenFilePicker',{value:undefined,configurable:true});")
    page.on('pageerror',lambda e:errors.append(str(e)))
    def state():return page.evaluate('globalThis.visualizeSceneState')
    def wait(expression):
        page.wait_for_function('()=>{const v=globalThis.visualizeSceneState,w=globalThis.visualizeSceneWorkspace;return v&&w&&('+expression+');}',timeout=45000)
    def click(name):
        wait('v.controls['+json.dumps(name)+']?.width>0')
        b=target(page,name,trace);x,y=b['x']+b['width']/2,b['y']+b['height']/2
        assert 0<=x<page.viewport_size['width'] and 0<=y<page.viewport_size['height'],(name,b)
        page.mouse.click(x,y);page.wait_for_timeout(180)
    def preset(mib):
        before=state()
        click('MenuView');click('RenderingCacheMenu');click('RenderingCache'+str(mib))
        wait('!v.controls.RenderingCacheMenu && v.performance.pictureByteBudget==='+str(mib*1024*1024))
        after=state()
        for name in ['model','revision','selection','origin','zoom']:
            assert after[name]==before[name],(name,after[name],before[name])
        assert after['performance']['cacheChoices']=={str(n):n==mib for n in [0,4,16,64]}
    def record(name):
        assert not state()['error'],state()['error'];results.append({'name':name,'passed':True});print('PASS',name,flush=True)
    try:
        page.goto(url,wait_until='domcontentloaded',timeout=120000);wait('v.ready')
        with page.expect_file_chooser() as chooser:click('Open')
        chooser.value.set_files(str(fixture));wait("v.status==='Graph imported.' && v.model.Graph.Nodes.length===4 && v.performance.retainedEdgeCaptions===2")
        baseline=state()['model'];wait('v.performance.pictureByteBudget===16777216 && v.performance.estimatedPictureBytes>0')
        click('ModeSelect');click('MenuSelection');click('SelectEdges');wait('v.selection.length===2')
        recordings=state()['performance']['edgeCaptionRecordings']
        click('ZoomIn');click('ZoomOut');wait('v.performance.retainedEdgeCaptions===2')
        assert state()['performance']['edgeCaptionRecordings']==recordings
        record('Default cache and camera/selection changes reuse the actual edge-caption recordings')
        preset(0);wait('v.performance.retainedPictures===0 && v.performance.estimatedPictureBytes===0 && v.performance.retainedEdgeCaptions===0')
        page.screenshot(path=str(out/'cache-disabled.png'))
        record('Native Off preset releases all retained pictures without editing the graph or camera')
        for mib in [4,16,64,64]:
            preset(mib);wait('v.performance.retainedEdgeCaptions===2 && v.performance.estimatedPictureBytes>0')
            assert state()['performance']['estimatedPictureBytes']<=mib*1024*1024
            assert state()['performance']['retainedPictures']<=state()['performance']['pictureCapacity']
        record('All native presets enforce both budgets and repeated selection stays mutually exclusive')
        rev=state()['revision'];click('PropertyClearText');click('PropertyApply')
        wait(f'v.revision==={rev+1} && v.performance.retainedEdgeCaptions===0')
        assert all(e['Text']=='' for e in state()['model']['Graph']['Edges'])
        click('Undo');wait(f'v.revision==={rev+2} && v.performance.retainedEdgeCaptions===2')
        assert state()['model']==baseline
        record('Native batch caption clearing prunes old pictures and one undo restores exact text and rendering')
        preset(4)
        with page.expect_download() as pending:click('Save')
        saved=out/'saved.json';pending.value.save_as(str(saved));assert json.loads(saved.read_text())==baseline
        with page.expect_file_chooser() as chooser:click('Open')
        chooser.value.set_files(str(saved));wait("v.status==='Graph imported.' && v.selection.length===0 && v.performance.retainedEdgeCaptions===2")
        assert state()['model']==baseline and state()['performance']['pictureByteBudget']==4*1024*1024
        record('Actual JSON save/import leaves document content unchanged and retains the session cache preference')
        page.set_viewport_size({'width':640,'height':900});wait('!w.leftVisible && !w.rightVisible');page.wait_for_timeout(250)
        preset(0);wait('v.performance.retainedPictures===0');preset(16)
        click('MenuView');click('RenderingCacheMenu');wait('v.controls.RenderingCache16?.width>0')
        page.screenshot(path=str(out/'narrow-native-menu.png'));page.keyboard.press('Escape');page.keyboard.press('Escape')
        assert state()['model']==baseline
        record('Compact nested native cache menu remains usable in the narrow workspace')
        assert not errors,errors
    except Exception:
        traceback.print_exc();errors.append(traceback.format_exc())
        try:page.screenshot(path=str(out/'failure.png'))
        except Exception:pass
    finally:
        try:(out/'state.json').write_text(json.dumps(state(),indent=2))
        except Exception:pass
        (out/'report.json').write_text(json.dumps({'results':results,'errors':errors,'inputTargets':trace,
            'note':'Compiled Uno native pointer/menu/file input and read-only cache counters; SwiftShader, not hardware GPU throughput.'},indent=2))
        browser.close()
sys.exit(1 if errors else 0)
