<p align="center">
  <img src="src/McDm.App/Resources/logo.png" width="76" height="76" alt="FM Download Manager">
</p>

<h1 align="center">FM Download Manager</h1>

<p align="center">Dosyalar, medya ve torrentler. Tek bir Windows uygulamasında.</p>

<p align="center">
  <a href="https://github.com/Ferdi-krbk/fm-download-manager/releases/latest"><img alt="Güncel sürüm" src="https://img.shields.io/github/v/release/Ferdi-krbk/fm-download-manager?color=E50914&label=s%C3%BCr%C3%BCm"></a>
  <a href="https://github.com/Ferdi-krbk/fm-download-manager/actions/workflows/build.yml"><img alt="Derleme ve testler" src="https://github.com/Ferdi-krbk/fm-download-manager/actions/workflows/build.yml/badge.svg"></a>
  <img alt="Windows 10 ve 11" src="https://img.shields.io/badge/platform-Windows%2010%20%2F%2011-475569">
  <a href="LICENSE"><img alt="MIT lisansı" src="https://img.shields.io/badge/lisans-MIT-475569"></a>
</p>

<p align="center">
  <a href="https://github.com/Ferdi-krbk/fm-download-manager/releases/download/v1.0.0/FMDownloadManager-Setup-1.0.0.exe"><strong>Windows x64 için indir</strong></a>
  &nbsp;·&nbsp; <a href="README.md">English</a>
  &nbsp;·&nbsp; <a href="https://ferdi-krbk.github.io/fm-download-manager/">Genel bakış</a>
  &nbsp;·&nbsp; <a href="https://github.com/Ferdi-krbk/fm-download-manager/issues">Sorun bildir</a>
</p>

<picture>
  <source media="(prefers-color-scheme: light)" srcset="docs/screenshots/downloads-light.png">
  <img src="docs/screenshots/downloads-dark.png" alt="FM Download Manager 1.0.0 — indirme kuyruğu, canlı hız grafiği ve bağlantı ilerlemesi" width="1160">
</picture>

<p align="center"><sub>Güncel 1.0.0 WPF arayüzü; ayrı test klasöründe gerçekten indirilen dosyalarla oluşturuldu. İlerleme ve hız değerleri simülasyon değildir.</sub></p>

## Neler sunuyor?

| Alan | Özellikler |
| --- | --- |
| Dosya indirme | Paralel HTTP bağlantıları, kuyruk yönetimi, hız sınırı ve sunucu güvenilir doğrulayıcı sunduğunda kaldığı yerden devam etme. |
| Medya | Desteklenen video siteleri ve HLS/DASH akışları için yt-dlp/FFmpeg; ses çıkarma ve kullanılabilir altyazı seçenekleri. |
| Torrent | MonoTorrent üzerinden magnet ve `.torrent` desteği; duraklat/devam et ve indirme hız sınırı. |
| Tarayıcı | Dosya devri, medya algılama ve yüzen indirme çubuğu sağlayan Chromium eklentisi. |
| Masaüstü | Açık/koyu tema, vurgu renkleri, Türkçe/İngilizce arayüz, sistem tepsisi ve zamanlama. |

İndirme hızı sunucuya, bağlantınıza ve depolama biriminize bağlıdır. Medya biçimleri ve oturum gereksinimleri kaynağa göre değişir; her site için çalışma garantisi verilmez.

<details>
<summary>Açık tema ve indirme ayrıntıları</summary>

### Açık tema

![FM Download Manager 1.0.0 açık tema](docs/screenshots/downloads-light.png)

### İndirme ayrıntıları

![Canlı bayt aralıkları ve parça ilerlemesi](docs/screenshots/download-details.png)

Görseller kişisel indirme geçmişi yerine yerel kabul testi dosyalarını kullanır. Uygulamanın gerçek XAML kontrollerinden oluşturulmuştur.

</details>

## Kurulum

1. [FMDownloadManager-Setup-1.0.0.exe](https://github.com/Ferdi-krbk/fm-download-manager/releases/download/v1.0.0/FMDownloadManager-Setup-1.0.0.exe) dosyasını indirin.
2. Kullanıcı başına çalışan kurulum paketini açın. Varsayılan klasör: `%LOCALAPPDATA%\Programs\FM Download Manager`.
3. Uygulamayı başlatın. Tarayıcı eklentisi isteğe bağlıdır; doğrudan bağlantılar ve torrent dosyaları eklentisiz de kullanılabilir.

Kurulum .NET çalışma zamanını, yt-dlp, FFmpeg ve ffprobe araçlarını içerir; bunları ayrıca kurmanız gerekmez.

### Tarayıcı eklentisi

Eklenti şu anda Chrome Web Store üzerinden değil, paketlenmemiş uzantı olarak dağıtılır.

1. `chrome://extensions` veya `edge://extensions` sayfasında **Geliştirici modu**nu açın.
2. **Paketlenmemiş öğe yükle** seçeneğiyle kurulu uygulamanın `extension` klasörünü seçin.
3. Masaüstü uygulamasındaki **Tarayıcı kurulumu** ekranından bağlantıyı kontrol edin. Gerekirse eşleştirme kodunu eklenti penceresine yapıştırın.

Kurulu klasörü kullanın; böylece uygulama güncellenince eklenti dosyaları da yenilenir. Manifest değişikliklerinin uygulanması için güncellemeden sonra eklentiyi yeniden yükleyin. [Görsel kurulum rehberi](https://ferdi-krbk.github.io/fm-download-manager/EKLENTI_KURULUMU.html).

### 3.x sürümünden geçiş

**1.0.0 ilk resmi kararlı sürümdür.** Önceki numaralı yayınlar tarihsel ön sürüm olarak korunur. Eski güncelleyici küçük numarayı daha eski kabul ettiği için 3.x kullanıcılarının 1.0.0'ı bir kez elle kurması gerekir. [Geçiş notları](docs/MIGRATION-1.0.md).

## Gizlilik ve güvenlik

- Tarayıcı köprüsü `127.0.0.1` üzerinde çalışır; indirme ve kontrol istekleri kullanıcıya özel eşleştirme anahtarı gerektirir.
- Çerezler hedefin alan adı, yol, süre ve HTTPS koşullarına göre seçilir. Bekleyen işlerin oturum bilgileri Windows DPAPI ile korunur; tamamlanan/iptal edilen işlerin kayıtlarında tutulmaz.
- HTTPS sertifika doğrulaması açıktır. Paketlenen araçların sürümleri sabittir ve SHA-256 değerleri doğrulanır.
- Antivirüs taraması isteğe bağlıdır. İndirmenin tamamlanması dosyanın güvenli olduğu anlamına gelmez; kullanılamayan veya bitmeyen taramalar ayrı bildirilir.

İndirme adresleri ve dosya adları yerel geçmişe kaydedilir. Adresler erişim anahtarı içerebileceği için tanılama dosyalarını paylaşmadan önce kontrol edin. [Gizlilik](PRIVACY.md) · [Güvenlik](SECURITY.md).

## Kaynaktan derleme ve test

Gereksinimler: Windows x64, .NET 8 SDK ve eklenti testleri için Node.js. Kurulum paketi oluşturmak için ayrıca Inno Setup 6 gerekir.

```powershell
git clone https://github.com/Ferdi-krbk/fm-download-manager.git
cd fm-download-manager
./scripts/fetch-tools.ps1
dotnet build -c Release
dotnet run -c Release --project tests/McDm.Tests

foreach ($test in Get-ChildItem tests -Filter 'test_*.js') {
    node $test.FullName
    if ($LASTEXITCODE -ne 0) { throw $test.Name }
}
```

Çalışma zamanı dâhil uygulama çıktısı:

```powershell
dotnet publish src/McDm.App/McDm.App.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o publish/win-x64
```

Gerçek aktarımları, duraklat/devam et akışını, medya araçlarını ve güncel WPF arayüzünü sınayan Windows kabul testi:

```powershell
dotnet run -c Release --project tests/McDm.Acceptance
```

Etkileşimli tarayıcı ve gerçek antivirüs denemeleri yerel ortama bağlıdır; sonuçları ayrıca belirtilir. [Doğrulama notları](docs/VALIDATION-1.0.md).

## Katkı

Hata bildirirken uygulama ve Windows sürümünü, tekrar adımlarını ve görünen hatayı ekleyin. Çerezleri, eşleştirme kodlarını ve özel bağlantıları paylaşmayın. Özellik önerilerinde iyileşecek kullanım akışını anlatın. [Katkı rehberi](CONTRIBUTING.md).

## Lisans ve kaynaklar

Proje kodu [MIT lisansı](LICENSE) altındadır. Paketlenen araçların kendi lisansları vardır; [NOTICE](NOTICE.md) dosyasına bakın.

Mesut Çevik'in [MC Download Manager](https://github.com/mstcvk/mc-download-manager) projesinden esinlenmiştir. Tarayıcı eklentisi bu projeyi temel alır ve kaynak lisans bildirimini korur. .NET/WPF, MonoTorrent, yt-dlp ve FFmpeg kullanır.
