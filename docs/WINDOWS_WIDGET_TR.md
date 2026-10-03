# Windows 11 Widget'lar paneli

Ai UsageNest'in MSIX sürümü, mevcut masaüstü penceresinin yanında Windows 11'in
**Win + W** paneli için yerel bir widget sağlayıcısı içerir. Portable ZIP ve Inno
Setup sürümleri Windows widget kataloğuna kayıt olmaz; panel için MSIX gerekir.

Kurulumdan sonra Widget'lar panelini aç, **Widget ekle** bölümünden **Ai UsageNest**
kartını seç. Küçük boyut bugünün cihazdaki token toplamını; orta/büyük boyutlar
hesap kotalarını ve ayarlarda açıksa tahmini API karşılığını da gösterir.
Hesap sırası, gizlenen hesaplar, kullanılan/kalan yüzde ve dil uygulamanın
ayarlarını takip eder. Ekrana sığmayan hesaplar uygulamada görülebilir.

**Uygulamayı aç**, açık örneği öne getirir veya uygulamayı başlatır. **Yenile**,
mevcut toplama ve kota yenileme yolunu kullanır; sağlayıcıların bekleme/backoff
kuralları geçerlidir. Panel widget'ı etkinleştirdiğinde veri toplayan uygulama
arka planda başlayabilir; masaüstü widget penceresini kendiliğinden göstermez.
Uygulama normal tepsi menüsündeki **Çıkış** ile kapatılır. Windows ile başlat
ayarı ayrı ve isteğe bağlıdır.

## Veri ve güncellik

WPF uygulaması atomik olarak `windows-widget.json` yazar. Ayrı sağlayıcı yalnız
bu görüntüleme özetini okur; ikinci bir SQLite yazıcısı veya kota istemcisi
çalıştırmaz. Özetin içinde anahtar, profil yolu, konuşma, proje veya oturum kimliği
yoktur. MSIX içindeki iki süreç aynı paket veri alanını kullanır.

Sağlayıcı kart aktifken 15 saniyede bir görüntülemeyi günceller; veri toplama
uygulamanın mevcut aralıklarında çalışır. İki dakikadan eski özet/yerel toplama
verisi önbellek uyarısı taşır. Önceki günün token toplamı bugünün toplamı olarak
sunulmaz. Kotaların kendi bağlantı/eski veri durumu ayrıca görünür.

## Derleme ve paketleme

`src/AiUsageViewer.Widgets` .NET 10 x64 ve `Microsoft.WindowsAppSDK.Widgets 2.0.5`
kullanır. Widget runtime ve .NET self-contained olarak pakete dahil edilir;
hedef makinede SDK kurulumu gerekmez. COM sınıfı ve widget tanımı MSIX manifestine
kayıtlıdır. Windows App SDK'nın lisansı sağlayıcının `Widgets` klasörüne eklenir.

```powershell
./scripts/package.ps1 -OutputRoot artifacts/widget-store-build
./scripts/package-msix.ps1 -PublishDir artifacts/widget-store-build/publish/win-x64
```

İkinci betik sağlayıcıyı otomatik derler. Önceden derlenmiş sağlayıcı için
`-WidgetPublishDir artifacts/windows-widgets/publish` kullanılabilir. İki
çalıştırılabilir dosyanın sürümü aynı olmalıdır. `packaging/msix/identity.json`
Store kimliği `Mikrofab.AiUsageNest`, yayıncı `Mikrofab` değerlerini içerir.

`packaging/msix/WidgetAssets/Overview.png`, gerçek Adaptive Cards JS renderer'ıyla
oluşturulan 300×304, şeffaf yuvarlatılmış köşeli **örnek veri** önizlemesidir.
Windows panelinin canlı ekran görüntüsü değildir. Yeniden üretmek için:

```powershell
./scripts/package-widgets.ps1
./artifacts/windows-widgets/publish/AIUsageViewer.Widgets.exe --export-preview artifacts/widget-preview/cards
npm install --prefix artifacts/widget-preview adaptivecards@3.0.5 playwright@1.56.1
node scripts/render-widget-preview.cjs artifacts/widget-preview/cards artifacts/widget-preview/node_modules artifacts/widget-preview/images
```

## Son kabul kontrolü

3 Ekim 2026 doğrulaması: Release derlemesi hatasız/uyarısız, 103/103 otomatik test
ve 18 sentetik WPF kontrolü başarılı. Üç kart boyutu Adaptive Cards renderer'ında
hatasız çizildi. Temiz Windows Sandbox (26100, PATH'te dotnet yok) kurulum,
özet üretimi, süreçler arası `IWidgetProvider` aktivasyonu, yeni oturumda arka plan
başlangıcı ve kaldırma/veri silme kontrollerini geçti:
`artifacts/widget-sandbox-5/msix-result.json`.

Store'a yükleme için son imzasız dosya:
`artifacts/msix-widget-store-v2/AIUsageViewer-0.3.0-beta.3-win-x64.msix`.
Test imzalı kopya yalnız yerel test içindir. Canlı Win+W görsel/düğme kabulü ve
Store sertifikasyonu henüz tamamlanmadı. Partner Center erişimi Microsoft'un
güncellenmiş hizmet sözleşmesinde durdu; yükleme veya yayın yapılmadı.

- `WindowsWidgetTests`: gece yarısı/yerel gün, eski veri, bozuk özet, JSON metni
  güvenliği, kart boyutları/düğmeler, Türkçe ve örnekler arası komut izolasyonu.
- `sandbox-test.ps1 -Scenario msix`: kurulum, paket veri alanında özet,
  widget runtime aktivasyonu ve kayıtlı `IWidgetProvider` COM sınıfına erişim,
  StartupTask ve kaldırma. Bu, panelde kartın çizildiğini tek başına kanıtlamaz.
- Windows 11 ve güncel Windows Web Experience Pack bulunan bir bilgisayarda
  widget'ı ekle; üç boyutu ve açık/kapalı uygulamada düğmeleri dene. Hesap gizleme,
  dil ve kalan yüzde ayarlarının yansımasını; paneli kapat/aç; uygulamadan Çıkış
  ve eski veri uyarısını kontrol et. Anahtar veya gerçek hesap ekranı paylaşma.
- Partner Center'a imzasız MSIX'i yükle, Store sertifikasyonu tamamlandıktan
  sonra yayınla. Widget Store koleksiyonuna dahil olmak ayrı, isteğe bağlı bir
  başvurudur; MSIX oluşturmak Store'da yayınlandığı anlamına gelmez.

Microsoft kaynakları: [C# sağlayıcı](https://learn.microsoft.com/en-us/windows/apps/develop/widgets/implement-widget-provider-cs),
[manifest kaydı](https://learn.microsoft.com/en-us/windows/apps/develop/widgets/widget-provider-manifest),
[widget seçici görseli](https://learn.microsoft.com/en-us/windows/apps/design/widgets/widgets-picker-integration).
