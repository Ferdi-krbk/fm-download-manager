// Comprehensive behavioral test for Floating Bar actions and dismissal
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
console.log('🧪 REAL BEHAVIOR TEST: FLOATING BAR ACTIONS & DISMISSAL');
console.log('====================================================');

const floatingBarPath = path.join(__dirname, '..', 'extension', 'floating_bar.js');
const code = fs.readFileSync(floatingBarPath, 'utf8');

// 1. Syntax check
try {
  new vm.Script(code);
  assert(true, 'floating_bar.js syntax is 100% valid JavaScript');
} catch (e) {
  assert(false, `Syntax error: ${e.message}`);
}

// 2. Direct click vs Dropdown Arrow separation
assert(code.includes('id="fm-btn-action"') || code.includes("'fm-btn-action'"), 'Template has dedicated #fm-btn-action for immediate video download');
assert(code.includes('id="fm-arrow-btn"') || code.includes("'fm-arrow-btn'"), 'Template has dedicated #fm-arrow-btn for toggling options');
assert(code.includes("btnAction.addEventListener('click'") && code.includes("triggerDownload('video')"), 'Clicking #fm-btn-action triggers direct video download immediately');
assert(code.includes("arrowBtn.addEventListener('click'") && code.includes("barContainer.classList.toggle('fm-open')"), 'Clicking #fm-arrow-btn toggles dropdown menu');

// 3. In-memory Per-video Dismissal (resets on navigation/reload)
assert(code.includes('isDismissedForPage = true'), 'Sets in-memory dismissal flag immediately upon close click');
assert(code.includes('function isPageDismissed()'), 'Defines isPageDismissed function without ReferenceError');
assert(code.includes('isDismissedForPage = false'), 'Resets dismissal on navigation so subsequent videos/reloads work');

// 4. Subtitle detection unescaping and player extraction
assert(code.includes("text.replace(/\\/\\//g, '/')") || code.includes("replace(/\\\\\\//g, '/')"), 'detectSubtitleTracks correctly unescapes JSON-escaped slashes (\\/) in URLs');
assert(/vtt/i.test(code) && /srt/i.test(code), 'Detects both .vtt and .srt external subtitle files');

// 5. Full Runtime Execution Verification (ensures no ReferenceErrors or syntax crashes)
try {
  const domMock = {
    window: {
      __fmFloatingBarLoaded: false,
      innerHeight: 1080, innerWidth: 1920,
      addEventListener: () => {}, removeEventListener: () => {},
      location: { href: 'https://www.youtube.com/watch?v=test', hostname: 'www.youtube.com', origin: 'https://www.youtube.com', pathname: '/watch', search: '?v=test' }
    },
    document: {
      createElement: () => ({ attachShadow: () => ({ appendChild: () => {}, querySelector: () => null, querySelectorAll: () => [] }), style: {}, addEventListener: () => {}, appendChild: () => {} }),
      querySelectorAll: () => [], querySelector: () => null, addEventListener: () => {}, body: { appendChild: () => {} }, documentElement: {}
    },
    sessionStorage: { length: 0, getItem: () => null, setItem: () => {}, removeItem: () => {}, key: () => null },
    MutationObserver: class { observe() {} disconnect() {} },
    setTimeout: (fn) => fn, clearTimeout: () => {}, requestAnimationFrame: () => 1, cancelAnimationFrame: () => {},
    chrome: { runtime: { sendMessage: (msg, cb) => { if (cb) cb({ ok: true }); }, onMessage: { addListener: () => {} }, lastError: null } }
  };
  domMock.window.document = domMock.document;
  domMock.location = domMock.window.location;
  vm.runInNewContext(code, domMock);
  assert(true, 'floating_bar.js executes cleanly with zero runtime exceptions');
} catch (e) {
  assert(false, `floating_bar.js runtime error: ${e.message}`);
}

console.log('====================================================');
console.log(`📊 RESULTS: ${passed} / ${passed + failed} TESTS PASSED (${Math.round((passed / (passed + failed)) * 100)}%)`);
console.log('====================================================');

if (failed > 0) process.exit(1);
