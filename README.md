# HardwareMonitorByYuinn

Windows için bir donanım izleme uygulaması. Arka planda bir ASP.NET Core servisi olarak
çalışır, verileri SignalR ile gerçek zamanlı aktarır; arayüz kendi masaüstü penceresinde
(WebView2 tabanlı) açılır — ayrı bir tarayıcı sekmesi gerekmez. Ayarlar'dan açılabilen
**Yerel Ağa Açma** özelliğiyle aynı ağdaki başka bir cihazın (telefon, ikinci bilgisayar)
tarayıcısından da PIN korumalı erişilebilir. Amaç, HWiNFO/AIDA64 gibi araçların topladığı
bilgiyi sade ve özelleştirilebilir bir arayüzde göstermek.

İşlemci, ekran kartı, bellek, disk ve ağ verilerinin büyük kısmı Windows'un kendi
arayüzlerinden (WMI performans sayaçları, D3DKMT, ETW) doğrudan okunur. Üretici bazlı
değerler (GPU çekirdek gücü/hotspot sıcaklığı, RAM SPD bilgisi, SMART disk sağlığı gibi)
için LibreHardwareMonitorLib ve birkaç küçük ek kütüphane kullanılır — bunların hepsi
değiştirilmeden, derlenmiş haliyle dağıtılır (bkz. `LICENSES.txt`).

## Özellikler

- Gerçek zamanlı CPU / GPU / RAM / disk / ağ kartları, eşik bazlı renklendirme
  (dikkat/kritik seviyeleri)
- Kalıcı geçmiş kaydı ve CSV/JSON dışa aktarma
- Oyun profilleri: işlem adına göre otomatik algılama, oyuna özel panel düzeni ve
  otomatik anlık görüntü
- FPS takibi (ETW üzerinden), %1 / %0.1 low ve kare süresi grafiği
- Isı haritası ve oyun bazlı geçmiş karşılaştırma
- SMART disk sağlığı, sistem olayı zaman çizelgesi (Windows Event Log), basit
  anomali/bellek sızıntısı tespiti
- Açılışta genel sistem sağlığı özeti (0-100 puan)
- Panel kartlarının görünürlüğü ve sürükle-bırak sıralaması, birden fazla düzen profili
- Hazır tema paketleri, cam efekti, kompakt/normal/detaylı yoğunluk modları
- PIN korumalı yerel ağa açma — aynı ağdaki başka bir cihazdan (telefon, ikinci monitör)
  panele erişim
- Sistem tepsisi simgesi (Göster/Çıkış); pencerenin kapat düğmesi tepsiye küçültme ya da
  doğrudan kapatma arasında ayarlanabilir (Ayarlar → Sistem)
- Pencere kenarlığı/başlık çubuğu aktif temanın arka plan rengiyle uyumlu

## Teknoloji

- .NET 10 / ASP.NET Core, SignalR
- Microsoft.Web.WebView2 (masaüstü penceresi)
- Microsoft.Data.Sqlite (kalıcı geçmiş için)
- LibreHardwareMonitorLib, DiskInfoToolkit, RAMSPDToolkit (donanım okuma)
- Microsoft.Diagnostics.Tracing.TraceEvent (ETW üzerinden FPS ölçümü)
- Sade JavaScript/CSS — istemci tarafında ek bir framework yok

Proje `Business` / `DataAccess` / `Entity` / `Web` katmanlarına ayrılmıştır.

## Gereksinimler

- Windows 10/11 (WMI, D3DKMT ve ETW gibi Windows'a özgü arayüzler kullanıldığı için
  başka bir işletim sisteminde çalışmaz)
- Derlemek için .NET 10 SDK
- Bazı sensörler (özellikle ETW tabanlı FPS ölçümü ve bazı donanım sayaçları) yönetici
  yetkisi gerektirebilir
- Microsoft Edge WebView2 Runtime — neredeyse tüm Windows 10/11 makinelerinde zaten
  kurulu gelir (Edge ile birlikte); yoksa uygulama ilk açılışta kendisi sessizce kurar
  (bir kereye mahsus, internet gerektirir)

## Çalıştırma

### Hazır derleme (kurulum yapmadan)

Kaynak koddan derlemek istemiyorsan [Releases](https://github.com/yusufemreshn/HardwareMonitorByYuinn/releases)
sayfasından en güncel `HardwareMonitorByYuinn-win-x64.zip` dosyasını indir, bir klasöre
çıkart ve `HardwareMonitorByYuinn.Web.exe`'yi çalıştır. Uygulama kendi penceresinde açılır
(tarayıcı sekmesi değil). Self-contained bir derlemedir, ayrıca .NET kurulumu gerekmez.

### Kaynak koddan

```
git clone https://github.com/yusufemreshn/HardwareMonitorByYuinn.git
cd HardwareMonitorByYuinn
dotnet build
dotnet run --project HardwareMonitorByYuinn.Web
```

`dotnet run` ile başlatıldığında (geliştirme ortamı) pencere otomatik açılmaz; sunucu
`http://127.0.0.1:5250` adresinde dinler, tarayıcıdan elle açman gerekir — masaüstü
penceresi yalnızca derlenmiş `.exe`'nin normal kullanımında açılır. Yerel ağa açma ve
PIN koruması Ayarlar sekmesinden etkinleştirilebilir.

Kalıcı geçmiş verileri (`history.db`, oyun oturumları, process örnekleri, giriş
denemeleri) `%LOCALAPPDATA%\HardwareMonitorByYuinn\` altında ayrı SQLite dosyaları
olarak tutulur; proje klasörünün kendisiyle bir ilgisi yoktur.

## Lisans

Bu depodaki kaynak kod [PolyForm Noncommercial License 1.0.0](https://polyformproject.org/licenses/noncommercial/1.0.0)
altındadır (bkz. `LICENSE`) — kişisel/ticari olmayan kullanım, inceleme ve katkı
serbesttir, **ticari kullanım yasaktır**. Uygulamayla birlikte dağıtılan üçüncü
taraf kütüphaneler (LibreHardwareMonitorLib dahil, çoğunluğu Mozilla Public
License 2.0) kendi lisans koşullarına tabidir; tam liste ve ayrıntılar için
`LICENSES.txt` dosyasına bakın.
