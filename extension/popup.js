'use strict';
const api = typeof browser !== 'undefined' ? browser : chrome;
const $ = (id) => document.getElementById(id);

const I18N_DICTS = {
  tr: {
    popupConnecting: "Bağlanıyor…",
    popupConnected: "Bağlı (v$1)",
    popupActive: "$1 aktif",
    popupOffline: "Uygulama kapalı",
    popupNotPaired: "Eşleşme Gerekli",
    popupOfflineBox: "Uygulama çalışmıyor. FM Download Manager uygulamasını açın.",
    popupLaunch: "Uygulamayı Aç",
    popupPairBox: "FM Download Manager'ı açın, 'Eşleşme kodunu kopyala' seçeneğini seçin ve kodu buraya yapıştırın.",
    popupPairPlaceholder: "Eşleşme kodu",
    popupPairButton: "Eşle",
    popupPairOk: "Eşleşme başarılı!",
    popupPairBad: "Geçersiz kod",
    popupIntercept: "İndirmeleri FM'e gönder",
    popupSniff: "Sayfalardaki videoları yakala",
    popupAutoLaunch: "İndirme geldiğinde uygulamayı aç",
    popupPageVideo: "Bu sayfadaki videoyu indir (yt-dlp)",
    popupPageLink: "Sayfa URL'sini dosya olarak indir",
    popupDownloadsTitle: "İndirmeler",
    popupNoDownloads: "Aktif indirme yok. Başlatılan indirmeler burada görünür.",
    popupMediaTitle: "Yakalanan Medya",
    popupNoMedia: "Bu sekmede henüz yakalanan medya yok. Bir video oynatın.",
    popupSettings: "Ayarlar",
    popupPort: "Uygulama portu",
    popupMinSize: "Yalnızca şundan büyük indirmeleri yakala (MB, 0 = tümü)",
    popupTheme: "Tema:",
    popupSent: "Gönderildi!",
    popupStarted: "Başlatıldı!"
  },
  en: {
    popupConnecting: "Connecting…",
    popupConnected: "Connected (v$1)",
    popupActive: "$1 active",
    popupOffline: "App is offline",
    popupNotPaired: "Pairing Required",
    popupOfflineBox: "The app is not running. Open FM Download Manager.",
    popupLaunch: "Open App",
    popupPairBox: "Open FM Download Manager, choose 'Copy pairing code', then paste the code here.",
    popupPairPlaceholder: "Pairing code",
    popupPairButton: "Pair",
    popupPairOk: "Pairing successful!",
    popupPairBad: "Invalid code",
    popupIntercept: "Send downloads to FM",
    popupSniff: "Capture videos on pages",
    popupAutoLaunch: "Open the app when a download arrives",
    popupPageVideo: "Download video from this page (yt-dlp)",
    popupPageLink: "Download page URL as a file",
    popupDownloadsTitle: "Downloads",
    popupNoDownloads: "No active downloads. Started downloads show here.",
    popupMediaTitle: "Captured media",
    popupNoMedia: "No media captured in this tab yet. Start playing a video.",
    popupSettings: "Settings",
    popupPort: "App port",
    popupMinSize: "Only capture downloads larger than (MB, 0 = all)",
    popupTheme: "Theme:",
    popupSent: "Sent!",
    popupStarted: "Started!"
  }
};

let currentLang = 'tr';
let currentAccent = '#E50914';
let currentThemeMode = 'dark';
let userToggledLang = false;
let userToggledTheme = false;

const PALETTE = [
  { name: 'Crimson', hex: '#E50914' },
  { name: 'Cyan', hex: '#00E5FF' },
  { name: 'Emerald', hex: '#10B981' },
  { name: 'Violet', hex: '#8B5CF6' },
  { name: 'Orange', hex: '#FF6B00' },
  { name: 'Gold', hex: '#F59E0B' },
  { name: 'Pink', hex: '#EC4899' },
  { name: 'Silver', hex: '#94A3B8' },
];

function t(k, subs) {
  const dict = I18N_DICTS[currentLang] || I18N_DICTS.tr;
  let text = dict[k] || (typeof api.i18n !== 'undefined' ? api.i18n.getMessage(k, subs) : '') || k;
  if (subs) {
    if (Array.isArray(subs)) {
      subs.forEach((val, i) => { text = text.replace(new RegExp(`\\$${i+1}`, 'g'), val); });
    } else {
      text = text.replace(/\$1/g, subs);
    }
  }
  return text;
}

function updateUiTexts() {
  document.querySelectorAll('[data-i18n]').forEach((el) => {
    el.textContent = t(el.dataset.i18n);
  });
  document.querySelectorAll('[data-i18n-placeholder]').forEach((el) => {
    el.placeholder = t(el.dataset.i18nPlaceholder);
  });
  const langBtn = $('langToggle');
  if (langBtn) langBtn.textContent = currentLang.toUpperCase();
  document.documentElement.lang = currentLang;
}

function applyAccentColor(hex) {
  if (!hex) return;
  currentAccent = hex;
  const num = parseInt(hex.replace('#', ''), 16) || 0xE50914;
  const r = (num >> 16) & 255;
  const g = (num >> 8) & 255;
  const b = num & 255;
  const root = document.documentElement.style;
  root.setProperty('--accent', hex);
  root.setProperty('--accent-hover', `rgb(${Math.min(255, r + 25)}, ${Math.min(255, g + 25)}, ${Math.min(255, b + 25)})`);
  root.setProperty('--accent-press', `rgb(${Math.max(0, r - 30)}, ${Math.max(0, g - 30)}, ${Math.max(0, b - 30)})`);
  root.setProperty('--accent-dim', `rgba(${r}, ${g}, ${b}, 0.18)`);

  document.querySelectorAll('.swatch').forEach(sw => {
    sw.classList.toggle('active', sw.dataset.hex.toLowerCase() === hex.toLowerCase());
  });
}

function applyThemeMode(mode) {
  currentThemeMode = mode === 'light' ? 'light' : 'dark';
  document.body.classList.toggle('theme-light', currentThemeMode === 'light');
  const themeBtn = $('themeToggle');
  if (themeBtn) themeBtn.textContent = currentThemeMode === 'light' ? '☀️' : '🌙';
}

function initPalette() {
  const container = $('swatches');
  if (!container) return;
  container.innerHTML = '';
  PALETTE.forEach(p => {
    const sw = document.createElement('div');
    sw.className = 'swatch';
    sw.dataset.hex = p.hex;
    sw.title = p.name;
    sw.style.backgroundColor = p.hex;
    sw.addEventListener('click', async () => {
      applyAccentColor(p.hex);
      await api.storage.local.set({ accentColor: p.hex });
      send({ type: 'setAppConfig', accentColor: p.hex });
    });
    container.appendChild(sw);
  });
}

// Firefox's browser.runtime.sendMessage only returns a promise (a callback argument is rejected).
const IS_FIREFOX = typeof browser !== 'undefined' && String(browser.runtime.getURL('')).startsWith('moz-extension:');

function send(msg) {
  return new Promise((resolve) => {
    try {
      if (IS_FIREFOX) browser.runtime.sendMessage(msg).then(resolve, () => resolve(null));
      else api.runtime.sendMessage(msg, (r) => resolve(api.runtime.lastError ? null : r));
    } catch { resolve(null); }
  });
}
function fmtSize(n) {
  if (!n) return '';
  const u = ['B', 'KB', 'MB', 'GB'];
  let i = 0; while (n >= 1024 && i < u.length - 1) { n /= 1024; i++; }
  return n.toFixed(i ? 1 : 0) + ' ' + u[i];
}

let tab = null;

async function currentTab() {
  const tabs = await api.tabs.query({ active: true, currentWindow: true });
  return tabs && tabs[0];
}

async function sendTabMessageWithTimeout(tabId, msg, timeoutMs = 1200) {
  if (!tabId) return null;
  return new Promise((resolve) => {
    let settled = false;
    const timer = setTimeout(() => {
      if (!settled) { settled = true; resolve(null); }
    }, timeoutMs);
    try {
      api.tabs.sendMessage(tabId, msg, (r) => {
        if (!settled) {
          settled = true;
          clearTimeout(timer);
          resolve(api.runtime.lastError ? null : r);
        }
      });
    } catch {
      if (!settled) { settled = true; clearTimeout(timer); resolve(null); }
    }
  });
}

async function getTabSubtitles(tabId) {
  let subs = [];
  if (tabId) {
    try {
      const resp = await sendTabMessageWithTimeout(tabId, { type: 'detectSubtitles' }, 1200);
      if (resp && Array.isArray(resp.subtitles) && resp.subtitles.length > 0) {
        subs = resp.subtitles;
      }
    } catch {}
  }

  try {
    const bgInfo = await send({ type: 'getBestMedia', tabId });
    if (bgInfo && Array.isArray(bgInfo.subtitles)) {
      for (const s of bgInfo.subtitles) {
        if (s && s.url && !subs.some(x => x.url === s.url)) {
          subs.push({
            code: s.lang || s.code || 'und',
            lang: s.lang || s.code || 'und',
            label: s.label || 'Altyazı',
            url: s.url
          });
        }
      }
    }
  } catch {}

  return subs;
}

function chooseBestSubUrl(subs, targetLang = 'tr') {
  if (!subs || !subs.length) return '';
  const nonForcedTr = subs.find(s => (s.code === targetLang || s.lang === targetLang) && !/forced/i.test(s.url || '') && !/forced/i.test(s.label || ''));
  if (nonForcedTr?.url) return nonForcedTr.url;
  const anyTr = subs.find(s => s.code === targetLang || s.lang === targetLang);
  if (anyTr?.url) return anyTr.url;
  return subs[0]?.url || '';
}

async function refresh() {
  const st = await send({ type: 'status' });
  if (st && st.accentColor) applyAccentColor(st.accentColor);
  if (st && st.themeMode && !userToggledTheme) applyThemeMode(st.themeMode);
  if (st && st.language && !userToggledLang) {
    currentLang = st.language;
    updateUiTexts();
  }
  const online = !!(st && st.online);
  const reachable = !!(st && st.reachable);
  const paired = !!(st && st.paired);
  $('dot').classList.toggle('on', online);
  $('statusText').textContent = online
    ? t('popupConnected', [String(st.app.version)]) + (st.app.active ? ` · ${t('popupActive', [String(st.app.active)])}` : '')
    : (reachable && !paired ? t('popupNotPaired') : t('popupOffline'));
  $('offline').classList.toggle('hidden', reachable);
  $('pair').classList.toggle('hidden', !(reachable && !paired));
  $('launch').classList.toggle('hidden', !(st && st.canLaunch));
  if (reachable && !paired) {
    try {
      const res = await fetch(api.runtime.getURL('token.json'));
      if (res.ok) {
        const d = await res.json();
        if (d && d.token) {
          const r = await send({ type: 'setToken', token: d.token });
          if (r && r.ok) { refresh(); return; }
        }
      }
    } catch {}
    try {
      const clip = (await navigator.clipboard.readText()).trim().toLowerCase();
      if (/^[0-9a-f]{64}$/.test(clip)) {
        $('pairCode').value = clip;
        const r = await send({ type: 'setToken', token: clip });
        if (r && r.ok) { refresh(); }
      }
    } catch {}
  }
  if (st && st.settings) {
    $('intercept').checked = !!st.settings.intercept;
    $('sniff').checked = !!st.settings.sniff;
    $('autoLaunch').checked = st.settings.autoLaunch !== false;
    $('port').value = st.settings.port;
    $('minSize').value = st.settings.minSizeMB || 0;
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

async function getActiveMediaFromTab(tabId) {
  if (!tabId) return null;
  // 1. Check background.js registry first (reported directly by floating_bar.js from the active video player/iframe)
  try {
    const fromBg = await send({ type: 'getActiveMedia', tabId });
    if (fromBg && fromBg.url && !/^https?:\/\/[^/]+\/?$/i.test(fromBg.url) && !/\.html?(\?|$)/i.test(fromBg.url)) {
      if (isGenericMediaName(fromBg.name)) {
        fromBg.name = cleanMediaTitle(tab?.title) || (tab?.title || 'Video').replace(/\s*-\s*YouTube$/i, '').trim();
      }
      return fromBg;
    }
  } catch {}

  // 2. Query active tab fallback
  try {
    const fromAny = await sendTabMessageWithTimeout(tabId, { type: 'getActiveMedia' }, 1200);
    if (fromAny && fromAny.url && !/^https?:\/\/[^/]+\/?$/i.test(fromAny.url) && !/\.html?(\?|$)/i.test(fromAny.url)) {
      if (isGenericMediaName(fromAny.name)) {
        fromAny.name = cleanMediaTitle(tab?.title) || (tab?.title || 'Video').replace(/\s*-\s*YouTube$/i, '').trim();
      }
      return fromAny;
    }
  } catch {}

  return null;
}

async function loadMedia() {
  tab = await currentTab();
  const list = (tab && (await send({ type: 'getMedia', tabId: tab.id }))) || [];

  if (tab && tab.id) {
    try {
      const isYouTube = tab.url && /(youtube\.com|youtu\.be)/i.test(tab.url);
      const ytMatch = isYouTube ? tab.url.match(/(?:v=|\/shorts\/)([a-zA-Z0-9_-]{11})/i) : null;
      const active = await getActiveMediaFromTab(tab.id);

      let canonicalUrl = '';
      if (ytMatch) {
        canonicalUrl = `https://www.youtube.com/watch?v=${ytMatch[1]}`;
      } else if (active && active.url && !/persist_identity|googleads/i.test(active.url)) {
        canonicalUrl = active.url;
        const ytEmbed = canonicalUrl.match(/(?:youtube\.com|youtube-nocookie\.com)\/embed\/([a-zA-Z0-9_-]{11})/i);
        if (ytEmbed) canonicalUrl = `https://www.youtube.com/watch?v=${ytEmbed[1]}`;
      }

      if (canonicalUrl) {
        let cleanName = (active && active.name) || tab.title || '';
        cleanName = cleanName.replace(/^(?:youtube\s*(?:video\s*player|frame)?|iframe)\s*[-–|•]?\s*/i, '').trim();
        if (isGenericMediaName(cleanName) || cleanName.toLowerCase() === 'youtube') {
          cleanName = cleanMediaTitle(tab?.title) || (tab?.title || 'Video').replace(/\s*-\s*YouTube$/i, '').trim();
        }

        const richItem = {
          url: canonicalUrl,
          name: cleanName,
          kind: (active && active.kind) || 'video',
          size: (active && active.size) || 0,
          quality: active?.videoQualities?.[0]?.label || (active?.quality || 'HD'),
          videoQualities: active?.videoQualities || [],
          audioTracks: active?.audioTracks || [],
          subtitles: active?.subtitles || [],
          initiator: active?.referer || tab.url
        };

        const existingIdx = list.findIndex(m => m.url === canonicalUrl || (active && m.url === active.url) || (tab.url && m.url === tab.url));
        if (existingIdx >= 0) {
          list[existingIdx] = { ...list[existingIdx], ...richItem };
        } else {
          list.unshift(richItem);
        }
      }
    } catch {}
  }

  const ul = $('media');
  ul.innerHTML = '';
  // Filter out redundant audio-only chunks, unplayable googlevideo chunks, or generic iframe placeholders
  const hasVideo = list.some(m => /-v\d+|video/i.test(m.url) || (m.size && m.size > 20 * 1024 * 1024));
  const filtered = list.filter(m => {
    if (!m || !m.url) return false;
    if (/googlevideo\.com\/videoplayback/i.test(m.url)) return false;
    if (/persist_identity|googleads|doubleclick/i.test(m.url)) return false;
    if (hasVideo && /sublist_aud|audio[_-]track|-a\d+\.m3u8/i.test(m.url) && !/-v\d+/i.test(m.url)) return false;
    return true;
  });

  $('count').textContent = filtered.length ? `(${filtered.length})` : '';
  $('noMedia').classList.toggle('hidden', filtered.length > 0);
  for (const m of filtered) {
    const li = document.createElement('li');
    const info = document.createElement('div'); info.className = 'info';
    let cleanItemName = m.name || m.url;
    cleanItemName = cleanItemName.replace(/^(?:youtube\s*(?:video\s*player|frame)?|iframe)\s*[-–|•]?\s*/i, '').trim();
    if (isGenericMediaName(cleanItemName) || /^(?:youtube|video|iframe)$/i.test(cleanItemName.toLowerCase())) {
      cleanItemName = cleanMediaTitle(tab?.title) || (tab?.title || 'Video').replace(/\s*-\s*YouTube$/i, '').trim();
    }
    const name = document.createElement('div'); name.className = 'name'; name.textContent = cleanItemName; name.title = cleanItemName;
    const meta = document.createElement('div'); meta.className = 'meta';
    const kind = m.kind === 'hls' ? t('kindHls') : m.kind === 'dash' ? t('kindDash') : (m.type || t('kindMedia'));
    const metaText = [kind, fmtSize(m.size)].filter(Boolean).join(' · ');
    if (m.quality) {
      const qSpan = document.createElement('span');
      qSpan.className = 'badge-q';
      qSpan.textContent = m.quality;
      meta.appendChild(qSpan);
      meta.appendChild(document.createTextNode(' ' + metaText));
    } else {
      meta.textContent = metaText;
    }
    info.append(name, meta);
    const btn = document.createElement('button'); btn.className = 'small'; btn.textContent = t('popupDownload');
    btn.onclick = async () => {
      btn.disabled = true; btn.textContent = '…';
      const k = m.kind === 'video' || m.kind === 'hls' || m.kind === 'dash' || m.kind === 'media' ? 'video' : 'file';
      const isVid = k === 'video';
      const ref = formatReferer(m.initiator, tab.url);
      let dlUrl = m.url;
      const ytEmbed = dlUrl.match(/(?:youtube\.com|youtube-nocookie\.com)\/embed\/([a-zA-Z0-9_-]{11})/i);
      if (ytEmbed) dlUrl = `https://www.youtube.com/watch?v=${ytEmbed[1]}`;
      const subs = isVid ? ((m.subtitles && m.subtitles.length) ? m.subtitles : await getTabSubtitles(tab?.id)) : [];
      const chosenSub = isVid ? chooseBestSubUrl(subs, 'tr') : '';

      const r = await send({
        type: 'download',
        url: dlUrl,
        filename: cleanItemName || '',
        referer: ref,
        kind: k,
        writeSubtitles: isVid,
        subtitles: subs,
        subtitleUrl: chosenSub,
        subtitleLangs: 'tr,en',
        videoQualities: m.videoQualities || [],
        audioTracks: m.audioTracks || []
      });
      if (r && r.ok) {
        btn.textContent = '✓ ' + t('popupSent');
        btn.classList.add('success');
      } else {
        btn.textContent = t('popupError');
        btn.classList.add('error');
        btn.disabled = false;
      }
    };
    li.append(info, btn);
    ul.appendChild(li);
  }
}

// ---------- Downloads (progress list) ----------
const seenActive = new Set();   // ids that were active while this popup was open → keep showing their final state
const STATE_LABEL = { queued: 'stateQueued', paused: 'statePaused', probing: 'stateProbing', completed: 'stateCompleted', failed: 'stateFailed', cancelled: 'stateCancelled' };
let dlTimer = null;

async function loadDownloads() {
  const all = (await send({ type: 'downloads' })) || [];
  const shown = all.filter((d) => {
    if (d.state === 'downloading' || d.state === 'probing') { seenActive.add(d.id); return true; }
    if (d.state === 'queued' || d.state === 'paused') return true;
    return seenActive.has(d.id);
  });
  const ul = $('dl');
  $('noDl').classList.toggle('hidden', shown.length > 0);
  // Rebuild only when the set of rows changes; otherwise update in place to keep buttons clickable.
  const ids = shown.map((d) => d.id).join('|');
  if (ul.dataset.ids !== ids) { ul.innerHTML = ''; ul.dataset.ids = ids; for (const d of shown) ul.appendChild(buildRow(d)); }
  for (const d of shown) updateRow(d);
}

function buildRow(d) {
  const li = document.createElement('li'); li.dataset.id = d.id;
  const info = document.createElement('div'); info.className = 'info';
  const name = document.createElement('div'); name.className = 'name';
  const bar = document.createElement('div'); bar.className = 'bar'; bar.appendChild(Object.assign(document.createElement('div'), { className: 'fill' }));
  const meta = document.createElement('div'); meta.className = 'meta';
  info.append(name, bar, meta);
  const actions = document.createElement('div'); actions.className = 'actions';
  const toggle = document.createElement('button'); toggle.className = 'icon toggle';
  toggle.onclick = () => send({ type: 'action', id: d.id, action: toggle.dataset.action }).then(loadDownloads);
  const cancel = document.createElement('button'); cancel.className = 'icon cancel'; cancel.textContent = '✕'; cancel.title = t('popupCancel');
  cancel.onclick = () => send({ type: 'action', id: d.id, action: cancel.dataset.action || 'cancel' }).then(loadDownloads);
  actions.append(toggle, cancel);
  li.append(info, actions);
  return li;
}

function updateRow(d) {
  const li = $('dl').querySelector(`li[data-id="${d.id}"]`); if (!li) return;
  li.querySelector('.name').textContent = d.name; li.querySelector('.name').title = d.name;
  const active = d.state === 'downloading' || d.state === 'probing';
  const pct = d.total > 0 ? Math.min(100, Math.round((100 * d.received) / d.total)) : (d.state === 'completed' ? 100 : 0);
  const bar = li.querySelector('.bar');
  bar.className = 'bar' + (active && d.total <= 0 ? ' indeterminate' : '') + (d.state === 'completed' ? ' done' : '') + (d.state === 'failed' ? ' failed' : '');
  bar.querySelector('.fill').style.width = (active && d.total <= 0) ? '' : pct + '%';
  const parts = [];
  if (d.state === 'downloading') {
    if (d.total > 0) parts.push(pct + '%', fmtSize(d.received) + ' / ' + fmtSize(d.total));
    if (d.speed > 0) parts.push(fmtSize(d.speed) + '/s');
    if (d.stage) parts.push(d.stage);
  } else {
    parts.push(d.stage && active ? d.stage : t(STATE_LABEL[d.state] || d.state));
    if (d.state === 'failed' && d.error) parts.push(d.error);
  }
  li.querySelector('.meta').textContent = parts.join(' · ');
  const toggle = li.querySelector('.toggle'), cancel = li.querySelector('.cancel');
  const terminal = d.state === 'completed' || d.state === 'failed' || d.state === 'cancelled';
  toggle.classList.toggle('hidden', terminal);
  if (active) { toggle.textContent = '❚❚'; toggle.title = t('popupPause'); toggle.dataset.action = 'pause'; }
  else { toggle.textContent = '▶'; toggle.title = t('popupResume'); toggle.dataset.action = 'resume'; }
  cancel.dataset.action = terminal ? 'remove' : 'cancel';
  cancel.title = terminal ? t('popupRemove') : t('popupCancel');
}

function pickBestMedia(list) {
  if (!list || !list.length) return null;
  const masterHls = list.find((m) =>
    m.kind === 'hls' &&
    (/master|playlist|manifest|index\.m3u8/i.test(m.url) || !/sublist|chunk|aud|vid\d/i.test(m.url))
  );
  if (masterHls) return masterHls;
  const directVideo = list.find((m) =>
    (m.kind === 'video' || m.kind === 'media') &&
    !/aud/i.test(m.url) &&
    !/sublist/i.test(m.url)
  );
  if (directVideo) return directVideo;
  const videoHls = list.find((m) => m.kind === 'hls' && !/aud|audio/i.test(m.url));
  if (videoHls) return videoHls;
  const nonAudio = list.find((m) => !/sublist_aud|audio[_-]track/i.test(m.url));
  return nonAudio || list[0];
}

$('pageVideo').onclick = async () => {
  tab = tab || (await currentTab());
  if (!tab?.url) return;

  const isYouTube = tab.url && /(youtube\.com|youtu\.be)/i.test(tab.url);
  const ytMatch = isYouTube ? tab.url.match(/(?:v=|\/shorts\/)([a-zA-Z0-9_-]{11})/i) : null;

  // 1. ALWAYS query the page / background active media first!
  let activeMedia = null;
  try {
    activeMedia = await getActiveMediaFromTab(tab.id);
  } catch {}

  let targetUrl = '';
  let filename = '';
  let kind = 'video';
  let referer = tab.url;
  let qualities = [];
  let audios = [];
  let subs = [];
  let best = null;

  if (ytMatch) {
    targetUrl = `https://www.youtube.com/watch?v=${ytMatch[1]}`;
    filename = (activeMedia?.name || tab.title || 'YouTube Video').replace(/\s*-\s*YouTube$/i, '').trim();
    qualities = activeMedia?.videoQualities || [];
    audios = activeMedia?.audioTracks || [];
    subs = activeMedia?.subtitles || [];
    referer = targetUrl;
  } else if (activeMedia && activeMedia.url && !/\.html?(\?|$)/i.test(activeMedia.url) && !/persist_identity|googleads|doubleclick/i.test(activeMedia.url)) {
    targetUrl = activeMedia.url;
    const ytEmbed = targetUrl.match(/(?:youtube\.com|youtube-nocookie\.com)\/embed\/([a-zA-Z0-9_-]{11})/i);
    if (ytEmbed) {
      targetUrl = `https://www.youtube.com/watch?v=${ytEmbed[1]}`;
      referer = targetUrl;
    } else {
      referer = activeMedia.referer || tab.url;
    }
    filename = activeMedia.name || tab.title || 'Video';
    kind = 'video';
    qualities = activeMedia.videoQualities || [];
    audios = activeMedia.audioTracks || [];
    subs = activeMedia.subtitles || [];
  } else {
    // Non-YouTube sites (e.g. HDFilmcehennemi, movie embeds)
    const mediaList = (await send({ type: 'getMedia', tabId: tab.id })) || [];
    best = pickBestMedia(mediaList);
    if (best && best.url && !/\.html?(\?|$)/i.test(best.url)) {
      targetUrl = best.url;
      const ytEmbed = targetUrl.match(/(?:youtube\.com|youtube-nocookie\.com)\/embed\/([a-zA-Z0-9_-]{11})/i);
      if (ytEmbed) {
        targetUrl = `https://www.youtube.com/watch?v=${ytEmbed[1]}`;
        referer = targetUrl;
      } else {
        referer = formatReferer(best.initiator, tab.url);
      }
      filename = best.name || tab.title || 'Video';
      kind = 'video';
      qualities = best.videoQualities || [];
      audios = best.audioTracks || [];
      subs = best.subtitles || [];
    } else {
      const isKnownVideoHost = /(tiktok\.com|twitter\.com|x\.com|vimeo\.com|dailymotion\.com|instagram\.com|twitch\.tv|reddit\.com)/i.test(tab.url);
      if (isKnownVideoHost) {
        targetUrl = tab.url;
        filename = tab.title || 'Video';
        kind = 'video';
        referer = tab.url;
      } else {
        const direct = await sendTabMessageWithTimeout(tab.id, { type: 'getDirectMediaUrl' }, 1200);
        if (direct && direct.url && !direct.url.startsWith('blob:') && !/\.html?(\?|$)/i.test(direct.url)) {
          targetUrl = direct.url;
          filename = direct.name || tab.title || 'Video';
          kind = 'video';
          referer = tab.url;
        } else {
          $('pageVideo').textContent = t('popupPlayFirst') || 'Önce medyayı oynatın';
          setTimeout(() => ($('pageVideo').textContent = t('popupPageVideo')), 2500);
          return;
        }
      }
    }
  }

  filename = filename.replace(/^(?:youtube\s*(?:video\s*player|frame)?|iframe)\s*[-–|•]?\s*/i, '').trim();
  if (isGenericMediaName(filename) || /^(?:youtube|video|iframe)$/i.test(filename.toLowerCase())) {
    filename = cleanMediaTitle(tab?.title) || (tab?.title || 'Video').replace(/\s*-\s*YouTube$/i, '').trim();
  }

  $('pageVideo').textContent = t('popupSending');
  try {
    const finalSubs = (subs && subs.length) ? subs : ((activeMedia?.subtitles && activeMedia.subtitles.length) ? activeMedia.subtitles : await getTabSubtitles(tab?.id));
    const chosenSub = chooseBestSubUrl(finalSubs, 'tr');
    const finalAudios = (audios && audios.length) ? audios : ((activeMedia?.audioTracks && activeMedia.audioTracks.length) ? activeMedia.audioTracks : (best?.audioTracks || []));
    const finalQualities = (qualities && qualities.length) ? qualities : ((activeMedia?.videoQualities && activeMedia.videoQualities.length) ? activeMedia.videoQualities : (best?.videoQualities || []));

    const r = await send({
      type: 'download',
      url: targetUrl,
      filename,
      referer,
      kind: 'video',
      writeSubtitles: true,
      subtitles: finalSubs,
      subtitleUrl: chosenSub,
      subtitleLangs: 'tr,en',
      videoQualities: finalQualities,
      audioTracks: finalAudios
    });
    if (r && r.ok) {
      $('pageVideo').textContent = t('popupStarted');
    } else if (r && r.cancelled) {
      $('pageVideo').textContent = t('popupPageVideo');
      return;
    } else {
      const isUnauth = r && r.error && r.error.toLowerCase().includes('unauthorized');
      $('pageVideo').textContent = isUnauth ? t('popupNotPaired') : t('popupFailed');
      if (isUnauth) refresh();
    }
  } catch (err) {
    $('pageVideo').textContent = t('popupFailed');
  }
  loadDownloads();
  setTimeout(() => ($('pageVideo').textContent = t('popupPageVideo')), 2500);
};
$('pageLink').onclick = async () => {
  tab = tab || (await currentTab());
  if (!tab?.url) return;
  await send({ type: 'download', url: tab.url, referer: tab.url, kind: 'file' });
  window.close();
};
$('launch').onclick = async () => {
  await send({ type: 'launchApp' });
  setTimeout(refresh, 1500);
};
$('intercept').onchange = (e) => send({ type: 'setSettings', settings: { intercept: e.target.checked } });
$('sniff').onchange = (e) => send({ type: 'setSettings', settings: { sniff: e.target.checked } });
$('autoLaunch').onchange = (e) => send({ type: 'setSettings', settings: { autoLaunch: e.target.checked } });
$('port').onchange = (e) => send({ type: 'setSettings', settings: { port: parseInt(e.target.value, 10) || 48213 } }).then(refresh);
$('minSize').onchange = (e) => send({ type: 'setSettings', settings: { minSizeMB: parseInt(e.target.value, 10) || 0 } });

$('pairBtn').onclick = async () => {
  let val = $('pairCode').value.trim();
  if (!val) {
    try {
      val = (await navigator.clipboard.readText()).trim();
      $('pairCode').value = val;
    } catch {}
  }
  const r = await send({ type: 'setToken', token: val });
  $('pairMsg').textContent = r && r.ok ? t('popupPairOk') : t('popupPairBad');
  if (r && r.ok) { $('pairCode').value = ''; refresh(); }
};

initPalette();
if ($('themeToggle')) {
  $('themeToggle').onclick = async () => {
    userToggledTheme = true;
    const nextTheme = currentThemeMode === 'light' ? 'dark' : 'light';
    applyThemeMode(nextTheme);
    await api.storage.local.set({ themeMode: nextTheme });
    send({ type: 'setAppConfig', themeMode: nextTheme });
  };
}
$('langToggle').onclick = async () => {
  userToggledLang = true;
  currentLang = currentLang === 'tr' ? 'en' : 'tr';
  updateUiTexts();
  await api.storage.local.set({ language: currentLang });
  send({ type: 'setAppConfig', language: currentLang });
  refresh();
};

api.storage.local.get(['accentColor', 'language', 'themeMode'], (res) => {
  if (res) {
    if (res.accentColor) applyAccentColor(res.accentColor);
    if (res.themeMode) applyThemeMode(res.themeMode);
    if (res.language) { currentLang = res.language; updateUiTexts(); }
  }
});

refresh();
loadMedia();
loadDownloads();
dlTimer = setInterval(loadDownloads, 1000);
