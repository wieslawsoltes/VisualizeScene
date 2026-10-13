function observeMenuTarget({name, probe}) {
    const state = globalThis.visualizeSceneState;
    const bounds = state?.controls?.[name];
    const token = state?.menuInput?.tokens?.[name];
    if (!state || !token || !bounds ||
        ![bounds.x, bounds.y, bounds.width, bounds.height].every(Number.isFinite) ||
        bounds.width <= 0 || bounds.height <= 0 || bounds.x < 0 || bounds.y < 0 ||
        bounds.x + bounds.width > innerWidth || bounds.y + bounds.height > innerHeight) {
        probe.count = 0; probe.key = null; probe.snapshot = null;
        return false;
    }
    // Require distinct native diagnostics publications, not repeated reads of one cached state.
    if (probe.snapshot === state) return false;
    probe.snapshot = state;
    const key = JSON.stringify([token, bounds]);
    if (probe.key !== key) { probe.key = key; probe.count = 0; }
    if (++probe.count < 3) return false;
    return {name, token, bounds};
}
