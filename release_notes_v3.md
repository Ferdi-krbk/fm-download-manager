# FM Download Manager v3.0.0 🚀

Modern, ultra-hızlı, çok parçalı indirme yöneticisi ve tarayıcı entegrasyonu (Windows x64).

---

## 🇹🇷 Neler Yeni? (Changelog)

### 🎨 Yeni Görsel Kimlik & Arayüz (Burgundy Dark Theme)
- **Yeni Renk Paleti:** Klasik turuncu vurgu yerine modern ve zarif **Koyu Bordo / Vişne Çürüğü Kırmızısı** (`#C92A3E` ana vurgu, `#E11D48` hover, `#9F1239` basılı, `#2E1018` koyu arka plan tonu) uygulandı.
- **Geometrik DIN Tipografisi:** Sert ve köşeli yazı tipi yerine modern Windows geometrisine uygun **`Bahnschrift`** (fallback: `'Segoe UI Variable Display'`) yazı tipine geçildi.
- **Oval & Yumuşak Hatlar:** Kare köşeler terk edilerek 10px – 16px oval hap (pill) biçiminde modern kontrol ve buton tasarımlarına geçildi.
- **Durum Çubuğu Düzeltmesi:** Alt durum çubuğundaki metinlerin üst üste binmesi düzeltildi; dikey modern ayrıcılarla disk durumu, hazır durumu ve eklenti durumu ayrıldı.

### 🎬 Güçlendirilmiş Medya & Video Yakalama (YouTube & X / Twitter)
- **Hata Giderildi:** Tarayıcı eklentisinden YouTube ve X videoları indirilirken ortaya çıkan "Hata" (Error 501) problemi kökten çözüldü.
- **Otomatik Araç Keşfi:** `yt-dlp.exe` ve `ffmpeg.exe` araçları hem uygulama klasöründe hem kullanıcı dizininde hem de sistem `PATH` yolunda dinamik taranarak köprüye bağlandı.
- **Özel Eklenti Tasarımı:** Chrome / Edge / Brave eklentisi, masaüstü uygulamasının yeni Koyu Bordo ve Bahnschrift tasarımına uygun olarak baştan stilize edildi.

### 🛠️ Kararlılık ve Performans İyileştirmeleri
- **Açılmama Sorunu Çözüldü:** WPF `ResourceDictionary` içerisindeki mükerrer `SystemColors` anahtarı temizlendi, başlangıç çökmeleri engellendi.
- **Kopya & Eşleştirme Donması Çözüldü:** Pano kopyalama işlemi non-blocking Win32 STA thread altyapısına taşındı.
- **%100 Test Başarısı:** Çekirdek indirme, parçalama, duraklatma/devam etme, hız limitleyici ve köprü testlerinin 20/20'si başarıyla onaylandı.

---

## 🇬🇧 What's New? (English)

- **Burgundy Dark Theme:** Replaced bright orange with sophisticated deep crimson / burgundy red palette (`#C92A3E`).
- **Bahnschrift Typography:** High-legibility modern geometric DIN typography across desktop and browser extension.
- **Oval Pill Contours:** Smooth 10–16px rounded pill buttons, cards, inputs, and context menus.
- **YouTube & X (Twitter) Media Capture Fix:** Resolved 501 error by linking `ToolPaths` and `yt-dlp` hooks directly to `BridgeServer`.
- **Browser Extension Redesign:** Revamped Chrome/Edge/Brave extension popup with matching custom dark theme.
- **Zero Startup Crashes:** Fixed duplicate SystemColors resource dictionary key and added robust crash reporting.

---

## 📦 Kurulum (Installation)
1. Aşağıdaki **`FMDownloadManager-Setup-3.0.0.exe`** dosyasını indirin ve çalıştırın.
2. Yönetici yetkisi gerektirmeden kullanıcı bazlı temiz kurulum yapılır.
3. Uygulama içerisinden **Tarayıcı Eklentisi** menüsüne tıklayarak Chrome, Edge veya Brave tarayıcınıza eklentiyi tek tıkla yükleyin.
