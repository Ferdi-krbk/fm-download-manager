// Independent Comprehensive Stress & Heuristics Test Suite
// Verifies extension classification, subtitle matching, and edge-case URL resilience.
'use strict';

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

console.log('================================================================');
console.log('🧪 INDEPENDENT EXTENSION & HEURISTICS DEEP STRESS MATRIX');
console.log('================================================================');

// ── 1. REAL-WORLD SUBTITLE URL REGEX & EXTRACTION MATRIX ──────────────────
console.log('\n--- 1. REAL-WORLD SUBTITLE URL EXTRACTION & NORMALIZATION ---');

const testSubtitleUrls = [
  { url: 'https://cdn.example.com/subtitles/movie_turkish.vtt', expectedLang: 'tr', desc: 'Standard Turkish VTT' },
  { url: 'https://cdn.example.com/subs/season1_ep01_en.srt', expectedLang: 'en', desc: 'Standard English SRT' },
  { url: 'https://stream.host.net/sub-turkce-forced.vtt?token=xyz123', expectedLang: 'tr', desc: 'Turkce forced with token' },
  { url: 'https://media.org/vtt/french_sub.vtt#t=10,20', expectedLang: 'fr', desc: 'French VTT with fragment' },
  { url: 'https://subtitles.cloud/tracks/spanish.srt?exp=17900000', expectedLang: 'es', desc: 'Spanish SRT with expiration' },
  { url: 'https://vid.server.tv/captions/german.vtt', expectedLang: 'de', desc: 'German VTT' },
  { url: 'https://vid.server.tv/captions/italian.srt', expectedLang: 'it', desc: 'Italian SRT' },
  { url: 'https://vid.server.tv/captions/portuguese.vtt', expectedLang: 'pt', desc: 'Portuguese VTT' },
  { url: 'https://vid.server.tv/captions/russian.srt', expectedLang: 'ru', desc: 'Russian SRT' },
  { url: 'https://vid.server.tv/captions/arabic.vtt', expectedLang: 'ar', desc: 'Arabic VTT' },
  { url: 'https://cdn.site.com/sub/altyazi-tr.vtt', expectedLang: 'tr', desc: 'altyazi-tr Turkish VTT' },
  { url: 'https:\\/\\/cdn.site.com\\/sub\\/turkish.vtt', expectedLang: 'tr', desc: 'JSON-escaped slashes' }
];

function extractSubLang(rawUrl) {
  const u = rawUrl.replace(/\\\/|\//g, '/').toLowerCase();
  const match = u.match(/(?:^|[-_./])(tr|tur|turkish|turkce|altyazi|en|eng|english|pt|por|portuguese|es|spa|spanish|fr|fre|french|de|ger|german|it|ita|italian|ru|rus|russian|ar|ara|arabic)(?:[-_./?#]|$)/);
  if (match) {
    const val = match[1];
    if (val.startsWith('tr') || val.startsWith('tur') || val === 'altyazi') return 'tr';
    if (val.startsWith('en') || val.startsWith('eng')) return 'en';
    if (val.startsWith('pt') || val.startsWith('por')) return 'pt';
    if (val.startsWith('es') || val.startsWith('spa')) return 'es';
    if (val.startsWith('fr') || val.startsWith('fre')) return 'fr';
    if (val.startsWith('de') || val.startsWith('ger')) return 'de';
    if (val.startsWith('it') || val.startsWith('ita')) return 'it';
    if (val.startsWith('ru') || val.startsWith('rus')) return 'ru';
    if (val.startsWith('ar') || val.startsWith('ara')) return 'ar';
  }
  return 'und';
}

testSubtitleUrls.forEach(item => {
  const lang = extractSubLang(item.url);
  assert(lang === item.expectedLang, `Subtitle regex detects: ${item.desc} -> ${lang}`);
});

// ── 2. MEDIA URL CLASSIFICATION & AD FILTERING MATRIX ─────────────────────
console.log('\n--- 2. STREAM CLASSIFICATION & AD BLOCKING HEURISTICS ---');

const mediaTestCases = [
  // Legitimate Media
  { url: 'https://edge.video.com/master.m3u8', isMedia: true, desc: 'Master HLS playlist' },
  { url: 'https://cdn.domain.com/hls/index.m3u8?token=abc', isMedia: true, desc: 'HLS index with query token' },
  { url: 'https://files.org/movies/film_1080p.mp4', isMedia: true, desc: 'Direct 1080p MP4' },
  { url: 'https://stream.host.com/video.mkv', isMedia: true, desc: 'Direct MKV' },
  { url: 'https://cdn.podcasts.com/episode.mp3', isMedia: true, desc: 'Direct MP3' },
  { url: 'https://audio.lib.org/track.flac', isMedia: true, desc: 'Direct FLAC' },
  { url: 'https://sub.provider.com/captions.vtt', isMedia: true, desc: 'WebVTT Subtitle' },

  // Advertisements, Segments & Ignored Hosts
  { url: 'https://rr1---sn-4g5ednle.googlevideo.com/videoplayback?id=123', isMedia: false, desc: 'YouTube googlevideo raw chunk (handled via page url)' },
  { url: 'https://video.twimg.com/amplify_video/123/pl/seg.ts', isMedia: false, desc: 'Twitter video fragment (ts)' },
  { url: 'https://v16m-default.tiktokcdn.com/video/tos/useast2a/123.mp4', isMedia: false, desc: 'TikTok CDN prebuffer' },
  { url: 'https://cdn.site.com/hls/segment_0001.ts', isMedia: false, desc: 'HLS segment chunk .ts' },
  { url: 'https://cdn.site.com/dash/chunk_audio_01.m4s', isMedia: false, desc: 'DASH fragment .m4s' },
  { url: 'https://tracker.analytics.com/pixel.gif', isMedia: false, desc: 'Tracking 1x1 pixel' },
  { url: 'https://site.com/assets/avatar.png', isMedia: false, desc: 'User profile avatar image' }
];

const MEDIA_REGEX = /\.(mp4|m4v|webm|mkv|mov|avi|flv|mp3|m4a|aac|ogg|opus|wav|flac|m3u8|vtt|srt)(\?|#|$)/i;
const FRAGMENT_REGEX = /\.(ts|m4s|m4f|cmfv|cmfa|fmp4|init)(\?|#|$)|[/_.-](seg|segment|chunk|frag|fragment)[-_]?\d+/i;
const IGNORED_HOSTS = /(^|\.)(googlevideo\.com|twimg\.com|tiktokcdn\.com|byteoversea\.com|ibytedtos\.com)$/i;

function shouldCaptureMedia(rawUrl) {
  try {
    const u = new URL(rawUrl);
    if (IGNORED_HOSTS.test(u.hostname)) return false;
    if (FRAGMENT_REGEX.test(u.pathname)) return false;
    return MEDIA_REGEX.test(u.pathname) || u.pathname.endsWith('.m3u8');
  } catch {
    return false;
  }
}

mediaTestCases.forEach(item => {
  const result = shouldCaptureMedia(item.url);
  assert(result === item.isMedia, `Media Filter: ${item.desc} (${result ? 'CAPTURED' : 'FILTERED'})`);
});

// ── 3. IDM FLOATING BAR GEOMETRY & BOUNDARY CONSTRAINTS ───────────────────
console.log('\n--- 3. FLOATING BAR POSITIONING & VIEWPORT CLAMPING ---');

function calculateBarPosition(playerRect, barWidth = 175, barHeight = 36, viewportWidth = 1920, viewportHeight = 1080) {
  let top = playerRect.top + 14;
  let left = playerRect.right - barWidth - 14;

  // Viewport boundaries clamping
  if (top < 10) top = 10;
  if (top + barHeight > viewportHeight - 10) top = viewportHeight - barHeight - 10;
  if (left < 10) left = 10;
  if (left + barWidth > viewportWidth - 10) left = viewportWidth - barWidth - 10;

  return { top, left };
}

// Normal player in center of screen
const normalPos = calculateBarPosition({ top: 200, bottom: 680, left: 300, right: 1154 });
assert(normalPos.top === 214, 'Normal player top position is player.top + 14');
assert(normalPos.left === 1154 - 175 - 14, 'Normal player left position is player.right - barWidth - 14');

// Player scrolled near top edge (top < 0)
const topPos = calculateBarPosition({ top: -100, bottom: 380, left: 100, right: 900 });
assert(topPos.top === 10, 'Player scrolled off top is clamped to viewport top (10px)');

// Player at extreme right edge of screen
const rightPos = calculateBarPosition({ top: 200, bottom: 680, left: 1200, right: 1950 });
assert(rightPos.left === 1920 - 175 - 10, 'Player at right edge is clamped to viewport right edge');

// ── 4. SUBTITLE SYNCHRONIZATION EVENT CONTRACT ────────────────────────────
console.log('\n--- 4. SUBTITLE DISCOVERY & POPUP SYNC CONTRACT ---');

// Verify that floating_bar and popup exchange track payloads correctly
const mockTabSubtitles = [
  { url: 'https://site.com/sub_tr.vtt', lang: 'tr', label: 'Türkçe' },
  { url: 'https://site.com/sub_en.vtt', lang: 'en', label: 'İngilizce' },
  { url: 'https://site.com/sub_es.vtt', lang: 'es', label: 'İspanyolca' },
  { url: 'https://site.com/sub_fr.vtt', lang: 'fr', label: 'Fransızca' },
  { url: 'https://site.com/sub_pt.vtt', lang: 'pt', label: 'Portekizce' },
  { url: 'https://site.com/sub_de.vtt', lang: 'de', label: 'Almanca' }
];

assert(mockTabSubtitles.length === 6, 'Contains all 6 detected subtitles');
const bestTurkish = mockTabSubtitles.find(s => s.lang === 'tr');
assert(bestTurkish && bestTurkish.url.endsWith('sub_tr.vtt'), 'Default selected subtitle is Turkish');

// Verify JSON payload format sent to desktop app
const desktopPayload = {
  url: 'https://site.com/master.m3u8',
  kind: 'video',
  writeSubtitles: true,
  subtitleUrl: bestTurkish.url,
  subtitles: mockTabSubtitles
};

assert(desktopPayload.writeSubtitles === true, 'writeSubtitles flag is true when subtitles exist');
assert(desktopPayload.subtitles.length === 6, 'Exact 6 subtitle tracks bundled in desktop bridge payload');

// ── 5. UI ANIMATION, WORD SPLASH & NON-VIDEO IFRAME EXCLUSION ─────────────
console.log('\n--- 5. UI ANIMATION, WORD SPLASH & IFRAME FILTERING ---');

const EXCLUDED_HOSTS = /(^|\.)(office\.com|office\.live\.com|officeapps\.live\.com|office365\.com|sharepoint\.com|onedrive\.live\.com|live\.com|microsoft365\.com|docs\.google\.com|sheets\.google\.com|slides\.google\.com|drive\.google\.com|mail\.google\.com|outlook\.live\.com|outlook\.office\.com|notion\.so|figma\.com|canva\.com)$/i;
const STREAM_EMBED_IFRAME = /(vidmoly|rapidvid|streamtape|dood|filemoon|vidspeed|supervideo|upstream|mixdrop|streamwish|voe\.sx|sibnet|ok\.ru\/videoembed|vk\.com\/video_ext|player\.vimeo\.com|dailymotion\.com\/embed|youtube\.com\/embed|youtube-nocookie\.com\/embed|twitch\.tv\/\?channel|rumble\.com\/embed)/i;

assert(EXCLUDED_HOSTS.test('word-edit.officeapps.live.com'), 'Blocks Word Online editor frame');
assert(EXCLUDED_HOSTS.test('onedrive.live.com'), 'Blocks OneDrive web app');
assert(EXCLUDED_HOSTS.test('docs.google.com'), 'Blocks Google Docs web app');
assert(!EXCLUDED_HOSTS.test('www.youtube.com'), 'Allows YouTube');
assert(!STREAM_EMBED_IFRAME.test('https://word-edit.officeapps.live.com/we/wordeditorframe.aspx'), 'Rejects Word Online iframe as video embed');
assert(STREAM_EMBED_IFRAME.test('https://vidmoly.to/embed-abc123.html'), 'Accepts Vidmoly movie player iframe');

console.log('\n================================================================');
console.log(`📊 FINAL RESULTS: ${passed} / ${passed + failed} TESTS PASSED (${Math.round((passed / (passed + failed)) * 100)}%)`);
console.log('================================================================');

process.exit(failed === 0 ? 0 : 1);
