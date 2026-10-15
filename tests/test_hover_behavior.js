// DOM Simulation Test for Floating Bar Hover & Video Interaction
const fs = require('fs');
const path = require('path');
const vm = require('vm');

let passed = 0;
let failed = 0;

function assert(condition, message) {
  if (condition) {
    console.log(`  ✅ PASS: ${message}`);
    passed++;
  } else {
    console.error(`  ❌ FAIL: ${message}`);
    failed++;
  }
}

console.log('====================================================');
console.log('🧪 REAL HOVER & EVENT SIMULATION TEST');
console.log('====================================================');

const code = fs.readFileSync(path.join(__dirname, '..', 'extension', 'floating_bar.js'), 'utf8');

// Build an interactive mock DOM environment
const listeners = {};
function addListener(target, type, fn, opts) {
  if (!listeners[target]) listeners[target] = [];
  listeners[target].push({ type, fn, opts });
}

function triggerEvent(target, type, eventObj = {}) {
  const list = listeners[target] || [];
  for (const l of list) {
    if (l.type === type) l.fn(eventObj);
  }
}

class MockElement {
  constructor(tag) {
    this.tagName = tag.toUpperCase();
    this.children = [];
    this.parentElement = null;
    this.className = '';
    this.classList = {
      _classes: new Set(),
      add: (c) => this.classList._classes.add(c),
      remove: (c) => this.classList._classes.delete(c),
      contains: (c) => this.classList._classes.has(c),
      toggle: (c) => {
        if (this.classList._classes.has(c)) this.classList._classes.delete(c);
        else this.classList._classes.add(c);
      }
    };
    this.style = {};
    this.attributes = {};
    this.textContent = '';
    this.listeners = {};
    this._rect = { left: 0, top: 0, right: 800, bottom: 450, width: 800, height: 450 };
  }

  get isConnected() {
    let curr = this;
    while (curr) {
      if (curr === mockDoc.documentElement || curr === mockDoc.body) return true;
      curr = curr.parentElement;
    }
    return false;
  }

  attachShadow(opts) {
    this.shadowRoot = new MockElement('SHADOW-ROOT');
    this.shadowRoot.parentElement = this;
    return this.shadowRoot;
  }

  appendChild(el) {
    el.parentElement = this;
    this.children.push(el);
    return el;
  }

  setAttribute(k, v) { this.attributes[k] = v; }
  getAttribute(k) { return this.attributes[k]; }

  addEventListener(type, fn, opts) {
    if (!this.listeners[type]) this.listeners[type] = [];
    this.listeners[type].push(fn);
  }

  trigger(type, ev = {}) {
    if (this.listeners[type]) {
      this.listeners[type].forEach(fn => fn(ev));
    }
  }

  getBoundingClientRect() { return this._rect; }

  querySelector(sel) {
    for (const c of this.children) {
      if (sel.startsWith('.') && c.classList.contains(sel.slice(1))) return c;
      if (sel.startsWith('#') && c.id === sel.slice(1)) return c;
      if (c.tagName.toLowerCase() === sel.toLowerCase()) return c;
      const sub = c.querySelector(sel);
      if (sub) return sub;
    }
    return null;
  }

  querySelectorAll(sel) {
    let res = [];
    for (const c of this.children) {
      if (sel.startsWith('.') && c.classList.contains(sel.slice(1))) res.push(c);
      if (sel.startsWith('#') && c.id === sel.slice(1)) res.push(c);
      if (c.tagName.toLowerCase() === sel.toLowerCase()) res.push(c);
      res = res.concat(c.querySelectorAll(sel));
    }
    return res;
  }

  closest(selector) {
    let curr = this;
    while (curr) {
      if (selector.includes('#' + curr.id)) return curr;
      if (curr.className && selector.split(',').some(s => curr.className.includes(s.trim().replace('.', '')))) return curr;
      curr = curr.parentElement;
    }
    return null;
  }
}

const mockDoc = {
  documentElement: new MockElement('html'),
  body: new MockElement('body'),
  createElement: (tag) => new MockElement(tag),
  createElementNS: (ns, tag) => new MockElement(tag),
  querySelector: (sel) => mockDoc.body.querySelector(sel),
  querySelectorAll: (sel) => mockDoc.body.querySelectorAll(sel),
  addEventListener: (t, fn, o) => addListener('document', t, fn, o)
};
mockDoc.documentElement.appendChild(mockDoc.body);

// Set up mock video and player
const mockPlayer = new MockElement('div');
mockPlayer.id = 'movie_player';
mockPlayer.className = 'html5-video-player';
mockPlayer._rect = { left: 50, top: 100, right: 904, bottom: 580, width: 854, height: 480 };

const mockVideo = new MockElement('video');
mockVideo._rect = { left: 50, top: 100, right: 904, bottom: 580, width: 854, height: 480 };
mockPlayer.appendChild(mockVideo);
mockDoc.body.appendChild(mockPlayer);

const mockWindow = {
  __fmFloatingBarLoaded: false,
  innerWidth: 1920,
  innerHeight: 1080,
  location: { href: 'https://www.youtube.com/watch?v=dQw4w9WgXcQ', hostname: 'www.youtube.com' },
  addEventListener: (t, fn, o) => addListener('window', t, fn, o),
  setTimeout: setTimeout,
  clearTimeout: clearTimeout,
  requestAnimationFrame: (cb) => setTimeout(cb, 16),
  cancelAnimationFrame: (id) => clearTimeout(id)
};

const sandbox = {
  window: mockWindow,
  document: mockDoc,
  location: mockWindow.location,
  chrome: { runtime: { sendMessage: () => {}, onMessage: { addListener: () => {} }, lastError: null } },
  sessionStorage: { length: 0, removeItem: () => {}, key: () => null },
  setTimeout: setTimeout,
  clearTimeout: clearTimeout,
  Date: Date,
  MutationObserver: class { observe() {} disconnect() {} }
};

vm.createContext(sandbox);

try {
  vm.runInContext(code, sandbox);
  assert(true, 'floating_bar.js evaluated without errors in sandbox');
} catch (e) {
  assert(false, `Evaluation error: ${e.message}`);
}

// 1. Check if video events attached
assert(mockVideo.listeners['play'] && mockVideo.listeners['play'].length > 0, 'play event attached to video');
assert(mockPlayer.listeners['mouseenter'] && mockPlayer.listeners['mouseenter'].length > 0, 'mouseenter event attached to player');
assert(mockPlayer.listeners['mousemove'] && mockPlayer.listeners['mousemove'].length > 0, 'mousemove event attached to player');

// 2. Trigger mousemove over the player
triggerEvent('window', 'mousemove', { clientX: 200, clientY: 200 });

// Find host element in document
const host = mockDoc.body.children.find(c => c.id === 'fm-floating-bar-host');
assert(!!host, 'Host element mounted into document body on hover');
assert(!!host.shadowRoot, 'Host has shadowRoot attached');

const wrap = host.shadowRoot.children.find(c => c.id === 'fm-wrap');
assert(!!wrap, '#fm-wrap container exists in shadowRoot');
assert(wrap.classList.contains('fm-visible'), '#fm-wrap has .fm-visible class after hover');

console.log('  Wrap top:', wrap.style.top, 'left:', wrap.style.left);
assert(wrap.style.top === '114px', 'Calculated top position is 114px (rect.top 100 + 14)');
assert(wrap.style.left === '890px', 'Calculated left position is 890px (rect.right 904 - 14)');

// 3. Move mouse over the bar itself
wrap._rect = { left: 750, top: 114, right: 890, bottom: 150, width: 140, height: 36 };
triggerEvent('window', 'mousemove', { clientX: 800, clientY: 130 });
assert(wrap.classList.contains('fm-visible'), 'Bar remains visible while mouse is hovering directly over the bar');

// 4. Test IDM Auto-show on play event
wrap.classList.remove('fm-visible');
mockVideo.trigger('play');
assert(wrap.classList.contains('fm-visible'), 'Bar automatically appears when video triggers play event');

console.log('====================================================');
console.log(`📊 SIMULATION RESULTS: ${passed} / ${passed + failed} PASSED`);
console.log('====================================================');
if (failed > 0) process.exit(1);
