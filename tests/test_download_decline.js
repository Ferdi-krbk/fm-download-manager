'use strict';
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');
const ext = path.join(__dirname, '../extension');
const tick = () => new Promise(resolve => setImmediate(resolve));

async function backgroundCase(firefox, accepted, storageFailure = false) {
  let determining, created, message;
  const calls = { cancel: 0, erase: 0, pause: 0, resume: 0, suggest: 0, download: 0, posts: 0 };
  const store = { token: 'a'.repeat(64), settings: { intercept: true, autoLaunch: false } };
  let intercepting = false;
  const event = { addListener() {} };
  const api = {
    runtime: { id: 'fixture', getURL: name => `${firefox ? 'moz' : 'chrome'}-extension://fixture/${name}`, onMessage: { addListener(fn) { message = fn; } }, onInstalled: event, onStartup: event },
    storage: { local: { async get(key) { if (storageFailure && intercepting) throw new Error('storage fixture failure'); return typeof key === 'string' ? { [key]: store[key] } : store; }, async set(values) { Object.assign(store, values); } } },
    downloads: {
      ...(firefox ? { onCreated: { addListener(fn) { created = fn; } } } : { onDeterminingFilename: { addListener(fn) { determining = fn; } } }),
      async cancel() { calls.cancel++; }, async erase() { calls.erase++; }, async pause() { calls.pause++; }, async resume() { calls.resume++; },
      async search() { return [{ state: 'in_progress', paused: firefox }]; }, async download() { calls.download++; }
    },
    cookies: { async getAll() { return []; } }, tabs: { onUpdated: event, onRemoved: event, async query() { return []; } },
    contextMenus: { removeAll(callback) { callback?.(); }, create() {}, onClicked: event }, notifications: { create() {} },
    action: { async setBadgeText() {}, async setBadgeBackgroundColor() {} }, i18n: { getMessage: key => key, getUILanguage: () => 'en' }
  };
  const sandbox = {
    chrome: api, ...(firefox ? { browser: api } : {}), URL, AbortController, console,
    setTimeout, clearTimeout, setInterval: () => 0, clearInterval() {}, navigator: { userAgent: 'Download decline regression' },
    async fetch(url) {
      if (url.endsWith('/download')) {
        calls.posts++;
        return { ok: true, status: 200, async json() { return accepted ? { ok: true, id: 'fixture' } : { ok: false, error: 'cancelled by user' }; } };
      }
      return { ok: true, async json() { return { paired: true, captureExtensions: ['pdf'], items: [] }; } };
    }
  };
  vm.createContext(sandbox); vm.runInContext(fs.readFileSync(path.join(ext, 'background.js'), 'utf8'), sandbox);
  const status = await new Promise(resolve => message({ type: 'status' }, {}, resolve));
  assert.equal(status.clickIntercept, false, 'A browser with a native downloads API must not also intercept page clicks');
  const item = { id: 7, url: 'https://files.test/one-use-post', filename: 'lecture.pdf', state: 'in_progress', totalBytes: 1048576 };
  intercepting = true;
  if (firefox) created(item); else determining(item, () => { calls.suggest++; });
  for (let n = 0; n < 20; n++) await tick();
  assert.equal(calls.posts, storageFailure ? 0 : 1);
  assert.equal(calls.cancel, accepted ? 1 : 0);
  assert.equal(calls.erase, accepted ? 1 : 0);
  assert.equal(calls.download, 0, 'The original POST/one-use download must not be recreated with a new GET');
  if (!accepted) assert.equal(firefox ? calls.resume : calls.suggest, 1, 'Declining or closing FM must continue the original browser download');
}

async function contentCase(clickIntercept, accepted) {
  let handler;
  let prevented = 0, nativeClicks = 0, requests = 0;
  const link = { href: 'https://files.test/file.pdf', hasAttribute: () => false, getAttribute: () => '' };
  const event = () => ({ button: 0, target: { closest: () => link }, preventDefault() { prevented++; }, stopPropagation() {} });
  link.click = () => { nativeClicks++; handler(event()); };
  const api = { runtime: { getURL: () => 'chrome-extension://fixture/', onMessage: { addListener() {} }, sendMessage(msg, callback) {
    if (msg.type === 'status') callback({ clickIntercept, settings: { intercept: true }, captureExtensions: ['pdf'] });
    else { requests++; callback({ ok: accepted, cancelled: !accepted }); }
  } } };
  const sandbox = { chrome: api, document: { addEventListener(type, fn) { if (type === 'click') handler = fn; } }, window: { top: {}, self: {} }, location: { href: 'https://files.test/page' }, setInterval: () => 0, Promise };
  vm.createContext(sandbox); vm.runInContext(fs.readFileSync(path.join(ext, 'content.js'), 'utf8'), sandbox);
  handler(event()); await tick();
  assert.equal(requests, clickIntercept ? 1 : 0);
  assert.equal(prevented, clickIntercept ? 1 : 0);
  assert.equal(nativeClicks, clickIntercept && !accepted ? 1 : 0, 'Click fallback must run exactly once without re-opening the FM prompt');
}

(async () => {
  for (const firefox of [false, true]) for (const accepted of [false, true]) await backgroundCase(firefox, accepted);
  for (const firefox of [false, true]) await backgroundCase(firefox, false, true);
  for (const clickIntercept of [false, true]) for (const accepted of [false, true]) await contentCase(clickIntercept, accepted);
  console.log('PASS Chrome/Firefox decline/close preserves native downloads; accepts hand over once; page clicks are not captured twice.');
})().catch(error => { console.error(error); process.exitCode = 1; });
