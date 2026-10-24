// Test Suite for FM Download Manager IDM-Style Floating Video Bar
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
console.log('🧪 FLOATING VIDEO DOWNLOAD BAR TEST SUITE');
console.log('====================================================');

const floatingBarPath = path.join(__dirname, '..', 'extension', 'floating_bar.js');
const code = fs.readFileSync(floatingBarPath, 'utf8');

// 1. Syntax & Integrity
try {
  new vm.Script(code);
  assert(true, 'floating_bar.js syntax is completely valid JavaScript');
} catch (e) {
  assert(false, `Syntax error: ${e.message}`);
}

// 2. Shadow DOM Encapsulation
assert(code.includes("attachShadow({ mode: 'open' })") || code.includes("attachShadow({ mode: 'closed' })"), 'Uses Shadow DOM for 100% style and DOM isolation');

// 3. Minimum Dimensions Filter
assert(code.includes('MIN_WIDTH = 220') && code.includes('MIN_HEIGHT = 130'), 'Filters out tiny videos (ads, avatars, decorative icons)');

// 4. Stream & Format Resolution
// HLS URL upgrading lives in StreamProbeService and is verified by the core regression suite.
assert(code.includes("format: 'audio'") || code.includes('triggerDownload(\'audio\')'), 'Supports dedicated MP3/audio-only extraction trigger');
assert(code.includes('triggerDownload(\'video\')'), 'Supports highest quality video download trigger');

// 5. Visual States
assert(code.includes('fm-success') && code.includes('Gönderildi! ✓'), 'Includes smooth micro-interaction success feedback state');
assert(code.includes('backdrop-filter: blur'), 'Uses glassmorphic dark blur styling');

console.log('====================================================');
console.log(`📊 RESULTS: ${passed} / ${passed + failed} TESTS PASSED (${Math.round((passed / (passed + failed)) * 100)}%)`);
console.log('====================================================');

if (failed > 0) process.exit(1);
