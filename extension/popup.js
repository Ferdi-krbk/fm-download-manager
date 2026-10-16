'use strict';
const api = typeof browser !== 'undefined' ? browser : chrome;
const $ = (id) => document.getElementById(id);
const t = (k, subs) => { try { return api.i18n.getMessage(k, subs) || k; } catch { return k; } };

document.querySelectorAll('[data-i18n]').forEach((el) => { el.textContent = t(el.dataset.i18n); });
document.querySelectorAll('[data-i18n-placeholder]').forEach((el) => { el.placeholder = t(el.dataset.i18nPlaceholder); });
document.documentElement.lang = (() => { try { return api.i18n.getUILanguage(); } catch { return 'en'; } })();

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

async function loadMedia() {
  tab = await currentTab();
  const list = (tab && (await send({ type: 'getMedia', tabId: tab.id }))) || [];
  const ul = $('media');
  ul.innerHTML = '';
  // Filter out redundant audio-only chunks if real video exists
  const hasVideo = list.some(m => /-v\d+|video/i.test(m.url) || (m.size && m.size > 20 * 1024 * 1024));
  const filtered = list.filter(m => {
    if (hasVideo && /sublist_aud|audio[_-]track|-a\d+\.m3u8/i.test(m.url) && !/-v\d+/i.test(m.url)) return false;
    return true;
  });

  $('count').textContent = filtered.length ? `(${filtered.length})` : '';
  $('noMedia').classList.toggle('hidden', filtered.length > 0);
  for (const m of filtered) {
    const li = document.createElement('li');
    const info = document.createElement('div'); info.className = 'info';
    const name = document.createElement('div'); name.className = 'name'; name.textContent = m.name || m.url; name.title = m.name || m.url;
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
      const k = m.kind === 'video' || m.kind === 'hls' || m.kind === 'dash' ? 'video' : 'file';
      const isVid = k === 'video';
      const ref = (m.initiator && !m.initiator.startsWith('chrome-extension://')) ? (m.initiator.endsWith('/') ? m.initiator : m.initiator + '/') : tab.url;
      const subs = isVid ? await getTabSubtitles(tab?.id) : [];
      const chosenSub = isVid ? chooseBestSubUrl(subs, 'tr') : '';

      const r = await send({
        type: 'download',
        url: m.url,
        filename: k === 'file' ? m.name : '',
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

  const isKnownVideoHost = /(youtube\.com|youtu\.be|tiktok\.com|twitter\.com|x\.com|vimeo\.com|dailymotion\.com|instagram\.com|twitch\.tv|reddit\.com)/i.test(tab.url);

  let targetUrl = tab.url;
  let kind = 'video';
  let filename = tab.title || '';
  let referer = tab.url;
  let best = null;
  let activeMedia = null;

  if (isKnownVideoHost) {
    // Known video platform (YouTube, TikTok, Twitter): ALWAYS use the active page URL & title!
    targetUrl = tab.url;
    filename = tab.title || '';
    kind = 'video';
    referer = tab.url;
    try {
      activeMedia = await sendTabMessageWithTimeout(tab.id, { type: 'getActiveMedia' }, 1200);
      if (activeMedia && activeMedia.url) targetUrl = activeMedia.url;
      if (activeMedia && activeMedia.name) filename = activeMedia.name;
    } catch {}
  } else {
    const mediaList = (await send({ type: 'getMedia', tabId: tab.id })) || [];
    best = pickBestMedia(mediaList);

    if (best) {
      targetUrl = best.url;
      filename = best.name || tab.title || '';
      kind = best.kind === 'hls' || best.kind === 'dash' || best.kind === 'video' ? 'video' : 'file';
      if (best.initiator && !best.initiator.startsWith('chrome-extension://')) {
        referer = best.initiator.endsWith('/') ? best.initiator : best.initiator + '/';
      }
    } else {
      // Generic website with no captured media yet: check for playing video/audio element
      const direct = await sendTabMessageWithTimeout(tab.id, { type: 'getDirectMediaUrl' }, 1200);

      if (direct && direct.url && !direct.url.startsWith('blob:')) {
        targetUrl = direct.url;
        filename = direct.name || '';
        kind = /\.m3u8(\?|$)/i.test(direct.url) ? 'video' : 'file';
      } else {
        // No media detected on generic page (e.g. NotebookLM before clicking play):
        // Alert user instead of sending unsupported webpage URL to yt-dlp
        $('pageVideo').textContent = t('popupPlayFirst') || 'Önce medyayı oynatın';
        setTimeout(() => ($('pageVideo').textContent = t('popupPageVideo')), 2500);
        return;
      }
    }
  }

  $('pageVideo').textContent = t('popupSending');
  try {
    const isVid = kind === 'video' || kind === 'hls' || kind === 'dash';
    const subs = isVid ? ((activeMedia?.subtitles && activeMedia.subtitles.length) ? activeMedia.subtitles : await getTabSubtitles(tab?.id)) : [];
    const chosenSub = isVid ? chooseBestSubUrl(subs, 'tr') : '';
    const audios = (activeMedia?.audioTracks && activeMedia.audioTracks.length) ? activeMedia.audioTracks : (best?.audioTracks || []);
    const qualities = (activeMedia?.videoQualities && activeMedia.videoQualities.length) ? activeMedia.videoQualities : (best?.videoQualities || []);

    const r = await send({
      type: 'download',
      url: targetUrl,
      filename,
      referer,
      kind,
      writeSubtitles: isVid,
      subtitles: subs,
      subtitleUrl: chosenSub,
      subtitleLangs: 'tr,en',
      videoQualities: qualities,
      audioTracks: audios
    });
    if (r && r.ok) {
      $('pageVideo').textContent = t('popupStarted');
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

refresh();
loadMedia();
loadDownloads();
dlTimer = setInterval(loadDownloads, 1000);
