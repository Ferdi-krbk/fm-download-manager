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

  const isYouTube = /(youtube\.com|youtu\.be)/i.test(location.hostname);
  const isTwitter = /(twitter\.com|x\.com)/i.test(location.hostname);
  const isTikTok = /tiktok\.com/i.test(location.hostname);

  const reported = new Set();
  function scanMedia() {
    const items = [];

    // On standard websites (not YouTube), scan direct audio/video elements
    if (!isYouTube) {
      document.querySelectorAll('video, audio, source').forEach((el) => {
        let src = el.currentSrc || el.src || el.getAttribute('src');
        if (src && !src.startsWith('blob:') && /^https?:/i.test(src) && !reported.has(src)) {
          reported.add(src);
          let title = document.title ? document.title.slice(0, 80) : '';
          items.push({ url: src, type: el.getAttribute('type') || 'video/mp4', name: title, kind: 'video' });
        }
      });
    }

    // YouTube: strictly report active video/short URL with clean title (never preview blobs or frames)
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
      const hasVideo = document.querySelector('video') !== null;
      if (hasVideo) {
        let tweetUrl = location.href;
        if (!tweetUrl.includes('/status/')) {
          const v = document.querySelector('video');
          const a = v ? v.closest('article')?.querySelector('a[href*="/status/"]') : null;
          if (a && a.href) tweetUrl = a.href;
        }
        if (tweetUrl.includes('/status/') && !reported.has(tweetUrl)) {
          reported.add(tweetUrl);
          const cleanTitle = (document.title || 'Twitter Video').replace(/\s*\|\s*X$/i, '').trim();
          items.push({ url: tweetUrl, type: 'video/mp4', name: cleanTitle.slice(0, 100), kind: 'video' });
        }
      }
    } else if (isTikTok) {
      const hasVideo = document.querySelector('video') !== null;
      if (hasVideo && !reported.has(location.href)) {
        reported.add(location.href);
        const cleanTitle = (document.title || 'TikTok Video').replace(/\s*\|\s*TikTok$/i, '').trim();
        items.push({ url: location.href, type: 'video/mp4', name: cleanTitle.slice(0, 100), kind: 'video' });
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
