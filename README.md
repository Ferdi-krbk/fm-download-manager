<p align="center">
  <img src="docs/icon.png" width="130" height="130" alt="FM Download Manager Logo" />
</p>

<h1 align="center">FM Download Manager v3.4.3</h1>

<p align="center">
  <b>Ultra-hızlı, çok parçalı indirme yöneticisi, entegre BitTorrent motoru, akıllı tarayıcı entegrasyonu ve 4K medya yakalayıcısı (Windows 10 & 11).</b><br>
  <i>Modern Windows için sıfırdan inşa edilmiş, IDM ve FDM standartlarını aşan açık kaynaklı indirme istasyonu.</i>
</p>

<p align="center">
  <a href="https://github.com/Ferdi-krbk/fm-download-manager/releases"><img src="https://img.shields.io/badge/Release-v3.4.3-crimson.svg?style=for-the-badge&logo=windows" alt="Release v3.4.3"></a>
  <a href="https://dotnet.microsoft.com/download/dotnet/8.0"><img src="https://img.shields.io/badge/.NET-8.0%20LTS-purple.svg?style=for-the-badge&logo=dotnet" alt=".NET 8"></a>
  <a href="https://github.com/Ferdi-krbk/fm-download-manager"><img src="https://img.shields.io/badge/BitTorrent-MonoTorrent%203.0-blue.svg?style=for-the-badge&logo=bittorrent" alt="BitTorrent"></a>
  <a href="https://github.com/Ferdi-krbk/fm-download-manager"><img src="https://img.shields.io/badge/YouTube-yt--dlp%20FastClient-red.svg?style=for-the-badge&logo=youtube" alt="yt-dlp"></a>
  <a href="LICENSE"><img src="https://img.shields.io/badge/License-MIT-green.svg?style=for-the-badge" alt="License MIT"></a>
</p>

<p align="center">
  <a href="#-ekran-görüntüleri-showcase">Arayüz &amp; Ekran Görüntüleri</a> •
  <a href="#-öne-çıkan-özellikler-highlights">Özellikler</a> •
  <a href="#-mimari-architecture">Mimari</a> •
  <a href="#-tarayıcı-kurulumu-browser-integration">Tarayıcı Entegrasyonu</a> •
  <a href="#-klavye-kısayolları-shortcuts">Kısayollar</a> •
  <a href="#-kurulum--installation">Kurulum</a>
</p>

---

## 📸 Ekran Görüntüleri (Showcase)

<p align="center">
  <img src="docs/ui/dashboard_dark.png" width="900" alt="FM Download Manager Ana Ekran" style="border-radius: 8px; box-shadow: 0 8px 24px rgba(0,0,0,0.5);" />
  <br>
  <i>FM Download Manager v3.3.0 — Evrensel Medya Yakalama, 1 Gbps Turbo Motor, Otomatik Güncelleme Bildirimi ve Çok Parçalı İndirme Kuyruğu</i>
</p>

<br>

<table align="center" width="100%">
  <tr>
    <td width="33%" align="center">
      <img src="docs/ui/speed_limiter.png" width="280" alt="Canlı Hız Limiti &amp; Vektör Grafiği" />
      <br>
      <b>🎛️ Donanım Hızlandırmalı Hız Grafiği &amp; Slider</b>
      <p><i>EMA yumuşatma, pik hız göstergesi ve tek tıkla bant genişliği sınırlandırma</i></p>
    </td>
    <td width="33%" align="center">
      <img src="docs/ui/browser_setup.png" width="280" alt="Tarayıcı Eklentisi Kurulum Merkezi" />
      <br>
      <b>🌐 Sıfır Tık Eşleştirme (Zero-Click Pairing)</b>
      <p><i>Token.json tohumlama ile kod kopyalamadan otomatik bağlantı doğrulama</i></p>
    </td>
    <td width="33%" align="center">
      <img src="docs/ui/extension_popup.png" width="280" alt="Medya Yakalayıcı Eklenti" />
      <br>
      <b>🎬 Akıllı Medya Yakalayıcısı</b>
      <p><i>YouTube, Twitter, TikTok, HDFilmcehennemi, NotebookLM ve 1000+ siteden tek tıkla 4K/MP3 indirme</i></p>
    </td>
  </tr>
</table>

---

## 🚀 Öne Çıkan Özellikler (Highlights)

```
┌────────────────────────────────────────────────────────────────────────┐
│  FM DOWNLOAD MANAGER v3.4.3 - UNIVERSAL MEDIA, DUAL-AUDIO & SUBTITLES  │
├────────────────────────────────────────────────────────────────────────┤
│  ⚡ 1 Gbps Turbo Motor (32 Segment) │  🧲 Dahili BitTorrent & Magnet   │
│  📊 Canlı Vektör Hız Grafiği (EMA)  │  🎛️ Anlık Hız Limiti Kadranı     │
│  🎬 YouTube 4K / MP3 & Hız Kalkanı  │  💬 Otomatik VTT ➔ SRT Altyazı   │
│  🎧 Dual-Audio Çift Ses Desteği     │  🎥 Film / Dizi Siteleri Uyumu   │
│  🌐 Tek Tıkla Eklenti Kurulumu      │  ⏱️ Gece Kuyruk Planlayıcı       │
│  🎨 Bordo & AMOLED Çift Tema        │  🔒 256-Bit Yerel Token Güvenliği│
└────────────────────────────────────────────────────────────────────────┘
```

---

## 💎 Temel Yetenekler (Core Features)

### 1. ⚡ Dinamik Yeniden Segmentasyonlu Çok Parçalı Motor (Dynamic Resegmentation)
* Dosyaları 16 paralel bağlantıya kadar parçalayarak ISS sınırlarını ve sunucu bant genişliğini sonuna kadar kullanır.
* **Akıllı Parça Paylaşımı:** İndirme bitmeye yakınken yavaş kalan bir parça olduğunda, boşalan iş parçacığı o parçayı otomatik olarak ortadan ikiye bölerek kalan yarısını paralel çeker.

### 2. 🧲 Entegre BitTorrent & Magnet Desteği
* Harici bir torrent istemcisine (uTorrent, qBittorrent vb.) gerek kalmadan `magnet:?xt=urn:btih:...` ve `.torrent` bağlantılarını doğrudan çeker.
* MonoTorrent 3.0.2 motoruyla DHT, eş değişimi (PEX) ve şifreli aktarım sağlar.

### 3. 🎬 Süper Hızlı Medya & YouTube İndiricisi (MP4 Garantisi)
* `yt-dlp` ve `FFmpeg` motoruyla YouTube, Twitter/X, TikTok, Instagram ve 1000+ siteden tek tıkla video indirir.
* **Garantili MP4 Formatı:** Tüm videolar en yüksek kalitede otomatik olarak temiz MP4/M4A formatında birleştirilir ve remux edilir (asla uyumsuz WebM dosyaları oluşturmaz).
* **Format ve Kalite Seçici:** Bağlantı eklendiğinde açılan modern arayüzle *En Yüksek Kalite (4K/1080p)*, *720p HD* veya doğrudan *Yalnızca Ses Çıkar (MP3 / 320 kbps)* seçimi sunar.
* **Ultra-Hızlı Başlatma:** `--concurrent-fragments 4` ve optimize edilmiş istemci argümanlarıyla indirmeler saniyeler içinde anında başlar.

### 4. 🎧 Özel Film & Dizi Platformları ve Dual-Audio (Çift Ses) Desteği
* **Rapidrame & HDFilmcehennemi Uyumu:** Türkçe Dublaj ve Orijinal ses parçası barındıran çift sesli videolarda, sadece ses dosyasının inmesi hatası tamamen ortadan kaldırılmıştır.
* **Kusursuz Ses+Görüntü Eşleme:** Seçtiğiniz çözünürlük (`1080p`, `720p` vb.) tercih edilen ses parçasıyla otomatik olarak eşleştirilir ve ~700 MB - 2 GB aralığında eksiksiz MP4 video olarak teslim edilir.
* **Desteklenen Başlıca Platformlar:**
  * **HDFilmcehennemi**
  * **Rapidrame Player**
  * **FilmMakinesi**
  * **DiziPal**
  * **Closeload & Vidmoly**
  * **YouTube, Twitter/X, TikTok, Instagram, Reddit, Facebook, NotebookLM** ve 1000+ video kaynağı.

### 5. 💬 Evrensel Altyazı Yakalama & Otomatik VTT ➔ SRT Dönüştürücü
* **3 Farklı İndirme Seçeneği:**
  1. **Video ile Birlikte:** Videoyu seçtiğiniz dildeki altyazıyla birlikte indirme.
  2. **Yalnızca Altyazı (.srt):** Videoyu indirmeye gerek kalmadan, doğrudan `.srt` altyazı dosyasını saniyeler içinde kaydetme.
  3. **Yalnızca Video (Altyazısız):** Temiz, ham video akışını indirme.
* **Otomatik WebVTT ➔ SubRip (.srt) Dönüşümü:** Web oynatıcılarının sunduğu ham `.vtt` altyazıları otomatik olarak zaman damgası ve etiket temizliğinden geçirilerek VLC, televizyonlar ve tüm yerel oynatıcılarla %100 uyumlu standart `.srt` formatına dönüştürülür.
* **Gizli Akışları Yakalama:** Oynatıcıda o an seçili olmasa dahi ağ üzerinden akan Türkçe, İngilizce ve diğer dillerdeki tüm altyazı akışlarını otomatik listeler.

### 6. 🚀 IDM Tarzı Modern Yüzen İndirme Butonu (Floating Bar)
* **Shadow DOM & Trusted-Types Güvenliği:** Sayfa stillerinden ve sitelerin CSP/Trusted-Types güvenlik politikalarından etkilenmeyen bağımsız modern cam tasarım (Glassmorphic Pill). Saf DOM node inşasıyla YouTube üzerinde sıfır hata garantisi.
* **Akıllı Hover & Mousemove Yakalama:** YouTube veya film/dizi sitelerinde farenizi videonun üzerine getirdiğiniz anda sağ üst köşede pürüzsüz bir animasyonla belirir.
* **Hızlı Menü:** Tek tıkla *Video İndir*, *MP3 İndir* veya *Altyazı İndir* seçeneklerine erişim sağlar.

### 7. 📊 Desteklenen Platform & Özellik Matrisi

| Platform / Oynatıcı | Video İndirme (4K/1080p MP4) | Çift Ses / Dual-Audio (TR Dublaj + Orijinal) | Altyazı (.srt) Desteği | Yüzen Çubuk (Floating Bar) |
|---|:---:|:---:|:---:|:---:|
| **YouTube** | ✅ (4K60 / 1080p60 MP4) | ✅ Otomatik Eşleme | ✅ TR, EN, Otomatik (.srt) | ✅ Akıllı Hover |
| **HDFilmcehennemi** | ✅ (Full HD 1080p) | ✅ TR Dublaj / Orijinal Eşleme | ✅ VTT ➔ SRT Otomatik | ✅ Tam Uyumlu |
| **Rapidrame** | ✅ (Full HD 1080p MP4) | ✅ Çift Ses Garantili Remux | ✅ VTT ➔ SRT Otomatik | ✅ Tam Uyumlu |
| **FilmMakinesi** | ✅ (HD / Full HD) | ✅ Çoklu Ses Desteği | ✅ Türkçe / İngilizce SRT | ✅ Tam Uyumlu |
| **DiziPal** | ✅ (720p / 1080p) | ✅ Orijinal + Dublaj | ✅ Gömülü & Ağ Altyazıları | ✅ Tam Uyumlu |
| **Closeload / Vidmoly** | ✅ (Hızlı Akış) | ✅ Tek / Çift Ses | ✅ SRT İndirme | ✅ Tam Uyumlu |
| **Twitter/X & TikTok** | ✅ (En Yüksek Çözünürlük) | ✅ Dahili Ses | — | ✅ Anında Hover |
| **1000+ Popüler Web Sitesi** | ✅ (`yt-dlp` Entegrasyonu) | ✅ Tüm Ses Akışları | ✅ Tüm Altyazı Parçaları | ✅ Genel Yakalayıcı |

### 8. 📊 Donanım Hızlandırmalı Canlı Vektör Hız Grafiği
* Sol kenar çubuğunda ve indirme ayrıntılarında WPF vektör çizimiyle gerçek zamanlı hız dalgalanmalarını gösterir.
* **EMA Filtresi & Bezier Eğrileri:** Ağ dalgalanmalarını Exponential Moving Average ile pürüzsüzleştirir, pik hız göstergesiyle birlikte akıcı bir grafik çizer.

### 9. 🎛️ Modern Hız Limiti Kadranı (Speed Limiter Slider)
* İndirme devam ederken sol paneldeki özel tasarımlı kaydırıcı veya hazır çipler (`[Oto] [2M] [5M] [10M] [20M]`) üzerinden tek tıkla internet kullanımını sınırlandırabilirsiniz.

### 10. 🌐 Sıfır Tık Tarayıcı Eklentisi Eşleştirmesi
* **Google Chrome, Microsoft Edge ve Brave** için eklenti klasör yolunu otomatik panoya kopyalar ve geliştirici sayfasını açar.
* Eklenti klasörüne otomatik olarak güvenlik anahtarı tohumlar (`token.json`). Kullanıcı kod kopyalayıp yapıştırmakla uğraşmadan eklenti tek tıkla yeşile döner ve eşleşir.

### 11. 📂 Otomatik Kategori Klasörlemesi
* Tamamlanan indirmeleri uzantılarına göre klasörlere ayırır:
  * `İndirilenler\Videos` (mp4, mkv, webm, avi...)
  * `İndirilenler\Music` (mp3, flac, wav, aac...)
  * `İndirilenler\Torrents` (torrent ve magnet indirmeleri)
  * `İndirilenler\Programs` (exe, msi, iso, apk...)
  * `İndirilenler\Documents` (pdf, docx, xlsx...)
  * `İndirilenler\Compressed` (zip, rar, 7z...)

### 12. ⏱️ Zamanlayıcı & Gece Planlayıcısı (Scheduler)
* Gece saatlerinde (örn. 02:00 – 07:00) indirmeleri otomatik başlatıp süre dolduğunda duraklatarak internet kotanızı ve bant genişliğinizi korur.

### 13. 🎨 Modern Çift Tema (Dark Burgundy & AMOLED Pure Black)
* Varsayılan koyu bordo ve titanyum grafit tasarımı (`#C92A3E` + `#121318`).
* Araç çubuğundaki ay simgesiyle tek tıkla **Derin Siyah AMOLED** (`#000000`) moduna geçiş imkanı.
* Keskin Windows DIN tipografisi (**Bahnschrift**) ve akıcı oval hatlar.

---

## 🏗️ Mimari (Architecture)

```mermaid
flowchart TD
    subgraph Browser["Chromium Tarayıcıları (Chrome / Edge / Brave)"]
        EXT["FM Eklentisi (Manifest V3)"]
    end

    subgraph DesktopApp["FM Download Manager v3.0 (WPF & C# .NET 8)"]
        UI["WPF Arayüzü (Bordo / AMOLED Tema + Canlı Grafikler)"]
        TRAY["Windows Bildirim Tepsisi (FM Monogram İkonu)"]
        LIMITER["Hız Sınırlayıcı (Token-Bucket Limiter)"]
        BRIDGE["Güvenli HTTP Köprüsü (127.0.0.1:48213 + X-MCDM-Token)"]
        MGR["DownloadManager (Kuyruk & Otomatik Klasörleme)"]
        HTTP_ENG["SegmentedDownloader (Dinamik Parçalama Motoru)"]
        TORRENT_ENG["TorrentDownloader (MonoTorrent 3.0 Motoru)"]
        VIDEO_ENG["VideoDownloader (Hızlı yt-dlp + FFmpeg İstemcisi)"]
    end

    EXT -- "HTTP + X-MCDM-Token" --> BRIDGE
    BRIDGE --> MGR
    UI --> LIMITER
    LIMITER --> HTTP_ENG
    MGR --> HTTP_ENG
    MGR --> TORRENT_ENG
    MGR --> VIDEO_ENG
    TRAY --> MGR
    HTTP_ENG -- "Paralel Range İstekleri" --> WebServers["Web Sunucuları"]
    TORRENT_ENG -- "Eşler & BitTorrent Sürüsü" --> P2P["P2P Swarm (DHT/PEX)"]
    VIDEO_ENG -- "Çoklu Akış Akışı" --> VideoHosts["YouTube / Twitter / TikTok"]
```

---

## ⌨️ Klavye Kısayolları (Shortcuts)

| Kısayol | İşlem |
|:---|:---|
| `Ctrl + V` | Panodaki indirme veya video bağlantısını anında ekle |
| `Ctrl + N` | Toplu URL / Bağlantı ekleme penceresini aç |
| `Ctrl + T` | Bordo Koyu Mod ile Derin Siyah AMOLED tema arasında geçiş yap |
| `Space` (Boşluk) | Seçili indirmeyi duraklat veya devam ettir |
| `Delete` | Seçili indirmeyi listeden kaldır |
| `Shift + Delete` | Seçili indirmeyi hem listeden hem de diskten kalıcı olarak sil |
| `Enter` | İndirilen dosyayı varsayılan uygulamasıyla aç |
| `F5` | İndirme listesini yenile |

---

## 📦 Kurulum (Installation)

1. [GitHub Releases](https://github.com/Ferdi-krbk/fm-download-manager/releases) sayfasından en son **`FMDownloadManager-Setup-3.4.0.exe`** dosyasını indirin.
2. Kurulum sihirbazını çalıştırın (kullanıcı bazlı temiz kurulum, yönetici izni gerektirmez).
3. Program açıldığında araç çubuğundaki **Tarayıcı Eklentisi** butonuna tıklayın ve tarayıcınızı seçin. Eklenti saniyeler içinde otomatik olarak yüklenip eşleşecektir.

---

## 🛠️ Kaynaktan Derleme (Building from Source)

Gereksinimler:
* [.NET 8.0 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
* Windows 10/11 x64

```powershell
# 1. Depoyu klonlayın
git clone https://github.com/Ferdi-krbk/fm-download-manager.git
cd fm-download-manager

# 2. Testleri çalıştırın
dotnet run --project tests/McDm.Tests

# 3. Bağımsız (Self-Contained) Release sürümünü derleyin
dotnet publish src/McDm.App/McDm.App.csproj -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -o publish/win-x64
```

---

## 📄 Lisans

Bu proje [MIT Lisansı](LICENSE) altında dağıtılmaktadır.
Mesut Çevik'in açık kaynaklı [MC Download Manager](https://github.com/mstcvk/mc-download-manager) projesinden esinlenilmiş ve Windows için sıfırdan geliştirilmiştir.
