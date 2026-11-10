/**
 * The bundles the packaged-module specs serve inside the sandboxed frame. Each one is a CLASSIC
 * script that speaks the bridge protocol (v1) by hand, with no help from the authoring library:
 * the specs are about the shell's side of the contract, so the other side is written out in plain
 * ES5 here where a reader can see exactly which bytes a hostile or broken module would send.
 *
 * A script is assembled from {@link HELPERS}, the body a behaviour supplies and {@link START}.
 * Function declarations hoist across the whole script, so a body defines `onInit(d)` (called with
 * the shell's `init` once the port arrives), `onShell(m)` (every later shell message), or
 * `start()` to replace the handshake itself.
 */

/** Helpers every bundle shares: the port, `send`, `api`, `show` and the handshake plumbing. */
const HELPERS = `
var nonce = location.hash.slice(1);
var port = null;
function send(m) { port.postMessage(m); }
function ready(n) { parent.postMessage({ v: 1, type: 'ready', nonce: n }, '*'); }
function api(id, method, path, body) {
  var m = { v: 1, type: 'api', id: id, method: method, path: path };
  if (body !== undefined) { m.body = body; }
  send(m);
}
function show(text) {
  var p = document.createElement('p');
  p.textContent = text;
  document.body.appendChild(p);
}
function attach(event) {
  var d = event.data;
  if (d && d.type === 'init' && event.ports.length > 0 && port === null) {
    port = event.ports[0];
    port.onmessage = function (e) { if (typeof onShell === 'function') { onShell(e.data); } };
    if (typeof onInit === 'function') { onInit(d); }
  }
}
`

/** The handshake every well-behaved bundle ends with: listen for `init`, then announce itself. */
const START = `
window.addEventListener('message', attach);
if (typeof start === 'function') { start(); } else { ready(nonce); }
`

/**
 * Assembles a bundle.
 * @param body The behaviour: function declarations for `onInit`, `onShell` and/or `start`.
 * @returns The classic script's source.
 */
const bundle = (body: string): string => {
  return `${HELPERS}\n${body}\n${START}`
}

/**
 * The probe of what a frame can reach: the shell's cookie, its local storage and its document.
 * Shared by the sandboxed bundle and the positive control, so a difference in the answers is a
 * difference in the frames and not in the probes.
 */
const PROBE = `
function attempt(f) {
  try { return 'read:' + String(f()); } catch (e) { return 'blocked:' + e.name; }
}
function probe() {
  return 'cookie=' + attempt(function () { return document.cookie; }) +
    ';storage=' + attempt(function () { return localStorage.getItem('shell-secret'); }) +
    ';parent=' + attempt(function () { return parent.document.title; });
}
`

/** A well-behaved module: reads the surroundings, asks for data through the bridge, resizes and toasts. */
export const GOOD_SCRIPT: string = bundle(`
function onInit(d) {
  show('init locale:' + d.locale + ' theme:' + d.theme + ' role:' + d.roleName + ' route:' + d.route);
  send({ v: 1, type: 'resize', height: 240 });
  send({ v: 1, type: 'toast', text: 'Toast from the module' });
  api('greeting', 'GET', '/api/v1/hello/greeting');
}
function onShell(m) {
  if (m.type === 'api-result') { show('result ' + m.id + ' ' + m.status + ' ' + JSON.stringify(m.body)); }
  if (m.type === 'locale') { show('locale-changed ' + m.locale); }
  if (m.type === 'theme') { show('theme-changed ' + m.theme); }
}
`)

/** A bundle that throws while it loads, so it never reaches the handshake. */
export const THROWS_ON_LOAD_SCRIPT: string = `throw new Error('boom on load');`

/** A bundle that loads fine and never announces itself. */
export const SILENT_SCRIPT: string = bundle(`function start() {}`)

/** A bundle that completes the handshake and then throws inside its own handler. */
export const THROWS_AFTER_READY_SCRIPT: string = bundle(`
function onInit() { throw new Error('boom after ready'); }
`)

/** Three malformed messages: a wrong field type, a wrong protocol version, and a bare string. */
export const MALFORMED_SCRIPT: string = bundle(`
function onInit() {
  send({ v: 1, type: 'resize', height: 'tall' });
  send({ v: 2, type: 'resize', height: 5 });
  send('just a string');
}
`)

/** Three messages of a type the protocol does not know. */
export const UNKNOWN_TYPE_SCRIPT: string = bundle(`
function onInit() {
  send({ v: 1, type: 'steal-cookie' });
  send({ v: 1, type: 'init', locale: 'en', theme: 'dark', roleName: 'admin', route: '/' });
  send({ v: 1, type: 'ready', nonce: nonce });
}
`)

/** Three toasts of 70 000 characters each, over the 64 KiB limit. */
export const OVERSIZED_SCRIPT: string = bundle(`
function onInit() {
  var text = new Array(70001).join('x');
  send({ v: 1, type: 'toast', text: text });
  send({ v: 1, type: 'toast', text: text });
  send({ v: 1, type: 'toast', text: text });
}
`)

/** Three API calls whose body is over the 1 MiB limit. */
export const OVERSIZED_API_SCRIPT: string = bundle(`
function onInit() {
  var big = new Array(1100001).join('y');
  api('a', 'POST', '/api/v1/hello/items', { big: big });
  api('b', 'POST', '/api/v1/hello/items', { big: big });
  api('c', 'POST', '/api/v1/hello/items', { big: big });
}
`)

/** Two hundred valid API calls in one breath, over the 30-per-second limit. */
export const FLOOD_SCRIPT: string = bundle(`
function onInit() {
  for (var i = 0; i < 200; i++) { api('f' + i, 'GET', '/api/v1/hello/ping'); }
}
`)

/** Announces itself correctly, then announces itself again three times. */
export const SECOND_READY_SCRIPT: string = bundle(`
function onInit() {
  ready(nonce);
  ready(nonce);
  ready(nonce);
}
`)

/** Never sends the right nonce: three announcements with another one. */
export const WRONG_NONCE_SCRIPT: string = bundle(`
function start() {
  var wrong = new Array(33).join('0');
  ready(wrong);
  ready(wrong);
  ready(wrong);
}
`)

/** One announcement with a wrong nonce, then the right one: a single counted violation, and ready. */
export const ONE_WRONG_NONCE_THEN_RIGHT_SCRIPT: string = bundle(`
function start() {
  ready(new Array(33).join('0'));
  ready(nonce);
}
function onInit(d) { show('ready after one wrong nonce, route:' + d.route); }
`)

/** Three navigations to a screen the module never declared. */
export const OUT_OF_ROUTE_SCRIPT: string = bundle(`
function onInit() {
  send({ v: 1, type: 'navigate', path: '/not-declared' });
  send({ v: 1, type: 'navigate', path: '/also-not-declared' });
  send({ v: 1, type: 'navigate', path: '/nor-this' });
}
`)

/** Asks to move to the module's second screen from the first, and says which screen it is on. */
export const NAVIGATE_SCRIPT: string = bundle(`
function onInit(d) {
  show('screen ' + d.route);
  if (d.route === '/hello') { send({ v: 1, type: 'navigate', path: '/other' }); }
}
`)

/** Calls its own endpoint, another module's endpoint and writes, and shows each answer. */
export const API_SCOPE_SCRIPT: string = bundle(`
function onInit() {
  api('own', 'GET', '/api/v1/hello/greeting');
  api('other', 'GET', '/api/v1/accounts');
  api('write', 'POST', '/api/v1/hello/items', { name: 'x' });
}
function onShell(m) {
  if (m.type === 'api-result') { show('result ' + m.id + ' ' + m.status + ' ' + JSON.stringify(m.body)); }
}
`)

/** Probes what the sandboxed frame can reach and reports it to the shell as a toast. */
export const PROBE_SCRIPT: string = bundle(`${PROBE}
function onInit() { send({ v: 1, type: 'toast', text: probe() }); }
`)

/** The same probe in a page that is NOT sandboxed: it writes the answer into its own title. */
export const PROBE_CONTROL_SCRIPT: string = `${PROBE}\ndocument.title = probe();`
