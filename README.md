# HardwareMonitorByYuinn

Windows için, tarayıcı üzerinden çalışan bir donanım izleme paneli. Arka planda bir
ASP.NET Core servisi olarak çalışır, verileri SignalR ile gerçek zamanlı olarak
tarayıcıya aktarır. Amaç, HWiNFO/AIDA64 gibi araçların topladığı bilgiyi ayrı bir
masaüstü uygulaması yerine, açık kalan bir web sayfasında sade ve özelleştirilebilir
şekilde göstermek.

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

## Teknoloji

- .NET 10 / ASP.NET Core, SignalR
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

## Çalıştırma

### Hazır derleme (kurulum yapmadan)

Kaynak koddan derlemek istemiyorsan [Releases](https://github.com/yusufemreshn/HardwareMonitorByYuinn/releases)
sayfasından en güncel `HardwareMonitorByYuinn-win-x64.zip` dosyasını indir, bir klasöre
çıkart ve `HardwareMonitorByYuinn.Web.exe`'yi çalıştır. Self-contained bir derlemedir,
ayrıca .NET kurulumu gerekmez.

### Kaynak koddan

```
git clone https://github.com/yusufemreshn/HardwareMonitorByYuinn.git
cd HardwareMonitorByYuinn
dotnet build
dotnet run --project HardwareMonitorByYuinn.Web
```

Varsayılan olarak `http://127.0.0.1:5250` adresinde açılır. Yerel ağa açma ve PIN
koruması Ayarlar sekmesinden etkinleştirilebilir.

Kalıcı geçmiş verileri (`history.db`, oyun oturumları, process örnekleri, giriş
denemeleri) `%LOCALAPPDATA%\HardwareMonitorByYuinn\` altında ayrı SQLite dosyaları
olarak tutulur; proje klasörünün kendisiyle bir ilgisi yoktur.

## Lisans

Bu depodaki kaynak kod MIT lisansı altındadır (bkz. `LICENSE`). Uygulamayla birlikte
dağıtılan üçüncü taraf kütüphaneler (LibreHardwareMonitorLib dahil, çoğunluğu
Mozilla Public License 2.0) kendi lisans koşullarına tabidir; tam liste ve ayrıntılar
için `LICENSES.txt` dosyasına bakın.
