"use strict";
const api = typeof browser !== "undefined" ? browser : chrome;
const $ = (id) => document.getElementById(id),
  root = $("fm-flow");
const IS_FIREFOX =
  typeof browser !== "undefined" &&
  String(browser.runtime.getURL("")).startsWith("moz-extension:");
let tab = null,
  current = null,
  subs = [],
  selected = "video",
  language = "tr",
  theme = "dark",
  accent = "#C3AFFF",
  online = false,
  busy = false;
const changed = new Set();
const strings = {
  tr: {
    extension: "Tarayıcı eklentisi",
    onPage: "BU SAYFADA",
    video: "Video",
    audio: "Ses",
    subtitle: "Altyazı",
    send: "Uygulamaya gönder",
    page: "Sayfa bağlantısını kullan",
    appearance: "Görünüm",
    custom: "Özel renk",
    dark: "Gece",
    light: "Gündüz",
    connected: "Uygulamaya bağlı",
    offline: "Uygulama kapalı",
    pairing: "Eşleştirme gerekli",
    empty: "Henüz medya yok",
    play: "Bir video oynatınca burada görünecek.",
    audioFormat: "MP3 olarak çıkar",
    noSubtitle: "Altyazı bulunamadı",
    detected: "Yakalanan video",
    available: "altyazı",
    streams: "akış",
    sent: "Uygulamaya gönderildi.",
    failed: "Gönderilemedi. Bağlantıyı ve eşleştirmeyi kontrol et.",
    sending: "Gönderiliyor…",
    offlineHelp: "İndirmek için FM Download Manager’ı aç.",
    pairHelp: "Uygulamadaki eşleştirme kodunu gir.",
    pair: "Eşleştir",
    openApp: "Uygulamayı aç",
    intercept: "İndirmeleri uygulamaya gönder",
    sniff: "Sayfalardaki videoları yakala",
    autoLaunch: "Gerektiğinde uygulamayı aç",
    advanced: "Gelişmiş",
    port: "Uygulama portu",
    minimum: "En küçük dosya (MB)",
    pageFile: "Sayfa adresini dosya olarak indir",
    downloads: "İndirmeler",
    pause: "Duraklat",
    resume: "Devam et",
    cancel: "İptal et",
  },
  en: {
    extension: "Browser extension",
    onPage: "ON THIS PAGE",
    video: "Video",
    audio: "Audio",
    subtitle: "Subtitles",
    send: "Send to app",
    page: "Use page link",
    appearance: "Appearance",
    custom: "Custom color",
    dark: "Dark",
    light: "Light",
    connected: "Connected to app",
    offline: "App is offline",
    pairing: "Pairing required",
    empty: "No media yet",
    play: "Play a video to see it here.",
    audioFormat: "Extract as MP3",
    noSubtitle: "No subtitles found",
    detected: "Captured video",
    available: "subtitles",
    streams: "streams",
    sent: "Sent to the app.",
    failed: "Could not send. Check connection and pairing.",
    sending: "Sending…",
    offlineHelp: "Open FM Download Manager to download.",
    pairHelp: "Enter the pairing code from the app.",
    pair: "Pair",
    openApp: "Open app",
    intercept: "Send downloads to the app",
    sniff: "Capture videos on pages",
    autoLaunch: "Open the app when needed",
    advanced: "Advanced",
    port: "App port",
    minimum: "Minimum file size (MB)",
    pageFile: "Download page URL as a file",
    downloads: "Downloads",
    pause: "Pause",
    resume: "Resume",
    cancel: "Cancel",
  },
};
const t = (key) => strings[language][key] || key;
function send(msg) {
  if (IS_FIREFOX) return api.runtime.sendMessage(msg).catch(() => null);
  return new Promise((resolve) => {
    try {
      api.runtime.sendMessage(msg, (value) =>
        resolve(api.runtime.lastError ? null : value),
      );
    } catch {
      resolve(null);
    }
  });
}
function feedback(message) {
  $("feedback").textContent = message;
  $("feedback").hidden = !message;
}
function installIcons() {
  const paths = {
    "sliders-horizontal": "M2 4h14M2 9h14M2 14h14M6 1v6M12 6v6M7 11v6",
    video: "M2 4h10v11H2zM12 7l4-3v11l-4-3",
    "audio-lines": "M2 7v4M5 4v10M9 1v16M13 4v10M16 7v4",
    captions: "M2 3h14v12H2zM4 8h3M10 8h4M4 11h5M12 11h2",
    "arrow-down-to-line": "M9 1v11M4 7l5 5 5-5M2 13v3h14v-3",
    link: "M7 5l2-2a4 4 0 015 5l-3 3M11 13l-2 2a4 4 0 01-5-5l3-3M6 12l6-6",
  };
  document.querySelectorAll("[data-lucide]").forEach((node) => {
    const svg = document.createElementNS("http://www.w3.org/2000/svg", "svg");
    svg.setAttribute("viewBox", "0 0 18 18");
    svg.setAttribute("aria-hidden", "true");
    const path = document.createElementNS(svg.namespaceURI, "path");
    path.setAttribute("d", paths[node.dataset.lucide] || paths.video);
    svg.appendChild(path);
    node.replaceWith(svg);
  });
}
function luminance(rgb) {
  const c = rgb.map((v) => {
    v /= 255;
    return v <= 0.04045 ? v / 12.92 : ((v + 0.055) / 1.055) ** 2.4;
  });
  return 0.2126 * c[0] + 0.7152 * c[1] + 0.0722 * c[2];
}
function applyAppearance() {
  root.style.colorScheme = theme;
  const rgb = accent.match(/[\da-f]{2}/gi).map((v) => parseInt(v, 16)),
    l = luminance(rgb);
  root.style.setProperty("--f-accent", accent);
  root.style.setProperty(
    "--f-onaccent",
    (l + 0.05) / 0.05 > 1.05 / (l + 0.05) ? "#000000" : "#FFFFFF",
  );
  root.style.setProperty(
    "--f-soft",
    `color-mix(in srgb, ${accent} 13%, var(--f-panel))`,
  );
  let ink = [...rgb];
  const bg = theme === "light" ? 1 : luminance([25, 25, 31]);
  for (let n = 0; n < 32; n++) {
    const v = luminance(ink);
    if ((Math.max(v, bg) + 0.05) / (Math.min(v, bg) + 0.05) >= 4.5) break;
    ink = ink.map((v) =>
      Math.round(v * 0.9 + (theme === "light" ? 0 : 255) * 0.1),
    );
  }
  root.style.setProperty("--f-accent-ink", `rgb(${ink.join(",")})`);
  document.querySelectorAll("[data-mode]").forEach((b) => {
    b.setAttribute("aria-pressed", String(b.dataset.mode === theme));
    b.textContent = t(b.dataset.mode);
  });
  document
    .querySelectorAll("[data-color]")
    .forEach((b) =>
      b.setAttribute(
        "aria-pressed",
        String(b.dataset.color.toUpperCase() === accent.toUpperCase()),
      ),
    );
  $("customAccent").value = accent;
}
function localize() {
  document.documentElement.lang = language;
  document.querySelector(".f-popup-head small").textContent = t("extension");
  document.querySelector(".f-popup-body .f-eyebrow").textContent = t("onPage");
  for (const key of ["video", "audio", "subtitle"])
    document.querySelector(`[data-select-stream="${key}"] strong`).textContent =
      t(key);
  document.querySelector("[data-send]").lastChild.textContent = " " + t("send");
  document.querySelector("[data-demo-action]").lastChild.textContent =
    " " + t("page");
  for (const [id, key] of Object.entries({
    appearanceLabel: "appearance",
    customColorLabel: "custom",
    interceptLabel: "intercept",
    sniffLabel: "sniff",
    autoLaunchLabel: "autoLaunch",
    advancedLabel: "advanced",
    portLabel: "port",
    minSizeLabel: "minimum",
    pageFileButton: "pageFile",
    downloadsLabel: "downloads",
    pairButton: "pair",
    launchButton: "openApp",
  }))
    $(id).textContent = t(key);
  document
    .querySelector("[data-popup-settings]")
    .setAttribute("aria-label", t("appearance"));
  $("langToggle").textContent = language.toUpperCase();
  applyAppearance();
}
function setSelection(format) {
  selected = format;
  document.querySelectorAll("[data-select-stream]").forEach((b) => {
    const yes = b.dataset.selectStream === format;
    b.setAttribute("aria-pressed", String(yes));
    b.querySelector(".f-check-dot").textContent = yes ? "●" : "○";
  });
}
function isYouTube() {
  return /(?:youtube\.com|youtu\.be)/i.test(tab?.url || "");
}
function pickSubtitle() {
  return (
    subs.find(
      (s) =>
        (s.code === "tr" || s.lang === "tr") &&
        !s.isForced &&
        !/forced/i.test(s.url || ""),
    ) ||
    subs.find((s) => s.code === "tr" || s.lang === "tr") ||
    subs.find((s) => s.code === "en" || s.lang === "en") ||
    subs[0]
  );
}
function renderMedia() {
  $("pageTitle").textContent = current
    ? cleanMediaTitle(current.name || tab?.title) || tab?.title || t("video")
    : t("empty");
  $("mediaSummary").textContent = current
    ? `${1 + (current.audioTracks?.length || 0)} ${t("streams")} · ${subs.length} ${t("available")}`
    : t("play");
  $("videoMeta").textContent =
    current?.videoQualities?.[0]?.label || current?.quality || t("detected");
  $("audioMeta").textContent =
    current?.audioTracks?.[0]?.label || t("audioFormat");
  const sub = pickSubtitle();
  $("subtitleMeta").textContent = sub
    ? `${sub.label || sub.lang || sub.code || "—"} · SRT`
    : t("noSubtitle");
  document.querySelector('[data-select-stream="subtitle"]').disabled =
    !sub && !isYouTube();
  if (selected === "subtitle" && !sub && !isYouTube()) setSelection("video");
  for (const key of ["video", "audio"])
    document.querySelector(`[data-select-stream="${key}"]`).disabled = !current;
  document.querySelector("[data-send]").disabled = !current || !online || busy;
}
async function resolveMedia() {
  tab = await currentTab();
  if (!tab?.id) {
    renderMedia();
    return;
  }
  const [active, list, detected] = await Promise.all([
    getActiveMediaFromTab(tab.id),
    send({ type: "getMedia", tabId: tab.id }),
    getTabSubtitles(tab.id),
  ]);
  current =
    active ||
    pickBestMedia(
      (Array.isArray(list) ? list : []).filter(
        (m) =>
          m?.url &&
          !/googlevideo|doubleclick|googleads|sublist_aud/i.test(m.url),
      ),
    ) ||
    null;
  if (isYouTube()) {
    const m = tab.url.match(/(?:v=|\/shorts\/|youtu\.be\/)([a-zA-Z0-9_-]{11})/);
    if (m)
      current = {
        ...(current || {}),
        url: "https://www.youtube.com/watch?v=" + m[1],
        name: tab.title,
        kind: "video",
      };
  }
  const seen = new Set();
  subs = [...(current?.subtitles || []), ...(detected || [])].filter((s) => {
    const key = s.url || s.content;
    if (!key || seen.has(key)) return false;
    seen.add(key);
    return true;
  });
  renderMedia();
}
async function refreshStatus() {
  const st = await send({ type: "status" });
  online = !!st?.online;
  if (!changed.has("accent") && /^#[\da-f]{6}$/i.test(st?.accentColor || ""))
    accent = st.accentColor;
  if (!changed.has("theme") && ["dark", "light"].includes(st?.themeMode))
    theme = st.themeMode;
  if (!changed.has("language") && ["tr", "en"].includes(st?.language))
    language = st.language;
  localize();
  $("connectionText").textContent = online
    ? t("connected")
    : st?.reachable
      ? t("pairing")
      : t("offline");
  $("connectionDot").style.background = online
    ? "var(--f-success)"
    : "var(--f-muted)";
  $("notice").hidden = online;
  $("noticeText").textContent = st?.reachable
    ? t("pairHelp")
    : t("offlineHelp");
  $("pairCode").hidden = $("pairButton").hidden = !st?.reachable || online;
  $("launchButton").hidden = !st?.canLaunch || online;
  if (st?.settings) {
    for (const key of ["intercept", "sniff", "autoLaunch"])
      $(key).checked = !!st.settings[key];
    $("port").value = st.settings.port || 48213;
    $("minSize").value = st.settings.minSizeMB || 0;
  }
  renderMedia();
}
async function dispatch(usePage = false) {
  if (busy) return;
  const media = usePage
    ? { url: tab?.url, name: tab?.title, referer: tab?.url }
    : current;
  if (!media?.url || !/^https?:/i.test(media.url)) return;
  const format = usePage
      ? "video"
      : selected === "subtitle"
        ? "subtitles"
        : selected,
    sub = pickSubtitle();
  busy = true;
  renderMedia();
  feedback(t("sending"));
  try {
    const r = await send({
      type: "download",
      url: media.url,
      filename: cleanMediaTitle(media.name || tab?.title) || "",
      referer: formatReferer(media.referer || media.initiator, tab?.url),
      kind: "video",
      format,
      audioOnly: format === "audio",
      subtitlesOnly: format === "subtitles",
      writeSubtitles: format === "video",
      subtitleLangs: sub?.code || sub?.lang || "tr,en",
      subtitleUrl: sub?.url || "",
      subtitles: subs,
      videoQualities: media.videoQualities || [],
      audioTracks: media.audioTracks || [],
      audioStreamUrl: media.audioStreamUrl,
    });
    feedback(r?.ok ? t("sent") : r?.cancelled ? "" : t("failed"));
  } finally {
    busy = false;
    renderMedia();
    await loadTransfers();
  }
}
async function savePreference(key, value) {
  changed.add(key);
  const field = {
    accent: "accentColor",
    theme: "themeMode",
    language: "language",
  }[key];
  await api.storage.local.set({ [field]: value });
  await send({ type: "setAppConfig", [field]: value });
}
async function loadTransfers() {
  const data = await send({ type: "downloads" }),
    rows = (Array.isArray(data) ? data : []).filter((d) =>
      ["downloading", "probing", "queued", "paused"].includes(d.state),
    );
  $("transfers").hidden = !rows.length;
  const list = $("transferList");
  list.replaceChildren();
  for (const d of rows) {
    const row = document.createElement("div"),
      name = document.createElement("p");
    name.className = "transfer-title";
    name.textContent = d.name || "";
    row.appendChild(name);
    for (const action of [
      d.state === "paused" ? "resume" : "pause",
      "cancel",
    ]) {
      const b = document.createElement("button");
      b.textContent = t(action);
      b.onclick = () =>
        send({ type: "action", id: d.id, action }).then(loadTransfers);
      row.appendChild(b);
    }
    list.appendChild(row);
  }
}
async function initialize() {
  installIcons();
  for (const [name, color] of [
    ["Lavanta", "#C3AFFF"],
    ["Pembe", "#EC4899"],
    ["Gri", "#94A3B8"],
    ["Kırmızı", "#E50914"],
    ["Mavi", "#00E5FF"],
    ["Yeşil", "#10B981"],
    ["Turuncu", "#FF6B00"],
    ["Altın", "#F59E0B"],
  ]) {
    const b = document.createElement("button");
    b.className = "f-swatch";
    b.dataset.color = color;
    b.style.setProperty("--chip", color);
    b.setAttribute("aria-label", name);
    b.title = name;
    b.onclick = () => {
      accent = color;
      applyAppearance();
      savePreference("accent", color);
    };
    $("swatches").appendChild(b);
  }
  $("customAccent").oninput = (e) => {
    accent = e.target.value;
    applyAppearance();
    savePreference("accent", accent);
  };
  document.querySelectorAll("[data-mode]").forEach(
    (b) =>
      (b.onclick = () => {
        theme = b.dataset.mode;
        applyAppearance();
        savePreference("theme", theme);
      }),
  );
  document
    .querySelectorAll("[data-select-stream]")
    .forEach((b) => (b.onclick = () => setSelection(b.dataset.selectStream)));
  document.querySelector("[data-send]").onclick = () => dispatch();
  document.querySelector("[data-demo-action]").onclick = () => dispatch(true);
  document.querySelector("[data-popup-settings]").onclick = () => {
    const open = $("settingsPanel").hidden;
    $("settingsPanel").hidden = !open;
    $("capturePanel").hidden = open;
    document
      .querySelector("[data-popup-settings]")
      .setAttribute("aria-expanded", String(open));
  };
  $("langToggle").onclick = () => {
    language = language === "tr" ? "en" : "tr";
    localize();
    renderMedia();
    savePreference("language", language);
  };
  for (const key of ["intercept", "sniff", "autoLaunch"])
    $(key).onchange = (e) =>
      send({ type: "setSettings", settings: { [key]: e.target.checked } });
  $("port").onchange = (e) =>
    send({
      type: "setSettings",
      settings: {
        port: Math.max(1024, Math.min(65535, Number(e.target.value) || 48213)),
      },
    });
  $("minSize").onchange = (e) =>
    send({
      type: "setSettings",
      settings: { minSizeMB: Math.max(0, Number(e.target.value) || 0) },
    });
  $("pageFileButton").onclick = () =>
    tab?.url &&
    send({ type: "download", url: tab.url, referer: tab.url, kind: "file" });
  $("pairButton").onclick = async () => {
    const r = await send({
      type: "setToken",
      token: $("pairCode").value.trim(),
    });
    if (r?.ok) {
      $("pairCode").value = "";
      await refreshStatus();
    } else feedback(t("failed"));
  };
  $("launchButton").onclick = () =>
    send({ type: "launchApp" }).then(() => setTimeout(refreshStatus, 1200));
  const st = await api.storage.local.get([
    "accentColor",
    "themeMode",
    "language",
  ]);
  if (/^#[\da-f]{6}$/i.test(st?.accentColor || "")) accent = st.accentColor;
  if (["dark", "light"].includes(st?.themeMode)) theme = st.themeMode;
  if (["tr", "en"].includes(st?.language)) language = st.language;
  localize();
  await Promise.all([refreshStatus(), resolveMedia(), loadTransfers()]);
  setInterval(loadTransfers, 1500);
  setInterval(refreshStatus, 4000);
}
initialize().catch(() => feedback(t("failed")));
