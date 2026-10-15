const assert = require('assert');

// Simulate YouTube player response payload
const sampleYtPlayerResponse = {
  videoDetails: { title: "$456,000 Squid Game In Real Life!" },
  streamingData: {
    adaptiveFormats: [
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

function parseYtData(obj) {
  const audioTracks = [];
  const seenAudios = new Set();
  const formats = obj.streamingData?.adaptiveFormats || [];
  formats.forEach(f => {
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

  return { audioTracks, subtitles };
}

console.log('--- Testing YouTube Multi-Track Extraction ---');
const parsed = parseYtData(sampleYtPlayerResponse);

assert.strictEqual(parsed.audioTracks.length, 8, 'Must extract 8 audio tracks');
assert.ok(parsed.audioTracks.some(a => a.lang === 'tr' && a.isDefault), 'Turkish dub must be present and marked default');
assert.ok(parsed.audioTracks.some(a => a.lang === 'en-US' && a.label.includes('orijinal')), 'English original track must be detected');
assert.ok(parsed.audioTracks.some(a => a.lang === 'es' && a.label === 'İspanyolca'), 'Spanish dub must be detected');

assert.strictEqual(parsed.subtitles.length, 4, 'Must extract 4 subtitles');
assert.ok(parsed.subtitles.some(s => s.code === 'tr' && s.url.includes('lang=tr')), 'Turkish subtitle must have correct url');
assert.ok(parsed.subtitles.some(s => s.code === 'de' && s.url.includes('lang=de')), 'German subtitle must have correct url');

console.log('✅ ALL YOUTUBE MULTI-TRACK EXTRACTION TESTS PASSED!');
