// FM Download Manager — IDM-Style Modern Floating Video Download Bar
// Isolated Shadow DOM, Glassmorphic UI, Auto Video Tracking, 100% Non-Intrusive.
(() => {
  'use strict';
  const api = typeof browser !== 'undefined' ? browser : chrome;

  // Prevent multiple injections in the same frame
  if (window.__fmFloatingBarLoaded) return;
  window.__fmFloatingBarLoaded = true;

  // Minimum dimensions to be considered a real video (ignores avatars, ads, stickers)
  const MIN_WIDTH = 220;
  const MIN_HEIGHT = 130;

  // Active tracking state
  let currentVideo = null;
  let hideTimer = null;
  let updatePositionRaf = null;

  // Host element & closed shadow root for 100% CSS isolation
  let hostEl = null;
  let shadowRoot = null;
  let barContainer = null;
  let dropdownMenu = null;
  let mainBtn = null;
  let statusText = null;

  // 1. Create the Isolated Shadow DOM Host
  function ensureHost() {
    if (hostEl && hostEl.isConnected) return;

    hostEl = document.createElement('div');
    hostEl.id = 'fm-floating-bar-host';
    hostEl.style.cssText = 'all: initial; position: absolute; z-index: 2147483647; pointer-events: none; top: 0; left: 0;';

    shadowRoot = hostEl.attachShadow({ mode: 'closed' });

    // Inject styles & template
    shadowRoot.innerHTML = `
      <style>
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
          pointer-events: auto;
          transition: opacity 0.25s ease, transform 0.25s cubic-bezier(0.16, 1, 0.3, 1);
          opacity: 0;
          transform: translateY(-6px) scale(0.96);
          pointer-events: none;
        }

        .fm-wrap.fm-visible {
          opacity: 1;
          transform: translateY(0) scale(1);
          pointer-events: auto;
        }

        /* Glassmorphic Pill Button */
        .fm-bar {
          display: inline-flex;
          align-items: center;
          gap: 6px;
          background: rgba(15, 17, 23, 0.88);
          backdrop-filter: blur(14px);
          -webkit-backdrop-filter: blur(14px);
          border: 1px solid rgba(201, 42, 62, 0.45);
          border-radius: 20px;
          padding: 5px 10px 5px 8px;
          box-shadow: 0 4px 18px rgba(0, 0, 0, 0.45), 0 0 12px rgba(201, 42, 62, 0.25);
          cursor: pointer;
          transition: all 0.2s cubic-bezier(0.16, 1, 0.3, 1);
        }

        .fm-bar:hover {
          background: rgba(22, 25, 35, 0.96);
          border-color: rgba(229, 62, 62, 0.8);
          box-shadow: 0 6px 24px rgba(0, 0, 0, 0.6), 0 0 18px rgba(201, 42, 62, 0.45);
          transform: translateY(-1px);
        }

        .fm-logo-badge {
          width: 20px;
          height: 20px;
          border-radius: 6px;
          background: #C92A3E;
          display: flex;
          align-items: center;
          justify-content: center;
          color: #fff;
          font-weight: 800;
          font-size: 10px;
          letter-spacing: -0.5px;
          box-shadow: 0 2px 6px rgba(201, 42, 62, 0.5);
        }

        .fm-title {
          font-size: 12px;
          font-weight: 600;
          color: #f1f5f9;
          letter-spacing: 0.1px;
          white-space: nowrap;
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
          width: 16px;
          height: 16px;
          border-radius: 50%;
          display: flex;
          align-items: center;
          justify-content: center;
          color: #64748b;
          font-size: 11px;
          margin-left: 2px;
          transition: background 0.15s, color 0.15s;
        }

        .fm-close-btn:hover {
          background: rgba(255, 255, 255, 0.12);
          color: #cbd5e1;
        }

        /* Success State */
        .fm-bar.fm-success {
          background: rgba(6, 78, 59, 0.92);
          border-color: rgba(16, 185, 129, 0.8);
          box-shadow: 0 4px 18px rgba(0, 0, 0, 0.4), 0 0 16px rgba(16, 185, 129, 0.4);
        }

        .fm-bar.fm-success .fm-title {
          color: #6ee7b7;
        }

        /* Dropdown Menu */
        .fm-menu {
          margin-top: 6px;
          background: rgba(15, 17, 23, 0.94);
          backdrop-filter: blur(16px);
          -webkit-backdrop-filter: blur(16px);
          border: 1px solid rgba(45, 52, 70, 0.8);
          border-radius: 12px;
          padding: 5px;
          min-width: 190px;
          box-shadow: 0 10px 30px rgba(0, 0, 0, 0.65), 0 0 15px rgba(201, 42, 62, 0.15);
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
          background: rgba(201, 42, 62, 0.22);
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
      </style>

      <div class="fm-wrap" id="fm-wrap">
        <div class="fm-bar" id="fm-main-btn" title="FM Download Manager ile İndir">
          <div class="fm-logo-badge">FM</div>
          <span class="fm-title" id="fm-title">Videoyu İndir</span>
          <svg class="fm-arrow" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.5" stroke-linecap="round" stroke-linejoin="round">
            <polyline points="6 9 12 15 18 9"></polyline>
          </svg>
          <div class="fm-close-btn" id="fm-close-btn" title="Gizle">✕</div>
        </div>

        <div class="fm-menu" id="fm-menu">
          <div class="fm-item" id="fm-opt-video">
            <span class="fm-item-icon">🎬</span>
            <span>Video Olarak İndir</span>
            <span class="fm-item-sub">En Yüksek</span>
          </div>
          <div class="fm-item" id="fm-opt-audio">
            <span class="fm-item-icon">🎵</span>
            <span>Sadece Ses İndir</span>
            <span class="fm-item-sub">MP3</span>
          </div>
        </div>
      </div>
    `;

    barContainer = shadowRoot.getElementById('fm-wrap');
    dropdownMenu = shadowRoot.getElementById('fm-menu');
    mainBtn = shadowRoot.getElementById('fm-main-btn');
    statusText = shadowRoot.getElementById('fm-title');
    const closeBtn = shadowRoot.getElementById('fm-close-btn');

    // Event: Toggle Dropdown or Quick Download
    mainBtn.addEventListener('click', (e) => {
      e.stopPropagation();
      if (e.target === closeBtn) return;
      barContainer.classList.toggle('fm-open');
    });

    // Event: Option Video Click
    shadowRoot.getElementById('fm-opt-video').addEventListener('click', (e) => {
      e.stopPropagation();
      barContainer.classList.remove('fm-open');
      triggerDownload('video');
    });

    // Event: Option Audio Click
    shadowRoot.getElementById('fm-opt-audio').addEventListener('click', (e) => {
      e.stopPropagation();
      barContainer.classList.remove('fm-open');
      triggerDownload('audio');
    });

    // Event: Close / Dismiss
    closeBtn.addEventListener('click', (e) => {
      e.stopPropagation();
      hideBar();
    });

    // Keep visible while hovering over the bar itself
    barContainer.addEventListener('mouseenter', () => {
      clearTimeout(hideTimer);
    });
    barContainer.addEventListener('mouseleave', () => {
      scheduleHide(1500);
    });

    const targetParent = document.fullscreenElement || document.body;
    if (targetParent) targetParent.appendChild(hostEl);
  }

  // 2. Resolve Active Video / Stream URL
  function resolveTargetMedia() {
    const isYouTube = /(youtube\.com|youtu\.be)/i.test(location.hostname);
    const isTwitter = /(twitter\.com|x\.com)/i.test(location.hostname);
    const isTikTok = /tiktok\.com/i.test(location.hostname);

    if (isYouTube) {
      const cleanTitle = (document.title || 'YouTube Video').replace(/\s*-\s*YouTube$/i, '').trim();
      return {
        url: location.href,
        name: cleanTitle,
        kind: 'video',
        referer: location.href
      };
    }

    if (isTwitter) {
      if (location.pathname.includes('/status/')) {
        return { url: location.href.split('?')[0], name: document.title, kind: 'video', referer: location.href };
      }
      if (currentVideo) {
        const a = currentVideo.closest('article')?.querySelector('a[href*="/status/"]');
        if (a && a.href) {
          return { url: a.href.split('?')[0], name: document.title, kind: 'video', referer: location.href };
        }
      }
    }

    if (isTikTok) {
      if (location.pathname.includes('/video/')) {
        return { url: location.href.split('?')[0], name: document.title, kind: 'video', referer: location.href };
      }
      if (currentVideo) {
        const a = currentVideo.closest('[data-e2e*="feed-item"], article, div')?.querySelector('a[href*="/video/"]');
        if (a && a.href) {
          return { url: a.href.split('?')[0], name: document.title, kind: 'video', referer: location.href };
        }
      }
    }

    // Direct HTML5 video / HLS stream on movie or news sites
    if (currentVideo) {
      let src = currentVideo.currentSrc || currentVideo.src;
      if (!src) {
        const source = currentVideo.querySelector('source');
        if (source) src = source.src || source.getAttribute('src');
      }

      // Upgrade sublist_vid1.m3u8 to master.m3u8 automatically
      if (src && /sublist_vid\d*\.m3u8/i.test(src)) {
        src = src.replace(/sublist_vid\d*\.m3u8/i, 'master.m3u8');
      }

      if (src && /^https?:/i.test(src) && !src.startsWith('blob:')) {
        return {
          url: src,
          name: document.title || 'video',
          kind: /\.m3u8(\?|$)/i.test(src) ? 'hls' : 'video',
          referer: location.href
        };
      }
    }

    // Fallback to active movie iframe or page URL
    const iframe = document.querySelector('iframe[src*="vidmoly"], iframe[src*="rapid"], iframe[src*="stream"], iframe[src*="embed"]');
    if (iframe && iframe.src) {
      return { url: iframe.src, name: document.title, kind: 'video', referer: location.href };
    }

    return { url: location.href, name: document.title, kind: 'video', referer: location.href };
  }

  // 3. Dispatch Download to Desktop App
  function triggerDownload(format) {
    const media = resolveTargetMedia();
    if (!media || !media.url) return;

    // Show visual feedback
    mainBtn.classList.add('fm-success');
    statusText.textContent = 'Gönderildi! ✓';

    try {
      api.runtime.sendMessage({
        type: 'download',
        url: media.url,
        filename: media.name || '',
        referer: media.referer || location.href,
        kind: format === 'audio' ? 'video' : (media.kind || 'video'),
        format: format // 'video' or 'audio'
      }, (res) => {
        // Reset after 1.8 seconds
        setTimeout(() => {
          mainBtn.classList.remove('fm-success');
          statusText.textContent = 'Videoyu İndir';
          scheduleHide(1000);
        }, 1800);
      });
    } catch (err) {
      setTimeout(() => {
        mainBtn.classList.remove('fm-success');
        statusText.textContent = 'Videoyu İndir';
      }, 1800);
    }
  }

  // 4. Smooth Positioning Logic
  function updatePosition() {
    if (!currentVideo || !hostEl || !barContainer) return;

    const rect = currentVideo.getBoundingClientRect();
    if (rect.width < MIN_WIDTH || rect.height < MIN_HEIGHT) {
      hideBar();
      return;
    }

    // Check if video is visible in viewport
    const isVisible = rect.bottom > 0 && rect.top < window.innerHeight && rect.right > 0 && rect.left < window.innerWidth;
    if (!isVisible) {
      hideBar();
      return;
    }

    const scrollX = window.scrollX || window.pageXOffset || 0;
    const scrollY = window.scrollY || window.pageYOffset || 0;

    // Position at top-right corner inside the video player
    const rightOffset = (window.innerWidth - rect.right);
    const topPos = rect.top + scrollY + 12;
    const rightPos = rightOffset - scrollX + 12;

    barContainer.style.top = `${Math.max(8, topPos)}px`;
    barContainer.style.right = `${Math.max(8, rightPos)}px`;
  }

  function showBarFor(video) {
    ensureHost();
    currentVideo = video;
    clearTimeout(hideTimer);

    updatePosition();
    barContainer.classList.add('fm-visible');

    // Attach to fullscreen element if playing fullscreen
    const fsEl = document.fullscreenElement || document.webkitFullscreenElement;
    if (fsEl && hostEl.parentElement !== fsEl) {
      fsEl.appendChild(hostEl);
    } else if (!fsEl && hostEl.parentElement !== document.body) {
      document.body.appendChild(hostEl);
    }
  }

  function hideBar() {
    if (barContainer) {
      barContainer.classList.remove('fm-visible');
      barContainer.classList.remove('fm-open');
    }
    currentVideo = null;
  }

  function scheduleHide(ms = 2500) {
    clearTimeout(hideTimer);
    hideTimer = setTimeout(hideBar, ms);
  }

  // 5. Video Element Listeners
  function attachVideoEvents(v) {
    if (v.__fmWatched) return;
    v.__fmWatched = true;

    v.addEventListener('mouseenter', () => showBarFor(v), { passive: true });
    v.addEventListener('play', () => showBarFor(v), { passive: true });
    v.addEventListener('loadedmetadata', () => {
      const rect = v.getBoundingClientRect();
      if (rect.width >= MIN_WIDTH && rect.height >= MIN_HEIGHT) {
        showBarFor(v);
        scheduleHide(3500);
      }
    }, { passive: true });

    v.addEventListener('mouseleave', () => scheduleHide(2500), { passive: true });
  }

  function scanAllVideos() {
    const videos = document.querySelectorAll('video');
    videos.forEach(attachVideoEvents);
  }

  // 6. Global Observers and Lifecycle
  scanAllVideos();

  // MutationObserver for SPA / dynamic videos (YouTube, TikTok, Netflix, stream players)
  const observer = new MutationObserver(() => {
    scanAllVideos();
  });
  observer.observe(document.documentElement, { childList: true, subtree: true });

  // Update position on scroll / resize
  window.addEventListener('scroll', () => {
    if (currentVideo) {
      cancelAnimationFrame(updatePositionRaf);
      updatePositionRaf = requestAnimationFrame(updatePosition);
    }
  }, { passive: true });

  window.addEventListener('resize', () => {
    if (currentVideo) updatePosition();
  }, { passive: true });

  document.addEventListener('fullscreenchange', () => {
    setTimeout(updatePosition, 100);
  });
})();
