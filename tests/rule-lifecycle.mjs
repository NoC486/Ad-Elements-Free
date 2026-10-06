import { readFileSync } from 'node:fs';
import { resolve } from 'node:path';
import { runInNewContext } from 'node:vm';
import assert from 'node:assert/strict';

const directory = process.argv[2];
const script = readFileSync(resolve(directory, 'rule-script.js'), 'utf8');
const remove = readFileSync(resolve(directory, 'rule-remove.js'), 'utf8');
function page(headExists = true) {
  const styles = new Map(), events = new Map();
  const head = { appendChild(style) { styles.set(style.id, style); } };
  const document = {
    readyState: 'loading', head: headExists ? head : null,
    getElementById(id) { return styles.get(id); },
    createElement() { return { remove() { styles.delete(this.id); } }; },
    addEventListener(name, callback) { events.set(name, callback); },
    removeEventListener(name, callback) { if (events.get(name) === callback) events.delete(name); }
  };
  const window = {}; window.top = window;
  return { context: { URL, document, window, location: new URL('http://localhost:5888/app/discover') }, styles, events, head };
}
let p = page();
assert.equal(runInNewContext(script, p.context), true);
assert.equal(p.styles.size, 1, 'CSS is inserted while page is still loading when head exists');
runInNewContext(script, p.context);
assert.equal(p.styles.size, 1, 'Repeated execution does not duplicate style');
runInNewContext(remove, p.context);
assert.equal(p.styles.size, 0, 'Turning off removes style');
p = page(false);
assert.equal(runInNewContext(script, p.context), true);
assert.equal(p.events.size, 1, 'Document-start injection queues one readiness callback');
p.context.document.head = p.head;
p.events.get('DOMContentLoaded')();
assert.equal(p.styles.size, 1, 'Readiness event applies CSS without timer');
p = page(false);
runInNewContext(script, p.context);
runInNewContext(remove, p.context);
assert.equal(p.events.size, 0, 'Disabling before readiness cancels queued application');
for (const url of ['http://localhost:5890/app/discover', 'http://localhost:5888/login', 'https://evil.test/app/discover']) {
  p = page(); p.context.location = new URL(url);
  assert.equal(runInNewContext(script, p.context), false);
  assert.equal(p.styles.size, 0, 'Changed origin/path rejected');
}
p = page(); p.context.window.top = {};
assert.equal(runInNewContext(script, p.context), false, 'Subframes rejected');
console.log('PASS CSS immediate application, readiness, deduplication, cleanup and identity guards');
