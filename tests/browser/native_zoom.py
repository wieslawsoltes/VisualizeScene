"""Commit a native zoom choice once, observing popup realization, focus and selection."""
from native_menu import target


def choose(page, index, trace):
    if index not in range(7):
        raise ValueError("Zoom choice index must be between zero and six")
    box = target(page, "ZoomLevels", trace)
    page.mouse.click(box["x"] + box["width"] / 2, box["y"] + box["height"] / 2)
    page.wait_for_function("() => {const z=globalThis.visualizeSceneZoomChoice;return z?.open && z.ready && z.focused;}", timeout=10000)
    page.keyboard.press("Home")
    for _ in range(index):
        page.keyboard.press("ArrowDown")
    page.keyboard.press("Enter")
    page.wait_for_function("i => {const z=globalThis.visualizeSceneZoomChoice;return z && !z.open && z.index===i && globalThis.visualizeSceneState?.graphFocused;}", arg=index, timeout=10000)
    trace.append({"name":"ZoomSelection","index":index,"state":page.evaluate("globalThis.visualizeSceneZoomChoice")})
