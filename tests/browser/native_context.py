"""Wait for a native context target to settle; never invoke or retry an action."""
import json


def target(page, name, trace):
    """Return a fully visible target after three distinct read-only state publications."""
    # Native popups can reposition after Loaded/Opened. Different diagnostics objects
    # are required: re-reading one stale rectangle at requestAnimationFrame speed is
    # not evidence of layout stability. A new popup generation resets the observation.
    handle = page.wait_for_function("""({name, probe}) => {
        const v=globalThis.visualizeSceneState, w=globalThis.visualizeSceneWorkspace;
        const b=v?.controls[name];
        if (!v || !w?.contextOpen || !b || !(b.width>0 && b.height>0) ||
            b.x<0 || b.y<0 || b.x+b.width>innerWidth || b.y+b.height>innerHeight) {
            probe.count=0; probe.key=null; return false;
        }
        if (probe.snapshot===v) return false;
        probe.snapshot=v;
        const key=JSON.stringify([w.contextGeneration,b]);
        if (probe.key!==key) { probe.key=key; probe.count=0; }
        if (++probe.count<3) return false;
        return {name,generation:w.contextGeneration,bounds:b};
    }""", arg={"name": name, "probe": {}}, timeout=10000)
    try:
        result=handle.json_value()
    finally:
        handle.dispose()
    trace.append(result)
    return result["bounds"]
