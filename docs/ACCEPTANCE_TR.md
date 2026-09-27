# Windows beta kabul durumu

27 Eylül 2026 · 0.3.0-beta.2 · Kapsam: [ürün planı](PLAN_TR.md).

Tüm aşamaların uygulama özellikleri bağımsız `app/` kod tabanında geliştirildi.
Kararlı **v1.0 kabulü henüz tamamlanmadı**; aşağıdaki saha kontrolleri açık.

| Aşama | Teslim edilen | Kabul durumu |
|---|---|---|
| P0 | Yerel doğrulama aracı, anonim testler, Claude/Codex salt okunur kota bağlantıları | Claude Code 2.1.283 `/usage` ve Codex 0.155.1 `/status` ile kapsam/yüzde/reset karşılaştırıldı |
| P1 | C#/.NET 10/WPF, SQLite, tray/widget/detay, Türkçe/İngilizce | Tamamlandı |
| P2 | Claude/Codex token geçmişi, artımlı okuma, kota/reset kartları, profil/kaynak ayarları, eski veri durumları | Gerçek veride çalışıyor; tekrar okuma/yeniden başlatma ve hata durumları test edildi |
| P3 | Grafik, ısı haritası, model/proje/oturum analizi, kaynak/tarihli fiyatlar, kullanıcı tarifesi, CSV/JSON | Uygulandı ve test edildi |
| P4 | OpenRouter anahtar limiti/harcama, yetkiye bağlı bakiye ve ayrı hesap geçmişi | API sözleşmesi/kısmi yetki/period testleri geçti; gerçek anahtar sağlanmadığı için canlı kontrol bekliyor |
| P5 | Özelleştirme, bildirimler, açılışta başlatma, portable/kurulum, lisanslar, belgeler ve CI tanımı | Windows beta hazır; fiziksel ekran/uyku/uzun kullanım kabulü açık |

Doğrulama: 68 otomatik test geçti. Arayüz testi boş veri, üç ana pencere,
detaylara geçiş, ayarların kaydı, TR/EN ve açık/koyu tema, kart sırası/gizleme,
OpenRouter örnek geçmişi ve tray'de çalışma davranışını kapsıyor. Widget her zaman
bugünün cihaz toplamını gösteriyor; analiz filtreleri bu toplamı değiştirmiyor.

Gerçek Claude ve Codex hesaplarıyla son salt okunur kontrol 18:02:49 UTC'de geçti:
iki hesap da Ready, yeni kota yanıtları mevcut. Model çağrısı veya kota sıfırlama
işlemi yapılmadı. Ağ erişimi olmayan koşuda son bilinen değerlerin korunması da
gözlendi. Kullanıcının gerçek verileri dağıtım arşivine alınmıyor.

Temiz Windows Sandbox build 26100'da .NET SDK olmadan portable ve kurulum sürümü
çalıştı. Yeniden açılışta 632 yapay kayıt ve 9.762.481 token değişmedi. Aynı sürümün
yeniden kurulumu ve kaldırma kontrolü geçti; uygulama ve kendi başlangıç girdisi
silindi, kullanıcı geçmişi korundu. Beta.1→beta.2 yükseltmesi de ayrıca doğrulandı.
Gerçek Sandbox oturum kapatma/açma testinde uygulama HKCU Run üzerinden başladı;
boşluk içeren yollar çalıştı ve detay penceresi kendiliğinden açılmadı.

İlk veri aktarımından sonra 30 dakikalık normal arka plan gözlemi tamamlandı:
232 ek tarama, 950 değişen dosya okuması, sıfır toplama hatası/kaynak uyarısı.
Claude ve Codex kota bağlantıları Ready kaldı. Özel bellek 69–96 MiB aralığında,
çalışma kümesi 335–396 MiB aralığındaydı; handle sayısı 921'den 847'ye indi.
Bu ölçüm gece boyu kullanım veya bellek sızıntısı olmadığı kanıtı sayılmaz.

Kararlı sürüm için açık kalan işler:

1. Uygulama ayarlarına girilecek standart ve yönetim OpenRouter anahtarıyla canlı test.
   Anahtarlar sohbet veya kaynak kodunda paylaşılmamalı.
2. Fiziksel %150/%200 ölçek, monitör değiştirme/çıkarma ve gerçek uyku/uyanma kontrolü.
   Mevcut ekran 96 DPI; büyük çözünürlüklü render testleri fiziksel DPI testi sayılmadı.
3. Tamamlanan 30 dakikalık ölçümden daha uzun günlük/gece boyu kullanım gözlemi.
   Ana makinede oturum kapatma veya uykuya
   geçirme işlemi otomatik uygulanmadı; açılış testi ayrı Sandbox'ta tamamlandı.
4. İndirilebilir beta yayınının doğrulanması. MIT lisanslı
   kaynaklar [GitHub deposunda](https://github.com/ozkurkuran/ai-usage-viewer) yayımlandı;
   özel güvenlik bildirimi kanalı etkin. İlk Windows CI çalışması başarılı.

Uygulama Windows 11 x64 masaüstü widget'ıdır; Win+W panosu entegrasyonu değildir.
Claude kota kaynağı deneysel. Eski Codex fork kayıtlarında kimliği doğrulanamayan
olaylar toplam dışında gösterilir. Tahmini API karşılığı abonelik faturası değildir;
bilinmeyen fiyat ücretsiz sayılmaz. Bu makinede soğuk dosya açılışları yaklaşık
40 saniye; boş veritabanına 357 dosyanın ilk aktarımı yaklaşık 132 saniye sürdü.
Sonraki artımlı okumalarda üç değişen dosya yaklaşık 0,19 saniyede işlendi.

Uygulama ayarları/hesap tanımları ve kullanıcı fiyatları sürümlü ayar dosyasında;
olaylar, okuma ilerlemesi, kota gözlemleri ve OpenRouter geçmişi SQLite'ta tutulur.
Bu tercih aynı ayarların iki yerde yönetilmesini önler. Kimlik bilgileri kullanıcı
kapsamlı Windows DPAPI ile korunur. Dağıtılan ilk paketler imzasızdır.

Ayrıntılı kanıt ve test senaryoları: [Implementation status](IMPLEMENTATION_STATUS.md),
[Windows QA](WINDOWS_QA.md), [sağlayıcı sözleşmeleri](providers/README.md).
