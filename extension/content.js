// MC Download Manager — content script
// 1) In Safari (no downloads API) intercepts clicks on file links.
// 2) Reports <video>/<audio>/<source> URLs on the page to the background script.
(() => {
  'use strict';
  const api = typeof browser !== 'undefined' ? browser : chrome;
  const DL_EXT = /\.(zip|rar|7z|tar|gz|tgz|bz2|xz|dmg|pkg|iso|exe|msi|apk|deb|rpm|pdf|epub|mobi|doc|docx|xls|xlsx|ppt|pptx|mp4|mkv|avi|mov|webm|mp3|flac|wav|m4a|aac|ogg|jar|bin|img|csv|psd|ai|ttf|otf|woff2?)(\?|#|$)/i;

  let state = { online: false, clickIntercept: false, settings: { intercept: true } };

  // Firefox's browser.runtime.sendMessage only returns a promise (a callback argument is rejected).
  const IS_FIREFOX = typeof browser !== 'undefined' && String(browser.runtime.getURL('')).startsWith('moz-extension:');

  function refresh() {
    const apply = (r) => { if (r) state = r; };
    try {
      if (IS_FIREFOX) {
        browser.runtime.sendMessage({ type: 'status' }).then(apply, () => {});
      } else {
        api.runtime.sendMessage({ type: 'status' }, (r) => { if (!api.runtime.lastError) apply(r); });
      }
    } catch {}
  }
  refresh();
  setInterval(refresh, 30000);

  function send(msg) {
    return new Promise((resolve) => {
      try {
        if (IS_FIREFOX) browser.runtime.sendMessage(msg).then(resolve, () => resolve(null));
        else api.runtime.sendMessage(msg, (r) => resolve(api.runtime.lastError ? null : r));
      } catch { resolve(null); }
    });
  }

  let passThrough = null;   // link re-clicked after the app could not take the download
  document.addEventListener('click', (e) => {
    if (state.settings?.intercept === false) return;
    if (e.defaultPrevented || e.button !== 0 || e.metaKey || e.ctrlKey || e.altKey || e.shiftKey) return;
    const a = e.target && e.target.closest ? e.target.closest('a[href]') : null;
    if (!a) return;
    if (a === passThrough) { passThrough = null; return; }
    const href = a.href;
    if (!/^https?:/i.test(href)) return;
    const list = state.captureExtensions || [];
    const m = href.split(/[?#]/)[0].match(/\.([a-z0-9]{1,8})$/i);
    const ext = m ? m[1].toLowerCase() : '';
    if (ext === 'html' || ext === 'htm' || ext === 'php' || ext === 'asp' || ext === 'aspx' || ext === 'jsp') return;
    if (list.length ? !(ext && list.includes(ext)) : !(a.hasAttribute('download') || DL_EXT.test(href))) return;
    e.preventDefault();
    e.stopPropagation();
    send({ type: 'download', url: href, filename: a.getAttribute('download') || '', referer: location.href, kind: 'file' }).then((r) => {
      if (r && r.ok) { return; }
      // The app is unavailable: let the browser handle the click after all.
      passThrough = a;
      a.click();
    });
  }, true);

  // Only top window scans media (blocks iframes from reporting 'YouTube Frame')
  if (window.top !== window.self) return;

  const EXCLUDED_HOSTS = /(^|\.)(office\.com|office\.live\.com|officeapps\.live\.com|office365\.com|sharepoint\.com|onedrive\.live\.com|live\.com|microsoft365\.com|docs\.google\.com|sheets\.google\.com|slides\.google\.com|drive\.google\.com|mail\.google\.com|outlook\.live\.com|outlook\.office\.com|notion\.so|figma\.com|canva\.com)$/i;
  if (EXCLUDED_HOSTS.test(location.hostname)) return;

  const isYouTube = /(youtube\.com|youtu\.be)/i.test(location.hostname);
  const isTwitter = /(twitter\.com|x\.com)/i.test(location.hostname);
  const isTikTok = /tiktok\.com/i.test(location.hostname);
  const isInstagram = /instagram\.com/i.test(location.hostname);
  const isSocial = isYouTube || isTwitter || isTikTok || isInstagram;

  function findActiveTweetUrl() {
    if (location.pathname.includes('/status/')) return location.href.split('?')[0];
    const videos = Array.from(document.querySelectorAll('video'));
    let activeV = videos.find((v) => !v.paused && v.currentTime > 0) || videos[0];
    if (activeV) {
      const a = activeV.closest('article')?.querySelector('a[href*="/status/"]');
      if (a && a.href) return a.href.split('?')[0];
    }
    return null;
  }

  function findActiveTikTokUrl() {
    if (location.pathname.includes('/video/')) return location.href.split('?')[0];
    const videos = Array.from(document.querySelectorAll('video'));
    let activeV = videos.find((v) => !v.paused && v.currentTime > 0) || videos[0];
    if (activeV) {
      const a = activeV.closest('[data-e2e*="feed-item"], article, div')?.querySelector('a[href*="/video/"]');
      if (a && a.href) return a.href.split('?')[0];
    }
    return null;
  }

  function findActiveMovieEmbedUrl() {
    const iframes = Array.from(document.querySelectorAll('iframe'));
    for (const f of iframes) {
      let src = f.src || f.getAttribute('src') || f.getAttribute('data-src') || '';
      if (src.startsWith('//')) src = location.protocol + src;
      if (/vidmoly|rapid|streamtape|dood|filemoon|player|embed|vidspeed|supervideo|upstream|mixdrop|streamwish/i.test(src) && /^https?:/i.test(src)) {
        return src;
      }
    }
    return null;
  }

  // Allow popup.js to ask for the active video URL directly
  api.runtime.onMessage.addListener((msg, sender, sendResponse) => {
    if (msg?.type === 'getDirectMediaUrl') {
      const mediaEl = Array.from(document.querySelectorAll('video, audio')).find(el => !el.paused || el.currentTime > 0) || document.querySelector('video, audio');
      if (mediaEl) {
        const src = mediaEl.currentSrc || mediaEl.src || mediaEl.querySelector('source')?.src;
        if (src && /^https?:/i.test(src)) {
          sendResponse({ url: src, name: (document.title || 'media').slice(0, 100), ok: true });
          return;
        }
      }
      sendResponse({ ok: false });
    }
  });

  const reported = new Set();
  function scanMedia() {
    const items = [];

    // On standard websites (not social platforms), scan direct audio/video elements
    if (!isSocial) {
      document.querySelectorAll('video, audio, source').forEach((el) => {
        const mediaParent = el.tagName === 'SOURCE' ? el.parentElement : el;
        if (mediaParent && mediaParent.tagName === 'VIDEO') {
          if (mediaParent.getAttribute('aria-hidden') === 'true' || mediaParent.getAttribute('role') === 'presentation') return;
          if (!mediaParent.controls && (mediaParent.muted || mediaParent.defaultMuted) && mediaParent.loop) return;
          if (!mediaParent.controls && mediaParent.duration > 0 && mediaParent.duration < 6) return;
        }
        let src = el.currentSrc || el.src || el.getAttribute('src');
        if (src && !src.startsWith('blob:') && /^https?:/i.test(src) && !reported.has(src)) {
          if (/(splash|loader|loading|spinner|intro[-_]?anim|hero[-_]?bg|bg[-_]?video|background[-_]?video|lottie|onboarding|welcome[-_]?anim)/i.test(src)) return;
          reported.add(src);
          let title = document.title ? document.title.slice(0, 80) : '';
          items.push({ url: src, type: el.getAttribute('type') || 'video/mp4', name: title, kind: 'video' });
        }
      });
    }

    // YouTube: strictly report active video/short URL with clean title
    if (isYouTube) {
      const isWatch = location.pathname.startsWith('/watch') || location.pathname.startsWith('/shorts/');
      if (isWatch && !reported.has(location.href)) {
        reported.add(location.href);
        const cleanTitle = (document.title || 'YouTube Video')
          .replace(/\s*-\s*YouTube$/i, '')
          .trim();
        items.push({
          url: location.href,
          type: 'video/mp4',
          name: cleanTitle.slice(0, 100),
          kind: 'video'
        });
      }
    } else if (isTwitter) {
      const tweetUrl = findActiveTweetUrl();
      if (tweetUrl && !reported.has(tweetUrl)) {
        reported.add(tweetUrl);
        const article = document.querySelector(`article a[href*="${tweetUrl.split('/status/')[1]}"]`)?.closest('article');
        const userEl = article?.querySelector('[data-testid="User-Name"]');
        const author = userEl ? userEl.textContent.split('@')[0].trim() : '';
        const cleanTitle = (author ? author + ' - ' : '') + (document.title || 'Twitter Video').replace(/\s*\|\s*X$/i, '').trim();
        items.push({ url: tweetUrl, type: 'video/mp4', name: cleanTitle.slice(0, 100), kind: 'video' });
      }
    } else if (isTikTok) {
      const videoUrl = findActiveTikTokUrl();
      if (videoUrl && !reported.has(videoUrl)) {
        reported.add(videoUrl);
        const author = location.pathname.split('/')[1] || '';
        const cleanTitle = (author ? author + ' - ' : '') + (document.title || 'TikTok Video').replace(/\s*\|\s*TikTok$/i, '').trim();
        items.push({ url: videoUrl, type: 'video/mp4', name: cleanTitle.slice(0, 100), kind: 'video' });
      }
    }

    if (items.length) {
      try { api.runtime.sendMessage({ type: 'pageMedia', items }); } catch {}
    }
  }
  scanMedia();
  document.addEventListener('play', scanMedia, true);
  document.addEventListener('loadedmetadata', scanMedia, true);
  let n = 0;
  const iv = setInterval(() => { scanMedia(); if (++n > 20) clearInterval(iv); }, 3000);
})();
