<p align="center">
  <img src="docs/icon.png" width="120" height="120" alt="FM Download Manager Logo" />
</p>

<h1 align="center">FM Download Manager</h1>

<p align="center">
  <b>Windows 10 ve 11 için akıllı tarayıcı entegrasyonlu ve 4K video indiricili, çoklu bağlantılı yüksek hızlı indirme yöneticisi.</b>
</p>

<p align="center">
  <a href="https://github.com/Ferdi-krbk/fm-download-manager/releases"><img src="https://img.shields.io/badge/S%C3%BCr%C3%BCm-v0.1.0-blue.svg?style=for-the-badge&logo=windows" alt="Sürüm v0.1.0"></a>
  <a href="https://dotnet.microsoft.com/download/dotnet/8.0"><img src="https://img.shields.io/badge/.NET-8.0%20LTS-purple.svg?style=for-the-badge&logo=dotnet" alt=".NET 8"></a>
  <a href="LICENSE"><img src="https://img.shields.io/badge/Lisans-MIT-green.svg?style=for-the-badge" alt="Lisans MIT"></a>
  <a href="README.md"><img src="https://img.shields.io/badge/Language-English-blue.svg?style=for-the-badge" alt="English Documentation"></a>
</p>

<p align="center">
  <a href="#-öne-çıkan-özellikler">Özellikler</a> •
  <a href="#-mimari">Mimari</a> •
  <a href="#-tarayıcı-entegrasyonu-ve-eşleştirme">Tarayıcı Entegrasyonu</a> •
  <a href="#-kurulum">Kurulum</a> •
  <a href="#-kaynak-koddan-derleme">Derleme</a> •
  <a href="#-sorun-giderme">Sorun Giderme</a>
</p>

---

## ⚡ Öne Çıkan Özellikler

- 🚀 **Maksimum Bant Genişliği:** Parçalı (segmented) indirme motoru (dosya başına 16 paralel bağlantıya kadar).
- 🌐 **Kusursuz Tarayıcı Entegrasyonu:** **Chrome, Edge ve Brave** indirmelerini oturum çerezleri, Referer ve başlık bilgileriyle anında yakalar.
- 🎬 **4K Video İndirme:** Web sayfalarındaki videoları tek tıkla yakalayan `yt-dlp` ve `FFmpeg` destekli video indirici (4K MP4/MKV desteği).
- 🛡️ **Kesintisiz Devamlılık:** Atomik `.mcdm` durum dosyası sayesinde duraklatma/devam etme, sunucu ETag değiştiğinde otomatik sıfırdan başlama ve ağ kopmalarına karşı dayanıklılık.
- 🎨 **Modern Native Arayüz:** Sistem tepsisi entegrasyonu, kategorilere ayrılmış görünüm, pano izleyicisi ve bağlantı detay penceresi içeren hızlı WPF arayüzü.
- 🔒 **Sıfır Telemetri & Tamamen Yerel:** 256-bit eşleştirme anahtarı (`X-MCDM-Token`) ile korunan yerel HTTP köprüsü. Buluta veya harici sunuculara bağımlılık yoktur.

---

## 📸 Arayüz & Yetenekler

### 🗂️ Akıllı Kategori Ayrımı ve Arama
İndirilen dosyalar dosya türüne göre otomatik olarak ayrılır (*Tümü, Resimler, Müzik, Video, Programlar, Belgeler, Arşivler, Diğer*). Anlık arama kutusu ve sıralanabilir sütunlar içerir.

### 📊 Bağlantı ve Parça Görselleştirici (IDM Tarzı)
Her bir parçanın bayt aralığını, indirilme yüzdesini ve anlık transfer hızını detaylı pencerede inceleyin.

```
┌────────────────────────────────────────────────────────────────────────┐
│  İndirilen: arsiv-yedek.zip (2.4 GB)                                   │
├────────────────────────────────────────────────────────────────────────┤
│  [=====================================>         ] %78 (18.4 MB/sn)    │
│                                                                        │
│  Parça 1:  [====================] %100 (300 MB / 300 MB)               │
│  Parça 2:  [====================] %100 (300 MB / 300 MB)               │
│  Parça 3:  [==============>     ]  %72 (216 MB / 300 MB)               │
│  Parça 4:  [===========>        ]  %55 (165 MB / 300 MB)               │
└────────────────────────────────────────────────────────────────────────┘
```

### 📋 Otomatik Pano Algılama
Panoya bir `http(s)` dosya bağlantısı kopyalandığında FM Download Manager sağ altta zarif bir onay kutusu çıkartarak indirmek isteyip istemediğinizi sorar.

### 🌐 Sistem Tepsisi (Tray) Bildirimleri
- Pencere kapatıldığında arka planda tepside çalışmaya devam eder.
- Tepsi simgesi üzerine gelindiğinde anlık indirme hızı ve aktif indirme sayısı görüntülenir.
- İndirme tamamlandığında yerel Windows bildirimi gösterilir.

---

## 🏗️ Mimari

```mermaid
flowchart TD
    subgraph Browser["Chromium Tarayıcı (Chrome / Edge / Brave)"]
        EXT["Eklenti (Manifest V3)"]
    end

    subgraph App["FM Download Manager (WPF & Çekirdek Motor)"]
        UI["WPF Arayüzü (Kategoriler, Arama, Detaylar)"]
        TRAY["Sistem Tepsisi & Pano İzleyici"]
        SERVER["Köprü Sunucusu (Kestrel 127.0.0.1:48213)"]
        MGR["DownloadManager (Kuyruk Planlayıcı & Veritabanı)"]
        ENGINE["SegmentedDownloader (Çoklu Bağlantı Motoru)"]
        VIDEO["VideoDownloader (yt-dlp + FFmpeg İşlem Yöneticisi)"]
    end

    EXT -- "HTTP POST/GET + X-MCDM-Token" --> SERVER
    SERVER --> MGR
    UI --> MGR
    TRAY --> MGR
    MGR --> ENGINE
    MGR --> VIDEO
    ENGINE -- "Paralel Range Parçaları" --> Target["Uzak Web Sunucusu"]
    VIDEO -- "Alt İşlem Ağacı" --> Tools["Tools/ (yt-dlp.exe, ffmpeg.exe)"]
```

---

## 🔌 Tarayıcı Entegrasyonu ve Eşleştirme

FM Download Manager, **Google Chrome, Microsoft Edge ve Brave** tarayıcıları için hazır Manifest V3 eklentisiyle birlikte gelir.

```
  1. FM Download Manager'ı açın
       ↓
  2. Araç çubuğunda "Tarayıcı kurulumu" butonuna tıklayın
       ↓
  3. "Eklenti klasörünü aç" butonuna tıklayın
       ↓
  4. Tarayıcıda: Uzantılar (chrome://extensions) → "Geliştirici modu"nu açın → "Paketlenmemiş öge yükle"
       ↓
  5. Eklenti simgesine tıklayın → Eşleştirme kodunu yapıştırın → Tamamlandı (✓ Eşleşti)
```

---

## 📥 Kurulum

### Yöntem 1: Hazır Kurulum Programı (Önerilen)
[GitHub Releases](https://github.com/Ferdi-krbk/fm-download-manager/releases) sayfasından en güncel `FMDownloadManager-Setup-0.1.0.exe` dosyasını indirin.
- Yönetici izni gerekmeden doğrudan `%LOCALAPPDATA%\Programs\FM Download Manager` dizinine yüklenir.
- Masaüstü ve Başlat menüsü kısayollarını otomatik yapılandırır.

### Yöntem 2: Taşınabilir (Portable)
Tek dosya olarak yayımlanan bağımsız `.exe` dosyasını indirip kurulum yapmadan doğrudan çalıştırabilirsiniz.

---

## 🛠️ Kaynak Koddan Derleme

### Gereksinimler
- Windows 10 (1809+) veya Windows 11 (x64)
- [.NET 8.0 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- Inno Setup 6 (kurulum paketi derlemek için)

### Komutlar

```powershell
# Depoyu klonlayın
git clone https://github.com/Ferdi-krbk/fm-download-manager.git
cd fm-download-manager

# Projeyi derleyin
dotnet build -c Release

# 20 adet otomatik entegrasyon testini çalıştırın
dotnet run -c Release --project tests/McDm.Tests

# Bağımsız tek dosya olarak yayınlayın
dotnet publish src/McDm.App/McDm.App.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o publish/win-x64

# (İsteğe bağlı) Resmi yt-dlp ve FFmpeg araçlarını indirin
powershell -ExecutionPolicy Bypass -File scripts/fetch-tools.ps1

# (İsteğe bağlı) Inno Setup kurulum paketini derleyin
& "C:\Users\Ferdi\AppData\Local\Programs\Inno Setup 6\ISCC.exe" installer\setup.iss
```

---

## 📄 Lisans ve Atıf

- Bu proje **MIT Lisansı** altında yayımlanmıştır.
- Upstream macOS projesi olan [mstcvk/mc-download-manager](https://github.com/mstcvk/mc-download-manager) temel alınarak Windows'a uyarlanmıştır.
- Paketle birlikte sağlanan üçüncü taraf araçlar (FFmpeg ve yt-dlp) kendi lisanslarına tabidir (LGPL/GPL ve Unlicense). Ayrıntılar için [NOTICE.md](NOTICE.md) dosyasına bakabilirsiniz.
