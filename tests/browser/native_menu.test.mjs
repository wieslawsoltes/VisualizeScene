import assert from 'node:assert/strict';
import fs from 'node:fs';
const observe = Function(`return (${fs.readFileSync(new URL('./native_menu_predicate.js', import.meta.url), 'utf8')})`)();
globalThis.innerWidth = 800; globalThis.innerHeight = 600;
const bounds = {x: 10, y: 20, width: 100, height: 30};
const publish = (token = 1, box = bounds) => globalThis.visualizeSceneState = {
    controls: {SaveGraphML: {...box}}, menuInput: {tokens: {SaveGraphML: token}}
};
const args = () => ({name: 'SaveGraphML', probe: {}});
let a = args(); publish(); assert.equal(observe(a), false);
for (let i=0; i<100; i++) assert.equal(observe(a), false); // frozen diagnostics cannot become stable
publish(); assert.equal(observe(a), false); publish(); assert.deepEqual(observe(a).bounds, bounds);
a = args(); publish(); observe(a); publish(2); assert.equal(observe(a), false);
publish(2); assert.equal(observe(a), false); publish(2); assert.equal(observe(a).token, 2);
a = args(); publish(); observe(a); publish(); observe(a);
publish(1, {...bounds, x: 50}); assert.equal(observe(a), false);
publish(1, {...bounds, x: 50}); assert.equal(observe(a), false);
publish(1, {...bounds, x: 50}); assert.equal(observe(a).bounds.x, 50);
for (const bad of [{...bounds, x: -1}, {...bounds, y: 590}, {...bounds, width: 0}, {...bounds, x: NaN}]) {
    a = args(); publish(); observe(a); publish(); observe(a); publish(1, bad); assert.equal(observe(a), false);
    publish(); assert.equal(observe(a), false); publish(); assert.equal(observe(a), false); publish(); assert.ok(observe(a));
}
a = args(); publish(); observe(a); publish(); observe(a); publish(0); assert.equal(observe(a), false);
publish(); assert.equal(observe(a), false); publish(); assert.equal(observe(a), false); publish(); assert.ok(observe(a));
const state = publish(); Object.freeze(state.controls.SaveGraphML); Object.freeze(state.controls); Object.freeze(state.menuInput.tokens);
Object.freeze(state.menuInput); Object.freeze(state); assert.equal(observe(args()), false);
console.log('PASS native menu observer: frozen states, replacement, movement, clipping, invalid geometry, unload and read-only state');
