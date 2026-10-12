# FM Download Manager v3.0.0 🚀

Modern, ultra-hızlı, çok parçalı indirme yöneticisi, BitTorrent motoru ve tarayıcı entegrasyonu (Windows x64).

---

## 🇹🇷 Neler Yeni? (Changelog)

### 💎 Yepyeni Marka Kimliği & Logo Tasarımı
- **Özel Tasarım FM Logosu:** Eski varsayılan turkuaz simge tamamen kaldırılarak, koyu bordo ve metalik titanyum zemin üzerinde aerodinamik çift kanatlı beyaz indirme oku ile modern bir **FM** amblemi tasarlandı.
- **Tüm Çözünürlüklerde Net:** 16x16, 32x32 (tarayıcı araç çubuğu), 48x48, 96x96, 128x128 (eklenti mağazası) ve çok katmanlı 256x256 `app.ico` (Windows görev çubuğu ve uygulama simgesi) entegre edildi.
- **Koyu Bordo / Vişne Çürüğü Paleti:** Turuncu yerine modern `#C92A3E` (ana vurgu), `#E11D48` (hover), `#9F1239` (basılı) ve `#121318` koyu arka plan renkleri uygulandı.
- **Bahnschrift Tipografisi & Oval Hatlar:** Modern Windows DIN tipografisi ve 10–16px oval hap (pill) biçimli kontroller.

### ⚡ Canlı Vektör Hız Grafiği (Live Speed Sparkline)
- **Yan Panel Hız Kartı:** Sol kenar çubuğundaki toplam indirme hızı kartına donanım hızlandırmalı gerçek zamanlı canlı vektör hız grafiği eklendi.
- **Ayrıntılar Penceresi Grafiği:** İndirme ayrıntıları penceresinde anlık hız dalgalanmalarını ve tepe hız (pik) değerini gösteren bordo gradyanlı canlı grafik eklendi.

### 🧲 BitTorrent & Magnet Bağlantı Desteği
- **Entegre Torrent Motoru:** `MonoTorrent 3.0.2` çekirdeğe entegre edildi. Artık `magnet:?xt=urn:btih:...` bağlantıları ve `.torrent` dosyaları doğrudan FM Download Manager ile indirilebiliyor.
- **Otomatik Pano Yakalama:** Panoya kopyalanan magnet bağlantıları anında tespit edilir ve tek tıkla indirme onayı sunulur.

### 📂 Kategoriye Göre Otomatik Klasörleme
- İndirilen dosyalar uzantılarına göre otomatik olarak alt klasörlere ayrılır:
  - `Downloads\Videos` (mp4, mkv, webm, avi, mov vb.)
  - `Downloads\Music` (mp3, flac, wav, aac vb.)
  - `Downloads\Images` (png, jpg, webp, gif vb.)
  - `Downloads\Programs` (exe, msi, iso, apk vb.)
  - `Downloads\Documents` (pdf, docx, xlsx vb.)
  - `Downloads\Compressed` (zip, rar, 7z, tar vb.)
  - `Downloads\Torrents` (torrent ve magnet indirmeleri)
- Ayarlar penceresinden tek tıkla açılıp kapatılabilir.

### ⏱️ Zamanlayıcı / Kuyruk Planlayıcı (Scheduler)
- Belirlenen saat aralığında (örneğin gece 02:00 – 07:00, gece yarısını aşan pencereler dahil) indirmeleri otomatik başlatma ve bitiş saatinde duraklatma seçeneği eklendi.

### 🌐 Tek Tıkla Tarayıcı Eklentisi Kurulumu & Otomatik Eşleştirme
- **Tek Tıkla Kurulum:** Eklenti sihirbazından Chrome / Edge / Brave butonuna basıldığında eklenti klasör yolu anında panoya kopyalanır ve `--load-extension` parametresiyle tarayıcı doğrudan eklenti geliştirici modunda açılır.
- **Otomatik Eşleştirme (Zero-Click Pairing):** Eklenti açıldığında panodaki 64 karakterlik güvenlik kodu algılanarak tek bir tık dahi yapmadan anında eşleşir ve yeşile döner.

### 🔧 Hata Düzeltmeleri & Kararlılık
- **YouTube "The page needs to be reloaded" Çözüldü:** Stale tarayıcı çerezlerinin YouTube bot korumasına takılması engellendi; çerez reddi durumunda otomatik temiz bağlantı moduna geçiş eklendi.
- **İsimsiz Düşme Düzeltildi:** Panodan link eklendiğinde veya YouTube videosu indirilirken listenin "isimsiz" görünmesi sorunu çözüldü; akıllı dinamik dosya adı çözümleyicisi (`DisplayName`) eklendi.
- **Toplu Ekleme Penceresi:** "İndirmeyi Başlat" butonundaki metin kırpılması ve metin kutusundaki kaydırma çubuğu görsel hatası giderildi; magnet/torrent toplu ekleme desteği getirildi.
- **%100 Test Başarısı:** Çekirdeğin tüm 20 birim testi (parçalama, birleştirme, köprü güvenliği, limitler) eksiksiz onaylandı.

---

## 🇬🇧 What's New? (English)

- **Brand New Custom FM Logo:** Replaced placeholder cyan icon with high-contrast, titanium & burgundy FM monogram and precision speed arrow across 16x16, 32x32, 48x48, 96x96, 128x128 and 256x256 `app.ico`.
- **Live Vector Speed Charts:** Hardware-accelerated real-time sparkline speed graphs in sidebar and download details with peak speed indicators.
- **BitTorrent & Magnet Integration:** Full support for `magnet:?` and `.torrent` downloads via integrated MonoTorrent engine.
- **Smart Category Subfolders:** Automatic routing of finished downloads into Videos, Music, Images, Programs, Documents, Compressed, and Torrents.
- **Queue Scheduler:** Configurable start & stop schedule windows with auto-resume and auto-pause.
- **One-Click Extension Setup & Zero-Click Pairing:** Automatic extension path clipboard copy, `--load-extension` launch, and clipboard auto-pairing.
- **YouTube & Name Resolution Fixes:** Fixed YouTube bot reload error with cookie fallback, eliminated blank/unnamed list items with smart `DisplayName` resolver.
- **20/20 Test Suite Pass:** Verified 100% test suite compatibility.

---

## 📦 Kurulum (Installation)
1. Aşağıdaki **`FMDownloadManager-Setup-3.0.0.exe`** dosyasını indirin ve kurun.
2. Yönetici yetkisi gerektirmeden kullanıcı bazlı temiz kurulum yapılır.
3. Uygulamayı başlatın, **Tarayıcı Eklentisi** butonuna tıklayarak tarayıcınıza bağlayın.
