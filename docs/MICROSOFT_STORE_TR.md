# Microsoft Store yayını

Uygulama Microsoft Store'a **MSIX** paketi olarak gönderilir. Store paketi kendi
sertifikasıyla imzalar; kod imzalama sertifikası satın almak gerekmez. Inno Setup
kurulumu ve portable ZIP GitHub sürümleri için aynen kalır.

| | |
|---|---|
| Store adı (ayrıldı) | **Ai UsageNest** (yazım birebir böyle; manifest bununla eşleşmeli) |
| Yayıncı | **Mikrofab** (şirket hesabı) |
| Destek | `app@mikrofab.com` |
| Gizlilik politikası | `https://github.com/ozkurkuran/ai-usage-viewer/blob/main/PRIVACY.md` |
| Fiyat | Ücretsiz, açık kaynak (MIT) |

## Hazır olanlar

MSIX paketi Windows 11 Widget'lar paneli sağlayıcısını da içerir. Kurulumdan
sonra kullanıcı Win + W → Widget ekle → **Ai UsageNest** yoluyla kartı ekler.
Masaüstü widget'ı ve panel widget'ı birlikte kullanılabilir. Sağlayıcının
derleme, görsel ve kabul adımları [Windows widget rehberinde](WINDOWS_WIDGET_TR.md).

| Parça | Yer |
|---|---|
| MSIX manifest şablonu (x64, Windows 11+, `runFullTrust`, StartupTask) | `packaging/msix/AppxManifest.template.xml` |
| Store kimlik değerleri | `packaging/msix/identity.json` |
| Uygulama simgesi ve MSIX görselleri | `src/AiUsageViewer.App/Assets/AppIcon.ico`, `packaging/msix/Assets/` (`scripts/generate-assets.ps1` ile üretilir) |
| Store logoları | `packaging/store/AppTileIcon-300.png`, `packaging/store/BoxArt-1080.png` |
| Paketleme | `scripts/package-msix.ps1` |
| Store ekran görüntüleri (3840×2160, örnek veri) | `scripts/store-screenshots.ps1 -Language en` (12 dil desteklenir) |
| 12 dilde mağaza açıklama taslakları | `packaging/store/listings/*.json` |
| Gizlilik politikası | [PRIVACY.md](../PRIVACY.md) (uygulamada kenar çubuğundan da açılır) |

### Dil desteği ve yerelleştirilmiş mağaza sayfası

Uygulamada 12 dil seçeneği vardır: İngilizce, Türkçe, İspanyolca, Almanca,
Fransızca, Portekizce (Portekiz ve Brezilya), Rusça, Hollandaca, Çekçe,
İtalyanca ve Lehçe. Varsayılan **Sistem dili** her açılışta Windows görüntüleme
dilini izler; `es-MX`, `es-AR`, `fr-CA`, `de-AT`, `nl-BE` gibi bölgesel diller
aynı dilin arayüzüyle eşleşir. Desteklenmeyen dilde İngilizce kullanılır.
Ayarlardan elle seçilen dil kaydedilir. Eski sürümde kaydedilmiş `en`/`tr`
tercihi korunur; otomatik seçim için kullanıcı Sistem dili seçeneğine geçebilir.

MSIX manifesti bu 12 dili bildirir. Paket yüklendiğinde desteklenen diller
mağazada gösterilir; açıklamaların yerelleştirilmesi ayrı bir Partner Center
adımıdır. [Microsoft'un dil belgesi](https://learn.microsoft.com/en-us/windows/apps/design/globalizing/manage-language-and-region)
paket dil listesinin mağazada gösterildiğini açıklar. Mağaza istemcisinin
dil/bölge tercihleriyle seçtiği açıklama dilini uygulama kodu belirlemez.

Partner Center'da **Store listings → Add/remove languages** bölümünden
`en-us`, `tr-tr`, `es-es`, `de-de`, `fr-fr`, `pt-pt`, `pt-br`, `ru-ru`,
`nl-nl`, `cs-cz`, `it-it`, `pl-pl` listelemelerini ekle. Her dil için
`packaging/store/listings/<dil>.json` içindeki `shortDescription`, `description`
ve `productFeatures` alanlarını ilgili form alanlarına aktar. JSON dosyaları
doğrudan içe aktarılabilen Partner Center CSV'si değildir; toplu aktarımda
önce Partner Center'ın verdiği CSV şablonunu dışa aktar ve alanlarını doldur.
**What's new** isteğe bağlıdır; ilk gönderimde boş bırakılabilir.
[Microsoft'un listeleme belgesi](https://learn.microsoft.com/en-us/windows/apps/publish/publish-your-app/msix/add-and-edit-store-listing-info)
bu alanları, CSV aktarımını ve her dil için açıklama ile ekran görüntüsü
gereksinimini açıklar.

İlgili dilde görüntüler için örnek komut:
`./scripts/store-screenshots.ps1 -Executable <yeni-publish>/AIUsageViewer.exe -Language de -Output artifacts/store-screenshots-de`.
Her dili ayrı çıktı klasörüne al; yerelleştirilmiş görüntüleri o dilin
listelemesine yükle. Açıklama metni yeni sürümün dil desteğini tarif ettiğinden
eski iki dilli pakete bu metinleri ekleme. Bu dosyaların oluşturulması mağazaya
yükleme veya yayınlama yapmaz.

Paketli sürümde "Windows ile başlat" ayarı HKCU Run yerine Windows StartupTask
kullanır. Kullanıcı bunu Ayarlar > Uygulamalar > Başlangıç bölümünden de kapatıp
açabilir. Oturum açılışında uygulama detay penceresini açmadan arka planda başlar.

## Tek seferde kabul için kontrol listesi

Microsoft Store Policies 7.20 (14 Eylül 2026) maddelerine göre:

| Politika | Ne yapıldı |
|---|---|
| 10.1.1 Doğru tanıtım, ad | Uygulama içi ad, Başlat menüsü, tepsi, kurulum ve manifest **Ai UsageNest**. Ekran görüntüleri gerçek arayüzden, üzerinde "DEMO DATA" etiketi var |
| 10.1.1 İlk açılışta değer anlaşılmalı | Kayıt yoksa genel bakışta **Başlarken** kartı: ne gerektiğini anlatır, Ayarlar ve **Örnek veriyle dene** düğmeleri |
| 10.1.3 Arama terimleri | 7 terim, marka/ürün adı ve fiyat ifadesi yok |
| 10.2.4 Bağımlılık açıklaması | Claude Code / Codex / OpenRouter gereksinimi açıklamanın **ilk paragrafında** |
| 10.2.7 Temiz kaldırma | MSIX; kaldırmada paket verisi silinir (Sandbox'ta doğrulandı) |
| 10.2.8 Windows ayarı değişikliği | Başlangıç görevi varsayılan kapalı, yalnız kullanıcı açınca StartupTask API ile |
| 10.3 Test edilebilirlik | Hesap/giriş gerekmez; sertifikasyon notu örnek veri düğmesini anlatır |
| 10.4.2 Kararlılık | Başlatma hatası iki dilli mesajla kapanır; Sandbox testinde çökme yok |
| 10.5.1 Gizlilik | Win32 uygulaması olduğu için zorunlu; URL Partner Center'a girilir, uygulamada bağlantı var, Mikrofab ve iletişim bilgisi yazılı |
| 10.6 Yetenekler | Yalnız `internetClient` ve `runFullTrust`; gerekçe aşağıda |
| 10.7 Yerelleştirme | Manifest 12 dil bildirir; ilgili dillerde açıklama taslakları hazırdır. En az bir listeleme gerekir; Microsoft desteklenen her dil için listeleme önerir. Varsayılan arayüz dili Windows diline göre seçilir |
| 10.14 Şirket hesabı | Destek iletişimi (`app@mikrofab.com`) Partner Center'a girilir |
| 11.2 Üçüncü taraf adları | Anthropic, OpenAI, OpenRouter ile bağlantı olmadığı açıklamada yazılı; onların logoları kullanılmıyor |

## 1. Partner Center (bir kez)

1. Şirket hesabı (Mikrofab) açıldı ve doğrulandı.
2. **Apps and games > New product > MSIX or PWA app** ile ad ayrıldı: `Ai UsageNest`.
3. **Product management > Product identity** sayfasındaki üç değeri
   `packaging/msix/identity.json` dosyasına kopyala:

   | Partner Center | identity.json |
   |---|---|
   | Package/Identity/Name | `identityName` |
   | Package/Identity/Publisher (`CN=...`) | `publisher` |
   | Package/Properties/PublisherDisplayName | `publisherDisplayName` |

   Bu değerler gizli değildir; Store'da herkese görünür. Boş bırakılırsa betik
   `-localtest` adlı, Store'un reddedeceği bir test paketi üretir.

## 2. Paketi oluştur

```powershell
./scripts/package.ps1 -OutputRoot artifacts/store-build
./scripts/package-msix.ps1 -PublishDir artifacts/store-build/publish/win-x64
```

Çıktı: `artifacts/msix/AIUsageViewer-<sürüm>-win-x64.msix` (imzasız; Partner
Center'a yüklenecek dosya) ve `SHA256SUMS.txt`.

Store sürüm numarası dört parçalı olmalı ve son parça 0 kalmalıdır. Betik şu
eşlemeyi kullanır: `X.Y.Z-beta.N → X.Y.(Z×100+N).0`, `X.Y.Z → X.Y.(Z×100+99).0`.
Örnek: `0.3.0-beta.3 → 0.3.3.0`, `0.3.0 → 0.3.99.0`, `0.3.1 → 0.3.199.0`.
Her gönderim bir öncekinden büyük sürüm ister.

## 3. Yükleme öncesi test

`-TestSign` geçici bir sertifikayla imzalı ikinci bir paket ve `.cer` üretir. Özel
anahtar diske kalıcı yazılmaz. Bu paketi ana makineye kurmak için sertifikayı
yönetici olarak güvenilir listeye eklemek gerekir. Bunun yerine tek kullanımlık
Windows Sandbox testini kullan:

```powershell
./scripts/package-msix.ps1 -PublishDir artifacts/store-build/publish/win-x64 -TestSign
./scripts/sandbox-test.ps1 -Scenario msix -Output artifacts/msix-check
```

Misafir sırasıyla şunları yapar:
- Paketi kurar.
- Başlat menüsü etkinleştirmesiyle detay penceresinin açıldığını ve verinin paket alanına yazıldığını kontrol eder.
- `--validate-package` ile StartupTask'ı etkinleştirir.
- `msix-stage.json` dosyasına `stage: ready-for-logoff` yazar.

Bundan sonra yalnız o misafirde oturumu kapatıp yeniden bağlan
([WINDOWS_QA.md](WINDOWS_QA.md) içindeki komutlar, dosya adı `msix-stage.json`).
Yeni oturumda uygulamanın detay penceresi olmadan başladığı doğrulanır, paket
kaldırılır ve sonuç `msix-result.json` dosyasına yazılır.

Windows App Certification Kit'i (WACK) yönetici PowerShell'de çalıştır. Store'un
otomatik testleri büyük ölçüde aynıdır:

```powershell
& "${env:ProgramFiles(x86)}\Windows Kits\10\App Certification Kit\appcert.exe" reset
& "${env:ProgramFiles(x86)}\Windows Kits\10\App Certification Kit\appcert.exe" test -appxpackagepath artifacts\msix\AIUsageViewer-0.3.0-beta.3-win-x64.msix -reportoutputpath artifacts\msix\wack.xml
```

## 4. Gönderim formu

### Pricing and availability

- Markets: tümü (varsayılan).
- Discoverability: *Make this product available and discoverable in the Microsoft Store*.
- Base price: **Free**. Free trial: yok.

### Properties

- Category: **Developer tools** (alt kategori yok).
- Privacy policy URL: `https://github.com/ozkurkuran/ai-usage-viewer/blob/main/PRIVACY.md`
- Website: `https://github.com/ozkurkuran/ai-usage-viewer`
- Support contact info: `app@mikrofab.com`
- Contact details (şirket hesabı için zorunlu): Mikrofab adresi, telefonu ve
  `app@mikrofab.com`. Bu bilgiler bazı bölgelerde Store sayfasında görünür.
- Display mode / Game settings: boş.
- Product declarations: varsayılanlar. "Customers can install this app to
  alternate drives" açık kalabilir.
- System requirements: Minimum ve Recommended için yalnız **Keyboard** ve
  **Mouse**; mimari x64.

### Age ratings (IARC)

Kategori olarak **Utility / Productivity / Other app** seç. Yanıtlar:

| Soru | Yanıt |
|---|---|
| Kullanıcılar arası iletişim veya içerik paylaşımı | Hayır |
| Kullanıcının konumunu paylaşma | Hayır |
| Dijital ürün satın alma | Hayır |
| Serbest internet tarayıcısı | Hayır |
| Şiddet, cinsellik, kumar, uyuşturucu, küfür | Hayır |
| Kişisel bilgi toplayıp üçüncü taraflarla paylaşma | Hayır (veri cihazda kalır) |

Beklenen sonuç: herkes için (3+ / Everyone).

### Packages

`AIUsageViewer-<sürüm>-win-x64.msix` dosyasını yükle. Device family: yalnız
**Windows 10/11 Desktop** işaretli kalsın (paket zaten Windows 11 22000+ ister).

### Submission options

**Restricted capabilities (runFullTrust) açıklaması:**

> Ai UsageNest is a WPF desktop (Win32) application packaged as MSIX. Full trust
> is required to read the Claude Code and Codex usage log files in the profile
> folders the user selects (by default %USERPROFILE%\.claude and
> %USERPROFILE%\.codex), to start the locally installed Codex CLI app-server
> process for quota information, and to show its notification-area icon and
> desktop widget. The app does not install drivers or services.

**Notes for certification:**

> Ai UsageNest is a local monitor for AI developer tool usage. No app account,
> sign-in or purchase is needed.
>
> HOW TO TEST WITHOUT CLAUDE CODE OR CODEX: on first launch the Overview page
> shows a "Getting started" card. Click "Try with sample data" to open a second
> window with generated example records (marked DEMO DATA) that demonstrates the
> dashboard, models, projects, sessions and subscription views. Closing that
> window ends the sample instance. The sample mode does not read files or use the
> network.
>
> Without Claude Code or Codex installed and signed in, the main window shows
> "No usage records yet" and no account cards; this is expected. Real data
> requires the third-party tools listed at the start of the description, or an
> OpenRouter API key added in Settings.
>
> The app runs in the notification area. Right-click its icon for Open details,
> Widget, Settings and Exit. Closing windows keeps the app in the notification
> area; Exit quits it. "Start with Windows" is off by default and uses the
> StartupTask API only after the user turns it on. Privacy policy link: bottom of
> the left sidebar. Source code: https://github.com/ozkurkuran/ai-usage-viewer

Publishing hold: istersen "Publish manually" seç; sertifikasyon geçince yayına
kendin alırsın.

## 5. Store listing: English (en-us)

**Product name:** Ai UsageNest

**Description**

> Requires Claude Code and/or Codex (installed and signed in on this PC) to show
> their usage, or an OpenRouter API key for OpenRouter. Without them, use "Try
> with sample data" to explore the app.
>
> Ai UsageNest keeps your AI coding usage visible on the Windows desktop. It reads
> the local usage records written by Claude Code and Codex and shows today's
> tokens, subscription quota windows and reset times in a compact widget, a
> notification-area panel and a detailed dashboard.
>
> • Local token history for Claude Code and Codex: input, output and cache tokens by model, project and session, with an hourly usage chart, a 13-week heatmap and top models.
> • Subscription quota and reset times for Claude and Codex accounts signed in with the official command-line tools.
> • Pace marker on every quota bar: rows read Under, On or Ahead of pace, turn amber when ahead and red at 90% or more, and warn with a forecast such as “At this pace it runs out Mon ~06:30” before the reset. Optional Pace alert notification.
> • OpenRouter key limits and spend, account credits and completed-day activity history, depending on key permissions.
> • Estimated API-equivalent cost using dated, sourced prices and your own overrides; CSV and JSON export.
> • Compact or standard desktop widget and tray flyout, with card ordering, used or remaining view, opacity, dark, light and System themes, and 12 language options with automatic Windows language selection.
> • Optional notifications for quota thresholds, resets and low balance; optional start with Windows.
>
> Private by design: usage data stays on your device. There is no app account,
> telemetry or analytics, and the app connects only to the providers you set up.
> OpenRouter keys are protected with Windows DPAPI.
>
> Ai UsageNest is free and open source (MIT license), published by Mikrofab. It is
> not affiliated with or endorsed by Anthropic, OpenAI or OpenRouter. The Claude
> quota source is experimental. Estimated cost is not your subscription bill.

**What's new in this version:** New design with pace markers and run-out forecasts on quota bars, plus an optional pace alert.

**Product features** (her satır ayrı alan):
- Today's tokens and subscription quotas in a desktop widget
- Claude Code and Codex token history by model, project and session
- Claude and Codex quota windows with reset times
- OpenRouter key usage, credits and daily activity
- Estimated API-equivalent cost with sourced prices
- CSV and JSON export
- Dark, light and System themes, 12 language options
- Pace marker and run-out forecast on quota bars, with an optional Pace alert
- Dashboard with hourly usage chart, 13-week heatmap and top models
- No telemetry; data stays on your device

**Screenshots** (`artifacts/store-screenshots`, sırasıyla) ve açıklamaları:
1. `01-overview-en.png`: Today's tokens and subscription quotas at a glance
2. `02-widget-and-tray-en.png`: Desktop widget and notification-area quick view
3. `03-project-session-detail-en.png`: Drill down from projects to sessions and models
4. `04-openrouter-history-en.png`: OpenRouter account activity by completed UTC day
5. `05-settings-en.png`: Accounts and per-model prices

**Store logos:** 1:1 App tile icon `packaging/store/AppTileIcon-300.png`;
1:1 Box art `packaging/store/BoxArt-1080.png`.

**Search terms** (7): `token usage`, `AI usage`, `usage widget`, `quota monitor`,
`LLM tokens`, `developer tools`, `API cost`. Başka şirketlerin marka adlarını arama
terimi olarak kullanma.

**Copyright and trademark info:** `Copyright © 2026 Mikrofab`
**Additional license terms:** `Free and open source under the MIT License: https://github.com/ozkurkuran/ai-usage-viewer/blob/main/LICENSE`
**Developed by:** `Mikrofab`

## 6. Store listing: Türkçe (tr-tr)

Türkçe kullanıcılar için bu listelemeyi de doldur. Diğer dillerin açıklama
taslakları `packaging/store/listings/` altındadır.

**Ürün adı:** Ai UsageNest

**Açıklama**

> Kullanım verilerini göstermek için bu bilgisayarda kurulu ve giriş yapılmış
> Claude Code ve/veya Codex ya da OpenRouter için bir API anahtarı gerekir. Bunlar
> yoksa uygulamayı "Örnek veriyle dene" ile inceleyebilirsin.
>
> Ai UsageNest, yapay zekâ kodlama kullanımını Windows masaüstünde görünür tutar.
> Claude Code ve Codex'in yerel kullanım kayıtlarını okuyarak bugünkü tokenları,
> abonelik kota pencerelerini ve yenilenme zamanlarını widget'ta, bildirim alanı
> panelinde ve ayrıntılı detay ekranında gösterir.
>
> • Claude Code ve Codex için model, proje ve oturum bazında input/output/cache token geçmişi, saatlik kullanım grafiği, 13 haftalık ısı haritası ve en çok kullanılan modeller.
> • Resmî CLI ile giriş yapılmış Claude ve Codex hesaplarının kota pencereleri ve yenilenme zamanları.
> • Her kota çubuğunda hız işareti: satırlar Hızın altında, Hızında veya Hızın önünde der, hızın önündeyken kehribar, %90 ve üzerinde kırmızı olur ve yenilenmeden önce tükenme tahminiyle uyarır. İsteğe bağlı Hız uyarısı bildirimi.
> • OpenRouter anahtar limiti/harcaması, hesap bakiyesi ve tamamlanmış gün geçmişi (anahtar yetkisine bağlı).
> • Kaynaklı ve tarihli fiyatlarla tahmini API karşılığı, kullanıcı tarifeleri, CSV/JSON dışa aktarma.
> • Kompakt veya standart masaüstü widget'ı ve bildirim alanı paneli; kart sırası, kullanılan/kalan gösterimi, opaklık, koyu, açık ve Sistem teması ve Windows diline göre otomatik seçilen 12 dil seçeneği.
> • İsteğe bağlı kota/yenilenme/düşük bakiye bildirimleri ve Windows ile başlatma.
>
> Gizlilik öncelikli: kullanım verileri cihazında kalır. Uygulama hesabı,
> telemetri veya analitik yoktur; uygulama yalnız senin kurduğun servislere
> bağlanır. OpenRouter anahtarları Windows DPAPI ile korunur.
>
> Ai UsageNest, Mikrofab tarafından yayımlanan ücretsiz ve açık kaynak (MIT
> lisanslı) bir uygulamadır. Anthropic, OpenAI veya OpenRouter ile bağlantılı
> değildir ve onlar tarafından onaylanmamıştır. Claude kota kaynağı deneyseldir.
> Tahmini maliyet abonelik faturası değildir.

**Bu sürümdeki yenilikler:** Kota çubuklarında hız işaretleri ve tükenme tahminleriyle yeni tasarım; ayrıca isteğe bağlı hız uyarısı.

**Ürün özellikleri:**
- Masaüstü widget'ında bugünkü tokenlar ve abonelik kotaları
- Model, proje ve oturum bazında Claude Code ve Codex token geçmişi
- Yenilenme zamanlarıyla Claude ve Codex kota pencereleri
- OpenRouter anahtar kullanımı, bakiye ve günlük etkinlik
- Kaynaklı fiyatlarla tahmini API karşılığı
- CSV ve JSON dışa aktarma
- Koyu, açık ve Sistem teması, 12 dil seçeneği
- Kota çubuklarında hız işareti ve tükenme tahmini; isteğe bağlı Hız uyarısı
- Saatlik kullanım grafiği, 13 haftalık ısı haritası ve en çok kullanılan modellerle gösterge paneli
- Telemetri yok; veriler cihazında kalır

**Ekran görüntüleri** (`artifacts/store-screenshots-tr`, sırasıyla):
1. `01-overview-tr.png`: Bugünkü tokenlar ve abonelik kotaları tek bakışta
2. `02-widget-and-tray-tr.png`: Masaüstü widget'ı ve bildirim alanı hızlı görünümü
3. `03-project-session-detail-tr.png`: Projelerden oturumlara ve modellere in
4. `04-openrouter-history-tr.png`: Tamamlanmış UTC günlerine göre OpenRouter etkinliği
5. `05-settings-tr.png`: Hesaplar ve model fiyatları

**Arama terimleri** (7): `token kullanımı`, `yapay zeka kullanımı`, `kullanım widget`,
`kota takibi`, `LLM token`, `geliştirici araçları`, `API maliyeti`.

Telif hakkı, lisans ve "Developed by" alanları İngilizce listelemeyle aynı.

## Store sürümüne özgü davranış

- Veriler Windows'un uygulama paketi alanında tutulur
  (`%LOCALAPPDATA%\Packages\<paket>\LocalCache\Local\AiUsageViewer`).
- Inno Setup veya portable sürümün verisi Store sürümüne otomatik taşınmaz.
- İki sürümü aynı anda çalıştırma: aynı veri yolu adını kullandıkları için ikinci
  örnek açılmaz.
- Güncellemeleri Store dağıtır; uygulama kendi kendini güncellemez.
