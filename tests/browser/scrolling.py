"""Native Uno scrollbars: actual input at two pixel densities; diagnostics are read-only."""
import json
from pathlib import Path
import subprocess
import sys
import traceback
from playwright.sync_api import sync_playwright

url = sys.argv[1] if len(sys.argv)>1 else 'http://127.0.0.1:8080/VisualizeScene/?test=1'
density = int(sys.argv[2]) if len(sys.argv)>2 else 1
assert density in (1,2)
out=Path('artifacts/browser-workspace')/('scrolling' if density==1 else 'scrolling-dpr2');out.mkdir(parents=True,exist_ok=True)
g={'Nodes':[{'Id':f'n{i}','Bounds':{'X':i%40*300-4000,'Y':i//40*300-3000,'Width':100,'Height':60},
    'Text':f'Node {i}','Shape':'RoundedRectangle','Fill':'#DDEBFA','Stroke':'#4778A5'} for i in range(800)],'Edges':[],'Ports':[],'Labels':[]}
fixture=out/'fixture.json';fixture.write_text(json.dumps({'Version':1,'Graph':g}))
empty=out/'empty.json';empty.write_text(json.dumps({'Version':1,'Graph':{key:[] for key in g}}))
results,errors=[],[]
with sync_playwright() as p:
    browser=p.chromium.launch(headless=True,args=['--enable-unsafe-swiftshader','--use-angle=swiftshader','--disable-dev-shm-usage'])
    page=browser.new_page(viewport={'width':1440,'height':1050},device_scale_factor=density)
    page.add_init_script("Object.defineProperty(window,'showOpenFilePicker',{value:undefined,configurable:true});")
    page.on('pageerror',lambda e:errors.append(str(e)))
    def state():return page.evaluate('globalThis.visualizeSceneState')
    def scroll():return page.evaluate('globalThis.visualizeSceneScroll')
    def wait(expression):
        page.wait_for_function('() => {const v=globalThis.visualizeSceneState,s=globalThis.visualizeSceneScroll;return v&&s&&('+expression+');}',timeout=45000)
    def bounds(name):
        key=json.dumps(name);wait(f'v.controls[{key}]?.width>0 && v.controls[{key}]?.height>0')
        b=state()['controls'][name]
        if name in ('GraphHThumb','GraphVThumb'):
            rail=state()['controls'][name.replace('Thumb','Scroll')]
            # Default overlay templates previously placed the interactive thumb outside the compact rail.
            assert b['x']>=rail['x']-.51 and b['y']>=rail['y']-.51,(name,b,rail)
            assert b['x']+b['width']<=rail['x']+rail['width']+.51,(name,b,rail)
            assert b['y']+b['height']<=rail['y']+rail['height']+.51,(name,b,rail)
        return b
    def click(name):
        b=bounds(name);page.mouse.click(b['x']+b['width']/2,b['y']+b['height']/2);page.wait_for_timeout(180)
    def load(path):
        with page.expect_file_chooser() as chooser:click('Open')
        chooser.value.set_files(str(path))
    def unit_zoom():
        click('ZoomLevels');page.keyboard.press('Home')
        for _ in range(3):page.keyboard.press('ArrowDown')
        page.keyboard.press('Enter');wait('Math.abs(v.zoom-1)<1e-9 && s.horizontal.enabled && s.vertical.enabled');page.wait_for_timeout(200)
    def record(name):
        assert not state()['error'],state()['error']
        assert state()['revision']==revision and state()['model']==baseline,'Viewport input mutated graph data'
        results.append({'name':name,'passed':True});print('PASS',density,name,flush=True)
    try:
        page.goto(url,wait_until='domcontentloaded',timeout=120000);wait('v.ready && s.visible')
        load(fixture);wait("v.status==='Graph imported.' && v.model.Graph.Nodes.length===800")
        unit_zoom();baseline=state()['model'];revision=state()['revision']
        h=bounds('GraphHScroll');v=bounds('GraphVScroll');c=state()['canvas']
        assert abs(h['height']-16)<1 and abs(v['width']-16)<1
        assert h['y']>=c['y']+c['height']-1 and v['x']>=c['x']+c['width']-1
        bounds('GraphHThumb');bounds('GraphVThumb')
        page.screenshot(path=str(out/'desktop-scrollbars.png'))
        record('Native scrollbars and their hit targets stay within compact graph rails')
        click('GraphHThumb');prior=state();maximum=scroll()['horizontal']['max'];page.keyboard.press('End')
        wait(f'Math.abs(v.origin.x-({maximum}))<1e-6')
        assert state()['zoom']==prior['zoom'] and state()['origin']['y']==prior['origin']['y']
        page.keyboard.press('Home');wait('Math.abs(s.horizontal.value)<1e-9');origin=state()['origin']['x']
        page.keyboard.press('ArrowRight');wait(f'v.origin.x>({origin})')
        assert abs(state()['origin']['x']-origin-24)<1e-6
        record('Scrollbar Home, End and arrow keys move only the horizontal camera in exact screen steps')
        # Native RepeatButtons, not synthetic keyboard navigation, must use the same line step.
        prior=state()['origin']['x'];h=bounds('GraphHScroll')
        page.mouse.click(h['x']+h['width']-8,h['y']+8);wait(f'v.origin.x>({prior})')
        assert abs(state()['origin']['x']-prior-24)<1e-6
        record('Desktop arrow button advances one line using the native scrollbar command')
        click('GraphVThumb');page.keyboard.press('Home');wait('s.vertical.value<1e-9');origin=state()['origin']['y'];page_size=state()['canvas']['height']*.85
        page.keyboard.press('PageDown');wait(f'v.origin.y>({origin})')
        assert abs(state()['origin']['y']-origin-page_size)<1e-6
        record('Vertical PageDown uses the visible page size and cannot nudge selected graph items')
        click('GraphHThumb');page.keyboard.press('Home');wait('s.horizontal.value<1e-9');b=bounds('GraphHThumb');start=state()['origin']['x'];builds=scroll()['builds']
        x,y=b['x']+b['width']/2,b['y']+b['height']/2
        page.mouse.move(x,y);page.mouse.down();page.mouse.move(x+80,y,steps=8)
        wait(f's.dragging && v.origin.x>({start})')
        assert scroll()['builds']==builds,'Thumb drag rebuilt its frozen coordinate range'
        page.screenshot(path=str(out/'thumb-drag.png'));page.mouse.up();wait('!s.dragging')
        record('Native thumb dragging uses one frozen range and completes without graph edits')
        before=state()['performance'].copy();click('GraphHThumb');page.keyboard.press('ArrowRight');page.keyboard.press('ArrowLeft')
        page.wait_for_timeout(250);after=state()['performance']
        for counter in ['documentBuilds','sceneBuilds','nodeIndexBuilds','edgeIndexBuilds','annotationIndexBuilds','createdPaths']:
            assert after[counter]==before[counter],counter
        record('Scrollbar camera movement reuses serialized document data and renderer preparation')
        click('ModeSelect');page.keyboard.press('Control+Home');wait("v.selection.length===1 && v.navigationFocus==='n0'");page.wait_for_timeout(250)
        builds=scroll()['builds'];page.keyboard.press('Control+a');wait('v.selection.length===800');page.wait_for_timeout(250)
        assert scroll()['builds']==builds
        record('Select All does not rebuild scrollbar geometry')
        click('GraphHThumb');b=bounds('GraphHThumb');x,y=b['x']+b['width']/2,b['y']+b['height']/2
        page.mouse.move(x,y);page.mouse.down();page.mouse.move(x+30,y,steps=4);wait('s.dragging')
        prior=state()['origin'];page.set_viewport_size({'width':1380,'height':1050});wait('!s.dragging');page.wait_for_timeout(180)
        page.mouse.move(x+100,y,steps=5);page.mouse.up();page.wait_for_timeout(250)
        assert state()['origin']==prior,'Late thumb event overwrote camera after resize'
        page.set_viewport_size({'width':1440,'height':1050});page.wait_for_timeout(250)
        record('Resize invalidates an active thumb gesture and rejects late movement')
        c=state()['canvas'];click('MenuView');click('ToggleCanvasScrollbars');wait('!s.visible && !v.controls.ToggleCanvasScrollbars');page.wait_for_timeout(180)
        assert abs(state()['canvas']['width']-c['width']-16)<1
        click('MenuView');click('ToggleCanvasScrollbars');wait('s.visible && !v.controls.ToggleCanvasScrollbars');page.wait_for_timeout(180)
        assert abs(state()['canvas']['width']-c['width'])<1
        record('View menu hides and restores scrollbars without changing the document')
        old_zoom=state()['zoom'];click('GraphScrollFit');wait(f'v.zoom<({old_zoom})')
        record('Corner Fit button uses the same graph viewport and preserves undo history')
        page.set_viewport_size({'width':640,'height':900});wait('v.canvas.width>600');page.wait_for_timeout(200)
        b=bounds('GraphVScroll');assert b['x']+b['width']<=640
        page.screenshot(path=str(out/'narrow-scrollbars.png'));page.set_viewport_size({'width':1440,'height':1050});page.wait_for_timeout(200)
        record('Scrollbar chrome remains usable with responsive side panes hidden')
        import selection_zoom
        selection_zoom.run(page,state,wait,click,record,out)
        load(empty);wait('v.model.Graph.Nodes.length===0 && !s.horizontal.enabled && !s.vertical.enabled')
        baseline=state()['model'];revision=state()['revision'];click('GraphScrollFit')
        record('Empty document replacement clears stale ranges and disables inapplicable scrolling')
        assert not errors,errors
    except Exception:
        traceback.print_exc();errors.append(traceback.format_exc())
        try:page.screenshot(path=str(out/'failure.png'))
        except Exception:pass
    finally:
        for name,fn in [('state',state),('scroll',scroll)]:
            try:(out/(name+'.json')).write_text(json.dumps(fn(),indent=2))
            except Exception:pass
        (out/'report.json').write_text(json.dumps({'density':density,'results':results,'errors':errors,'note':'Actual Uno pointer/keyboard input; SwiftShader software rendering, not physical GPU evidence.'},indent=2))
        browser.close()
if errors:
    sys.exit(1)
if density==1:
    sys.exit(subprocess.run([sys.executable,__file__,url,'2'],check=False).returncode)
sys.exit(0)
