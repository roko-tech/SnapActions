const { test } = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const vm = require('node:vm');
const path = require('node:path');
const script = fs.readFileSync(path.join(__dirname, '../read-selection.js'), 'utf8');

function read({ text = 'مرحباً ChatGPT (42)\nEnglish مع العربية', active = {}, focused = true, direction = 'ltr' } = {}) {
  const document = { hasFocus: () => focused, activeElement: active, childNodes: [active] };
  active.parentNode = document;
  active.nodeType = 1;
  const selection = { isCollapsed: false, anchorNode: active, anchorOffset: 0,
    focusNode: active, focusOffset: text.length, toString: () => text,
    getRangeAt: () => ({ startContainer: active }) };
  return vm.runInNewContext(script, { document, window: { getSelection: () => selection },
    Node: { ELEMENT_NODE: 1 }, getComputedStyle: () => ({ direction }) });
}

test('preserves mixed direction text, whitespace, emoji and multiple lines exactly', () => {
  const text = '  استخدم \u2066ChatGPT\u2069 2.0!\nثم English 👋  ';
  assert.equal(read({ text }).text, text);
});
test('carries source direction without changing English-first text in an RTL paragraph', () => {
  const text = 'ChatGPT مع العربية';
  const result = read({ text, direction: 'rtl' });
  assert.equal(result.text, text);
  assert.equal(result.direction, 'rtl');
});
test('reads the selected substring of a text input instead of a previous document selection', () => {
  const value = 'قبل ChatGPT بعد';
  const active = { tagName: 'INPUT', type: 'text', value, selectionStart: 4, selectionEnd: 11 };
  const result = read({ text: 'stale', active });
  assert.equal(result.text, 'ChatGPT');
  assert.equal(result.editable, true);
});
test('textarea preserves line breaks and read-only state', () => {
  const active = { tagName: 'TEXTAREA', value: 'عربي\nEnglish', selectionStart: 0, selectionEnd: 12, readOnly: true };
  assert.equal(read({ active }).text, active.value);
  assert.equal(read({ active }).editable, false);
});
test('ignores unfocused documents and parent frames', () => {
  assert.equal(read({ focused: false }), null);
  assert.equal(read({ active: { tagName: 'IFRAME' } }), null);
});
test('never reads passwords or unsupported input controls', () => {
  for (const type of ['password', 'number', 'email', 'hidden'])
    assert.equal(read({ active: { tagName: 'INPUT', type, value: 'secret', selectionStart: 0, selectionEnd: 6 } }), null);
});
test('rejects oversized text without truncating it into another selection', () => {
  assert.equal(read({ text: 'a'.repeat(32769) }), null);
});

function background() {
  const listeners = {};
  const event = name => ({ addListener: fn => { listeners[name] = fn; } });
  const connection = { onMessage: event('message'), onDisconnect: event('disconnect'), postMessage: value => replies.push(value) };
  const replies = [];
  let current = { id: 1, windowId: 10 };
  let duringRead = () => {};
  let lastTarget;
  const chrome = {
    tabs: { onActivated: event('activated'), onUpdated: event('updated'), query: async () => [current] },
    windows: { onFocusChanged: event('focus'), getAll: async () => [{ id: current.windowId, focused: true }] },
    runtime: { connectNative: () => connection, onStartup: event('startup'), onInstalled: event('installed') },
    action: { onClicked: event('clicked') },
    scripting: { executeScript: async request => {
      lastTarget = request.target;
      duringRead();
      return [{ frameId: 0, documentId: 'doc', result: { text: 'عربي English', identity: 'range', editable: false, direction: 'rtl' } }];
    } }
  };
  vm.runInNewContext(fs.readFileSync(path.join(__dirname, '../background.js'), 'utf8'), { chrome, clearTimeout, setTimeout });
  return { listeners, replies, target: () => lastTarget, changeTab: () => { current = { id: 2, windowId: 10 }; }, duringRead: fn => { duringRead = fn; } };
}
test('binds reply to its request, tab, frame and document', async () => {
  const fixture = background();
  await fixture.listeners.message({ id: 17, type: 'selection', version: 1 });
  const reply = fixture.replies[0];
  assert.equal(reply.id, 17);
  assert.equal(reply.text, 'عربي English');
  assert.equal(reply.direction, 'rtl');
  assert.deepEqual(JSON.parse(reply.identity), [1, 0, 'doc', 'range']);
});
test('discards replies after switching tabs while the read is in flight', async () => {
  const fixture = background();
  fixture.duringRead(fixture.changeTab);
  await fixture.listeners.message({ id: 1, type: 'selection', version: 1 });
  assert.equal(fixture.replies[0].status, 'inactive');
  assert.equal(fixture.replies[0].text, undefined);
});
test('discards replies even after switching away and back to the same tab', async () => {
  const fixture = background();
  fixture.duringRead(() => fixture.listeners.activated());
  await fixture.listeners.message({ id: 1, type: 'selection', version: 1 });
  assert.equal(fixture.replies[0].status, 'inactive');
});

test('validation targets the captured document instead of every frame', async () => {
  const fixture = background();
  await fixture.listeners.message({ id: 1, type: 'selection', version: 1, identity: JSON.stringify([1, 0, 'doc', 'range']) });
  assert.equal(fixture.replies[0].status, 'ok');
  assert.equal(fixture.target().allFrames, undefined);
  assert.deepEqual(Array.from(fixture.target().documentIds), ['doc']);
});
test('validation rejects changed range and incompatible protocol', async () => {
  const fixture = background();
  await fixture.listeners.message({ id: 1, type: 'selection', version: 1, identity: JSON.stringify([1, 0, 'doc', 'old-range']) });
  assert.equal(fixture.replies[0].status, 'inactive');
  await fixture.listeners.message({ id: 2, type: 'selection', version: 99 });
  assert.equal(fixture.replies[1].status, 'incompatible');
});
