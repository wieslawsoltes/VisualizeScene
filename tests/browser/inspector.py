"""Additional actual-input property scenarios, called by the workspace suite."""
def run(page, state, ws, wait, click, record, out):
    def fill(name, text):
        click('Property' + name)
        page.keyboard.press('Control+a')
        page.keyboard.insert_text(text)
    def node(ident):
        return next(n for n in state()['model']['Graph']['Nodes'] if n['Id'] == ident)
    def select_first():
        click('ModeSelect'); page.keyboard.press('Control+Home'); wait('v.selection.length===1 && w.inspector.count===9')
        return state()['selection'][0]
    def undo(): click('Undo')
    click('ModeNode'); page.keyboard.press('Escape'); wait('v.mode === "Select" && w.modeCaption === "Select"')
    record('Escape synchronizes the mode caption with the graph input mode')
    selected = select_first()
    with page.expect_download() as download: click('Save')
    download.value.save_as(str(out / 'inspector-savepoint.json'))
    wait("!w.documentTitle.endsWith('*')")
    baseline = state()['model']; revision = state()['revision']; old = node(selected)
    x = state()['controls']['PropertyX']
    assert x['height'] <= 34 and x['x'] + x['width'] <= 1440
    fill('Label', 'Reviewed label'); fill('X', str(old['Bounds']['X'] + .125))
    click('PropertyApply'); wait(f'v.revision==={revision+1} && w.documentTitle.endsWith("*")')
    assert node(selected)['Text'] == 'Reviewed label'
    undo(); wait(f'v.revision==={revision+2} && !w.documentTitle.endsWith("*")')
    assert state()['model'] == baseline
    click('Redo'); wait('w.documentTitle.endsWith("*")')
    undo(); wait('!w.documentTitle.endsWith("*")')
    record('Compact categorized properties commit once; undo and redo restore the saved-state marker')

    revision = state()['revision']
    fill('Fill', 'not-a-color'); fill('Label', 'MUST NOT COMMIT'); click('PropertyApply')
    wait('w.inspector.message.includes("Invalid node style")')
    assert state()['model'] == baseline and state()['revision'] == revision
    fill('Fill', old['Fill']); fill('Label', old['Text']); click('PropertyApply')
    wait('w.inspector.message === "Properties applied."')
    assert state()['revision'] == revision
    record('Invalid style fields reject the entire property transaction without losing graph data')

    click('PropertyFillPalette')
    wait('v.controls.PropertyFillSwatchFFCC66?.width>0')
    page.screenshot(path=str(out / 'property-color-palette.png'))
    click('PropertyFillSwatchFFCC66')
    assert state()['model'] == baseline
    click('PropertyApply'); wait(f'v.revision==={revision+1}')
    assert node(selected)['Fill'] == '#FFCC66'
    page.screenshot(path=str(out / 'compact-property-grid.png'))
    undo(); wait(f'v.revision==={revision+2}')
    assert state()['model'] == baseline
    record('Color swatches only update the editor until Apply and remain exactly undoable')

    click('PropertyGroupGeometry'); wait('!w.inspector.geometryExpanded')
    # Refresh through a selection change; the category's expansion preference must survive.
    click('ModeSelect'); page.keyboard.press('Control+ArrowDown'); page.wait_for_timeout(250)
    select_first(); wait('!w.inspector.geometryExpanded')
    click('PropertyGroupGeometry'); wait('w.inspector.geometryExpanded')
    assert state()['model'] == baseline
    record('Property group collapse persists across selection refresh without affecting graph state')

    click('MenuEdit'); click('AddLabel')
    wait('v.selection.length===1 && v.model.Graph.Labels.some(l=>l.Id===v.selection[0])')
    label_id = state()['selection'][0]; before_label = state()['model']; revision = state()['revision']
    fill('Text', 'Independent property label'); fill('OffsetX', '-12.125'); fill('OffsetY', '33.875'); fill('FontSize', '17.25')
    click('PropertyApply'); wait(f'v.revision==={revision+1}')
    label = next(l for l in state()['model']['Graph']['Labels'] if l['Id'] == label_id)
    assert label['Text'] == 'Independent property label' and label['Offset'] == {'X':-12.125,'Y':33.875} and label['FontSize'] == 17.25
    undo(); wait(f'v.revision==={revision+2}')
    assert state()['model'] == before_label
    undo(); wait(f'v.revision==={revision+3}')
    assert state()['model'] == baseline
    record('Independent label text, both offsets and font size have distinct editors and atomic undo')

    select_first(); click('MenuEdit'); click('AddPort')
    wait('v.selection.length===1 && v.model.Graph.Ports.some(p=>p.Id===v.selection[0])')
    port_id = state()['selection'][0]; before_port = state()['model']; revision = state()['revision']
    fill('RelativeX', '.123456789012345'); fill('RelativeY', '.987654321098765'); click('PropertyApply')
    wait(f'v.revision==={revision+1}')
    port = next(p for p in state()['model']['Graph']['Ports'] if p['Id'] == port_id)
    assert port['RelativeLocation'] == {'X':.123456789012345,'Y':.987654321098765}
    fill('RelativeX', '1.25'); click('PropertyApply'); wait('w.inspector.message.includes("between zero and one")')
    assert state()['revision'] == revision+1
    undo(); wait(f'v.revision==={revision+2}')
    assert state()['model'] == before_port
    undo(); wait(f'v.revision==={revision+3}')
    assert state()['model'] == baseline
    record('Port editors retain full relative precision and reject invalid ranges without partial mutation')
    select_first()
    page.screenshot(path=str(out / 'final-reviewed-workspace.png'))
