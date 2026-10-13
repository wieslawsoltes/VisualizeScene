"""Native menu readiness without invoking, retrying or replacing an application command."""
from pathlib import Path

PREDICATE = Path(__file__).with_name("native_menu_predicate.js").read_text(encoding="utf-8")


def target(page, name, trace):
    """Observe stable layout, then require a real native PointerEntered before one click."""
    initial = page.evaluate("name => {const v=globalThis.visualizeSceneState;return {bounds:v.controls[name],token:v.menuInput?.tokens[name]};}", name)
    if not initial.get("token"):
        return initial["bounds"]
    handle = page.wait_for_function(PREDICATE, arg={"name": name, "probe": {}}, timeout=10000)
    try:
        observation = handle.json_value()
    finally:
        handle.dispose()
    bounds = observation["bounds"]
    page.mouse.move(bounds["x"] + bounds["width"] / 2, bounds["y"] + bounds["height"] / 2)
    # The C# observer records native routing even when the control class handles the event.
    # A rectangle alone is not evidence that the native input tree has received this pointer.
    page.wait_for_function("""o => {
        const v=globalThis.visualizeSceneState;
        return v?.menuInput?.tokens[o.name]===o.token && v.menuInput.hovered===o.token &&
            JSON.stringify(v.controls[o.name])===JSON.stringify(o.bounds);
    }""", arg=observation, timeout=10000)
    trace.append(observation)
    return bounds
