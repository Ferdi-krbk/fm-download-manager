// FM Download Manager — IDM-Style Modern Floating Video Download Bar
// Isolated Shadow DOM, Glassmorphic UI, Auto Video Tracking, 100% Non-Intrusive.
(() => {
  'use strict';
  const api = typeof browser !== 'undefined' ? browser : chrome;

  // Prevent multiple injections in the same frame
  if (window.__fmFloatingBarLoaded) return;
  window.__fmFloatingBarLoaded = true;

  // Never show floating video download bar on Office / Word Online / Docs / Productivity web apps
  const EXCLUDED_HOSTS = /(^|\.)(office\.com|office\.live\.com|officeapps\.live\.com|office365\.com|sharepoint\.com|onedrive\.live\.com|live\.com|microsoft365\.com|docs\.google\.com|sheets\.google\.com|slides\.google\.com|drive\.google\.com|mail\.google\.com|outlook\.live\.com|outlook\.office\.com|notion\.so|figma\.com|canva\.com)$/i;
  if (EXCLUDED_HOSTS.test(location.hostname)) return;

  // If running inside a subframe, exit immediately for tracking, auth, or non-embed YouTube frames
  const isTopWindow = window.self === window.top;
  if (!isTopWindow) {
    if (/(studio\.youtube\.com|accounts\.google\.com|googleads|doubleclick)/i.test(location.hostname)) return;
    if (/(youtube\.com|youtu\.be)/i.test(location.hostname) && !location.pathname.startsWith('/embed/')) return;
  }

  // Minimum dimensions to be considered a real video (ignores avatars, ads, stickers)
  const MIN_WIDTH = 220;
  const MIN_HEIGHT = 130;

  // Active tracking state
  let currentVideo = null;
  let lastActiveVideo = null;
  let lastReportedUrl = null;
  let hideTimer = null;
  let updatePositionRaf = null;

  // Host element & shadow root for 100% CSS isolation
  let hostEl = null;
  let shadowRoot = null;
  let barContainer = null;
  let dropdownMenu = null;
  let mainBtn = null;
  let statusText = null;
  let mainMenuEl = null;
  let subsPanelEl = null;
  let subsListEl = null;
  // Bilingual dictionary & dynamic theme support
  let currentLang = 'tr';
  let currentAccent = '#E50914';

  const I18N = {
    tr: {
      mainBtnTitle: 'FM Download Manager ile İndir',
      downloadVideo: 'Videoyu İndir',
      downloadOptions: 'İndirme Seçenekleri',
      close: 'Kapat',
      optVideoText: 'Video Olarak İndir',
      optVideoSub: 'En Yüksek',
      optAudioText: 'Sadece Ses İndir',
      optAudioSub: 'MP3',
      optSubsText: 'Altyazı İndir',
      optSubsSub: 'SRT ›',
      subsBack: '‹ Geri',
      subsTitle: 'Altyazı Dili Seçin',
      searchingSubs: 'Altyazılar aranıyor...',
      noSubsFound: 'Harici altyazı akışı bulunamadı.',
      subsHint: '(Oynatıcıda altyazıyı açıp tekrar deneyin)',
      noSubsAlert: 'Altyazı Yok!',
      fetchingSubs: 'Altyazı Alınıyor! 🚀',
      sentToApp: 'Gönderildi! 🚀'
    },
    en: {
      mainBtnTitle: 'Download with FM Download Manager',
      downloadVideo: 'Download Video',
      downloadOptions: 'Download Options',
      close: 'Close',
      optVideoText: 'Download Video',
      optVideoSub: 'Highest',
      optAudioText: 'Download Audio Only',
      optAudioSub: 'MP3',
      optSubsText: 'Download Subtitles',
      optSubsSub: 'SRT ›',
      subsBack: '‹ Back',
      subsTitle: 'Select Subtitle Language',
      searchingSubs: 'Searching subtitles...',
      noSubsFound: 'No external subtitle streams found.',
      subsHint: '(Turn on captions in player and retry)',
      noSubsAlert: 'No Subtitles!',
      fetchingSubs: 'Fetching Subtitles! 🚀',
      sentToApp: 'Sent to FM! 🚀'
    }
  };

  function t(k) {
    const d = I18N[currentLang] || I18N.tr;
    return d[k] || I18N.tr[k] || k;
  }

  function hexToRgba(hex, alpha) {
    hex = (hex || '#E50914').replace('#', '');
    if (hex.length === 3) hex = hex.split('').map(ch => ch + ch).join('');
    const num = parseInt(hex, 16) || 0xE50914;
    const r = (num >> 16) & 255;
    const g = (num >> 8) & 255;
    const b = num & 255;
    return `rgba(${r}, ${g}, ${b}, ${alpha})`;
  }

  function lightenHex(hex, amt = 25) {
    hex = (hex || '#E50914').replace('#', '');
    if (hex.length === 3) hex = hex.split('').map(ch => ch + ch).join('');
    const num = parseInt(hex, 16) || 0xE50914;
    const r = Math.min(255, ((num >> 16) & 255) + amt);
    const g = Math.min(255, ((num >> 8) & 255) + amt);
    const b = Math.min(255, (num & 255) + amt);
    return `#${((1 << 24) + (r << 16) + (g << 8) + b).toString(16).slice(1)}`;
  }

  function applyTheme(accentHex, lang) {
    if (accentHex) currentAccent = accentHex;
    if (lang) currentLang = lang;
    if (shadowRoot && shadowRoot.host) {
      shadowRoot.host.style.setProperty('--fm-accent', currentAccent);
      shadowRoot.host.style.setProperty('--fm-accent-glow', hexToRgba(currentAccent, 0.45));
      shadowRoot.host.style.setProperty('--fm-accent-dim', hexToRgba(currentAccent, 0.15));
      shadowRoot.host.style.setProperty('--fm-accent-hover', lightenHex(currentAccent, 25));
    }
    updateBarTexts();
  }

  function updateBarTexts() {
    if (!mainBtn) return;
    mainBtn.title = t('mainBtnTitle');
    if (statusText && !mainBtn.classList.contains('fm-success')) {
      statusText.textContent = t('downloadVideo');
    }
    const arrow = shadowRoot?.getElementById('fm-arrow-btn');
    if (arrow) arrow.title = t('downloadOptions');
    const closeB = shadowRoot?.getElementById('fm-close-btn');
    if (closeB) closeB.title = t('close');

    const vItem = shadowRoot?.getElementById('fm-opt-video');
    if (vItem) {
      const spans = vItem.querySelectorAll('span');
      if (spans[1]) spans[1].textContent = t('optVideoText');
      if (spans[2]) spans[2].textContent = t('optVideoSub');
    }
    const aItem = shadowRoot?.getElementById('fm-opt-audio');
    if (aItem) {
      const spans = aItem.querySelectorAll('span');
      if (spans[1]) spans[1].textContent = t('optAudioText');
      if (spans[2]) spans[2].textContent = t('optAudioSub');
    }
    const sItem = shadowRoot?.getElementById('fm-opt-subs');
    if (sItem) {
      const spans = sItem.querySelectorAll('span');
      if (spans[1]) spans[1].textContent = t('optSubsText');
      if (spans[2]) spans[2].textContent = t('optSubsSub');
    }
    const sbBack = shadowRoot?.getElementById('fm-subs-back');
    if (sbBack) sbBack.textContent = t('subsBack');
    const sbTitle = shadowRoot?.querySelector('.fm-subs-title');
    if (sbTitle) sbTitle.textContent = t('subsTitle');
  }

  // Load and listen to theme / language from storage
  try {
    if (typeof chrome !== 'undefined' && chrome.storage && chrome.storage.local) {
      chrome.storage.local.get(['accentColor', 'language'], (res) => {
        if (res) {
          if (res.accentColor) currentAccent = res.accentColor;
          if (res.language) currentLang = res.language;
          applyTheme(currentAccent, currentLang);
        }
      });
      chrome.storage.onChanged.addListener((changes, area) => {
        if (area === 'local') {
          let ch = false;
          if (changes.accentColor) { currentAccent = changes.accentColor.newValue; ch = true; }
          if (changes.language) { currentLang = changes.language.newValue; ch = true; }
          if (ch) applyTheme(currentAccent, currentLang);
        }
      });
    }
  } catch {}


  // Dismissal tracking state (per-view in-memory, resets on new video/navigation/reload)
  let isDismissedForPage = false;
  let lastTrackedUrl = location.href;

  function isPageDismissed() {
    return isDismissedForPage;
  }

  try {
    for (let i = sessionStorage.length - 1; i >= 0; i--) {
      const k = sessionStorage.key(i);
      if (k && k.startsWith('fm_bar_dismissed_')) sessionStorage.removeItem(k);
    }
  } catch {}

  const barStyles = `
    :host {
      --fm-accent: #E50914;
      --fm-accent-glow: rgba(229, 9, 20, 0.45);
      --fm-accent-dim: rgba(229, 9, 20, 0.15);
      --fm-accent-hover: #ff2d46;
    }

    *, *::before, *::after {
      box-sizing: border-box;
      margin: 0;
      padding: 0;
      font-family: -apple-system, BlinkMacSystemFont, "Segoe UI", Roboto, Helvetica, Arial, sans-serif;
      user-select: none;
    }

    .fm-wrap {
      position: absolute;
      display: flex;
      flex-direction: column;
      align-items: flex-end;
      pointer-events: none;
      transition: opacity 0.22s cubic-bezier(0.16, 1, 0.3, 1), transform 0.22s cubic-bezier(0.16, 1, 0.3, 1);
      opacity: 0;
      transform: translateX(-100%) translateY(-6px) scale(0.96);
      will-change: transform, opacity;
    }

    .fm-wrap.fm-visible {
      opacity: 1;
      transform: translateX(-100%) translateY(0) scale(1);
      pointer-events: auto;
    }

    /* Glassmorphic Pill Button */
    .fm-bar {
      display: inline-flex;
      align-items: center;
      gap: 2px;
      background: rgba(15, 17, 23, 0.90);
      backdrop-filter: blur(14px);
      -webkit-backdrop-filter: blur(14px);
      border: 1px solid var(--fm-accent, #E50914);
      border-radius: 20px;
      padding: 3px 5px 3px 4px;
      box-shadow: 0 4px 18px rgba(0, 0, 0, 0.45), 0 0 12px var(--fm-accent-glow, rgba(229, 9, 20, 0.25));
      transition: all 0.2s cubic-bezier(0.16, 1, 0.3, 1);
    }

    .fm-bar:hover {
      background: rgba(22, 25, 35, 0.96);
      border-color: var(--fm-accent-hover, #ff2d46);
      box-shadow: 0 6px 24px rgba(0, 0, 0, 0.6), 0 0 18px var(--fm-accent-glow, rgba(229, 9, 20, 0.45));
    }

    .fm-btn-action {
      display: inline-flex;
      align-items: center;
      gap: 6px;
      padding: 3px 6px 3px 3px;
      cursor: pointer;
      border-radius: 14px;
      transition: background 0.15s;
    }

    .fm-btn-action:hover {
      background: rgba(255, 255, 255, 0.08);
    }

    .fm-logo-badge {
      width: 20px;
      height: 20px;
      border-radius: 6px;
      background: var(--fm-accent, #E50914);
      display: flex;
      align-items: center;
      justify-content: center;
      color: #fff;
      font-weight: 800;
      font-size: 10px;
      letter-spacing: -0.5px;
      box-shadow: 0 2px 6px var(--fm-accent-glow, rgba(229, 9, 20, 0.5));
    }

    .fm-title {
      font-size: 12px;
      font-weight: 600;
      color: #f1f5f9;
      letter-spacing: 0.1px;
      white-space: nowrap;
    }

    .fm-arrow-btn {
      display: inline-flex;
      align-items: center;
      justify-content: center;
      padding: 4px 5px;
      cursor: pointer;
      border-radius: 6px;
      transition: background 0.15s;
    }

    .fm-arrow-btn:hover {
      background: rgba(255, 255, 255, 0.12);
    }

    .fm-arrow {
      width: 12px;
      height: 12px;
      color: #94a3b8;
      transition: transform 0.2s ease;
    }

    .fm-wrap.fm-open .fm-arrow {
      transform: rotate(180deg);
    }

    .fm-close-btn {
      width: 17px;
      height: 17px;
      border-radius: 50%;
      display: flex;
      align-items: center;
      justify-content: center;
      color: #64748b;
      font-size: 11px;
      cursor: pointer;
      margin-left: 2px;
      transition: background 0.15s, color 0.15s;
    }

    .fm-close-btn:hover {
      background: rgba(255, 255, 255, 0.2);
      color: #cbd5e1;
    }

    /* Success State */
    .fm-bar.fm-success {
      background: rgba(6, 78, 59, 0.94);
      border-color: rgba(16, 185, 129, 0.85);
      box-shadow: 0 4px 18px rgba(0, 0, 0, 0.4), 0 0 16px rgba(16, 185, 129, 0.4);
    }

    .fm-bar.fm-success .fm-title {
      color: #6ee7b7;
    }

    /* Dropdown Menu */
    .fm-menu {
      margin-top: 6px;
      background: rgba(15, 17, 23, 0.95);
      backdrop-filter: blur(16px);
      -webkit-backdrop-filter: blur(16px);
      border: 1px solid rgba(45, 52, 70, 0.85);
      border-radius: 12px;
      padding: 5px;
      min-width: 195px;
      box-shadow: 0 10px 30px rgba(0, 0, 0, 0.65), 0 0 15px var(--fm-accent-dim, rgba(229, 9, 20, 0.15));
      display: none;
      flex-direction: column;
      gap: 3px;
    }

    .fm-wrap.fm-open .fm-menu {
      display: flex;
      animation: fmFadeDown 0.18s cubic-bezier(0.16, 1, 0.3, 1) forwards;
    }

    @keyframes fmFadeDown {
      from { opacity: 0; transform: translateY(-4px); }
      to { opacity: 1; transform: translateY(0); }
    }

    .fm-item {
      display: flex;
      align-items: center;
      gap: 8px;
      padding: 7px 10px;
      border-radius: 8px;
      font-size: 12px;
      font-weight: 500;
      color: #e2e8f0;
      cursor: pointer;
      transition: background 0.15s, color 0.15s;
    }

    .fm-item:hover {
      background: var(--fm-accent-dim, rgba(229, 9, 20, 0.25));
      color: #ffffff;
    }

    .fm-item-icon {
      font-size: 14px;
    }

    .fm-item-sub {
      font-size: 10px;
      color: #94a3b8;
      margin-left: auto;
    }

    .fm-subs-panel {
      display: flex;
      flex-direction: column;
      gap: 3px;
    }

    .fm-subs-header {
      display: flex;
      align-items: center;
      gap: 6px;
      padding: 3px 6px 6px 6px;
      border-bottom: 1px solid rgba(255, 255, 255, 0.08);
      margin-bottom: 4px;
    }

    .fm-subs-back {
      font-size: 11px;
      color: var(--fm-accent, #E50914);
      cursor: pointer;
      font-weight: 700;
      padding: 2px 4px;
      border-radius: 4px;
    }

    .fm-subs-back:hover {
      color: var(--fm-accent-hover, #ff2d46);
      background: var(--fm-accent-dim, rgba(229, 9, 20, 0.15));
    }

    .fm-subs-title {
      font-size: 11px;
      color: #94a3b8;
      font-weight: 600;
    }

    .fm-subs-list {
      display: flex;
      flex-direction: column;
      gap: 2px;
      max-height: 180px;
      overflow-y: auto;
    }

    .fm-sub-item {
      display: flex;
      align-items: center;
      justify-content: space-between;
      padding: 5px 8px;
      border-radius: 6px;
      font-size: 11.5px;
      color: #e2e8f0;
      cursor: pointer;
      transition: background 0.15s;
    }

    .fm-sub-item:hover {
      background: var(--fm-accent-dim, rgba(229, 9, 20, 0.25));
      color: #ffffff;
    }

    .fm-sub-code {
      font-size: 9.5px;
      color: #94a3b8;
      background: rgba(255, 255, 255, 0.08);
      padding: 1px 5px;
      border-radius: 4px;
    }
  `;

  // Subtitle track detection across network sniff, DOM <track>, and page script JSON
  function resolveSubtitleMetadata(url, rawCode, rawLabel) {
    const u = (url || '').toLowerCase();
    let code = (rawCode || '').toLowerCase().trim();
    let label = (rawLabel || '').trim();
    const isForced = /forced|zorunlu/i.test(u) || /forced|zorunlu/i.test(label);
    const isAuto = /[?&]kind=asr\b/i.test(u) || /otomatik|auto/i.test(label);

    const langMap = {
      tr: 'Türkçe',
      en: 'English',
      pt: 'Português',
      es: 'Español',
      fr: 'Français',
      de: 'Deutsch',
      it: 'Italiano',
      ru: 'Русский',
      ar: 'العربية',
      ja: '日本語',
      ko: '한국어',
      zh: '中文',
      nl: 'Nederlands',
      pl: 'Polski',
      uk: 'Українська',
      az: 'Azərbaycan'
    };

    if (code.includes('-')) code = code.split('-')[0];
    if (code.includes('_')) code = code.split('_')[0];

    // Check explicit query parameters first (e.g. YouTube /api/timedtext?...&lang=tr or &tlang=tr)
    if (!code || code === 'und' || !langMap[code]) {
      const qMatch = (url || '').match(/[?&](?:tlang|lang|hl)=([a-zA-Z0-9_-]+)/i);
      if (qMatch && qMatch[1]) {
        const qLang = qMatch[1].toLowerCase().split(/[-_]/)[0];
        if (langMap[qLang] || qLang.length === 2) {
          code = qLang;
        }
      }
    }

    if (!code || code === 'und' || !langMap[code]) {
      const m = u.match(/(?:^|[-_./])(tr|tur|turkish|turkce|en|eng|english|pt|por|portuguese|es|spa|spanish|fr|fre|french|de|ger|german|it|ita|italian|ru|rus|russian|ar|ara|arabic)(?:[-_./?]|$)/i);
      if (m) {
        const k = m[1].toLowerCase();
        if (k.startsWith('tr') || k.startsWith('tur')) code = 'tr';
        else if (k.startsWith('en') || k.startsWith('eng')) code = 'en';
        else if (k.startsWith('pt') || k.startsWith('por')) code = 'pt';
        else if (k.startsWith('es') || k.startsWith('spa')) code = 'es';
        else if (k.startsWith('fr')) code = 'fr';
        else if (k.startsWith('de') || k.startsWith('ger')) code = 'de';
        else if (k.startsWith('it')) code = 'it';
        else if (k.startsWith('ru')) code = 'ru';
        else if (k.startsWith('ar')) code = 'ar';
      } else if (u.includes('altyazi') || /türkçe|turkish/i.test(label)) {
        code = 'tr';
      } else if (/english|ingilizce/i.test(label)) {
        code = 'en';
      } else if (!code) {
        code = 'und';
      }
    }

    const baseName = langMap[code] || (label && !label.toLowerCase().includes('altyazı') ? label : 'Altyazı');
    let finalLabel = baseName;
    if (isForced && !finalLabel.toLowerCase().includes('zorunlu')) finalLabel = `${baseName} (Zorunlu)`;
    else if (isAuto && !finalLabel.toLowerCase().includes('otomatik')) finalLabel = `${baseName} (Otomatik)`;
    return { code, label: finalLabel, isForced, isAuto };
  }

  function normalizeResolutionTier(width, height, qualityLabel) {
    if (qualityLabel) {
      const m = String(qualityLabel).match(/\b(4320|2160|1440|1080|720|480|360|240|144)p/i);
      if (m) return parseInt(m[1], 10);
    }
    const w = width || 0;
    const h = height || 0;
    const longSide = Math.max(w, h);
    const shortSide = w > 0 && h > 0 ? Math.min(w, h) : h;
    if (longSide >= 7600 || shortSide >= 4320) return 4320;
    if (longSide >= 3800 || shortSide >= 2160) return 2160;
    if (longSide >= 2500 || shortSide >= 1440) return 1440;
    if (longSide >= 1900 || shortSide >= 1080) return 1080;
    if (longSide >= 1260 || shortSide >= 720) return 720;
    if (longSide >= 840 || shortSide >= 480) return 480;
    if (longSide >= 630 || shortSide >= 360) return 360;
    return shortSide > 0 ? shortSide : h;
  }

  function formatQualityLabel(h) {
    if (h >= 4320) return `${h}p 8K Ultra HD`;
    if (h >= 2160) return `${h}p 4K Ultra HD`;
    if (h >= 1440) return `${h}p 2K QHD`;
    if (h >= 1080) return `${h}p Full HD`;
    if (h >= 720) return `${h}p HD`;
    if (h >= 480) return `${h}p SD`;
    if (h >= 360) return `${h}p Düşük`;
    return `${h}p`;
  }

  function extractYouTubeMetadata() {
    if (!/(youtube\.com|youtu\.be)/i.test(location.hostname)) return null;
    try {
      let currentVideoId = null;
      try {
        const u = new URL(location.href);
        currentVideoId = u.searchParams.get('v');
        if (!currentVideoId && u.pathname.startsWith('/shorts/')) {
          currentVideoId = u.pathname.split('/shorts/')[1]?.split('/')[0] || null;
        }
      } catch {}

      const scripts = document.querySelectorAll('script');
      for (const s of scripts) {
        const text = s.textContent || '';
        if (!text.includes('ytInitialPlayerResponse')) continue;
        const idx = text.indexOf('ytInitialPlayerResponse');
        const start = text.indexOf('{', idx);
        if (start === -1) continue;

        let depth = 0, inString = false, escape = false, end = -1;
        for (let i = start; i < text.length; i++) {
          const ch = text[i];
          if (escape) { escape = false; continue; }
          if (ch === '\\') { escape = true; continue; }
          if (ch === '"') { inString = !inString; continue; }
          if (!inString) {
            if (ch === '{') depth++;
            else if (ch === '}') {
              depth--;
              if (depth === 0) { end = i + 1; break; }
            }
          }
        }
        if (end === -1) continue;

        const obj = JSON.parse(text.substring(start, end));
        if (!obj) continue;

        if (currentVideoId && obj.videoDetails?.videoId && obj.videoDetails.videoId !== currentVideoId) {
          continue;
        }

        const videoQualities = [];
        const seenHeights = new Set();
        const audioTracks = [];
        const seenAudios = new Set();
        const formats = [
          ...(obj.streamingData?.adaptiveFormats || []),
          ...(obj.streamingData?.formats || [])
        ];
        formats.forEach(f => {
          const isVideo = (f.mimeType && f.mimeType.startsWith('video/')) || (f.height && f.height > 0);
          if (isVideo) {
            const tier = normalizeResolutionTier(f.width || 0, f.height || 0, f.qualityLabel);
            if (tier > 0 && !seenHeights.has(tier)) {
              seenHeights.add(tier);
              videoQualities.push({
                height: tier,
                label: formatQualityLabel(tier),
                bitrate: f.bitrate ? Math.round(f.bitrate / 1000) : undefined
              });
            }
          }

          if (f.audioTrack && f.audioTrack.displayName) {
            const idParts = (f.audioTrack.id || '').split('.');
            const langCode = (idParts[0] || '').toLowerCase();
            const disp = f.audioTrack.displayName;
            const isDef = !!f.audioTrack.audioIsDefault || /original|orijinal/i.test(disp);
            const isTr = langCode === 'tr' || /türkçe|turkish/i.test(disp);
            const normTrackId = isDef ? 'original' : (isTr ? 'tr' : (langCode || f.audioTrack.id || disp));
            if (!seenAudios.has(normTrackId)) {
              seenAudios.add(normTrackId);
              audioTracks.push({
                trackId: normTrackId,
                lang: langCode,
                label: disp,
                isDefault: isDef
              });
            }
          }
        });
        videoQualities.sort((a, b) => b.height - a.height);

        const subtitles = [];
        const seenSubs = new Set();
        const caps = obj.captions?.playerCaptionsTracklistRenderer?.captionTracks || [];
        caps.forEach(c => {
          const code = (c.languageCode || '').toLowerCase();
          const rawLabel = c.name?.simpleText || (c.name?.runs && c.name.runs[0]?.text) || code;
          const isAuto = (c.vssId && c.vssId.startsWith('a.')) || /otomatik|auto/i.test(rawLabel);
          const label = isAuto && !rawLabel.toLowerCase().includes('otomatik') ? `${rawLabel} (Otomatik)` : rawLabel;
          let u = c.baseUrl;
          if (u && !/[?&]fmt=/i.test(u)) {
            u += (u.includes('?') ? '&' : '?') + 'fmt=vtt';
          }
          if (u && !seenSubs.has(code + '_' + label)) {
            seenSubs.add(code + '_' + label);
            subtitles.push({
              url: u,
              code: code,
              lang: code,
              label: label,
              isForced: false,
              isAuto: isAuto
            });
          }
        });

        return { videoQualities, audioTracks, subtitles };
      }
    } catch (e) {}
    return null;
  }

  function isLikelySubtitleM3u8(urlLower, code, label) {
    if (!urlLower.includes('.m3u8')) return true;
    return /(sub|subs|subtitle|subtitles|caption|captions|vtt|webvtt|altyazi|text)/i.test(urlLower) ||
           !!(code && code !== 'und') ||
           !!(label && label.length > 0);
  }

  async function detectSubtitleTracks() {
    const rawList = [];

    function addTrack(url, code, label) {
      if (!url) return;
      const u = url.toLowerCase();
      // Exclude video/audio HLS playlists and media segments while allowing explicit subtitle playlists
      if (/sublist_(aud|vid|\d+)|master\.m3u8|index-f\d+/i.test(u)) return;
      if (u.includes('.m3u8') && !isLikelySubtitleM3u8(u, code, label)) return;
      if (/\/hls\//i.test(u) && !/\.(vtt|srt|sub|ass|m3u8)(\?|#|$)/i.test(u)) return;

      const meta = resolveSubtitleMetadata(url, code, label);
      rawList.push({ url, code: meta.code, lang: meta.code, label: meta.label, isForced: meta.isForced, isAuto: meta.isAuto });
    }

    // 1. YouTube in-page full metadata extraction
    const isYouTube = /(youtube\.com|youtu\.be)/i.test(location.hostname);
    if (isYouTube) {
      const ytMeta = extractYouTubeMetadata();
      if (ytMeta && ytMeta.subtitles && ytMeta.subtitles.length > 0) {
        ytMeta.subtitles.forEach(s => addTrack(s.url, s.code, s.label));
      }
    }

    // 2. Sniffed network subtitles from background.js
    try {
      const bgData = await new Promise((res) => {
        api.runtime.sendMessage({ type: 'getBestMedia' }, (r) => res(api.runtime.lastError ? null : r));
      });
      if (bgData?.subtitles) {
        bgData.subtitles.forEach(s => {
          if (s.url) addTrack(s.url, s.lang || s.code, s.label);
        });
      }
    } catch {}

    // 3. DOM <track> elements on document and inside video tags
    try {
      const videoEls = document.querySelectorAll('video');
      videoEls.forEach(v => {
        if (v.textTracks) {
          for (let i = 0; i < v.textTracks.length; i++) {
            const tt = v.textTracks[i];
            const code = tt.language || '';
            const label = tt.label || '';
            const trackTag = Array.from(v.querySelectorAll('track')).find(t => (t.srclang === code || t.label === label));
            const u = trackTag?.src || trackTag?.getAttribute('src');
            if (u) addTrack(u, code, label);
          }
        }
      });
      const tracks = document.querySelectorAll('track');
      tracks.forEach(t => {
        const src = t.src || t.getAttribute('src');
        const code = t.getAttribute('srclang') || t.getAttribute('lang') || '';
        const label = t.getAttribute('label') || '';
        if (src) addTrack(src, code, label);
      });
    } catch {}

    // 4. Script tags & player configurations
    try {
      const scripts = document.querySelectorAll('script');
      for (const s of scripts) {
        const rawText = s.textContent || '';
        if (!rawText) continue;
        const text = rawText.replace(/\\\//g, '/');

        // Generic player subtitle configs
        const subUrlMatches = text.match(/https?:\/\/[^\s"'<>]+\.(?:vtt|srt|ass|sub)(?:\?[^\s"'<>]*)?/gi) ||
                              text.match(/(?:\/|\.\.\/)[^\s"'<>]+\.(?:vtt|srt|ass|sub)(?:\?[^\s"'<>]*)?/gi);
        if (subUrlMatches) {
          subUrlMatches.forEach(rawU => {
            let u = rawU;
            if (u.startsWith('/') || u.startsWith('.')) {
              try { u = new URL(u, location.href).href; } catch {}
            }
            addTrack(u, '', '');
          });
        }
      }
    } catch {}

    // Deduplicate by pathname + language/vssId so YouTube /api/timedtext?lang=tr and ?lang=en are NEVER collapsed into one!
    const seenUrls = new Set();
    const list = [];
    rawList.forEach(item => {
      let norm = item.url;
      try {
        const parsed = new URL(item.url);
        const qLang = parsed.searchParams.get('tlang') || parsed.searchParams.get('lang') || item.code || '';
        const qKind = parsed.searchParams.get('kind') || (item.isAuto ? 'asr' : '') || (item.isForced ? 'forced' : '');
        norm = `${parsed.origin}${parsed.pathname}|${qLang}|${qKind}|${item.label || ''}`;
      } catch {
        norm = `${item.url}|${item.code || ''}|${item.label || ''}`;
      }
      if (!seenUrls.has(norm)) {
        seenUrls.add(norm);
        list.push(item);
      }
    });


    if (list && list.length > 0) {
      try {
        api.runtime.sendMessage({ type: 'pageSubtitles', subtitles: list }, () => {
          if (api.runtime.lastError) {}
        });
      } catch {}
    }

    return list;
  }

  // 1. Create the Isolated Shadow DOM Host with Pure DOM Elements
  // (100% immune to YouTube Trusted Types CSP require-trusted-types-for 'script')
  function ensureHost() {
    if (hostEl && hostEl.isConnected && barContainer) return;

    if (!hostEl) {
      hostEl = document.createElement('div');
      hostEl.id = 'fm-floating-bar-host';
      hostEl.style.cssText = 'all: initial; position: fixed; top: 0; left: 0; width: 100%; height: 100%; z-index: 2147483647; pointer-events: none; overflow: hidden;';

      shadowRoot = hostEl.attachShadow({ mode: 'open' });

      // Add CSS styles via textContent (100% immune to Trusted Types CSP)
      const styleEl = document.createElement('style');
      styleEl.textContent = barStyles;
      shadowRoot.appendChild(styleEl);

      // Build DOM nodes directly via standard DOM APIs
      barContainer = document.createElement('div');
      barContainer.className = 'fm-wrap';
      barContainer.id = 'fm-wrap';

      mainBtn = document.createElement('div');
      mainBtn.className = 'fm-bar';
      mainBtn.id = 'fm-main-btn';
      mainBtn.title = t('mainBtnTitle');

      const btnAction = document.createElement('div');
      btnAction.className = 'fm-btn-action';
      btnAction.id = 'fm-btn-action';

      const logoBadge = document.createElement('div');
      logoBadge.className = 'fm-logo-badge';
      logoBadge.textContent = 'FM';

      statusText = document.createElement('span');
      statusText.className = 'fm-title';
      statusText.id = 'fm-title';
      statusText.textContent = t('downloadVideo');

      btnAction.appendChild(logoBadge);
      btnAction.appendChild(statusText);

      const arrowBtn = document.createElement('div');
      arrowBtn.className = 'fm-arrow-btn';
      arrowBtn.id = 'fm-arrow-btn';
      arrowBtn.title = t('downloadOptions');

      const svgArrow = document.createElementNS('http://www.w3.org/2000/svg', 'svg');
      svgArrow.setAttribute('class', 'fm-arrow');
      svgArrow.setAttribute('viewBox', '0 0 24 24');
      svgArrow.setAttribute('fill', 'none');
      svgArrow.setAttribute('stroke', 'currentColor');
      svgArrow.setAttribute('stroke-width', '2.5');
      svgArrow.setAttribute('stroke-linecap', 'round');
      svgArrow.setAttribute('stroke-linejoin', 'round');
      const polyline = document.createElementNS('http://www.w3.org/2000/svg', 'polyline');
      polyline.setAttribute('points', '6 9 12 15 18 9');
      svgArrow.appendChild(polyline);
      arrowBtn.appendChild(svgArrow);

      const closeBtn = document.createElement('div');
      closeBtn.className = 'fm-close-btn';
      closeBtn.id = 'fm-close-btn';
      closeBtn.title = t('close');
      closeBtn.textContent = '✕';

      mainBtn.appendChild(btnAction);
      mainBtn.appendChild(arrowBtn);
      mainBtn.appendChild(closeBtn);
      barContainer.appendChild(mainBtn);

      dropdownMenu = document.createElement('div');
      dropdownMenu.className = 'fm-menu';
      dropdownMenu.id = 'fm-menu';

      mainMenuEl = document.createElement('div');
      mainMenuEl.id = 'fm-main-menu';

      function createMenuItem(id, icon, text, sub) {
        const item = document.createElement('div');
        item.className = 'fm-item';
        item.id = id;
        const ic = document.createElement('span');
        ic.className = 'fm-item-icon';
        ic.textContent = icon;
        const tx = document.createElement('span');
        tx.textContent = text;
        const sb = document.createElement('span');
        sb.className = 'fm-item-sub';
        sb.textContent = sub;
        item.appendChild(ic);
        item.appendChild(tx);
        item.appendChild(sb);
        return item;
      }

      const optVideo = createMenuItem('fm-opt-video', '🎬', t('optVideoText'), t('optVideoSub'));
      const optAudio = createMenuItem('fm-opt-audio', '🎵', t('optAudioText'), t('optAudioSub'));
      const optSubs = createMenuItem('fm-opt-subs', '💬', t('optSubsText'), t('optSubsSub'));

      mainMenuEl.appendChild(optVideo);
      mainMenuEl.appendChild(optAudio);
      mainMenuEl.appendChild(optSubs);
      dropdownMenu.appendChild(mainMenuEl);

      subsPanelEl = document.createElement('div');
      subsPanelEl.className = 'fm-subs-panel';
      subsPanelEl.id = 'fm-subs-panel';
      subsPanelEl.style.display = 'none';

      const subsHeader = document.createElement('div');
      subsHeader.className = 'fm-subs-header';

      const subsBack = document.createElement('span');
      subsBack.className = 'fm-subs-back';
      subsBack.id = 'fm-subs-back';
      subsBack.textContent = t('subsBack');

      const subsTitle = document.createElement('span');
      subsTitle.className = 'fm-subs-title';
      subsTitle.textContent = t('subsTitle');

      subsHeader.appendChild(subsBack);
      subsHeader.appendChild(subsTitle);
      subsPanelEl.appendChild(subsHeader);

      subsListEl = document.createElement('div');
      subsListEl.className = 'fm-subs-list';
      subsListEl.id = 'fm-subs-list';
      subsPanelEl.appendChild(subsListEl);

      dropdownMenu.appendChild(subsPanelEl);
            applyTheme(currentAccent, currentLang);
      barContainer.appendChild(dropdownMenu);
      shadowRoot.appendChild(barContainer);

      // 1. Direct Download on Action Click
      btnAction.addEventListener('click', (e) => {
        e.stopPropagation();
        barContainer.classList.remove('fm-open');
        triggerDownload('video');
      });

      // 2. Toggle Dropdown on Arrow Click
      arrowBtn.addEventListener('click', (e) => {
        e.stopPropagation();
        barContainer.classList.toggle('fm-open');
        if (!barContainer.classList.contains('fm-open')) {
          mainMenuEl.style.display = 'block';
          subsPanelEl.style.display = 'none';
        }
      });

      // 3. Main pill body click
      mainBtn.addEventListener('click', (e) => {
        e.stopPropagation();
        if (e.target === closeBtn || closeBtn.contains(e.target)) return;
        if (e.target === arrowBtn || arrowBtn.contains(e.target)) return;
        barContainer.classList.remove('fm-open');
        triggerDownload('video');
      });

      optVideo.addEventListener('click', (e) => {
        e.stopPropagation();
        barContainer.classList.remove('fm-open');
        triggerDownload('video');
      });

      optAudio.addEventListener('click', (e) => {
        e.stopPropagation();
        barContainer.classList.remove('fm-open');
        triggerDownload('audio');
      });

      optSubs.addEventListener('click', (e) => {
        e.stopPropagation();
        renderSubtitleChoices();
        mainMenuEl.style.display = 'none';
        subsPanelEl.style.display = 'flex';
      });

      subsBack.addEventListener('click', (e) => {
        e.stopPropagation();
        subsPanelEl.style.display = 'none';
        mainMenuEl.style.display = 'block';
      });

      // Event: Close / Dismiss (In-memory for current video/view only)
      closeBtn.addEventListener('click', (e) => {
        e.stopPropagation();
        e.preventDefault();
        clearTimeout(hideTimer);
        isDismissedForPage = true;
        hideBar();
      });

      // Keep visible while hovering over the bar itself
      barContainer.addEventListener('mouseenter', () => {
        clearTimeout(hideTimer);
      });
      barContainer.addEventListener('mouseleave', () => {
        scheduleHide(1500);
      });
    }

    const targetParent = document.fullscreenElement || document.webkitFullscreenElement || document.body || document.documentElement;
    if (targetParent && hostEl.parentElement !== targetParent) {
      targetParent.appendChild(hostEl);
    }
  }

  async function renderSubtitleChoices() {
    if (!subsListEl) return;
    subsListEl.textContent = '';
    const loadingDiv = document.createElement('div');
    loadingDiv.style.cssText = 'padding:10px;font-size:11px;color:#94a3b8;text-align:center;';
    loadingDiv.textContent = t('searchingSubs');
    subsListEl.appendChild(loadingDiv);

    const choices = await detectSubtitleTracks();
    subsListEl.textContent = '';
    if (!choices || choices.length === 0) {
      const noSubDiv = document.createElement('div');
      noSubDiv.style.cssText = 'padding:14px 8px;font-size:11px;color:#f87171;text-align:center;line-height:1.4;';
      noSubDiv.textContent = t('noSubsFound');
      const hint = document.createElement('div');
      hint.style.cssText = 'font-size:9.5px;color:#94a3b8;margin-top:4px;';
      hint.textContent = t('subsHint');
      noSubDiv.appendChild(hint);
      subsListEl.appendChild(noSubDiv);
      return;
    }

    // Sort: Non-forced Turkish first, then forced Turkish, then English, then other non-forced, then other forced
    choices.sort((a, b) => {
      const aTr = a.code === 'tr';
      const bTr = b.code === 'tr';
      if (aTr !== bTr) return aTr ? -1 : 1;
      if (aTr && bTr) {
        if (a.isForced !== b.isForced) return a.isForced ? 1 : -1;
      }
      const aEn = a.code === 'en';
      const bEn = b.code === 'en';
      if (aEn !== bEn) return aEn ? -1 : 1;
      return (a.label || '').localeCompare(b.label || '');
    });

    choices.forEach(c => {
      const row = document.createElement('div');
      row.className = 'fm-sub-item';
      const lbl = document.createElement('span');
      lbl.textContent = c.label;
      const code = document.createElement('span');
      code.className = 'fm-sub-code';
      code.textContent = c.code;
      row.appendChild(lbl);
      row.appendChild(code);
      row.addEventListener('click', (e) => {
        e.stopPropagation();
        barContainer.classList.remove('fm-open');
        mainMenuEl.style.display = 'block';
        subsPanelEl.style.display = 'none';
        triggerDownload('subtitles', c.code, c.url, choices);
      });
      subsListEl.appendChild(row);
    });
  }

  function findRealVideoOnPage() {
    if (currentVideo && currentVideo.isConnected && isRealDownloadableVideo(currentVideo)) {
      return currentVideo;
    }
    if (lastActiveVideo && lastActiveVideo.isConnected && isRealDownloadableVideo(lastActiveVideo)) {
      return lastActiveVideo;
    }
    const vids = Array.from(document.querySelectorAll('video')).filter(v => isRealDownloadableVideo(v));
    return vids.find(v => !v.paused && v.currentTime > 0) || vids[0] || null;
  }

  let lastReportTime = 0;
  async function reportActiveMediaToBackground() {
    try {
      const media = await resolveTargetMedia();
      if (media && media.url && !/^https?:\/\/[^/]+\/?$/i.test(media.url)) {
        if (/(persist_identity|googleads|doubleclick)/i.test(media.url)) return;
        const now = Date.now();
        if (media.url === lastReportedUrl && (now - lastReportTime < 1500)) return;
        lastReportedUrl = media.url;
        lastReportTime = now;
        api.runtime.sendMessage({ type: 'reportActiveMedia', media }, () => {
          if (api.runtime.lastError) {}
        });
      }
    } catch {}
  }

  // 2. Resolve Active Video / Stream URL
  async function resolveTargetMedia() {
    const isYouTube = /(youtube\.com|youtu\.be)/i.test(location.hostname);
    const isTwitter = /(twitter\.com|x\.com)/i.test(location.hostname);
    const isTikTok = /tiktok\.com/i.test(location.hostname);

    if (isYouTube) {
      const isTop = window.self === window.top;
      let ytUrl = location.href;
      if (!isTop) {
        const m = location.href.match(/(?:embed|v)\/([a-zA-Z0-9_-]{11})/i) || location.search.match(/[?&]v=([a-zA-Z0-9_-]{11})/i);
        if (m) ytUrl = `https://www.youtube.com/watch?v=${m[1]}`;
        else return null;
      } else {
        const m = location.href.match(/(?:v=|\/shorts\/)([a-zA-Z0-9_-]{11})/i);
        if (m) ytUrl = `https://www.youtube.com/watch?v=${m[1]}`;
        else {
          const v = findRealVideoOnPage();
          if (!v) return null;
        }
      }
      const ytTitleEl = document.querySelector('h1.ytd-watch-metadata yt-formatted-string, #title h1 yt-formatted-string, h1.title');
      let cleanTitle = (ytTitleEl ? ytTitleEl.textContent : '') || document.title || 'YouTube Video';
      cleanTitle = cleanTitle.replace(/^\(\d+\)\s*/, '').replace(/\s*-\s*YouTube$/i, '').trim();
      cleanTitle = cleanTitle.replace(/^(?:youtube\s*(?:video\s*player|frame)?|iframe)\s*[-–|•]?\s*/i, '').trim();
      if (!cleanTitle || cleanTitle.toLowerCase() === 'youtube') cleanTitle = 'YouTube Video';
      const ytMeta = extractYouTubeMetadata();
      return {
        url: ytUrl,
        name: cleanTitle,
        kind: 'video',
        referer: ytUrl,
        videoQualities: ytMeta?.videoQualities || [],
        audioTracks: ytMeta?.audioTracks || [],
        subtitles: ytMeta?.subtitles || []
      };
    }

    const videoToUse = findRealVideoOnPage();

    if (isTwitter) {
      if (location.pathname.includes('/status/')) {
        return { url: location.href.split('?')[0], name: document.title, kind: 'video', referer: location.href };
      }
      const a = videoToUse?.closest('article')?.querySelector('a[href*="/status/"]') || document.querySelector('article a[href*="/status/"]');
      if (a && a.href) {
        return { url: a.href.split('?')[0], name: document.title, kind: 'video', referer: location.href };
      }
    }

    if (isTikTok) {
      if (location.pathname.includes('/video/')) {
        return { url: location.href.split('?')[0], name: document.title, kind: 'video', referer: location.href };
      }
      const a = videoToUse?.closest('[data-e2e*="feed-item"], article, div')?.querySelector('a[href*="/video/"]') || document.querySelector('a[href*="/video/"]');
      if (a && a.href) {
        return { url: a.href.split('?')[0], name: document.title, kind: 'video', referer: location.href };
      }
    }

    // 1. Query background.js first for sniffed direct media stream (picks HIGHEST quality 4K/1080p and subtitles!)
    try {
      const bgData = await new Promise((res) => {
        api.runtime.sendMessage({ type: 'getBestMedia' }, (r) => res(api.runtime.lastError ? null : r));
      });
      if (bgData?.best?.url) {
        const isEmbedFrame = window.self !== window.top;
        return {
          url: bgData.best.url,
          name: bgData.best.name || document.title || 'video',
          kind: bgData.best.kind || 'video',
          referer: isEmbedFrame ? location.href : (bgData.best.initiator || location.href),
          subtitles: bgData.subtitles || [],
          videoQualities: bgData.best.videoQualities || [],
          audioTracks: bgData.best.audioTracks || []
        };
      }
    } catch {}

    // 2. Direct HTML5 video / HLS stream fallback
    if (videoToUse) {
      let src = videoToUse.currentSrc || videoToUse.src;
      if (!src) {
        const source = videoToUse.querySelector('source');
        if (source) src = source.src || source.getAttribute('src');
      }

      if (src && /^https?:/i.test(src) && !src.startsWith('blob:')) {
        return {
          url: src,
          name: document.title || 'video',
          kind: /\.m3u8(\?|$)/i.test(src) ? 'hls' : 'video',
          referer: location.href,
          subtitles: await detectSubtitleTracks()
        };
      }
    }

    return null;
  }

  // 3. Dispatch Download to Desktop App
  async function triggerDownload(format, subLang, subUrl, allSubs) {
    const isYouTube = /(youtube\.com|youtu\.be)/i.test(location.hostname);
    const subsList = allSubs || [];
    const chosenSubUrl = subUrl || (subsList.find(s => s.code === subLang || s.lang === subLang)?.url) || '';

    // Safety: prevent downloading video when user asked for subtitles only and none exist
    if (format === 'subtitles' && !chosenSubUrl && !isYouTube) {
      mainBtn.classList.remove('fm-success');
      statusText.textContent = t('noSubsAlert');
      setTimeout(() => {
        statusText.textContent = t('downloadVideo');
      }, 2000);
      return;
    }

    const detectedSubs = await detectSubtitleTracks();
    const media = await resolveTargetMedia();
    if (!media || !media.url) return;

    // Show visual feedback
    mainBtn.classList.add('fm-success');
    statusText.textContent = format === 'subtitles' ? 'Altyazı Alınıyor! ✓' : 'Gönderildi! ✓';

    const mergedSubs = [...subsList, ...(media.subtitles || []), ...detectedSubs];
    const finalSubList = [];
    const seenSubKeys = new Set();
    mergedSubs.forEach(s => {
      const k = s.url || (s.code + '_' + s.label);
      if (!seenSubKeys.has(k)) {
        seenSubKeys.add(k);
        finalSubList.push(s);
      }
    });

    function selectBestSub(list, targetLang) {
      if (!list || !list.length) return '';
      const req = targetLang || 'tr';

      // 1. Non-forced requested language
      const nonForcedMatch = list.find(s => (s.code === req || s.lang === req) && !s.isForced && !/forced|zorunlu/i.test(s.url || ''));
      if (nonForcedMatch?.url) return nonForcedMatch.url;

      // 2. Non-forced Turkish
      const nonForcedTr = list.find(s => (s.code === 'tr' || s.lang === 'tr') && !s.isForced && !/forced|zorunlu/i.test(s.url || ''));
      if (nonForcedTr?.url) return nonForcedTr.url;

      // 3. Forced requested language
      const forcedMatch = list.find(s => s.code === req || s.lang === req);
      if (forcedMatch?.url) return forcedMatch.url;

      // 4. Forced Turkish
      const forcedTr = list.find(s => s.code === 'tr' || s.lang === 'tr');
      if (forcedTr?.url) return forcedTr.url;

      // If requested was Turkish, NEVER fall back to Portuguese/French/Spanish
      if (req === 'tr') return '';

      return list[0]?.url || '';
    }

    const finalSubUrl = chosenSubUrl || selectBestSub(finalSubList, subLang);

    try {
      api.runtime.sendMessage({
        type: 'download',
        url: media.url,
        filename: media.name || '',
        referer: media.referer || location.href,
        kind: 'video',
        format: format,
        subtitlesOnly: format === 'subtitles',
        subtitleLangs: subLang || 'tr,en',
        writeSubtitles: format === 'video',
        subtitleUrl: finalSubUrl,
        subtitles: finalSubList,
        videoQualities: media.videoQualities || [],
        audioTracks: media.audioTracks || []
      }, (res) => {
        // Reset after 1.8 seconds
        setTimeout(() => {
          mainBtn.classList.remove('fm-success');
          statusText.textContent = t('downloadVideo');
          scheduleHide(1000);
        }, 1800);
      });
    } catch (err) {
      setTimeout(() => {
        mainBtn.classList.remove('fm-success');
        statusText.textContent = t('downloadVideo');
      }, 1800);
    }
  }

  const IS_SOCIAL_SITE = /(^|\.)(youtube\.com|youtu\.be|twitter\.com|x\.com|tiktok\.com|instagram\.com|facebook\.com|fb\.watch|twitch\.tv|reddit\.com|vimeo\.com|dailymotion\.com|kick\.com|bilibili\.com)$/i.test(location.hostname);

  function isStreamingEmbedIframe(f) {
    if (!f || f.tagName !== 'IFRAME') return false;
    const src = (f.src || f.getAttribute('src') || f.getAttribute('data-src') || '').toLowerCase();
    if (!src || src === 'about:blank' || src.startsWith('javascript:')) return false;
    return /(vidmoly|rapidvid|streamtape|dood|filemoon|vidspeed|supervideo|upstream|mixdrop|streamwish|voe\.sx|sibnet|ok\.ru\/videoembed|vk\.com\/video_ext|player\.vimeo\.com|dailymotion\.com\/embed|youtube\.com\/embed|youtube-nocookie\.com\/embed|twitch\.tv\/\?channel|rumble\.com\/embed)/i.test(src);
  }

  function isRealDownloadableVideo(el) {
    if (!el || !el.isConnected) return false;
    if (el.tagName === 'IFRAME') return isStreamingEmbedIframe(el);
    if (el.tagName !== 'VIDEO') return false;

    // 1. Ignore decorative / hidden / presentation elements
    if (el.getAttribute('aria-hidden') === 'true' || el.getAttribute('role') === 'presentation') return false;

    // 2. Ignore YouTube homepage thumbnail hover previews (only show on actual watch/shorts player)
    if (/(youtube\.com|youtu\.be)/i.test(location.hostname)) {
      if (!location.pathname.startsWith('/watch') && !location.pathname.startsWith('/shorts/') && !location.pathname.startsWith('/embed/')) {
        return false;
      }
      if (el.closest && el.closest('ytd-video-preview, #video-preview, ytd-inline-preview-player, ytd-thumbnail-overlay-loading-preview-renderer')) {
        return false;
      }
    }

    // 3. Check video source URL for UI animation / splash / loader keywords or data: URIs
    const src = (el.currentSrc || el.src || el.querySelector('source')?.src || '').toLowerCase();
    if (src.startsWith('data:') || src.startsWith('chrome-extension:') || src.startsWith('moz-extension:')) return false;
    if (/(splash|loader|loading|spinner|intro[-_]?anim|hero[-_]?bg|bg[-_]?video|background[-_]?video|lottie|onboarding|welcome[-_]?anim|empty[-_]?state)/i.test(src)) {
      return false;
    }

    const insideKnownPlayer = !!(el.closest && el.closest('.html5-video-player, #movie_player, ytd-player, .jwplayer, .video-js, .plyr, .flowplayer, .artplayer, .dplayer'));

    // 4. Ignore empty <video> tags that have no media source and aren't in a known player
    if (!src && !insideKnownPlayer && el.readyState === 0) return false;

    // 5. Ignore short (< 6 sec) control-less splash/UI animations
    if (!el.controls && !insideKnownPlayer && el.duration > 0 && el.duration < 6) {
      return false;
    }

    // 6. On non-social websites, ignore muted looping background/UI animations without controls
    if (!IS_SOCIAL_SITE && !insideKnownPlayer && !el.controls) {
      const isMuted = el.muted || el.defaultMuted || el.hasAttribute('muted');
      const isLoop = el.loop || el.hasAttribute('loop');
      if (isMuted && isLoop) return false;
    }

    return true;
  }

  // Find player container for YouTube, VideoJS, JWPlayer, etc.
  function getPlayerContainer(v) {
    if (!v || !v.closest) return v ? (v.parentElement || v) : null;
    return v.closest([
      '.html5-video-player',
      '#movie_player',
      'ytd-player',
      '#player',
      '.jwplayer',
      '.video-js',
      '.plyr',
      '[class*="player"]',
      '[id*="player"]',
      '[data-player]'
    ].join(',')) || v.parentElement || v;
  }

  // 4. Smooth Positioning Logic
  function updatePosition() {
    if (!currentVideo || !hostEl || !barContainer) return;

    const vRect = currentVideo.getBoundingClientRect();
    const player = getPlayerContainer(currentVideo);
    const pRect = (player && player !== currentVideo && player.isConnected) ? player.getBoundingClientRect() : null;
    const rect = (pRect && pRect.width >= MIN_WIDTH && pRect.height >= MIN_HEIGHT) ? pRect : vRect;

    if (rect.width < MIN_WIDTH || rect.height < MIN_HEIGHT) {
      hideBar();
      return;
    }

    // Check if video/player is visible in viewport
    const isVisible = rect.bottom > 30 && rect.top < (window.innerHeight - 30) &&
                      rect.right > 30 && rect.left < (window.innerWidth - 30);
    if (!isVisible) {
      hideBar();
      return;
    }

    // Position at top-right corner of the video
    // Anchor left coordinate to (rect.right - 14px), with transform: translateX(-100%)
    // perfectly aligns the right edge inside the video boundaries!
    const topPos = Math.max(10, Math.min(window.innerHeight - 50, rect.top + 14));
    const leftPos = Math.max(10, Math.min(window.innerWidth - 10, rect.right - 14));

    barContainer.style.top = `${topPos}px`;
    barContainer.style.left = `${leftPos}px`;
  }

  function showBarFor(video) {
    if (!video || isDismissedForPage || isPageDismissed()) return;
    if (!isRealDownloadableVideo(video)) return;

    const vRect = video.getBoundingClientRect();
    const player = getPlayerContainer(video);
    const pRect = (player && player !== video && player.isConnected) ? player.getBoundingClientRect() : null;
    const activeRect = (pRect && pRect.width >= MIN_WIDTH && pRect.height >= MIN_HEIGHT) ? pRect : vRect;

    if (activeRect.width < MIN_WIDTH || activeRect.height < MIN_HEIGHT) return;

    ensureHost();
    if (!hostEl || !barContainer) return;

    currentVideo = video;
    lastActiveVideo = video;
    clearTimeout(hideTimer);
    reportActiveMediaToBackground();

    // Attach to fullscreen element if playing fullscreen
    const fsEl = document.fullscreenElement || document.webkitFullscreenElement;
    const targetParent = fsEl || document.body || document.documentElement;
    if (targetParent && hostEl.parentElement !== targetParent) {
      targetParent.appendChild(hostEl);
    }

    updatePosition();
    barContainer.classList.add('fm-visible');
  }

  function hideBar() {
    if (barContainer) {
      barContainer.classList.remove('fm-visible');
      barContainer.classList.remove('fm-open');
    }
    currentVideo = null;
  }

  function scheduleHide(ms = 1800) {
    clearTimeout(hideTimer);
    hideTimer = setTimeout(hideBar, ms);
  }

  // 5. Video Element & Player Listeners
  function attachVideoEvents(v) {
    if (v.__fmWatched) return;
    v.__fmWatched = true;

    const onHover = () => {
      if (isDismissedForPage || isPageDismissed()) return;
      showBarFor(v);
    };

    v.addEventListener('mouseenter', onHover, { passive: true });
    v.addEventListener('mousemove', onHover, { passive: true });

    // IDM-style auto show on video start or load
    v.addEventListener('play', () => {
      if (isDismissedForPage || isPageDismissed()) return;
      lastActiveVideo = v;
      showBarFor(v);
      scheduleHide(4500);
      reportActiveMediaToBackground();
    }, { passive: true });

    v.addEventListener('playing', () => {
      if (isDismissedForPage || isPageDismissed()) return;
      showBarFor(v);
      scheduleHide(4500);
    }, { passive: true });

    v.addEventListener('loadedmetadata', () => {
      if (isDismissedForPage || isPageDismissed()) return;
      showBarFor(v);
      scheduleHide(4000);
      detectSubtitleTracks().catch?.(() => {});
    }, { passive: true });

    const player = getPlayerContainer(v);
    if (player && player !== v && !player.__fmWatched) {
      player.__fmWatched = true;
      player.addEventListener('mouseenter', () => {
        if (isDismissedForPage || isPageDismissed()) return;
        const activeV = (v && v.isConnected && v.getBoundingClientRect().width >= MIN_WIDTH) ? v : (player.querySelector('video') || document.querySelector('video'));
        if (activeV) showBarFor(activeV);
      }, { passive: true });
      player.addEventListener('mousemove', () => {
        if (isDismissedForPage || isPageDismissed()) return;
        const activeV = (v && v.isConnected && v.getBoundingClientRect().width >= MIN_WIDTH) ? v : (player.querySelector('video') || document.querySelector('video'));
        if (activeV) showBarFor(activeV);
      }, { passive: true });
    }
  }

  function scanAllVideos() {
    if (isDismissedForPage || isPageDismissed()) return;
    const videos = document.querySelectorAll('video');
    videos.forEach(attachVideoEvents);
  }

  // 6. Global Observers and Lifecycle
  scanAllVideos();

  // Initial trigger for video already present / playing on page load
  setTimeout(() => {
    if (isDismissedForPage || isPageDismissed()) return;
    scanAllVideos();
    detectSubtitleTracks().catch?.(() => {});
    const activeVideo = Array.from(document.querySelectorAll('video')).find(v => {
      if (!isRealDownloadableVideo(v)) return false;
      const r = v.getBoundingClientRect();
      return r.width >= MIN_WIDTH && r.height >= MIN_HEIGHT;
    });
    if (activeVideo) {
      showBarFor(activeVideo);
      scheduleHide(4500);
    }
    reportActiveMediaToBackground();
  }, 400);

  setTimeout(() => {
    detectSubtitleTracks().catch?.(() => {});
    reportActiveMediaToBackground();
  }, 2000);

  window.addEventListener('yt-navigate-finish', () => setTimeout(reportActiveMediaToBackground, 400), { passive: true });
  window.addEventListener('popstate', () => setTimeout(reportActiveMediaToBackground, 400), { passive: true });

  // Fail-safe global capture mousemove: detect cursor over any video or player element even with complex overlays
  let lastMouseMoveTime = 0;
  window.addEventListener('mousemove', (e) => {
    if (isDismissedForPage || isPageDismissed()) return;
    const now = Date.now();
    if (now - lastMouseMoveTime < 40) return; // 40ms responsive throttle
    lastMouseMoveTime = now;

    // Don't auto-hide or re-target while dropdown menu is open
    if (barContainer && barContainer.classList.contains('fm-open')) return;

    // If mouse is hovering over the bar itself, keep it alive
    if (barContainer && barContainer.classList.contains('fm-visible')) {
      const bRect = barContainer.getBoundingClientRect();
      if (bRect.width > 0 &&
          e.clientX >= (bRect.left - 8) && e.clientX <= (bRect.right + 8) &&
          e.clientY >= (bRect.top - 8) && e.clientY <= (bRect.bottom + 8)) {
        clearTimeout(hideTimer);
        return;
      }
    }

    const videos = document.querySelectorAll('video');
    let matchedVideo = null;
    for (let i = 0; i < videos.length; i++) {
      const v = videos[i];
      if (!isRealDownloadableVideo(v)) continue;
      const vRect = v.getBoundingClientRect();
      const player = getPlayerContainer(v);
      const pRect = (player && player !== v && player.isConnected) ? player.getBoundingClientRect() : null;

      const inV = vRect.width >= MIN_WIDTH && vRect.height >= MIN_HEIGHT &&
                  e.clientX >= vRect.left && e.clientX <= vRect.right &&
                  e.clientY >= vRect.top && e.clientY <= vRect.bottom;

      const inP = pRect && pRect.width >= MIN_WIDTH && pRect.height >= MIN_HEIGHT &&
                  e.clientX >= pRect.left && e.clientX <= pRect.right &&
                  e.clientY >= pRect.top && e.clientY <= pRect.bottom;

      if (inV || inP) {
        matchedVideo = v;
        break;
      }
    }

    if (matchedVideo) {
      showBarFor(matchedVideo);
      return;
    }

    // Check iframes ONLY for known foreign movie / video streaming embeds
    const iframes = document.querySelectorAll('iframe');
    for (let i = 0; i < iframes.length; i++) {
      const f = iframes[i];
      if (!isStreamingEmbedIframe(f)) continue;
      const fRect = f.getBoundingClientRect();
      if (fRect.width >= MIN_WIDTH && fRect.height >= MIN_HEIGHT) {
        if (e.clientX >= fRect.left && e.clientX <= fRect.right &&
            e.clientY >= fRect.top && e.clientY <= fRect.bottom) {
          showBarFor(f);
          return;
        }
      }
    }

    // If mouse moved outside all videos and bar, schedule hide smoothly
    if (barContainer && barContainer.classList.contains('fm-visible')) {
      scheduleHide(1500);
    }
  }, { capture: true, passive: true });

  // YouTube SPA Navigation Listener
  window.addEventListener('yt-navigate-finish', () => {
    isDismissedForPage = false;
    lastTrackedUrl = location.href;
    setTimeout(() => {
      scanAllVideos();
      const v = document.querySelector('video');
      if (v) {
        showBarFor(v);
        scheduleHide(4500);
      }
    }, 600);
  });

  // MutationObserver for SPA / dynamic videos (YouTube, TikTok, stream players)
  const observer = new MutationObserver(() => {
    if (location.href !== lastTrackedUrl) {
      lastTrackedUrl = location.href;
      isDismissedForPage = false;
      setTimeout(reportActiveMediaToBackground, 400);
    }
    scanAllVideos();
  });
  observer.observe(document.documentElement, { childList: true, subtree: true });

  // Update position on scroll / resize
  window.addEventListener('scroll', () => {
    if (currentVideo) {
      cancelAnimationFrame(updatePositionRaf);
      updatePositionRaf = requestAnimationFrame(updatePosition);
    }
  }, { capture: true, passive: true });

  window.addEventListener('resize', () => {
    if (currentVideo) updatePosition();
  }, { passive: true });

  document.addEventListener('fullscreenchange', () => {
    setTimeout(updatePosition, 100);
  });

  api.runtime.onMessage.addListener((msg, sender, sendResponse) => {
    if (msg?.type === 'detectSubtitles') {
      detectSubtitleTracks().then((subs) => {
        sendResponse({ subtitles: subs || [], ok: true });
      }).catch(() => {
        sendResponse({ subtitles: [], ok: false });
      });
      return true;
    }
    if (msg?.type === 'getActiveMedia') {
      const isTop = window.self === window.top;
      const isYouTube = /(youtube\.com|youtu\.be)/i.test(location.hostname);

      if (!isTop && isYouTube) {
        return false;
      }
      const hasRealVideo = !!findRealVideoOnPage();
      if (!isTop && !hasRealVideo) {
        return false;
      }

      resolveTargetMedia().then((media) => {
        sendResponse(media || {});
      }).catch(() => {
        sendResponse({});
      });
      return true;
    }
  });
})();
