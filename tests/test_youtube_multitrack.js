const assert = require('assert');

// Simulate YouTube player response payload
const sampleYtPlayerResponse = {
  videoDetails: { title: "$456,000 Squid Game In Real Life!" },
  streamingData: {
    adaptiveFormats: [
      { mimeType: 'video/webm; codecs="vp9"', width: 3840, height: 1600, qualityLabel: '2160p60', bitrate: 18500000 },
      { mimeType: 'video/webm; codecs="vp9"', width: 2560, height: 1080, qualityLabel: '1440p', bitrate: 9200000 },
      { mimeType: 'video/mp4; codecs="avc1"', width: 1920, height: 1080, qualityLabel: '1080p', bitrate: 4500000 },
      { mimeType: 'video/mp4; codecs="avc1"', width: 1280, height: 720, qualityLabel: '720p', bitrate: 2200000 },
      { mimeType: 'audio/webm; codecs="opus"', audioTrack: { displayName: "Arapça", id: "ar.3", audioIsDefault: false } },
      { mimeType: 'audio/webm; codecs="opus"', audioTrack: { displayName: "Almanca", id: "de.3", audioIsDefault: false } },
      { mimeType: 'audio/webm; codecs="opus"', audioTrack: { displayName: "İspanyolca", id: "es.3", audioIsDefault: false } },
      { mimeType: 'audio/webm; codecs="opus"', audioTrack: { displayName: "İtalyanca", id: "it.3", audioIsDefault: false } },
      { mimeType: 'audio/webm; codecs="opus"', audioTrack: { displayName: "Japonca", id: "ja.3", audioIsDefault: false } },
      { mimeType: 'audio/webm; codecs="opus"', audioTrack: { displayName: "Korece", id: "ko.3", audioIsDefault: false } },
      { mimeType: 'audio/webm; codecs="opus"', audioTrack: { displayName: "Türkçe", id: "tr.3", audioIsDefault: true } },
      { mimeType: 'audio/webm; codecs="opus"', audioTrack: { displayName: "İngilizce (US) orijinal", id: "en-US.4", audioIsDefault: false } }
    ]
  },
  captions: {
    playerCaptionsTracklistRenderer: {
      captionTracks: [
        { baseUrl: "https://www.youtube.com/api/timedtext?v=test&lang=tr", languageCode: "tr", name: { simpleText: "Türkçe" } },
        { baseUrl: "https://www.youtube.com/api/timedtext?v=test&lang=en", languageCode: "en", name: { simpleText: "İngilizce" } },
        { baseUrl: "https://www.youtube.com/api/timedtext?v=test&lang=es", languageCode: "es", name: { simpleText: "İspanyolca" } },
        { baseUrl: "https://www.youtube.com/api/timedtext?v=test&lang=de", languageCode: "de", name: { simpleText: "Almanca" } }
      ]
    }
  }
};

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
  return shortSide > 0 ? shortSide : h;
}

function parseYtData(obj) {
  const videoQualities = [];
  const seenHeights = new Set();
  const audioTracks = [];
  const seenAudios = new Set();
  const formats = obj.streamingData?.adaptiveFormats || [];
  formats.forEach(f => {
    const isVideo = (f.mimeType && f.mimeType.startsWith('video/')) || (f.height && f.height > 0);
    if (isVideo) {
      const tier = normalizeResolutionTier(f.width || 0, f.height || 0, f.qualityLabel);
      if (tier > 0 && !seenHeights.has(tier)) {
        seenHeights.add(tier);
        videoQualities.push({ height: tier, label: `${tier}p` });
      }
    }
    if (f.audioTrack && f.audioTrack.displayName && !seenAudios.has(f.audioTrack.displayName)) {
      seenAudios.add(f.audioTrack.displayName);
      const idParts = (f.audioTrack.id || '').split('.');
      const langCode = idParts[0] || '';
      audioTracks.push({
        trackId: f.audioTrack.id || langCode,
        lang: langCode,
        label: f.audioTrack.displayName,
        isDefault: !!f.audioTrack.audioIsDefault
      });
    }
  });
  videoQualities.sort((a, b) => b.height - a.height);

  const subtitles = [];
  const seenSubs = new Set();
  const caps = obj.captions?.playerCaptionsTracklistRenderer?.captionTracks || [];
  caps.forEach(c => {
    const code = c.languageCode || '';
    const rawLabel = c.name?.simpleText || (c.name?.runs && c.name.runs[0]?.text) || code;
    const isAuto = (c.vssId && c.vssId.startsWith('a.')) || /otomatik|auto/i.test(rawLabel);
    const label = isAuto && !rawLabel.toLowerCase().includes('otomatik') ? `${rawLabel} (Otomatik)` : rawLabel;
    const u = c.baseUrl;
    if (u && !seenSubs.has(code + '_' + label)) {
      seenSubs.add(code + '_' + label);
      subtitles.push({
        url: u,
        code: code,
        label: label,
        isForced: false
      });
    }
  });

  return { videoQualities, audioTracks, subtitles };
}

console.log('--- Testing YouTube Multi-Track & 4K/2K Extraction ---');
const parsed = parseYtData(sampleYtPlayerResponse);

assert.strictEqual(parsed.videoQualities.length, 4, 'Must extract 4 video qualities (2160p, 1440p, 1080p, 720p)');
assert.strictEqual(parsed.videoQualities[0].height, 2160, 'First quality must be 2160p (4K)');
assert.strictEqual(parsed.videoQualities[1].height, 1440, 'Second quality must be 1440p (2K)');
assert.strictEqual(parsed.videoQualities[2].height, 1080, 'Third quality must be 1080p');
assert.strictEqual(parsed.videoQualities[3].height, 720, 'Fourth quality must be 720p');

assert.strictEqual(parsed.audioTracks.length, 8, 'Must extract 8 audio tracks');
assert.ok(parsed.audioTracks.some(a => a.lang === 'tr' && a.isDefault), 'Turkish dub must be present and marked default');
assert.ok(parsed.audioTracks.some(a => a.lang === 'en-US' && a.label.includes('orijinal')), 'English original track must be detected');
assert.ok(parsed.audioTracks.some(a => a.lang === 'es' && a.label === 'İspanyolca'), 'Spanish dub must be detected');

assert.strictEqual(parsed.subtitles.length, 4, 'Must extract 4 subtitles');
assert.ok(parsed.subtitles.some(s => s.code === 'tr' && s.url.includes('lang=tr')), 'Turkish subtitle must have correct url');
assert.ok(parsed.subtitles.some(s => s.code === 'de' && s.url.includes('lang=de')), 'German subtitle must have correct url');

console.log('✅ ALL YOUTUBE MULTI-TRACK & 4K/2K EXTRACTION TESTS PASSED!');
