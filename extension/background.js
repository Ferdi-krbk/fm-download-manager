// MC Download Manager bridge extension — background script (Chrome MV3 / Safari / Firefox)
'use strict';

const api = typeof browser !== 'undefined' ? browser : chrome;
const t = (k, subs) => { try { return api.i18n.getMessage(k, subs) || k; } catch { return k; } };
// Firefox (and its forks) serve extension pages from moz-extension:// URLs; works in content scripts too.
const IS_FIREFOX = typeof browser !== 'undefined' && String(browser.runtime.getURL('')).startsWith('moz-extension:');
const IS_SAFARI = !IS_FIREFOX && typeof browser !== 'undefined' && !!browser.runtime.sendNativeMessage && !chrome.downloads?.onDeterminingFilename;

const DEFAULTS = { port: 48213, intercept: true, minSizeMB: 0, sniff: true, autoLaunch: true };
// Native messaging host bundled with the app (Chrome/Brave/Edge/Firefox); Safari talks to its app extension.
const NATIVE_HOST = 'com.mesutcevik.mcdownloadmanager';
const BROWSER = IS_FIREFOX ? 'firefox' : IS_SAFARI ? 'safari' : (navigator.brave ? 'brave' : (/Edg\//.test(navigator.userAgent) ? 'edge' : 'chrome'));

const MEDIA_EXT = /\.(mp4|m4v|webm|mkv|mov|avi|flv|mp3|m4a|aac|ogg|opus|wav|flac)(\?|#|$)/i;
const FRAGMENT = /\.(ts|m4s|m4f|cmfv|cmfa|fmp4|init)(\?|#|$)|[/_.-](seg|segment|chunk|frag|fragment)[-_]?\d+/i;
const IGNORED_HOSTS = /(^|\.)(googlevideo\.com|twimg\.com|tiktokcdn\.com|byteoversea\.com|ibytedtos\.com)$/i;

// ---------- Settings ----------
async function getSettings() {
  const s = await api.storage.local.get('settings');
  return { ...DEFAULTS, ...(s.settings || {}) };
}
async function setSettings(patch) {
  const cur = await getSettings();
  await api.storage.local.set({ settings: { ...cur, ...patch } });
}
const base = (s) => `http://127.0.0.1:${s.port}`;
// Custom header = proof the request comes from an extension: ordinary web pages cannot send it
// without a CORS preflight, and the bridge only grants CORS to extension origins. Chrome omits the
// Origin header on extension GET requests that are covered by host permissions, so Origin alone is not enough.
// The id lets the app tell which extension is calling.
const BRIDGE_ID_HEADERS = { 'X-MCDM-Extension': BROWSER, 'X-MCDM-Extension-Id': api.runtime.id };
// Windows port: every call except OPTIONS and an anonymous /ping must carry the pairing token (X-MCDM-Token)
// that the app shows in "Browser setup"; the user pastes it once in the popup.
async function getToken() {
  const s = await api.storage.local.get('token');
  if (s.token) return s.token;
  try {
    const res = await fetch(api.runtime.getURL('token.json'));
    if (res.ok) {
      const data = await res.json();
      if (data && data.token) {
        await api.storage.local.set({ token: data.token });
        return data.token;
      }
    }
  } catch {}
  return '';
}
async function bridgeHeaders(extra) {
  const token = await getToken();
  return { ...BRIDGE_ID_HEADERS, ...(token ? { 'X-MCDM-Token': token } : {}), ...(extra || {}) };
}

// ---------- App communication ----------
// Raw /ping answer: reachable app, paired or not (unpaired answers only {name, platform, version, paired:false}).
async function pingRaw(settings) {
  settings = settings || (await getSettings());
  try {
    const ctrl = new AbortController();
    const t = setTimeout(() => ctrl.abort(), 1200);
    const r = await fetch(base(settings) + '/ping?browser=' + BROWSER, { signal: ctrl.signal, cache: 'no-store', headers: await bridgeHeaders() });
    clearTimeout(t);
    if (!r.ok) return null;
    const resData = await r.json();
    if (resData) {
      const updates = {};
      if (resData.token) updates.token = resData.token;
      if (resData.accentColor) updates.accentColor = resData.accentColor;
      if (resData.language) updates.language = resData.language;
      if (Object.keys(updates).length) api.storage.local.set(updates);
    }
    return resData;
  } catch {
    return null;
  }
}
// Usable app: reachable AND paired. Anything that hands a download over must use this, otherwise the browser
// would cancel its own download for an app that is going to refuse it.
async function ping(settings) {
  const app = await pingRaw(settings);
  if (!app || !app.paired) return null;
  if (app.active > 0) startProgress();
  if (Array.isArray(app.captureExtensions)) api.storage.local.set({ captureExtensions: app.captureExtensions });
  return app;
}

// ---------- Download progress: toolbar badge + popup list ----------
async function bridgeJSON(path, init) {
  const settings = await getSettings();
  const r = await fetch(base(settings) + path, { cache: 'no-store', ...init, headers: await bridgeHeaders(init?.headers) });
  return r.json();
}
async function getDownloads() {
  try { const j = await bridgeJSON('/downloads'); return (j && j.items) || []; } catch { return []; }
}
async function downloadAction(id, action) {
  try {
    const j = await bridgeJSON('/action', { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ id, action }) });
    tickProgress();
    return j;
  } catch (e) { return { ok: false, error: String(e) }; }
}
const ACTIVE_STATES = new Set(['downloading', 'probing']);
let progressTimer = null;
let badgeShown = false;
async function setGlobalBadge(text, color) {
  try { await api.action.setBadgeText({ text }); if (color) await api.action.setBadgeBackgroundColor({ color }); } catch {}
}
// Per-tab media counts override the global badge; while downloads run, every tab falls back to the global text.
async function useGlobalBadgeOnAllTabs(on) {
  try {
    const tabs = await api.tabs.query({});
    for (const tab of tabs) {
      if (on) { try { await api.action.setBadgeText({ tabId: tab.id, text: null }); } catch {} }
      else { const list = await readMedia(tab.id); try { await api.action.setBadgeText({ tabId: tab.id, text: list.length ? String(list.length) : '' }); } catch {} }
    }
  } catch {}
}
async function tickProgress() {
  const list = await getDownloads();
  const active = list.filter((i) => ACTIVE_STATES.has(i.state));
  if (active.length) {
    const measurable = active.filter((i) => i.total > 0);
    let text = '…';
    if (active.length > 1) text = active.length + '↓';
    else if (measurable.length) {
      const total = measurable.reduce((a, i) => a + i.total, 0);
      const got = measurable.reduce((a, i) => a + Math.min(i.received, i.total), 0);
      text = Math.round((100 * got) / total) + '%';
    }
    if (!badgeShown) { badgeShown = true; await useGlobalBadgeOnAllTabs(true); }
    await setGlobalBadge(text, '#2f6feb');
  } else {
    stopProgress();
    if (badgeShown) {
      badgeShown = false;
      await setGlobalBadge('✓', '#2ebd59');
      setTimeout(async () => { await setGlobalBadge('', '#2f6feb'); await useGlobalBadgeOnAllTabs(false); }, 4000);
    }
  }
}
function startProgress() {
  if (progressTimer) return;
  tickProgress();
  progressTimer = setInterval(tickProgress, 1500);  // each fetch keeps the MV3 worker alive while something runs
}
function stopProgress() {
  if (progressTimer) { clearInterval(progressTimer); progressTimer = null; }
}

async function cookiesFor(url) {
  try {
    const list = await api.cookies.getAll({ url });
    return {
      header: list.map((c) => `${c.name}=${c.value}`).join('; '),
      list: list.map((c) => ({
        name: c.name, value: c.value, domain: c.domain, path: c.path,
        secure: !!c.secure, expirationDate: c.expirationDate || 0,
      })),
    };
  } catch {
    return { header: '', list: [] };
  }
}

function notify(title, message) {
  try {
    api.notifications?.create?.('', {
      type: 'basic', iconUrl: 'icons/icon128.png', title, message: String(message || '').slice(0, 200),
    });
  } catch {}
}

async function sendToApp({ url, filename, referer, kind, format, audioOnly, subtitlesOnly, subtitleLangs, writeSubtitles, subtitleUrl, subtitles, videoQualities, audioTracks }) {
  const settings = await getSettings();
  const ck = await cookiesFor(url);
  const isAudio = audioOnly === true || format === 'audio';
  const isSubs = subtitlesOnly === true || format === 'subtitles';

  let chosenSub = subtitleUrl || '';
  if (!chosenSub && subtitles && subtitles.length > 0) {
    const nonForcedTr = subtitles.find(s => (s.code === 'tr' || s.lang === 'tr') && !/forced/i.test(s.url || '') && !/forced/i.test(s.label || ''));
    chosenSub = nonForcedTr?.url || subtitles[0]?.url || '';
  }

  let subtitleContent = '';
  if ((isSubs || writeSubtitles) && chosenSub && /^https?:/i.test(chosenSub)) {
    try {
      const subRes = await fetch(chosenSub, {
        headers: {
          'Referer': referer || '',
          'Accept': 'text/vtt,text/plain,*/*'
        },
        credentials: 'include',
        signal: AbortSignal.timeout ? AbortSignal.timeout(3000) : undefined
      });
      if (subRes.ok) {
        subtitleContent = await subRes.text();
      }
    } catch (e) {
      console.warn('[FM] Pre-fetch subtitle failed in extension:', e);
    }
  }

  let mergedCookieHeader = ck.header || '';
  let mergedCookies = ck.list ? [...ck.list] : [];
  if (chosenSub && /^https?:/i.test(chosenSub)) {
    try {
      const subCk = await cookiesFor(chosenSub);
      if (subCk?.header) {
        mergedCookieHeader = (mergedCookieHeader ? mergedCookieHeader + '; ' : '') + subCk.header;
      }
      if (subCk?.list?.length) {
        mergedCookies.push(...subCk.list);
      }
    } catch {}
  }

  let cleanName = (filename || '').trim();
  if (kind === 'video') {
    if (isGenericMediaName(cleanName)) {
      try {
        const tabs = await api.tabs.query({ active: true, currentWindow: true });
        if (tabs[0]?.title) {
          const cleaned = cleanMediaTitle(tabs[0].title);
          if (cleaned) cleanName = cleaned;
        }
      } catch {}
    }
    if (!cleanName || isGenericMediaName(cleanName)) cleanName = 'Video';
  } else {
    // Normal file: NEVER overwrite real filename with browser tab title!
    // If filename is generic or meaningless, clear it so desktop app probes real Content-Disposition/URL
    if (isGenericMediaName(cleanName)) {
      cleanName = '';
    }
  }

  const payload = {
    url, filename: cleanName, referer: referer || '', kind: kind || 'file',
    audioOnly: isAudio,
    subtitlesOnly: isSubs,
    subtitleLangs: subtitleLangs || 'tr,en',
    writeSubtitles: writeSubtitles === true,
    subtitleUrl: chosenSub || subtitleUrl || '',
    subtitles: subtitles || [],
    subtitleContent: subtitleContent || '',
    videoQualities: videoQualities || [],
    audioTracks: audioTracks || [],
    userAgent: navigator.userAgent, cookieHeader: mergedCookieHeader, cookies: mergedCookies,
  };

  const post = async () => fetch(base(settings) + '/download', {
    method: 'POST', headers: await bridgeHeaders({ 'Content-Type': 'application/json' }), body: JSON.stringify(payload),
  });

  let r;
  try {
    try {
      r = await post();
    } catch (e) {
      if (settings.autoLaunch) {
        const launched = await ensureApp(settings);
        if (launched) r = await post();
      }
      if (!r) {
        notify('FM Download Manager Kapalı', 'Masaüstü uygulaması çalışmıyor veya bağlantı kurulamadı.');
        return { ok: false, error: 'Masaüstü uygulaması kapalı', offline: true };
      }
    }
  } catch (errPost) {
    notify('FM Download Manager Kapalı', 'Masaüstü uygulamasına bağlanılamadı.');
    return { ok: false, error: 'Masaüstü uygulaması kapalı', offline: true };
  }

  if (r.status === 401) {
    notify('Eşleştirme Gerekli', 'Uzantı henüz FM Download Manager ile eşleştirilmemiş. Eklenti menüsünden eşleştirme kodunu girin.');
    return { ok: false, error: 'Eşleştirme gerekli (401 Unauthorized)', unauthorized: true };
  }

  if (r.status === 403) {
    notify('Erişim Reddedildi', 'Güvenlik doğrulaması başarısız oldu (403 Forbidden).');
    return { ok: false, error: 'Erişim reddedildi' };
  }

  let j = null;
  try { j = await r.json(); } catch {}

  if (r.status === 501 || (j && j.error && (j.error.includes('yt-dlp') || j.error.includes('missing')))) {
    notify('yt-dlp Bulunamadı', 'Video indirmesi için yt-dlp aracı eksik. Uygulama ayarlarından yt-dlp\'yi güncelleyin.');
    return { ok: false, error: 'yt-dlp eksik' };
  }

  if (!r.ok || (j && !j.ok)) {
    const errorMsg = j?.error || `HTTP ${r.status}`;
    if (errorMsg === 'cancelled by user') {
      return { ok: false, cancelled: true, error: 'Kullanıcı vazgeçti' };
    }
    if (r.status === 400) {
      notify('Geçersiz İndirme Talebi', errorMsg);
    } else {
      notify('İndirme Başlatılamadı', errorMsg);
    }
    return { ok: false, error: errorMsg };
  }

  const successTitle = isSubs ? 'Altyazı İndiriliyor' : (kind === 'video' ? t('notifVideoTitle') : t('notifSentTitle'));
  notify(successTitle, filename || url);
  startProgress();
  return { ok: true, id: j?.id };
}

// Launch the app through native messaging: Safari's app extension, or the host bundled with the app
// (registered by the app in each browser's NativeMessagingHosts folder).
async function launchApp() {
  if (!api.runtime.sendNativeMessage) return false;
  try {
    const r = await api.runtime.sendNativeMessage(IS_SAFARI ? 'application.id' : NATIVE_HOST, { action: 'launchApp' });
    return !!(r && r.ok);
  } catch {
    return false;
  }
}

// Returns the app's ping answer, starting the app first when it is not running (waits up to ~12 s).
let launching = null;
async function ensureApp(settings) {
  const app = await ping(settings);
  if (app) return app;
  if (!launching) {
    launching = (async () => {
      if (!(await launchApp())) return null;
      const until = Date.now() + 12000;
      while (Date.now() < until) {
        await new Promise((r) => setTimeout(r, 500));
        const a = await ping(settings);
        if (a) return a;
      }
      return null;
    })();
    launching.finally(() => { launching = null; });
  }
  return launching;
}

// ---------- Capture file-type filter (list comes from the app) ----------
function extOf(name) {
  const m = String(name || '').split(/[?#]/)[0].match(/\.([a-z0-9]{1,8})$/i);
  return m ? m[1].toLowerCase() : '';
}
function matchesCaptureTypes(app, filename, url) {
  const list = app && Array.isArray(app.captureExtensions) ? app.captureExtensions : [];
  if (!list.length || list.includes('*')) return true;
  let path = '';
  try { path = new URL(url).pathname; } catch {}
  const e = extOf(filename) || extOf(path);
  if (e === 'html' || e === 'htm' || e === 'php' || e === 'asp' || e === 'aspx' || e === 'jsp') return false;
  return !e || list.includes(e);
}

// ---------- Download interception (Chrome) ----------
const fallbackUrls = new Set();

async function interceptDownload(item) {
  const settings = await getSettings();
  if (!settings.intercept) return false;
  const url = item.finalUrl || item.url;
  if (!/^https?:/i.test(url)) return false;
  if (item.byExtensionId) return false;

  // If this download was re-triggered as a fallback because app handover failed, let browser handle it
  if (fallbackUrls.has(url)) {
    fallbackUrls.delete(url);
    return false;
  }

  if (settings.minSizeMB > 0 && item.totalBytes > 0 && item.totalBytes < settings.minSizeMB * 1024 * 1024) return false;
  let app = await ping(settings);
  if (!app && settings.autoLaunch) {
    // Only start the app for file types it would take (list remembered from its last answer).
    const { captureExtensions } = await api.storage.local.get('captureExtensions');
    if (matchesCaptureTypes({ captureExtensions }, item.filename || '', url)) app = await ensureApp(settings);
  }
  if (!app) return false;
  if (!matchesCaptureTypes(app, item.filename || '', url)) return false;
  // "large files only": the app-side minimum (Settings) and the popup's own minimum both apply
  const minMB = Math.max(settings.minSizeMB || 0, app.minSizeMB || 0);
  if (minMB > 0 && item.totalBytes > 0 && item.totalBytes < minMB * 1024 * 1024) return false;

  let filename = item.filename ? item.filename.split(/[\\/]/).pop() : '';
  if (!filename && url) {
    try {
      const u = new URL(url);
      const seg = u.pathname.split('/').filter(Boolean).pop();
      if (seg && !isGenericMediaName(seg)) {
        filename = decodeURIComponent(seg);
      }
    } catch {}
  }
  const result = await sendToApp({ url, filename, referer: item.referrer, kind: 'file' });

  if (result.ok) {
    // App accepted and started download: safely cancel and erase browser's copy
    try { await api.downloads.cancel(item.id); } catch {}
    if (IS_FIREFOX) {
      try {
        const [d] = await api.downloads.search({ id: item.id });
        if (d && d.state === 'complete' && d.exists !== false) await api.downloads.removeFile(item.id);
      } catch {}
    }
    try { await api.downloads.erase({ id: item.id }); } catch {}
    return true;
  }

  if (result.cancelled) {
    // User explicitly cancelled in the app prompt dialog: cancel browser download too
    try { await api.downloads.cancel(item.id); } catch {}
    try { await api.downloads.erase({ id: item.id }); } catch {}
    return true;
  }

  // Handover to app failed (offline, unpaired, error):
  // FALLBACK TO BROWSER DOWNLOAD: Ensure user never loses their file!
  try {
    const [d] = await api.downloads.search({ id: item.id });
    if (d && (d.state === 'interrupted' || d.paused)) {
      if (d.paused) {
        await api.downloads.resume(item.id);
      } else {
        fallbackUrls.add(url);
        await api.downloads.download({
          url,
          filename: filename || undefined,
          saveAs: false,
          conflictAction: 'uniquify'
        });
      }
    }
  } catch {}

  notify('Tarayıcıya Devredildi', `Uygulamaya aktarılamadı (${result.error || 'bağlantı hatası'}). İndirme tarayıcınızda devam ediyor.`);
  return false;
}

if (api.downloads?.onDeterminingFilename) {
  api.downloads.onDeterminingFilename.addListener((item, suggest) => {
    interceptDownload(item).then((took) => { if (!took) { try { suggest(); } catch {} } });
    return true;
  });
} else if (api.downloads?.onCreated) {
  // Firefox has no onDeterminingFilename: the browser download is already running when we hear about it.
  // Pause it at once so it does not keep downloading while the app is asked; resume it if the app declines.
  api.downloads.onCreated.addListener((item) => {
    const paused = item.state === 'in_progress' ? Promise.resolve(api.downloads.pause(item.id)).then(() => true, () => false) : Promise.resolve(false);
    interceptDownload(item).then(async (took) => {
      if (!took && (await paused)) { try { await api.downloads.resume(item.id); } catch {} }
    });
  });
}

// ---------- Media sniffing ----------
const mem = new Map(); // tabId -> Map(url -> info)   (fallback when storage.session is unavailable)

async function readMedia(tabId) {
  if (api.storage.session) {
    const key = 'media:' + tabId;
    const r = await api.storage.session.get(key);
    return r[key] || [];
  }
  return Array.from((mem.get(tabId) || new Map()).values());
}
async function writeMedia(tabId, list) {
  if (api.storage.session) {
    await api.storage.session.set({ ['media:' + tabId]: list });
  } else {
    mem.set(tabId, new Map(list.map((m) => [m.url, m])));
  }
  try {
    await api.action.setBadgeText({ tabId, text: list.length ? String(list.length) : '' });
    await api.action.setBadgeBackgroundColor({ tabId, color: '#2f6feb' });
  } catch {}
}
const tabActiveMedia = new Map(); // tabId -> media object

async function setActiveMedia(tabId, media) {
  if (!tabId || tabId < 0 || !media) return;
  tabActiveMedia.set(tabId, media);
  if (api.storage?.session) {
    try { await api.storage.session.set({ ['activeMedia:' + tabId]: media }); } catch {}
  }
}

async function getActiveMedia(tabId) {
  if (!tabId || tabId < 0) return null;
  let m = tabActiveMedia.get(tabId);
  if (!m && api.storage?.session) {
    try {
      const key = 'activeMedia:' + tabId;
      const r = await api.storage.session.get(key);
      m = r[key];
      if (m) tabActiveMedia.set(tabId, m);
    } catch {}
  }
  return m || null;
}

async function clearMedia(tabId) {
  if (api.storage?.session) {
    try {
      await api.storage.session.remove(['media:' + tabId, 'activeMedia:' + tabId]);
    } catch {}
  }
  mem.delete(tabId);
  tabActiveMedia.delete(tabId);
  try { await api.action.setBadgeText({ tabId, text: '' }); } catch {}
}

function normalizeKey(url) {
  try {
    const u = new URL(url);
    ['range', 'bytes', 'start', 'end', 'rn', 'rbuf', 'r', '_'].forEach((p) => u.searchParams.delete(p));
    return u.origin + u.pathname + (u.searchParams.toString() ? '?' + u.searchParams.toString() : '');
  } catch {
    return url;
  }
}

function isGenericMediaName(raw) {
  if (!raw) return true;
  const s = raw.trim().toLowerCase().replace(/\.[a-z0-9]{2,5}$/i, '');
  return /^(?:master|index|playlist|manifest|stream|video|audio|media|iframe|player|chunk|output|blob|embed|download|file|view|document)(?:[_-]?\d+)?$/i.test(s);
}

function cleanMediaTitle(raw) {
  if (!raw) return '';
  let s = raw.replace(/[\\/:*?"<>|]/g, '').trim();
  s = s.replace(/^(?:youtube\s*(?:video\s*player|frame)?|iframe)\s*[-–|•]?\s*/i, '');
  s = s.replace(/\s*[-–|•]\s*(?:hdfilmcehennemi|filmmodu|dizipal|fullhdfilmizlesene|dizibox|sezonlukdizi|filmevreni|youtube|netflix|blutv|gain|disney\+?).*$/i, '');
  s = s.replace(/\s+(?:izle|full izle|1080p|720p|türkçe dublaj|türkçe altyazılı|dublaj|altyazılı).*$/i, '');
  s = s.replace(/\s*[-–|•]\s*$/, '').trim();
  return s.slice(0, 60);
}

function detectQualityLabel(url) {
  const u = (url || '').toLowerCase();
  if (/4320p?|8k|-f5/i.test(u)) return '8K Ultra HD';
  if (/2160p?|4k|-f4/i.test(u)) return '4K Ultra HD';
  if (/1440p?|2k/i.test(u)) return '2K QHD';
  if (/1080p?|-f3|1080/i.test(u)) return '1080p Full HD';
  if (/720p?|-f2|720/i.test(u)) return '720p HD';
  if (/480p?|-f1-v1|480/i.test(u)) return '480p SD';
  if (/sublist_aud|audio[_-]track|-a\d+\.m3u8/i.test(u) && !/-v\d+/i.test(u)) return 'Ses Akışı';
  if (/master|playlist/i.test(u)) return 'Master Kalite';
  return '';
}

async function addMedia(tabId, info) {
  if (tabId < 0) return;
  const settings = await getSettings();
  if (!settings.sniff) return;
  const list = await readMedia(tabId);

  // Upgrade fragment streams to master.m3u8 so yt-dlp gets the highest quality video and audio
  let targetUrl = info.url;
  const ytEmbed = targetUrl.match(/(?:youtube\.com|youtube-nocookie\.com)\/embed\/([a-zA-Z0-9_-]{11})/i);
  if (ytEmbed) {
    targetUrl = `https://www.youtube.com/watch?v=${ytEmbed[1]}`;
  }

  const key = normalizeKey(targetUrl);
  const idx = list.findIndex((m) => m.key === key);
  let name = info.name || guessName(targetUrl, info.type);
  if (name) {
    name = name.replace(/^(?:youtube\s*(?:video\s*player|frame)?|iframe)\s*[-–|•]?\s*/i, '').trim();
  }
  if (!name || isGenericMediaName(name) || /^youtube$/i.test(name)) {
    try {
      const tab = await api.tabs.get(tabId);
      if (tab?.title) {
        const clean = cleanMediaTitle(tab.title);
        if (clean) {
          const ext = info.kind === 'hls' ? '.mp4' : (guessExt(info.type) || '');
          name = clean + (ext && !clean.endsWith(ext) ? ext : '');
        }
      }
    } catch {}
  }
  if (!name || isGenericMediaName(name)) name = 'Video';
  const quality = detectQualityLabel(targetUrl);
  const entry = { ...info, url: targetUrl, key, name, quality };
  if (idx >= 0) {
    if ((info.size || 0) > (list[idx].size || 0)) list[idx] = { ...list[idx], ...entry };
  } else {
    list.unshift(entry);
    if (list.length > 40) list.length = 40;
  }
  await writeMedia(tabId, list);

  if (info.kind === 'hls' && !entry.videoQualities) {
    parseHlsManifestQuick(targetUrl, tabId).then(async (parsed) => {
      if (parsed) {
        const currentList = await readMedia(tabId);
        const target = currentList.find((m) => m.key === key);
        if (target) {
          target.videoQualities = parsed.videoQualities;
          target.audioTracks = parsed.audioTracks;
          target.subtitles = parsed.subtitles;
          await writeMedia(tabId, currentList);
        }
      }
    }).catch?.(() => {});
  }
}

async function parseHlsManifestQuick(manifestUrl, tabId) {
  try {
    const res = await fetch(manifestUrl, { method: 'GET' });
    if (!res.ok) return null;
    const text = await res.text();
    if (!text.includes('#EXTM3U')) return null;

    const qualities = [];
    const heights = new Set();
    const audioTracks = [];
    const audioKeys = new Set();
    const subtitles = [];

    const lines = text.split(/\r?\n/);
    for (const line of lines) {
      const l = line.trim();
      if (l.startsWith('#EXT-X-STREAM-INF:')) {
        const m = l.match(/RESOLUTION=(\d+)x(\d+)/i);
        if (m) {
          const w = parseInt(m[1], 10);
          const rawH = parseInt(m[2], 10);
          const longSide = Math.max(w, rawH);
          const shortSide = w > 0 && rawH > 0 ? Math.min(w, rawH) : rawH;
          const h = (longSide >= 7600 || shortSide >= 4320) ? 4320
                  : (longSide >= 3800 || shortSide >= 2160) ? 2160
                  : (longSide >= 2500 || shortSide >= 1440) ? 1440
                  : (longSide >= 1900 || shortSide >= 1080) ? 1080
                  : (longSide >= 1260 || shortSide >= 720) ? 720
                  : (longSide >= 840 || shortSide >= 480) ? 480
                  : (longSide >= 630 || shortSide >= 360) ? 360
                  : (shortSide || rawH);
          if (h && !heights.has(h)) {
            heights.add(h);
            const label = h >= 4320 ? `${h}p 8K Ultra HD` : h >= 2160 ? `${h}p 4K Ultra HD` : h >= 1440 ? `${h}p 2K QHD` : h >= 1080 ? `${h}p Full HD` : h >= 720 ? `${h}p HD` : `${h}p SD`;
            qualities.push({ height: h, label });
          }
        }
      } else if (l.startsWith('#EXT-X-MEDIA:') && /TYPE=AUDIO/i.test(l)) {
        const nameM = l.match(/NAME="([^"]+)"/i);
        const langM = l.match(/LANGUAGE="([^"]+)"/i);
        const uriM = l.match(/URI="([^"]+)"/i);
        const defM = l.match(/DEFAULT=(YES|NO)/i);
        const name = nameM ? nameM[1] : 'Audio';
        const lang = langM ? langM[1] : '';
        const uri = uriM ? uriM[1] : '';
        const isDef = defM && defM[1].toUpperCase() === 'YES';
        const key = `${name}_${lang}_${uri}`.toLowerCase();
        if (!audioKeys.has(key)) {
          audioKeys.add(key);
          const isTr = /turk|türk|dublaj/i.test(name) || /^tr/i.test(lang) || /(?:^|[-_./])(tr|tur|turkish|dublaj)(?:[-_./]|$)/i.test(uri);
          const isExplicitOrig = /original|orijinal|altyaz|ingilizce|english/i.test(name) || /^en/i.test(lang) || /(?:^|[-_./])(en|eng|english|orig|original)(?:[-_./]|$)/i.test(uri);
          const isOrig = !isTr && (isExplicitOrig || isDef);

          let trackId = 'best';
          let label = name;
          if (isTr) {
            trackId = 'tr';
            label = 'Türkçe Dublaj';
          } else if (isOrig) {
            trackId = 'original';
            label = 'Orijinal Ses - İngilizce (English)';
          } else {
            trackId = lang || name;
            label = lang ? `${name} (${lang})` : name;
          }
          if (audioTracks.some(a => a.trackId === trackId)) {
            trackId = `${trackId}_${audioTracks.length + 1}`;
          }
          audioTracks.push({ trackId, label, lang: isTr ? (lang || 'tr') : (isOrig ? (lang || 'en') : lang), isDefault: isDef });
        }
      } else if (l.startsWith('#EXT-X-MEDIA:') && /TYPE=SUBTITLES/i.test(l)) {
        const nameM = l.match(/NAME="([^"]+)"/i);
        const langM = l.match(/LANGUAGE="([^"]+)"/i);
        const uriM = l.match(/URI="([^"]+)"/i);
        const name = nameM ? nameM[1] : 'Subtitle';
        const lang = langM ? langM[1] : 'tr';
        let subUrl = uriM ? uriM[1] : '';
        if (subUrl) {
          try { subUrl = new URL(subUrl, manifestUrl).href; } catch {}
        }
        let label = name;
        if (/turk/i.test(name) || lang === 'tr') label = 'Türkçe Altyazı';
        else if (/eng/i.test(name) || lang === 'en') label = 'İngilizce Altyazı';
        const subObj = { label, lang, url: subUrl, time: Date.now() };
        subtitles.push(subObj);
        if (tabId) addSubtitle(tabId, subObj).catch?.(() => {});
      }
    }
    if (/(trdual|dual[-_]?audio|[-_]dual)/i.test(manifestUrl || '')) {
      const hasTr = audioTracks.some(a => a.trackId === 'tr' || /^tr/i.test(a.lang) || /türkçe/i.test(a.label));
      const hasOrig = audioTracks.some(a => a.trackId === 'original' || /orijinal/i.test(a.label));
      if (hasTr && !hasOrig) {
        audioTracks.push({ trackId: 'original', label: 'Orijinal Ses - İngilizce (English)', lang: 'en', isDefault: false });
      } else if (hasOrig && !hasTr) {
        audioTracks.push({ trackId: 'tr', label: 'Türkçe Dublaj', lang: 'tr', isDefault: false });
      }
    }
    qualities.sort((a, b) => b.height - a.height);
    return { videoQualities: qualities, audioTracks, subtitles };
  } catch {
    return null;
  }
}

function getStreamQualityScore(url) {
  const u = (url || '').toLowerCase();
  if (/-f5|4320p?|8k/i.test(u)) return 5000;
  if (/-f4|2160p?|4k/i.test(u)) return 4000;
  if (/1440p?|2k/i.test(u)) return 3500;
  if (/-f3|1080p?|1080/i.test(u)) return 3000;
  if (/-f2|720p?|720/i.test(u)) return 2000;
  if (/-f1-v1|480p?|480/i.test(u)) return 1000;
  if (/-v\d+/i.test(u)) return 500;
  if (/master|playlist/i.test(u)) return 2500;
  return 100;
}

function pickBestMedia(list) {
  if (!list || !list.length) return null;
  const isAudioOnly = (url) => /sublist_aud|audio[_-]track|audio[_-]\d+|-a\d+\.m3u8/i.test(url) && !/-v\d+/i.test(url);

  // 1. Master or playlist HLS that is not audio-only
  const masterHls = list.find((m) =>
    m.kind === 'hls' &&
    /master|playlist|manifest/i.test(m.url) &&
    !isAudioOnly(m.url)
  );
  if (masterHls) return masterHls;

  // 2. Video streams (prioritize video streams with highest quality score f3 > f2 > f1)
  const videoStreams = list.filter((m) =>
    (m.kind === 'hls' || m.kind === 'video' || m.kind === 'media') &&
    !isAudioOnly(m.url)
  );
  if (videoStreams.length > 0) {
    videoStreams.sort((a, b) => {
      const scoreA = getStreamQualityScore(a.url);
      const scoreB = getStreamQualityScore(b.url);
      if (scoreA !== scoreB) return scoreB - scoreA;
      return (b.size || 0) - (a.size || 0);
    });
    return videoStreams[0];
  }

  const nonAudio = list.find((m) => !isAudioOnly(m.url));
  return nonAudio || list[0];
}

function formatReferer(raw, fallbackUrl) {
  if (!raw || raw.startsWith('chrome-extension://')) return fallbackUrl || '';
  try {
    const u = new URL(raw);
    if (!u.pathname || u.pathname === '/') {
      return u.origin + '/';
    }
    return u.href;
  } catch {
    return raw;
  }
}

function guessExt(type) {
  const t = (type || '').toLowerCase();
  if (t.includes('mp4')) return '.mp4';
  if (t.includes('mpeg') || t.includes('mp3')) return '.mp3';
  if (t.includes('webm')) return '.webm';
  if (t.includes('wav')) return '.wav';
  if (t.includes('ogg')) return '.ogg';
  return '';
}

function guessName(url, type) {
  try {
    const u = new URL(url);
    let n = decodeURIComponent(u.pathname.split('/').filter(Boolean).pop() || '') || u.hostname;
    if (!/\.[a-z0-9]{2,5}$/i.test(n) && type) {
      const ext = { 'video/mp4': 'mp4', 'video/webm': 'webm', 'audio/mpeg': 'mp3', 'audio/mp4': 'm4a', 'video/quicktime': 'mov' }[type.toLowerCase()];
      if (ext) n += '.' + ext;
    }
    return n;
  } catch {
    return url;
  }
}

function headerVal(headers, name) {
  const h = (headers || []).find((x) => x.name.toLowerCase() === name);
  return h ? h.value : '';
}
function classify(url, ct) {
  ct = (ct || '').toLowerCase();
  if (/\.m3u8(\?|#|$)/i.test(url) || /mpegurl/.test(ct)) {
    if (/sublist_aud|audio[_-]?track|audio[_-]\d+\.m3u8/i.test(url)) return null;
    return 'hls';
  }
  if (/\.mpd(\?|#|$)/i.test(url) || /dash\+xml/.test(ct)) return 'dash';
  if (FRAGMENT.test(url)) return null;
  if (/^(video|audio)\//.test(ct)) {
    if (/mp2t|iso\.segment|x-mpegurl/.test(ct)) return null;
    return 'media';
  }
  if (/octet-stream/.test(ct) && MEDIA_EXT.test(url)) return 'media';
  return null;
}

const tabSubtitles = new Map(); // tabId -> Array<{ url, lang, label, time }>

async function readSubtitles(tabId) {
  if (api.storage.session) {
    const key = 'subs:' + tabId;
    const r = await api.storage.session.get(key);
    return r[key] || [];
  }
  return tabSubtitles.get(tabId) || [];
}

async function addSubtitle(tabId, sub) {
  if (!tabId || tabId < 0 || !sub || !sub.url) return;
  let list = await readSubtitles(tabId);
  if (!list.some(s => s.url === sub.url)) {
    list.push(sub);
    if (list.length > 30) list.shift();
    if (api.storage.session) {
      await api.storage.session.set({ ['subs:' + tabId]: list });
    } else {
      tabSubtitles.set(tabId, list);
    }
  }
}
async function clearSubtitles(tabId) {
  if (api.storage.session) {
    try { await api.storage.session.remove('subs:' + tabId); } catch {}
  }
  tabSubtitles.delete(tabId);
}

if (api.webRequest?.onHeadersReceived) {
  api.webRequest.onHeadersReceived.addListener(
    (d) => {
      if (d.tabId < 0 || d.statusCode >= 400) return;
      let host = '';
      try { host = new URL(d.url).hostname; } catch {}
      if (IGNORED_HOSTS.test(host)) return;
      const ct = headerVal(d.responseHeaders, 'content-type').toLowerCase();

      // Check for subtitles (.vtt, .srt, .sub, .ass, text/vtt, caption/track endpoints)
      const uLower = d.url.toLowerCase();
      // Exclude HLS playlists, video segments, and audio streams that are NOT subtitles
      const isHlsOrMedia = /sublist_(aud|vid|\d+)|playlist|manifest|\.m3u8/i.test(uLower) ||
                           /\/hls\//i.test(uLower) && !/\.(vtt|srt|sub|ass)(\?|#|$)/i.test(uLower);
      const isSub = !isHlsOrMedia && (
        /\.(vtt|srt|sub|ass)(\?|#|$)/i.test(d.url) ||
        /text\/vtt|application\/x-subrip|text\/srt/i.test(ct) ||
        ((uLower.includes('subtitle') || uLower.includes('caption') || uLower.includes('/sub/') || uLower.includes('/tracks/') || uLower.includes('altyazi')) &&
         (uLower.includes('.vtt') || uLower.includes('.srt') || /vtt|sub/i.test(ct)))
      );

      if (isSub && !/audio|video/i.test(uLower)) {
        let lang = 'und';
        let label = 'Altyazı';
        const isForced = /forced|zorunlu/i.test(uLower);

        // Delimiter-isolated language detection (never match 'tr' inside 'bluraytrdual' or 'track')
        if (/(?:^|[-_./])(tr|tur|turkish|turkce)(?:[-_./]|$)/i.test(uLower) || uLower.includes('altyazi')) {
          lang = 'tr'; label = isForced ? 'Türkçe (Zorunlu)' : 'Türkçe';
        } else if (/(?:^|[-_./])(en|eng|english)(?:[-_./]|$)/i.test(uLower)) {
          lang = 'en'; label = isForced ? 'English (Forced)' : 'English';
        } else if (/(?:^|[-_./])(pt|por|portuguese)(?:[-_./]|$)/i.test(uLower)) {
          lang = 'pt'; label = isForced ? 'Português (Forced)' : 'Português';
        } else if (/(?:^|[-_./])(es|spa|spanish)(?:[-_./]|$)/i.test(uLower)) {
          lang = 'es'; label = isForced ? 'Español (Forced)' : 'Español';
        } else if (/(?:^|[-_./])(fr|fre|french)(?:[-_./]|$)/i.test(uLower)) {
          lang = 'fr'; label = isForced ? 'Français (Forced)' : 'Français';
        } else if (/(?:^|[-_./])(de|ger|german)(?:[-_./]|$)/i.test(uLower)) {
          lang = 'de'; label = isForced ? 'Deutsch (Forced)' : 'Deutsch';
        } else if (/(?:^|[-_./])(it|ita|italian)(?:[-_./]|$)/i.test(uLower)) {
          lang = 'it'; label = isForced ? 'Italiano (Forced)' : 'Italiano';
        } else if (/(?:^|[-_./])(ru|rus|russian)(?:[-_./]|$)/i.test(uLower)) {
          lang = 'ru'; label = isForced ? 'Русский (Forced)' : 'Русский';
        } else if (/(?:^|[-_./])(ar|ara|arabic)(?:[-_./]|$)/i.test(uLower)) {
          lang = 'ar'; label = isForced ? 'العربية (Forced)' : 'العربية';
        } else if (isForced) {
          label = 'Zorunlu Altyazı';
        }

        addSubtitle(d.tabId, { url: d.url, lang, label, time: Date.now() }).catch?.(() => {});
      }

      if (/googlevideo\.com\/videoplayback/i.test(d.url)) return;

      const kind = classify(d.url, ct);
      if (!kind) return;
      let size = parseInt(headerVal(d.responseHeaders, 'content-length'), 10) || 0;
      const cr = headerVal(d.responseHeaders, 'content-range').match(/\/(\d+)/);
      if (cr) size = parseInt(cr[1], 10);
      if (kind === 'media' && size > 0 && size < 300 * 1024) return;
      addMedia(d.tabId, {
        url: d.url,
        type: ct.split(';')[0],
        size,
        kind,
        time: Date.now(),
        initiator: d.initiator || d.documentUrl || '',
      });
    },
    { urls: ['<all_urls>'], types: ['media', 'xmlhttprequest', 'other', 'object', 'main_frame'] },
    ['responseHeaders']
  );
}

const dismissedTabs = new Set();

api.tabs.onUpdated.addListener((tabId, info) => {
  // Only clear media when the top-level URL genuinely navigates
  if (info.url && !info.url.startsWith('chrome-extension://')) {
    clearMedia(tabId);
    clearSubtitles(tabId);
    dismissedTabs.delete(tabId);
  }
});
api.tabs.onRemoved.addListener((tabId) => {
  clearMedia(tabId);
  clearSubtitles(tabId);
  dismissedTabs.delete(tabId);
});

// ---------- Context menu ----------
const menus = api.contextMenus || api.menus;
function createMenus() {
  if (!menus) return;
  const build = () => {
    const safeCreate = (props) => {
      try {
        menus.create(props, () => {
          // Void runtime.lastError so Chrome never flags duplicate IDs or existing items as unhandled errors
          void api.runtime?.lastError;
        });
      } catch {}
    };
    safeCreate({ id: 'dm-link', title: t('menuDownloadLink'), contexts: ['link', 'video', 'audio', 'image'] });
    safeCreate({ id: 'dm-page-video', title: t('menuPageVideo'), contexts: ['page', 'video', 'frame', 'action'] });
  };

  try {
    if (IS_FIREFOX) {
      const r = menus.removeAll();
      if (r && typeof r.then === 'function') r.then(build, build);
      else build();
    } else {
      menus.removeAll(() => {
        void api.runtime?.lastError;
        build();
      });
    }
  } catch {
    build();
  }
}
api.runtime.onInstalled.addListener(() => { createMenus(); ping(); });
api.runtime.onStartup?.addListener(() => { createMenus(); ping(); });
menus?.onClicked.addListener(async (info, tab) => {
  if (info.menuItemId === 'dm-link') {
    const url = info.linkUrl || info.srcUrl;
    if (url) {
      let filename = '';
      try {
        const u = new URL(url);
        const seg = u.pathname.split('/').filter(Boolean).pop();
        if (seg && !isGenericMediaName(seg)) filename = decodeURIComponent(seg);
      } catch {}
      sendToApp({ url, filename, referer: info.pageUrl || tab?.url, kind: 'file' });
    }
  } else if (info.menuItemId === 'dm-page-video') {
    let url = info.pageUrl || tab?.url;
    let kind = 'video';
    let referer = tab?.url || url;
    let filename = '';
    if (tab && tab.id) {
      const isKnown = /(youtube\.com|youtu\.be|tiktok\.com|twitter\.com|x\.com|vimeo\.com|dailymotion\.com|instagram\.com|twitch\.tv|reddit\.com)/i.test(url || '');
      const active = await getActiveMedia(tab.id);
      if (active && active.url) {
        url = active.url;
        filename = active.name || tab.title || '';
        referer = active.referer || tab.url || url;
        kind = 'video';
      } else if (!isKnown) {
        const list = await readMedia(tab.id);
        const best = pickBestMedia(list);
        if (best && best.url) {
          url = best.url;
          filename = best.name || tab.title || '';
          kind = 'video';
          referer = formatReferer(best.initiator, tab.url || url);
        }
      }
    }
    if (url) sendToApp({ url, filename, referer, kind });
  }
});

// ---------- Popup / content script messages ----------
api.runtime.onMessage.addListener((msg, sender, sendResponse) => {
  (async () => {
    try {
      switch (msg?.type) {
        case 'status': {
          const settings = await getSettings();
          const raw = await pingRaw(settings);
          const app = raw && raw.paired ? await ping(settings) : null;
          const cached = app ? null : (await api.storage.local.get('captureExtensions')).captureExtensions;
          const sTheme = await api.storage.local.get(['accentColor', 'language']);
          sendResponse({
            online: !!app, reachable: !!raw, paired: !!(raw && raw.paired), rawApp: raw,
            app, settings, clickIntercept: !api.downloads?.onDeterminingFilename, isSafari: IS_SAFARI,
            canLaunch: !!api.runtime.sendNativeMessage, captureExtensions: (app && app.captureExtensions) || cached || [],
            accentColor: (raw && raw.accentColor) || sTheme.accentColor || '#E50914',
            language: (raw && raw.language) || sTheme.language || 'tr',
          });
          break;
        }
        case 'getAppConfig': {
          const s = await api.storage.local.get(['accentColor', 'language']);
          sendResponse({
            accentColor: s.accentColor || '#E50914',
            language: s.language || 'tr'
          });
          break;
        }
        case 'setAppConfig': {
          const updates = {};
          if (msg.accentColor) updates.accentColor = msg.accentColor;
          if (msg.language) updates.language = msg.language;
          if (Object.keys(updates).length) await api.storage.local.set(updates);
          sendResponse({ ok: true });
          break;
        }
        case 'setToken': {
          // pairing: the user pastes the code shown in the app (Browser setup / tray menu)
          const token = String(msg.token || '').trim().toLowerCase();
          if (!/^[0-9a-f]{64}$/.test(token)) { sendResponse({ ok: false, error: 'bad code' }); break; }
          await api.storage.local.set({ token });
          const raw = await pingRaw();
          sendResponse({ ok: !!(raw && raw.paired), reachable: !!raw });
          break;
        }
        case 'reportActiveMedia': {
          const tabId = sender.tab?.id || msg.tabId;
          if (tabId && msg.media && msg.media.url) {
            if (/(persist_identity|googleads|doubleclick)/i.test(msg.media.url)) {
              sendResponse({ ok: false });
              break;
            }
            let mediaName = msg.media.name;
            if (isGenericMediaName(mediaName)) {
              const tabTitle = sender.tab?.title;
              if (tabTitle) {
                const cleaned = cleanMediaTitle(tabTitle);
                if (cleaned) mediaName = cleaned;
              }
            }
            if (mediaName) msg.media.name = mediaName;
            await setActiveMedia(tabId, msg.media);
            try {
              const currentList = await readMedia(tabId);
              const m = msg.media;
              let targetUrl = m.url;
              const ytEmbed = targetUrl.match(/(?:youtube\.com|youtube-nocookie\.com)\/embed\/([a-zA-Z0-9_-]{11})/i);
              if (ytEmbed) targetUrl = `https://www.youtube.com/watch?v=${ytEmbed[1]}`;
              const key = normalizeKey(targetUrl);
              const idx = currentList.findIndex((x) => x.key === key || x.url === targetUrl || x.url === m.url);
              const richEntry = {
                url: targetUrl,
                key,
                name: m.name || 'Video',
                kind: m.kind || 'video',
                type: 'video/mp4',
                size: m.size || 0,
                quality: m.videoQualities?.[0]?.label || (m.quality || 'HD'),
                videoQualities: m.videoQualities || [],
                audioTracks: m.audioTracks || [],
                subtitles: m.subtitles || [],
                initiator: m.referer || ''
              };
              if (idx >= 0) {
                currentList[idx] = { ...currentList[idx], ...richEntry };
              } else {
                currentList.unshift(richEntry);
              }
              await writeMedia(tabId, currentList);
            } catch {}
          }
          sendResponse({ ok: true });
          break;
        }
        case 'getActiveMedia': {
          const tabId = msg.tabId || sender.tab?.id;
          const active = tabId ? await getActiveMedia(tabId) : null;
          sendResponse(active || null);
          break;
        }
        case 'getMedia': sendResponse(await readMedia(msg.tabId)); break;
        case 'downloads': sendResponse(await getDownloads()); break;
        case 'action': sendResponse(await downloadAction(msg.id, msg.action)); break;
        case 'download': {
          const downloadMsg = { ...msg };
          if (downloadMsg.kind === 'video' && isGenericMediaName(downloadMsg.filename)) {
            const tabTitle = sender.tab?.title;
            if (tabTitle) {
              const cleaned = cleanMediaTitle(tabTitle);
              if (cleaned) downloadMsg.filename = cleaned;
            }
          }
          sendResponse(await sendToApp(downloadMsg));
          break;
        }
        case 'setSettings': await setSettings(msg.settings || {}); sendResponse({ ok: true }); break;
        case 'launchApp': sendResponse({ ok: await launchApp() }); break;
        case 'pageMedia': {
          if (sender.tab) {
            for (const m of msg.items || []) {
              const kind = m.kind || classify(m.url, m.type || '');
              if (kind || MEDIA_EXT.test(m.url)) {
                let init = sender.origin || '';
                try { if (!init && sender.url) init = new URL(sender.url).origin; } catch {}
                await addMedia(sender.tab.id, {
                  url: m.url,
                  type: m.type || '',
                  size: m.size || 0,
                  kind: kind || 'media',
                  time: Date.now(),
                  name: m.name,
                  initiator: init,
                  videoQualities: m.videoQualities || [],
                  audioTracks: m.audioTracks || [],
                  subtitles: m.subtitles || []
                });
              }
            }
          }
          sendResponse({ ok: true });
          break;
        }
        case 'pageSubtitles': {
          const tabId = sender.tab?.id || msg.tabId;
          if (tabId && Array.isArray(msg.subtitles)) {
            for (const s of msg.subtitles) {
              if (s && s.url) {
                await addSubtitle(tabId, {
                  url: s.url,
                  lang: s.code || s.lang || 'und',
                  label: s.label || 'Altyazı',
                  time: Date.now()
                });
              }
            }
          }
          sendResponse({ ok: true });
          break;
        }
        case 'dismissTabBar': {
          const tabId = sender.tab?.id;
          if (tabId) {
            dismissedTabs.add(tabId);
            try { api.tabs.sendMessage(tabId, { type: 'tab_bar_dismissed' }).catch?.(() => {}); } catch {}
          }
          sendResponse({ ok: true });
          break;
        }
        case 'getBestMedia': {
          const tabId = msg.tabId || sender.tab?.id;
          const list = tabId ? await readMedia(tabId) : [];
          const best = pickBestMedia(list);
          const subs = tabId ? await readSubtitles(tabId) : [];
          const isDismissed = tabId ? dismissedTabs.has(tabId) : false;
          sendResponse({ best, list, subtitles: subs, dismissed: isDismissed });
          break;
        }
        default: sendResponse(null);
      }
    } catch (e) {
      sendResponse({ ok: false, error: String(e) });
    }
  })();
  return true;
});
