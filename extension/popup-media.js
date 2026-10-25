// Media resolution shared by the new popup; no UI rendering here.
async function currentTab() {
  const tabs = await api.tabs.query({ active: true, currentWindow: true });
  return tabs && tabs[0];
}

async function sendTabMessageWithTimeout(tabId, msg, timeoutMs = 1200) {
  if (!tabId) return null;
  return new Promise((resolve) => {
    let settled = false;
    const timer = setTimeout(() => {
      if (!settled) {
        settled = true;
        resolve(null);
      }
    }, timeoutMs);
    try {
      if (IS_FIREFOX) {
        api.tabs.sendMessage(tabId, msg).then(
          (r) => {
            if (!settled) {
              settled = true;
              clearTimeout(timer);
              resolve(r);
            }
          },
          () => {
            if (!settled) {
              settled = true;
              clearTimeout(timer);
              resolve(null);
            }
          },
        );
        return;
      }
      api.tabs.sendMessage(tabId, msg, (r) => {
        if (!settled) {
          settled = true;
          clearTimeout(timer);
          resolve(api.runtime.lastError ? null : r);
        }
      });
    } catch {
      if (!settled) {
        settled = true;
        clearTimeout(timer);
        resolve(null);
      }
    }
  });
}

async function getTabSubtitles(tabId) {
  let subs = [];
  if (tabId) {
    try {
      const resp = await sendTabMessageWithTimeout(
        tabId,
        { type: "detectSubtitles" },
        1200,
      );
      if (resp && Array.isArray(resp.subtitles) && resp.subtitles.length > 0) {
        subs = resp.subtitles;
      }
    } catch {}
  }

  try {
    const bgInfo = await send({ type: "getBestMedia", tabId });
    if (bgInfo && Array.isArray(bgInfo.subtitles)) {
      for (const s of bgInfo.subtitles) {
        if (s && s.url && !subs.some((x) => x.url === s.url)) {
          subs.push({
            code: s.lang || s.code || "und",
            lang: s.lang || s.code || "und",
            label: s.label || "Altyazı",
            url: s.url,
          });
        }
      }
    }
  } catch {}

  return subs;
}

function chooseBestSubUrl(subs, targetLang = "tr") {
  if (!subs || !subs.length) return "";
  const nonForcedTr = subs.find(
    (s) =>
      (s.code === targetLang || s.lang === targetLang) &&
      !/forced/i.test(s.url || "") &&
      !/forced/i.test(s.label || ""),
  );
  if (nonForcedTr?.url) return nonForcedTr.url;
  const anyTr = subs.find(
    (s) => s.code === targetLang || s.lang === targetLang,
  );
  if (anyTr?.url) return anyTr.url;
  return subs[0]?.url || "";
}

function isGenericMediaName(raw) {
  if (!raw) return true;
  const s = raw
    .trim()
    .toLowerCase()
    .replace(/\.[a-z0-9]{2,5}$/i, "");
  return /^(?:master|index|playlist|manifest|stream|video|audio|media|iframe|player|chunk|output|blob|embed|download|file|view|document)(?:[_-]?\d+)?$/i.test(
    s,
  );
}

function cleanMediaTitle(raw) {
  if (!raw) return "";
  let s = raw.replace(/[\\/:*?"<>|]/g, "").trim();
  s = s.replace(
    /^(?:youtube\s*(?:video\s*player|frame)?|iframe)\s*[-–|•]?\s*/i,
    "",
  );
  s = s.replace(
    /\s*[-–|•]\s*(?:hdfilmcehennemi|filmmodu|dizipal|fullhdfilmizlesene|dizibox|sezonlukdizi|filmevreni|youtube|netflix|blutv|gain|disney\+?).*$/i,
    "",
  );
  s = s.replace(
    /\s+(?:izle|full izle|1080p|720p|türkçe dublaj|türkçe altyazılı|dublaj|altyazılı).*$/i,
    "",
  );
  s = s.replace(/\s*[-–|•]\s*$/, "").trim();
  return s.slice(0, 60);
}

function formatReferer(raw, fallbackUrl) {
  if (!raw || raw.startsWith("chrome-extension://")) return fallbackUrl || "";
  try {
    const u = new URL(raw);
    if (!u.pathname || u.pathname === "/") {
      return u.origin + "/";
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
    const fromBg = await send({ type: "getActiveMedia", tabId });
    if (
      fromBg &&
      fromBg.url &&
      !/^https?:\/\/[^/]+\/?$/i.test(fromBg.url) &&
      !/\.html?(\?|$)/i.test(fromBg.url)
    ) {
      if (isGenericMediaName(fromBg.name)) {
        fromBg.name =
          cleanMediaTitle(tab?.title) ||
          (tab?.title || "Video").replace(/\s*-\s*YouTube$/i, "").trim();
      }
      return fromBg;
    }
  } catch {}

  // 2. Query active tab fallback
  try {
    const fromAny = await sendTabMessageWithTimeout(
      tabId,
      { type: "getActiveMedia" },
      1200,
    );
    if (
      fromAny &&
      fromAny.url &&
      !/^https?:\/\/[^/]+\/?$/i.test(fromAny.url) &&
      !/\.html?(\?|$)/i.test(fromAny.url)
    ) {
      if (isGenericMediaName(fromAny.name)) {
        fromAny.name =
          cleanMediaTitle(tab?.title) ||
          (tab?.title || "Video").replace(/\s*-\s*YouTube$/i, "").trim();
      }
      return fromAny;
    }
  } catch {}

  return null;
}

function pickBestMedia(list) {
  if (!list || !list.length) return null;
  const masterHls = list.find(
    (m) =>
      m.kind === "hls" &&
      (/master|playlist|manifest|index\.m3u8/i.test(m.url) ||
        !/sublist|chunk|aud|vid\d/i.test(m.url)),
  );
  if (masterHls) return masterHls;
  const directVideo = list.find(
    (m) =>
      (m.kind === "video" || m.kind === "media") &&
      !/aud/i.test(m.url) &&
      !/sublist/i.test(m.url),
  );
  if (directVideo) return directVideo;
  const videoHls = list.find(
    (m) => m.kind === "hls" && !/aud|audio/i.test(m.url),
  );
  if (videoHls) return videoHls;
  const nonAudio = list.find((m) => !/sublist_aud|audio[_-]track/i.test(m.url));
  return nonAudio || list[0];
}
