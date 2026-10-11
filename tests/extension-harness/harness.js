// Runs the REAL extension/background.js against a live bridge with a mocked chrome.* API and prints a JSON report.
// usage: node harness.js <bridgePort> <token> <extensionDir> <urlOfFileToDownload>
'use strict';
const fs = require('fs');
const path = require('path');
const vm = require('vm');

const [port, goodToken, extDir, fileUrl] = process.argv.slice(2);
const EXT_ID = 'abcdefghijklmnopabcdefghijklmnop';
const ORIGIN = `chrome-extension://${EXT_ID}`;

const store = { settings: { port: Number(port), intercept: true, autoLaunch: false } };
const listeners = { determining: null, message: null };
const calls = { cancel: [], erase: [], suggested: 0, notifications: [] };
const requests = [];

// Chrome omits Origin on extension GETs but sends it on POST/preflight: reproduce both.
const realFetch = fetch;
async function recordingFetch(url, init = {}) {
  const headers = { ...(init.headers || {}) };
  const method = (init.method || 'GET').toUpperCase();
  if (method !== 'GET') headers['Origin'] = ORIGIN;
  requests.push({ method, path: new URL(url).pathname, headerNames: Object.keys(headers).sort() });
  return realFetch(url, { ...init, headers });
}

const chrome = {
  runtime: {
    id: EXT_ID,
    getURL: () => ORIGIN + '/',
    onInstalled: { addListener() {} },
    onStartup: { addListener() {} },
    onMessage: { addListener(fn) { listeners.message = fn; } },
  },
  storage: {
    local: {
      async get(key) { return typeof key === 'string' ? { [key]: store[key] } : { ...store }; },
      async set(obj) { Object.assign(store, obj); },
    },
  },
  downloads: {
    onDeterminingFilename: { addListener(fn) { listeners.determining = fn; } },
    async cancel(id) { calls.cancel.push(id); },
    async erase(q) { calls.erase.push(q.id); },
    async search() { return []; },
  },
  cookies: {
    async getAll({ url }) {
      return [
        { name: 'sid', value: 'abc123', domain: new URL(url).hostname, path: '/', secure: false, expirationDate: 1900000000 },
        { name: 'pref', value: 'x', domain: new URL(url).hostname, path: '/', secure: false },
      ];
    },
  },
  notifications: { create(_id, o) { calls.notifications.push(o.title); } },
  tabs: { onUpdated: { addListener() {} }, onRemoved: { addListener() {} }, async query() { return []; } },
  contextMenus: { removeAll(cb) { cb && cb(); }, create() {}, onClicked: { addListener() {} } },
  action: { async setBadgeText() {}, async setBadgeBackgroundColor() {} },
  i18n: { getMessage: (k) => k, getUILanguage: () => 'en' },
};

const sandbox = {
  chrome, fetch: recordingFetch, console, URL, AbortController, setTimeout, clearTimeout, setInterval, clearInterval,
  navigator: { userAgent: 'Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 Chrome/130.0.0.0 Safari/537.36' },
};
vm.createContext(sandbox);
vm.runInContext(fs.readFileSync(path.join(extDir, 'background.js'), 'utf8'), sandbox, { filename: 'background.js' });

const send = (msg) => new Promise((resolve) => { listeners.message(msg, {}, resolve); });
const sleep = (ms) => new Promise((r) => setTimeout(r, ms));

async function intercept(name) {
  const before = { cancel: calls.cancel.length, suggested: calls.suggested };
  listeners.determining(
    {
      id: 77, url: fileUrl, finalUrl: fileUrl,
      filename: 'C:\\Users\\me\\Downloads\\' + name, referrer: 'https://example.test/page', totalBytes: 12 * 1024 * 1024,
    },
    () => { calls.suggested++; }
  );
  for (let i = 0; i < 50; i++) {
    await sleep(100);
    if (calls.cancel.length > before.cancel || calls.suggested > before.suggested) break;
  }
  await sleep(300);
  return { taken: calls.cancel.length > before.cancel, suggested: calls.suggested > before.suggested };
}

(async () => {
  const report = {};
  report.statusUnpaired = await send({ type: 'status' });
  report.interceptUnpaired = await intercept('contract.bin');
  report.pairBad = await send({ type: 'setToken', token: 'nothex' });
  report.pairWrong = await send({ type: 'setToken', token: 'f'.repeat(64) });
  report.pairGood = await send({ type: 'setToken', token: goodToken });
  report.statusPaired = await send({ type: 'status' });
  report.interceptPaired = await intercept('contract.bin');
  report.downloads = await send({ type: 'downloads' });
  report.requests = requests;
  report.cancel = calls.cancel;
  report.erase = calls.erase;
  console.log('REPORT:' + JSON.stringify(report));
  process.exit(0);
})().catch((e) => { console.log('HARNESS-ERROR:' + e.stack); process.exit(2); });
