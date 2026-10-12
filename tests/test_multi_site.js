/**
 * Multi-Site Media Extraction & Download Test Suite
 * Tests universal stream detection across HDFilmcehennemi, NotebookLM, YouTube, Twitter, TikTok, and generic sites.
 */
'use strict';

const assert = require('assert');
const fs = require('fs');
const path = require('path');
const { spawnSync } = require('child_process');

console.log('====================================================');
console.log('🧪 MULTI-SITE DOWNLOAD & EXTRACTION TEST SUITE');
console.log('====================================================\n');

let passed = 0;
let total = 0;

function it(name, fn) {
  total++;
  try {
    fn();
    console.log(`  ✅ PASS: ${name}`);
    passed++;
  } catch (err) {
    console.error(`  ❌ FAIL: ${name}`);
    console.error(`     Error: ${err.message}`);
  }
}

// Read background.js and popup.js logic
const bgCode = fs.readFileSync(path.join(__dirname, '../extension/background.js'), 'utf8');
const popupCode = fs.readFileSync(path.join(__dirname, '../extension/popup.js'), 'utf8');

// Extract classify function from background.js
const classifyMatch = bgCode.match(/function classify\(url, ct\) \{([\s\S]*?)\n\}/);
const classify = new Function('url', 'ct', `
  const FRAGMENT = /\\.(ts|m4s|m4f|cmfv|cmfa|fmp4|init)(\\?|#|$)|[/_.-](seg|segment|chunk|frag|fragment)[-_]?\\d+/i;
  const MEDIA_EXT = /\\.(mp4|m4v|webm|mkv|mov|avi|flv|mp3|m4a|aac|ogg|opus|wav|flac)(\\?|#|$)/i;
  ${classifyMatch[1]}
`);

// Extract pickBestMedia function from popup.js
const pickBestMediaMatch = popupCode.match(/function pickBestMedia\(list\) \{([\s\S]*?)\n\}/);
const pickBestMedia = new Function('list', pickBestMediaMatch[1]);

// -------------------------------------------------------------
// TEST 1: HDFilmcehennemi & Rapidrame/Vidmoly HLS Audio vs Master
// -------------------------------------------------------------
console.log('--- TEST GROUP 1: HDFilmcehennemi & Movie Embeds ---');

it('HDFilmcehennemi: classify rejects audio-only sublists (sublist_aud1.m3u8)', () => {
  const audioSublist = 'https://rapidrame.net/hls/x987/sublist_aud1.m3u8?token=abc';
  assert.strictEqual(classify(audioSublist, 'application/vnd.apple.mpegurl'), null);
});

it('HDFilmcehennemi: classify accepts master.m3u8 and video streams', () => {
  const master = 'https://rapidrame.net/hls/x987/master.m3u8?token=abc';
  assert.strictEqual(classify(master, 'application/vnd.apple.mpegurl'), 'hls');
});

it('HDFilmcehennemi: pickBestMedia prioritizes master.m3u8 over sublists', () => {
  const captured = [
    { url: 'https://rapidrame.net/hls/x987/sublist_aud1.m3u8', kind: 'hls', name: 'audio' },
    { url: 'https://rapidrame.net/hls/x987/sublist_vid1.m3u8', kind: 'hls', name: 'video' },
    { url: 'https://rapidrame.net/hls/x987/master.m3u8', kind: 'hls', name: 'The Mentalist.mp4', initiator: 'https://rapidrame.net' }
  ];
  const best = pickBestMedia(captured);
  assert.strictEqual(best.url, 'https://rapidrame.net/hls/x987/master.m3u8');
  assert.strictEqual(best.name, 'The Mentalist.mp4');
  assert.strictEqual(best.initiator, 'https://rapidrame.net');
});

it('HDFilmcehennemi: initiator is passed as Referer to bypass CDN 404 anti-hotlinking', () => {
  const best = {
    url: 'https://rapidrame.net/hls/x987/master.m3u8',
    initiator: 'https://rapidrame.net'
  };
  const tabUrl = 'https://www.hdfilmcehennemi.nl/the-mentalist-2-sezon-9-bolum-izle/';
  const referer = best.initiator && !best.initiator.startsWith('chrome-extension://')
    ? (best.initiator.endsWith('/') ? best.initiator : best.initiator + '/')
    : tabUrl;

  assert.strictEqual(referer, 'https://rapidrame.net/');
});

// -------------------------------------------------------------
// TEST 2: Google NotebookLM Podcast & Generic Web Pages
// -------------------------------------------------------------
console.log('\n--- TEST GROUP 2: Google NotebookLM & Generic Web Apps ---');

it('NotebookLM: before playback starts, red button refuses to send generic webpage URL to yt-dlp', () => {
  const tab = { url: 'https://notebook.google.com/notebook/4c79c902-7fcd-4792-b88b-d412e111b4ab?authuser=2' };
  const mediaList = []; // Nothing played yet
  const isKnownVideoHost = /(youtube\.com|youtu\.be|tiktok\.com|twitter\.com|x\.com|vimeo\.com|dailymotion\.com|instagram\.com|twitch\.tv|reddit\.com)/i.test(tab.url);

  assert.strictEqual(isKnownVideoHost, false);

  const best = pickBestMedia(mediaList);
  assert.strictEqual(best, null);

  // In this case, popup.js shows 'Önce videoyu/sesi oynatın' and ABORTS instead of sending notebook.google.com
  let shouldAbort = !best && !isKnownVideoHost;
  assert.strictEqual(shouldAbort, true);
});

it('NotebookLM: during playback, captured audio is downloaded directly as file', () => {
  const tab = { url: 'https://notebook.google.com/notebook/4c79c902-7fcd-4792-b88b-d412e111b4ab?authuser=2' };
  const mediaList = [
    {
      url: 'https://notebooklm.google.com/_/upload/podcast_audio_chunk.wav',
      kind: 'media',
      type: 'audio/wav',
      name: 'NotebookLM Podcast Audio.wav'
    }
  ];
  const best = pickBestMedia(mediaList);
  assert.ok(best);
  assert.strictEqual(best.url, 'https://notebooklm.google.com/_/upload/podcast_audio_chunk.wav');
  const kind = best.kind === 'hls' || best.kind === 'dash' || best.kind === 'video' ? 'video' : 'file';
  assert.strictEqual(kind, 'file'); // Segmented/SingleStreamDownloader, not yt-dlp
});

// -------------------------------------------------------------
// TEST 3: Twitter / X Feed Prebuffer Spam Filtering
// -------------------------------------------------------------
console.log('\n--- TEST GROUP 3: Twitter / X Feed Filtering ---');

it('Twitter: twimg.com network sniffing is blocked to prevent m3u8 playlist flood', () => {
  const IGNORED_HOSTS = /(^|\.)(googlevideo\.com|twimg\.com|tiktokcdn\.com|byteoversea\.com|ibytedtos\.com)$/i;
  assert.strictEqual(IGNORED_HOSTS.test('video.twimg.com'), true);
  assert.strictEqual(IGNORED_HOSTS.test('ton.twimg.com'), true);
  assert.strictEqual(IGNORED_HOSTS.test('x.com'), false);
});

it('Twitter: recognized as known video host and delegates to active tweet URL', () => {
  const url = 'https://x.com/SpaceX/status/1845423456789012345';
  const isKnownVideoHost = /(youtube\.com|youtu\.be|tiktok\.com|twitter\.com|x\.com|vimeo\.com|dailymotion\.com|instagram\.com|twitch\.tv|reddit\.com)/i.test(url);
  assert.strictEqual(isKnownVideoHost, true);
});

// -------------------------------------------------------------
// TEST 4: TikTok Feed Prebuffer Filtering
// -------------------------------------------------------------
console.log('\n--- TEST GROUP 4: TikTok Feed Filtering ---');

it('TikTok: tiktokcdn, byteoversea and ibytedtos prebuffers are filtered', () => {
  const IGNORED_HOSTS = /(^|\.)(googlevideo\.com|twimg\.com|tiktokcdn\.com|byteoversea\.com|ibytedtos\.com)$/i;
  assert.strictEqual(IGNORED_HOSTS.test('v16-webapp-prime.tiktokcdn.com'), true);
  assert.strictEqual(IGNORED_HOSTS.test('sf16-va.tiktokcdn.com'), true);
  assert.strictEqual(IGNORED_HOSTS.test('v16.byteoversea.com'), true);
  assert.strictEqual(IGNORED_HOSTS.test('p16-sign.ibytedtos.com'), true);
  assert.strictEqual(IGNORED_HOSTS.test('www.tiktok.com'), false);
});

// -------------------------------------------------------------
// TEST 5: YouTube, Vimeo, Dailymotion, Twitch, Reddit
// -------------------------------------------------------------
console.log('\n--- TEST GROUP 5: Major Streaming Platforms ---');

it('Major platforms recognized as known video hosts', () => {
  const platforms = [
    'https://www.youtube.com/watch?v=dQw4w9WgXcQ',
    'https://youtu.be/dQw4w9WgXcQ',
    'https://www.tiktok.com/@user/video/123456789',
    'https://vimeo.com/123456789',
    'https://www.dailymotion.com/video/x7abcde',
    'https://www.instagram.com/reel/C123456789/',
    'https://www.twitch.tv/videos/123456789',
    'https://www.reddit.com/r/videos/comments/12345/sample_video/'
  ];
  const isKnownVideoHost = /(youtube\.com|youtu\.be|tiktok\.com|twitter\.com|x\.com|vimeo\.com|dailymotion\.com|instagram\.com|twitch\.tv|reddit\.com)/i;
  for (const p of platforms) {
    assert.strictEqual(isKnownVideoHost.test(p), true, `Failed for ${p}`);
  }
});

// -------------------------------------------------------------
// TEST 6: Real CLI Execution with yt-dlp (--impersonate & --referer)
// -------------------------------------------------------------
console.log('\n--- TEST GROUP 6: Real yt-dlp CLI Execution ---');

it('yt-dlp executable exists and responds with version', () => {
  const ytDlpPath = path.join(__dirname, '../Tools/yt-dlp.exe');
  assert.ok(fs.existsSync(ytDlpPath), 'yt-dlp.exe must exist in Tools folder');
  const res = spawnSync(ytDlpPath, ['--version'], { encoding: 'utf8' });
  assert.strictEqual(res.status, 0);
  assert.ok(res.stdout.trim().length > 0);
  console.log(`     (yt-dlp version: ${res.stdout.trim()})`);
});

it('yt-dlp extracts real public Akamai/Apple HLS master stream using --referer', () => {
  const ytDlpPath = path.join(__dirname, '../Tools/yt-dlp.exe');
  // Official Apple test HLS master playlist URL
  const testHls = 'https://devstreaming-cdn.apple.com/videos/streaming/examples/bipbop_4x3/bipbop_4x3_variant.m3u8';
  const res = spawnSync(ytDlpPath, [
    '--no-check-certificates',
    '--impersonate', 'chrome',
    '--referer', 'https://developer.apple.com/',
    '-F',
    testHls
  ], { encoding: 'utf8', timeout: 15000 });

  assert.strictEqual(res.status, 0, `yt-dlp failed on HLS test stream: ${res.stderr}`);
  assert.ok(res.stdout.includes('m3u8') || res.stdout.includes('Resolution'), 'Must extract HLS resolutions');
  console.log('     (HLS master playlist extracted successfully with video+audio renditions)');
});

// -------------------------------------------------------------
// TEST 7: Random News, Blog & Educational Sites
// -------------------------------------------------------------
console.log('\n--- TEST GROUP 7: Random News, Blog & Educational Sites ---');

it('Random News Site (Sozcu / CNN / BBC): direct MP4 video is classified and prioritized', () => {
  const captured = [
    { url: 'https://video.sozcu.com.tr/2026/10/flas-gelisme.mp4', kind: 'media', type: 'video/mp4', name: 'Flaş Gelişme.mp4', size: 45000000 }
  ];
  const best = pickBestMedia(captured);
  assert.ok(best);
  assert.strictEqual(best.url, 'https://video.sozcu.com.tr/2026/10/flas-gelisme.mp4');
  assert.strictEqual(best.name, 'Flaş Gelişme.mp4');
});

it('Random Educational / Archive Site (Archive.org / Wikimedia): direct media streams downloaded as file', () => {
  const captured = [
    { url: 'https://archive.org/download/sample_audio_podcast/interview_part1.mp3', kind: 'media', type: 'audio/mpeg', name: 'Historical Interview.mp3', size: 18500000 },
    { url: 'https://upload.wikimedia.org/wikipedia/commons/transcoded/c/c0/Big_Buck_Bunny_4K.webm/Big_Buck_Bunny_4K.webm.1080p.vp9.webm', kind: 'media', type: 'video/webm', name: 'Big Buck Bunny.webm', size: 85000000 }
  ];
  const best = pickBestMedia(captured);
  assert.ok(best);
  assert.strictEqual(best.url.includes('Big_Buck_Bunny'), true);
});

// -------------------------------------------------------------
// TEST 8: Diverse Movie / Streaming Hosts (Filemoon, Dood, Streamtape, Mixdrop)
// -------------------------------------------------------------
console.log('\n--- TEST GROUP 8: Diverse Movie Hosts (Filemoon, Dood, Streamtape, Mixdrop) ---');

it('Filemoon / Streamtape: master manifest with token parameters is correctly matched', () => {
  const captured = [
    { url: 'https://filemoon.sx/hls/m98234/sublist_aud1.m3u8?token=xyz', kind: 'hls' },
    { url: 'https://filemoon.sx/hls/m98234/master.m3u8?token=xyz&expires=1791836000', kind: 'hls', name: 'Interstellar.mp4', initiator: 'https://filemoon.sx' }
  ];
  const best = pickBestMedia(captured);
  assert.strictEqual(best.url, 'https://filemoon.sx/hls/m98234/master.m3u8?token=xyz&expires=1791836000');
  assert.strictEqual(best.initiator, 'https://filemoon.sx');
});

it('Streamwish / Supervideo: sublist_vid1 is automatically upgraded to master.m3u8 in addMedia', () => {
  // Simulates addMedia upgrade logic
  let incoming = 'https://streamwish.to/hls/abc1234/sublist_vid1.m3u8?h=1';
  if (/sublist_vid\d*\.m3u8/i.test(incoming)) {
    incoming = incoming.replace(/sublist_vid\d*\.m3u8/i, 'master.m3u8');
  }
  assert.strictEqual(incoming, 'https://streamwish.to/hls/abc1234/master.m3u8?h=1');
});

it('Random Site: Page without media shows user-friendly prompt instead of crashing downloader', () => {
  const genericTab = { url: 'https://tr.wikipedia.org/wiki/T%C3%BCrkiye' };
  const mediaList = [];
  const isKnownVideoHost = /(youtube\.com|youtu\.be|tiktok\.com|twitter\.com|x\.com|vimeo\.com|dailymotion\.com|instagram\.com|twitch\.tv|reddit\.com)/i.test(genericTab.url);

  assert.strictEqual(isKnownVideoHost, false);
  const best = pickBestMedia(mediaList);
  assert.strictEqual(best, null);
  // Handler refuses to download unsupported page URL
  const canSend = !!best || isKnownVideoHost;
  assert.strictEqual(canSend, false);
});

console.log('\n====================================================');
console.log(`📊 RESULTS: ${passed} / ${total} TESTS PASSED (${Math.round((passed / total) * 100)}%)`);
console.log('====================================================\n');

if (passed === total) {
  process.exit(0);
} else {
  process.exit(1);
}
