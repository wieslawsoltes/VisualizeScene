"""Selection-fit commands against the already loaded scroll fixture; input-only actions."""
def run(page, state, wait, click, record, out):
    def fit():
        click('MenuView'); click('ZoomSelection'); wait('!v.controls.ZoomSelection')
    def center():
        v=state();return (v['origin']['x']+v['canvas']['width']/v['zoom']/2,
                         v['origin']['y']+v['canvas']['height']/v['zoom']/2)
    click('ModeSelect');page.keyboard.press('Control+Home')
    wait("v.selection.length===1 && v.selection[0]==='n0'")
    fit();wait('Math.abs(v.zoom-2)<1e-9')
    x,y=center();assert abs(x+3950)<1e-6 and abs(y+2970)<1e-6,(x,y)
    page.screenshot(path=str(out/'zoom-selected-node.png'))
    record('Zoom to selection centers one selected node without changing selection or graph data')
    click('ModeSelect');page.keyboard.press('Control+Shift+ArrowRight')
    wait("v.selection.length===2 && v.selection.includes('n1')")
    fit();x,y=center();assert abs(x+3800)<1e-6 and abs(y+2970)<1e-6,(x,y)
    for ident in ['n0','n1']:
        v=state();b=next(n['Bounds'] for n in v['model']['Graph']['Nodes'] if n['Id']==ident)
        assert (b['X']-v['origin']['x'])*v['zoom']>=39
        assert (b['X']+b['Width']-v['origin']['x'])*v['zoom']<=v['canvas']['width']-39
    record('Zoom to a multiselection encloses both full node rectangles with fit padding')
    click('ModeSelect');page.keyboard.press('Control+Shift+a');wait('v.selection.length===0')
    click('MenuView')
    page.wait_for_function("() => globalThis.visualizeSceneWorkspace?.enabled.ZoomSelection===false")
    page.keyboard.press('Escape');wait('!v.controls.ZoomSelection')
    record('The View menu disables Zoom to selection immediately when selection becomes empty')
