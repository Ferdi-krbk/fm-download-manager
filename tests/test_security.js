'use strict';
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');

const extension = path.join(__dirname, '..', 'extension');
const manifest = JSON.parse(fs.readFileSync(path.join(extension, 'manifest.json'), 'utf8'));
assert(!(manifest.web_accessible_resources || []).some(group =>
  group.resources.some(resource => resource === 'token.json' || resource === '*' || resource === '*.json')),
  'Pairing credentials must not be readable by web pages');

let listener;
let payload;
const store = { token: '0'.repeat(64), settings: { port: 48213, autoLaunch: false } };
const event = { addListener() {} };
const chrome = {
  runtime: { id: 'test', getURL: name => `chrome-extension://test/${name}`, onMessage: { addListener(fn) { listener = fn; } }, onInstalled: event, onStartup: event },
  storage: { local: { async get(key) { return typeof key === 'string' ? { [key]: store[key] } : store; }, async set(data) { Object.assign(store, data); } } },
  cookies: { async getAll({ url }) { return [{ name: 'sid', value: new URL(url).hostname === 'video.test' ? 'video-secret' : 'subtitle-secret', domain: new URL(url).hostname, path: '/', secure: true }]; } },
  tabs: { onUpdated: event, onRemoved: event, async query() { return []; } },
  downloads: { onDeterminingFilename: event },
  contextMenus: { removeAll(cb) { cb?.(); }, create() {}, onClicked: event },
  notifications: { create() {} },
  action: { async setBadgeText() {}, async setBadgeBackgroundColor() {} },
  i18n: { getMessage: key => key, getUILanguage: () => 'en' },
};
const sandbox = {
  chrome, navigator: { userAgent: 'Security regression' }, URL, AbortController, AbortSignal, console,
  setTimeout, clearTimeout, setInterval: () => 0, clearInterval() {},
  async fetch(url, init = {}) {
    if (url.endsWith('/download')) {
      payload = JSON.parse(init.body);
      return { ok: true, async json() { return { ok: true, id: 'fixture' }; } };
    }
    return { ok: true, async json() { return { items: [] }; } };
  },
};
vm.createContext(sandbox);
vm.runInContext(fs.readFileSync(path.join(extension, 'background.js'), 'utf8'), sandbox);
(async () => {
  const result = await new Promise(resolve => listener({ type: 'download', kind: 'file', url: 'https://video.test/file', subtitleUrl: 'https://subs.test/sub.vtt' }, {}, resolve));
  assert.equal(result.ok, true);
  assert.equal(payload.cookieHeader, 'sid=video-secret', 'Subtitle credentials leaked into the video raw header');
  assert.equal(payload.cookies.length, 2);
  assert.equal(payload.cookies.find(c => c.domain === 'subs.test').value, 'subtitle-secret');
  assert.equal(payload.cookies.find(c => c.domain === 'video.test').value, 'video-secret');
  console.log('PASS security: pairing resource is private; actual background handoff preserves cookie domains');
})().catch(error => { console.error(error); process.exitCode = 1; });
