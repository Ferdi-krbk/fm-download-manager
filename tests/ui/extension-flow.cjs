// Real extension DOM in an isolated browser; chrome.* is a fixture, no personal browser profile is used.
const assert = require("node:assert/strict");
const fs = require("node:fs");
const path = require("node:path");
const { chromium } = require(
  process.env.MCDM_PLAYWRIGHT_MODULE || "playwright",
);
const repository = path.resolve(__dirname, "../..");
const output = path.join(repository, "dist/extension-ui");
fs.mkdirSync(output, { recursive: true });

function chromeFixture() {
  const media = {
    url: "https://fm.test/sample.mp4",
    name: "Sample video",
    kind: "video",
    size: 10485760,
    quality: "720p",
    subtitles: [
      {
        code: "tr",
        lang: "tr",
        label: "Turkish",
        url: "https://fm.test/captions.vtt",
      },
    ],
  };
  window.fixtureMessages = [];
  window.fixtureStore = {
    accentColor: "#C3AFFF",
    themeMode: "dark",
    language: "en",
  };
  window.fixtureStorageListeners = [];
  const status = () => ({
    online: true,
    reachable: true,
    paired: true,
    accentColor: window.fixtureStore.accentColor,
    themeMode: window.fixtureStore.themeMode,
    language: "en",
    app: { version: "1.1.0", active: 0 },
    settings: {
      intercept: true,
      sniff: true,
      autoLaunch: false,
      port: 48213,
      minSizeMB: 0,
    },
  });
  const response = (m) =>
    m.type === "status"
      ? status()
      : m.type === "getMedia"
        ? [media]
        : m.type === "getBestMedia"
          ? { best: media, subtitles: media.subtitles }
          : m.type === "getActiveMedia"
            ? { ...media, ...{ subtitles: media.subtitles } }
            : m.type === "downloads"
              ? []
              : m.type === "getSubtitles"
                ? media.subtitles
                : { ok: true };
  window.chrome = {
    runtime: {
      lastError: null,
      getURL: (p) => "https://fm.test/" + p,
      onMessage: { addListener() {} },
      sendMessage(m, cb) {
        window.fixtureMessages.push(m);
        const r = response(m);
        if (cb) cb(r);
        return Promise.resolve(r);
      },
    },
    storage: {
      local: {
        get(keys, cb) {
          const r = { ...window.fixtureStore };
          if (cb) cb(r);
          return Promise.resolve(r);
        },
        set(values) {
          Object.assign(window.fixtureStore, values);
          return Promise.resolve();
        },
      },
      onChanged: {
        addListener(fn) {
          window.fixtureStorageListeners.push(fn);
        },
      },
    },
    tabs: {
      query: async () => [
        { id: 1, url: "https://fm.test/player", title: "Sample video" },
      ],
      sendMessage(id, m, options, cb) {
        const callback = typeof options === "function" ? options : cb;
        if (callback) callback(response(m));
        return Promise.resolve(response(m));
      },
    },
    i18n: {
      getMessage: (key) =>
        ({ kindMedia: "Video", popupDownload: "Send", popupCancel: "Cancel" })[
          key
        ] || key,
    },
  };
}

(async () => {
  const options = { headless: true };
  if (process.env.MCDM_BROWSER_EXECUTABLE)
    options.executablePath = process.env.MCDM_BROWSER_EXECUTABLE;
  const browser = await chromium.launch(options);
  try {
    const context = await browser.newContext({
      viewport: { width: 1080, height: 800 },
      reducedMotion: "reduce",
      deviceScaleFactor: 2,
    });
    await context.addInitScript(chromeFixture);
    await context.route("https://fm.test/**", async (route) => {
      const pathname = new URL(route.request().url()).pathname;
      if (pathname === "/player")
        return route.fulfill({
          contentType: "text/html",
          body: '<!doctype html><title>Sample video</title><style>body{margin:40px;background:#101014}video{width:720px;height:405px;background:#202027}</style><video controls autoplay muted loop><source src="/sample.mp4" type="video/mp4"><track src="/captions.vtt" kind="subtitles" srclang="tr" label="Turkish"></video>',
        });
      if (pathname === "/sample.mp4") {
        const fixture =
          process.env.MCDM_SAMPLE_VIDEO ||
          "dist/flow-acceptance/fixtures/sample-clip.mp4";
        return route.fulfill({
          contentType: "video/mp4",
          body: fs.readFileSync(path.resolve(repository, fixture)),
        });
      }
      if (pathname === "/captions.vtt")
        return route.fulfill({
          contentType: "text/vtt",
          body: "WEBVTT\n\n00:00.000 --> 00:04.000\nSample captions\n",
        });
      const file = path.resolve(repository, "extension", pathname.slice(1));
      assert(file.startsWith(path.join(repository, "extension") + path.sep));
      return route.fulfill({
        path: file,
        contentType: file.endsWith(".js")
          ? "text/javascript"
          : file.endsWith(".css")
            ? "text/css"
            : file.endsWith(".ttf") ? "font/ttf" : "text/html",
      });
    });
    const page = await context.newPage();
    const errors = [];
    page.on("pageerror", (error) => errors.push(error.message));
    await page.goto("https://fm.test/player");
    await page.waitForFunction(
      () => document.querySelector("video").readyState >= 1,
    );
    await page.addScriptTag({
      path: path.join(repository, "extension/floating_bar.js"),
    });
    await page.locator("video").hover();
    const mark = page.locator("#fm-floating-bar-host #fm-btn-action");
    await mark.waitFor({ state: "visible" });
    await page.waitForFunction(() => Array.from(document.fonts).some(face => face.family === 'FM Inter' && face.weight === '400' && face.status === 'loaded'));
    assert((await page.locator('#fm-floating-bar-host .fm-item').first().evaluate(e => getComputedStyle(e).fontFamily)).includes('FM Inter'));
    const box = await page
      .locator("#fm-floating-bar-host #fm-main-btn")
      .boundingBox();
    assert.equal(box.width, 32);
    assert.equal(box.height, 32);
    assert.equal(await mark.locator(".fm-logo-badge").innerText(), "fm.");
    await mark.click();
    assert.equal(await mark.getAttribute("aria-expanded"), "true");
    assert.equal(
      await page.evaluate(
        () => fixtureMessages.filter((m) => m.type === "download").length,
      ),
      0,
    );
    await page.locator("#fm-floating-bar-host #fm-opt-audio").click();
    await page.waitForFunction(() =>
      fixtureMessages.some((m) => m.type === "download"),
    );
    const sent = await page.evaluate(() =>
      fixtureMessages.find((m) => m.type === "download"),
    );
    assert.equal(sent.format, "audio");
    assert.equal(sent.url, "https://fm.test/sample.mp4");
    await mark.click();
    await mark.press("Escape");
    assert.equal(await mark.getAttribute("aria-expanded"), "false");
    await mark.click();
    await page.screenshot({ path: path.join(output, "hover-menu.png") });

    const popup = await context.newPage();
    popup.on("pageerror", (error) => errors.push(error.message));
    await popup.setViewportSize({ width: 302, height: 760 });
    await popup.goto("https://fm.test/popup.html");
    await popup.waitForFunction(
      () => !document.querySelector("[data-send]").disabled,
    );
    await popup
      .locator("body")
      .screenshot({ path: path.join(output, "popup-dark.png") });
    await popup.evaluate(() => document.fonts.ready);
    assert(await popup.evaluate(() => Array.from(document.fonts).some(face => face.family === 'FM Inter' && face.status === 'loaded')));
    await popup.locator("[data-popup-settings]").click();
    await popup.locator('[data-color="#EC4899"]').click();
    assert.equal(
      await popup.evaluate(() => fixtureStore.accentColor),
      "#EC4899",
    );
    assert.equal(
      await popup
        .locator("#fm-flow")
        .evaluate((e) => e.style.getPropertyValue("--f-accent")),
      "#EC4899",
    );
    await popup.locator('[data-mode="light"]').click();
    assert.equal(
      await popup.locator("#fm-flow").evaluate((e) => e.style.colorScheme),
      "light",
    );
    await popup.locator("[data-popup-settings]").click();
    await popup
      .locator("body")
      .screenshot({ path: path.join(output, "popup-light.png") });
    await popup.locator("[data-popup-settings]").click();
    await popup.locator('[data-mode="dark"]').click();
    await popup.locator("#customAccent").evaluate((e) => {
      e.value = "#808080";
      e.dispatchEvent(new Event("input", { bubbles: true }));
    });
    assert.equal(
      await popup.evaluate(() => fixtureStore.accentColor),
      "#808080",
    );
    for (const mode of ["light", "dark"]) {
      await popup.evaluate((mode) => {
        theme = mode;
        accent = "#FFFFFF";
        applyAppearance();
      }, mode);
      const contrast = await popup.evaluate(() => {
        const css = getComputedStyle(document.getElementById("fm-flow")),
          text = css
            .getPropertyValue("--f-accent-ink")
            .match(/[\d.]+/g)
            .map(Number);
        const lum = (rgb) => {
          const c = rgb.map((v) => {
            v /= 255;
            return v <= 0.04045 ? v / 12.92 : ((v + 0.055) / 1.055) ** 2.4;
          });
          return 0.2126 * c[0] + 0.7152 * c[1] + 0.0722 * c[2];
        };
        const a = lum(text),
          b =
            document.getElementById("fm-flow").style.colorScheme === "light"
              ? 1
              : lum([25, 25, 31]);
        return (Math.max(a, b) + 0.05) / (Math.min(a, b) + 0.05);
      });
      assert(contrast >= 4.5);
    }
    assert(
      await popup.evaluate(() => document.documentElement.scrollWidth <= 302),
    );
    await popup.locator("[data-popup-settings]").click();
    await popup.locator('[data-select-stream="video"]').click();
    await popup.locator("[data-send]").click();
    await popup.waitForFunction(() =>
      fixtureMessages.some((m) => m.type === "download"),
    );
    const download = await popup.evaluate(() =>
      fixtureMessages.find((m) => m.type === "download"),
    );
    assert.equal(download.kind, "video");
    assert.equal(download.url, "https://fm.test/sample.mp4");
    assert.equal(download.writeSubtitles, true);
    await popup.waitForFunction(
      () => !document.querySelector("[data-send]").disabled,
    );
    await popup.locator('[data-select-stream="audio"]').click();
    await popup.locator("[data-send]").click();
    await popup.waitForFunction(() =>
      fixtureMessages.some((m) => m.type === "download" && m.audioOnly),
    );
    await popup.waitForFunction(
      () => !document.querySelector("[data-send]").disabled,
    );
    await popup.locator('[data-select-stream="subtitle"]').click();
    await popup.locator("[data-send]").click();
    await popup.waitForFunction(() =>
      fixtureMessages.some((m) => m.type === "download" && m.subtitlesOnly),
    );
    assert.deepEqual(errors, []);
    console.log(
      "PASS real extension UI: 32px fm. mark, choices before download, audio dispatch, Escape, popup media dispatch, themes, palette/custom colors, text contrast and popup width.",
    );
  } finally {
    await browser.close();
  }
})().catch((error) => {
  console.error(error);
  process.exitCode = 1;
});
