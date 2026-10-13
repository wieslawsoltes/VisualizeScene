"""Real compiled-Uno grid preferences, drawing counters and snapped pointer creation."""
import json
from pathlib import Path
import sys
import traceback
from playwright.sync_api import sync_playwright
from native_menu import target
from native_zoom import choose as zoom_choice

url=sys.argv[1] if len(sys.argv)>1 else 'http://127.0.0.1:8080/VisualizeScene/?test=1'
out=Path('artifacts/browser-workspace/grid');out.mkdir(parents=True,exist_ok=True)
fixture=out/'fixture.json';fixture.write_text(json.dumps({'Version':1,'Graph':{'Nodes':[
    {'Id':'a','Bounds':{'X':0,'Y':0,'Width':90,'Height':50},'Text':'A'},
    {'Id':'b','Bounds':{'X':360,'Y':180,'Width':90,'Height':50},'Text':'B'}],
    'Edges':[{'Id':'ab','SourceId':'a','TargetId':'b'}],'Ports':[],'Labels':[]}}))
results,errors,trace=[],[],[]
with sync_playwright() as p:
    browser=p.chromium.launch(headless=True,args=['--enable-unsafe-swiftshader','--use-angle=swiftshader','--disable-dev-shm-usage'])
    page=browser.new_page(viewport={'width':1440,'height':1050},accept_downloads=True)
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
    def settings():return {k:state()['grid'][k] for k in ['horizontal','vertical','origin','pattern','color','thickness','visible','snap','axes','anchor']}
    def frozen():return {k:state()[k] for k in ['model','revision','selection','zoom','origin']}
    def open_options():
        click('MenuView');click('GridSettings');wait('v.grid.ready && v.controls.GridOptionsApply?.width>0 && v.controls.GridOptionsCancel?.width>0')
    def text(name,value):
        click('GridOption'+name);page.keyboard.press('Control+a');page.keyboard.insert_text(value)
        wait('v.grid.inputs['+json.dumps(name)+']==='+json.dumps(value))
    def choice(name,index):
        key={'Pattern':'patternInput','Axes':'axesInput','Anchor':'anchorInput'}[name]
        click('GridOption'+name);wait('v.grid.'+key+'.open')
        page.keyboard.press('Home')
        for _ in range(index):page.keyboard.press('ArrowDown')
        page.keyboard.press('Enter');wait('v.grid.'+key+'.index==='+str(index)+' && !v.grid.'+key+'.open && v.grid.ready')
    def finish(apply=True):
        click('GridOptionsApply' if apply else 'GridOptionsCancel');wait('!v.grid.ready && !v.controls.GridOptionsApply && v.graphFocused')
    def footer_bounds():
        v=state();c=v['controls'];body=c['GridOptionBody'];footer=c['GridOptionFooter'];reset=c['GridOptionReset'];editor=c['GridOptionsEditor']
        assert reset['y']>=body['y']+body['height']-0.1,(body,reset)
        assert reset['y']>=footer['y'] and reset['y']+reset['height']<=footer['y']+footer['height']+0.1,(footer,reset)
        assert reset['y']+reset['height']<=editor['y']+editor['height']+0.1,(editor,reset)
        for name in ['GridOptionReset','GridOptionsApply','GridOptionsCancel']:
            b=c[name];assert b['x']>=0 and b['y']>=0 and b['x']+b['width']<=page.viewport_size['width'] and b['y']+b['height']<=page.viewport_size['height'],(name,b)
        return {k:c[k] for k in ['GridOptionFooter','GridOptionReset','GridOptionsApply','GridOptionsCancel']}
    def created_center(x_snap,y_snap):
        before=state();click('ModeNode');b=state()['canvas'];x=round(b['x']+b['width']*.63);y=round(b['y']+b['height']*.64)
        v=state();world=((x-b['x'])/v['zoom']+v['origin']['x'],(y-b['y'])/v['zoom']+v['origin']['y']);g=v['grid']
        expected=[round((world[0]-g['origin']['x'])/g['horizontal'])*g['horizontal']+g['origin']['x'] if x_snap else world[0],
                  round((world[1]-g['origin']['y'])/g['vertical'])*g['vertical']+g['origin']['y'] if y_snap else world[1]]
        page.mouse.click(x,y);wait('v.model.Graph.Nodes.length===3')
        node=state()['model']['Graph']['Nodes'][-1]['Bounds'];actual=[node['X']+node['Width']/2,node['Y']+node['Height']/2]
        assert all(abs(a-e)<1e-5 for a,e in zip(actual,expected)),(actual,expected,world,g)
        click('Undo');wait('v.model.Graph.Nodes.length===2');assert state()['model']==before['model'];click('ModeSelect')
    def record(name):
        assert not state()['error'],state()['error'];results.append({'name':name,'passed':True});print('PASS',name,flush=True)
    try:
        page.goto(url,wait_until='domcontentloaded',timeout=120000);wait('v.ready')
        with page.expect_file_chooser() as chooser:click('Open')
        chooser.value.set_files(str(fixture));wait("v.status==='Graph imported.' && v.model.Graph.Nodes.length===2")
        baseline=state()['model'];zoom_choice(page,3,trace);wait('v.zoom===1')
        before=frozen();old=settings();open_options();text('Horizontal','25');text('Vertical','40');choice('Pattern',1)
        assert frozen()==before and settings()==old
        page.screenshot(path=str(out/'staged-grid-options.png'));finish(False)
        assert frozen()==before and settings()==old
        record('Compact options preview stages native input and Cancel leaves the entire view and graph unchanged')
        open_options();footer_before=footer_bounds();text('Horizontal','0');click('GridOptionsApply');wait('v.grid.ready && v.grid.error.length>0');assert footer_bounds()==footer_before
        assert frozen()==before and settings()==old
        page.screenshot(path=str(out/'invalid-grid-options.png'));text('Horizontal','25');finish(False)
        record('Invalid spacing keeps the dialog open and rejects all settings atomically')
        open_options()
        for name,value in [('Horizontal','25'),('Vertical','40'),('OriginX','3'),('OriginY','7'),('Color','#A3B7CB'),('Thickness','1.5')]:text(name,value)
        choice('Pattern',1);choice('Anchor',1);finish();wait("v.grid.pattern==='Crosses' && v.grid.anchor==='Center' && v.grid.marks>0")
        assert frozen()==before
        assert settings()=={'horizontal':25,'vertical':40,'origin':{'x':3,'y':7},'pattern':'Crosses','color':'#A3B7CB','thickness':1.5,'visible':True,'snap':True,'axes':3,'anchor':'Center'}
        record('Apply publishes the complete rectangular origin-aware grid without touching document history, selection or camera')
        for index,pattern in enumerate(['Dots','Crosses','Lines','HorizontalLines','VerticalLines']):
            before=frozen();open_options();choice('Pattern',index);finish();wait('v.grid.pattern==='+json.dumps(pattern)+' && v.grid.marks>0')
            assert frozen()==before;page.screenshot(path=str(out/('grid-'+pattern+'.png')))
        record('All five native pattern choices paint through the actual graph renderer')
        created_center(True,True)
        record('Pointer-created nodes use the chosen center anchor and rectangular lattice; Undo restores exact graph data')
        before=frozen();open_options();text('Horizontal','100');text('Vertical','80');finish();assert frozen()==before
        zoom_choice(page,6,trace);wait('v.zoom===4 && v.grid.xStep===400 && v.grid.yStep===320')
        assert state()['grid']['xCount']<=256 and state()['grid']['yCount']<=256
        page.screenshot(path=str(out/'high-zoom-real-lattice.png'));zoom_choice(page,3,trace)
        record('High zoom keeps actual 100-by-80 world spacing instead of inventing intermediate visual grid points')
        open_options();choice('Axes',1);click('GridOptionVisible');finish();wait('!v.grid.visible && v.grid.snap && v.grid.axes===1 && v.grid.marks===0')
        created_center(True,False)
        record('Hidden grids still snap to the chosen X axis while leaving Y unconstrained')
        old=settings();open_options();footer_bounds();click('GridOptionReset');assert settings()==old;finish(False);assert settings()==old
        open_options();click('GridOptionReset');finish();wait("v.grid.pattern==='Dots' && v.grid.horizontal===10 && v.grid.vertical===10 && v.grid.anchor==='TopLeft'")
        assert state()['model']==baseline
        record('Reset is staged and only applies when confirmed, retaining graph edits and undo history')
        # Two repaints for grid visibility do not re-prepare unchanged graph indexes.
        wait('v.grid.marks>0');performance=state()['performance'];builds=state()['grid']['builds']
        click('MenuView');click('ToggleGrid');wait('!v.grid.visible && v.grid.marks===0')
        click('MenuView');click('ToggleGrid');wait('v.grid.visible && v.grid.marks>0')
        assert state()['grid']['builds']==builds
        assert state()['performance']['sceneBuilds']==performance['sceneBuilds']
        record('Visibility-only repaints reuse cached lattice geometry and graph spatial preparation')
        open_options();text('Horizontal','35');finish();preference=settings()
        with page.expect_download() as pending:click('Save')
        saved=out/'saved.json';pending.value.save_as(str(saved));assert json.loads(saved.read_text())==baseline
        with page.expect_file_chooser() as chooser:click('Open')
        chooser.value.set_files(str(saved));wait("v.status==='Graph imported.' && v.selection.length===0")
        assert settings()==preference and state()['model']==baseline
        record('Native JSON save/import retains session grid settings without serializing them into graph data')
        page.set_viewport_size({'width':640,'height':900});wait('!w.leftVisible && !w.rightVisible');open_options()
        page.screenshot(path=str(out/'narrow-grid-options.png'))
        text('Horizontal','45');finish();wait('v.grid.horizontal===45');assert state()['model']==baseline
        open_options();text('Horizontal','55');page.keyboard.press('Escape');wait('!v.grid.ready && v.graphFocused');assert state()['grid']['horizontal']==45
        record('Narrow native options remain usable and Escape discards changes with focus restored')
        # Resize an already-open native dialog; all action rows stay outside its scrollable body.
        open_options();page.set_viewport_size({'width':640,'height':480});wait('v.controls.GridOptionsEditor.height<=280');page.wait_for_timeout(250)
        pinned=footer_bounds();body=state()['controls']['GridOptionBody'];page.mouse.move(body['x']+body['width']/2,body['y']+body['height']/2)
        page.mouse.wheel(0,600);wait('v.grid.scrollOffset>0');assert footer_bounds()==pinned
        page.screenshot(path=str(out/'short-dialog-pinned-actions.png'))
        click('GridOptionReset');wait("v.grid.inputs.Horizontal==='10'");assert state()['grid']['horizontal']==45
        finish(False);assert state()['grid']['horizontal']==45 and state()['model']==baseline
        record('Short-window resize and native scrolling keep Reset, Apply and Cancel pinned and reachable')
        assert not errors,errors
    except Exception:
        traceback.print_exc();errors.append(traceback.format_exc())
        try:page.screenshot(path=str(out/'failure.png'))
        except Exception:pass
    finally:
        try:(out/'state.json').write_text(json.dumps(state(),indent=2))
        except Exception:pass
        (out/'report.json').write_text(json.dumps({'results':results,'errors':errors,'inputTargets':trace,
            'note':'Actual compiled Uno pointer, native combo, dialog and file input. Read-only observations; no model mutation hook. SwiftShader, not a physical GPU benchmark.'},indent=2))
        browser.close()
sys.exit(1 if errors else 0)
