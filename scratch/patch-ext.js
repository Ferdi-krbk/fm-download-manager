const fs = require('fs');
const add = {
  en: {
    popupNotPaired: 'App found – not paired',
    popupPairBox: 'Open MC Download Manager, choose "Copy pairing code" (tray menu or Browser setup), then paste the code here.',
    popupPairPlaceholder: 'Pairing code',
    popupPairButton: 'Pair',
    popupPairOk: 'Paired ✓',
    popupPairBad: 'Wrong code'
  },
  tr: {
    popupNotPaired: 'Uygulama bulundu – eşleşmedi',
    popupPairBox: 'MC Download Manager\'ı açın, "Eşleştirme kodunu kopyala"yı seçin (tepsi menüsü veya Tarayıcı kurulumu) ve kodu buraya yapıştırın.',
    popupPairPlaceholder: 'Eşleştirme kodu',
    popupPairButton: 'Eşleştir',
    popupPairOk: 'Eşleşti ✓',
    popupPairBad: 'Kod yanlış'
  }
};
for (const l of ['en', 'tr']) {
  const p = `extension/_locales/${l}/messages.json`;
  const j = JSON.parse(fs.readFileSync(p, 'utf8'));
  for (const [k, v] of Object.entries(add[l])) j[k] = { message: v };
  fs.writeFileSync(p, JSON.stringify(j, null, 2) + '\n');
}
const m = JSON.parse(fs.readFileSync('extension/manifest.json', 'utf8'));
m.permissions = m.permissions.filter((x) => x !== 'nativeMessaging');
m.version = '1.3.0.1';
fs.writeFileSync('extension/manifest.json', JSON.stringify(m, null, 2) + '\n');
