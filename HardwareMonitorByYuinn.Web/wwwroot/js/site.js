// Bildirim geçmişi — bir eşik "normal -> aşım" geçişi yaptığında (dashboard.js'teki
// notifyOnRisingEdge) buraya bir kayıt düşülür; tarayıcı bildirim izni verilmemiş olsa bile kayıt
// tutulur, çünkü "hangi eşik ne zaman kaç kez aşıldı" sorusu bildirim izninden bağımsızdır.
// Neden localStorage'da (SqliteHistoryStore'daki kalıcı process/oyun geçmişi gibi sunucuda değil):
// bu veri tamamen istemci tarafında hesaplanan eşik/bildirim mantığının (HwmonThresholds,
// HwmonNotifySettings — ikisi de localStorage) bir sonucu, sunucunun bundan haberi yok; bu yüzden
// kalıcı geçmişin aksine ayrı bir sunucu/DB round-trip'i gerektirmeden aynı katmanda tutulur.
window.HwmonNotificationHistory = (function () {
    "use strict";

    var STORAGE_KEY = "hwmon-notification-history";

    // Sınırsız büyümesin diye eski kayıtlar budanır; 300 kayıt bir kaç günlük normal kullanım için yeterli.
    var MAX_ENTRIES = 300;

    function load() {
        try {
            var saved = JSON.parse(localStorage.getItem(STORAGE_KEY));
            return Array.isArray(saved) ? saved : [];
        } catch (e) {
            return [];
        }
    }

    function record(entry) {
        var list = load();
        list.unshift({
            timestampIso: new Date().toISOString(),
            key: entry.key,
            title: entry.title,
            body: entry.body
        });
        if (list.length > MAX_ENTRIES) list.length = MAX_ENTRIES;
        try { localStorage.setItem(STORAGE_KEY, JSON.stringify(list)); } catch (e) { /* kota aşımı/gizli sekme: yok say */ }
        document.dispatchEvent(new CustomEvent("hwmon:notification-history-changed"));
    }

    function clear() {
        try { localStorage.removeItem(STORAGE_KEY); } catch (e) { /* gizli sekme: yok say */ }
        document.dispatchEvent(new CustomEvent("hwmon:notification-history-changed"));
    }

    // Aynı anda açık başka bir sekmede (ör. Panel'de yeni bir eşik aşımı) geçmiş değiştiğinde
    // Bildirimler sayfası açıksa anında güncellensin.
    window.addEventListener("storage", function (e) {
        if (e.key !== STORAGE_KEY) return;
        document.dispatchEvent(new CustomEvent("hwmon:notification-history-changed"));
    });

    return { load: load, record: record, clear: clear, storageKey: STORAGE_KEY };
})();

// Paylaşılan SignalR bağlantısı — daha önce dashboard.js/details.js VE threshold-notifier.js
// (her sayfada, _Layout.cshtml üzerinden yüklenir) her biri kendi bağlantısını açıyordu; Panel/
// Detaylar'da bu, aynı sayfada iki ayrı negotiate + iki WebSocket + her yayının iki kez alınması
// demekti (birden fazla sekmede daha da katlanıyordu). Artık tüm sayfalar aynı tek bağlantıyı
// paylaşır: kim önce start() çağırırsa bağlantıyı o başlatır, sonraki çağıranlar aynı Promise'i
// paylaşır. Bağlantı kopma/yeniden kurulma durumunu da tek bir yerden, görünür bir üst banner ile
// bildirir (önceden yalnızca console.error'a düşüyordu, kullanıcı fark etmiyordu).
window.HwmonHub = (function () {
    "use strict";

    if (typeof signalR === "undefined") return null;

    var connection = null;
    var startPromise = null;
    var banner = null;

    function ensureBanner() {
        if (banner) return banner;
        banner = document.createElement("div");
        banner.id = "hwmon-connection-banner";
        banner.style.cssText = "display:none;position:sticky;top:0;z-index:1000;text-align:center;" +
            "padding:6px 12px;font-size:.85rem;color:#fff;background:var(--accent-danger,#e5484d);";
        document.body.insertBefore(banner, document.body.firstChild);
        return banner;
    }

    function showBanner(text) {
        var el = ensureBanner();
        el.textContent = text;
        el.style.display = "block";
    }

    function hideBanner() {
        if (banner) banner.style.display = "none";
    }

    function get() {
        if (!connection) {
            connection = new signalR.HubConnectionBuilder()
                .withUrl("/hubs/hardware")
                .withAutomaticReconnect()
                .build();
            connection.onreconnecting(function () {
                showBanner("Sunucuyla bağlantı koptu, yeniden bağlanılıyor…");
            });
            connection.onreconnected(function () {
                hideBanner();
            });
            connection.onclose(function () {
                showBanner("Sunucuyla bağlantı kesildi. Veriler artık canlı güncellenmiyor, sayfayı yenileyin.");
            });
        }
        return connection;
    }

    function start() {
        if (!startPromise) startPromise = get().start();
        return startPromise;
    }

    return { get: get, start: start };
})();

// Değer+birim formatlama — eskiden dashboard.js/details.js/history.js/comparison.js (İKİ farklı
// kopyası, biri savunmacı null-kontrolü yapıyor diğeri yapmıyordu)/game-history.js/report.js/
// threshold-notifier.js olmak üzere 8 dosyada birebir kopyalanmıştı; biri değişip diğerleri
// unutulursa sayfalar arasında tutarsız gösterim riski vardı. Artık tek kaynak burası.
window.HwmonFormat = (function () {
    "use strict";

    function fmt(value, unit, digits) {
        if (typeof value !== "number" || isNaN(value)) return "--";
        var c = window.HwmonUnits ? window.HwmonUnits.convert(value, unit) : { value: value, unit: unit };
        return c.value.toFixed(digits) + " " + c.unit;
    }

    return { fmt: fmt };
})();

// Eşik ayarları — Panel'deki kart uyarıları ve bildirimleri (dashboard.js) ile Ayarlar sayfasındaki
// form (settings.js) aynı kaynağı kullanır. İşlemci ve Ekran Kartı için kullanıcı hangi metriğin
// (sıcaklık ya da kullanım %) izleneceğini seçebilir; varsayılan hâlâ sıcaklık ve eski 85°C değeridir,
// yani mevcut davranış değişmez, yalnızca değiştirilebilir hâle gelir. Bellek ve disk şimdilik tek
// bir metrikle sabit (önceki davranışla aynı).
window.HwmonThresholds = (function () {
    "use strict";

    var STORAGE_KEY = "hwmon-thresholds";

    var METRICS = {
        cpu: {
            temp: { label: "Sıcaklık", unit: "°C", field: "packageTemperatureC" },
            usage: { label: "Kullanım", unit: "%", field: "totalLoadPercent" }
        },
        gpu: {
            temp: { label: "Sıcaklık", unit: "°C", field: "coreTemperatureC" },
            usage: { label: "Kullanım", unit: "%", field: "loadPercent" }
        }
    };

    var DEFAULTS = {
        cpu: { metric: "temp", value: 85, cautionValue: 75 },
        gpu: { metric: "temp", value: 85, cautionValue: 75 },
        ramUsedPercent: 90,
        ramCautionPercent: 80,
        diskTempC: 60,
        diskCautionTempC: 50
    };

    // Hem localStorage'dan hem de (Oyun Profilleri gibi) başka bir kaynaktan gelen keyfi/olası
    // bozuk bir eşik nesnesini güvenli bir hâle getirir; geçersiz alanlar sessizce varsayılana döner.
    function sanitize(saved) {
        var result = {
            cpu: Object.assign({}, DEFAULTS.cpu),
            gpu: Object.assign({}, DEFAULTS.gpu),
            ramUsedPercent: DEFAULTS.ramUsedPercent,
            ramCautionPercent: DEFAULTS.ramCautionPercent,
            diskTempC: DEFAULTS.diskTempC,
            diskCautionTempC: DEFAULTS.diskCautionTempC
        };

        if (saved && typeof saved === "object") {
            ["cpu", "gpu"].forEach(function (cardKey) {
                var entry = saved[cardKey];
                if (entry && METRICS[cardKey][entry.metric] && typeof entry.value === "number" && !isNaN(entry.value)) {
                    var cautionValue = DEFAULTS[cardKey].cautionValue;
                    if (typeof entry.cautionValue === "number" && !isNaN(entry.cautionValue)) cautionValue = entry.cautionValue;
                    result[cardKey] = { metric: entry.metric, value: entry.value, cautionValue: cautionValue };
                }
            });
            if (typeof saved.ramUsedPercent === "number" && !isNaN(saved.ramUsedPercent)) result.ramUsedPercent = saved.ramUsedPercent;
            if (typeof saved.ramCautionPercent === "number" && !isNaN(saved.ramCautionPercent)) result.ramCautionPercent = saved.ramCautionPercent;
            if (typeof saved.diskTempC === "number" && !isNaN(saved.diskTempC)) result.diskTempC = saved.diskTempC;
            if (typeof saved.diskCautionTempC === "number" && !isNaN(saved.diskCautionTempC)) result.diskCautionTempC = saved.diskCautionTempC;
        }

        return result;
    }

    // value > critical ise "critical", value > caution ise "caution", aksi hâlde "normal" döner.
    // Kartlardaki iki kademeli (Dikkat sarı / Kritik kırmızı) renklendirme bu fonksiyona dayanır.
    function levelFor(value, cautionThreshold, criticalThreshold) {
        if (typeof value !== "number" || isNaN(value)) return "normal";
        if (value > criticalThreshold) return "critical";
        if (value > cautionThreshold) return "caution";
        return "normal";
    }

    function load() {
        try {
            return sanitize(JSON.parse(localStorage.getItem(STORAGE_KEY)));
        } catch (e) {
            return sanitize(null); // bozuk kayıt yok sayılır, varsayılanlar kullanılır
        }
    }

    function save(thresholds) {
        try {
            localStorage.setItem(STORAGE_KEY, JSON.stringify(thresholds));
        } catch (e) { /* kota aşımı/gizli sekme: kayıt yapılamadı, sessizce yok say */ }
    }

    // Aynı anda açık başka bir sekmede eşikler değiştirildiğinde bu sekme de anında güncellensin
    // (Renkler ve Kart Sıralaması ile aynı desen).
    window.addEventListener("storage", function (e) {
        if (e.key !== STORAGE_KEY) return;
        document.dispatchEvent(new CustomEvent("hwmon:thresholds-changed"));
    });

    // Sağlık puanı: her seviye (levelFor) için -20 (kritik) / -8 (dikkat) düşülüp 0-100 aralığına
    // kırpılır. Bu formül eskiden dashboard.js/history.js/report.js'te üç kez bağımsız kopyalanmıştı
    // (biri güncellenip diğerleri unutulursa Panel/Geçmiş/Rapor farklı puan gösterebilirdi) — artık
    // tek kaynak burası. Panel'in canlı puanı (dashboard.js) DEĞİŞKEN sayıda seviye kullanır
    // (CPU/GPU/RAM/Disk/SMART), bu yüzden ham `scoreFromLevels` doğrudan çağrılır; Geçmiş/Rapor'daki
    // günlük puan ise disk sıcaklığı kalıcı geçmişte tutulmadığından yalnızca CPU/GPU/RAM'e dayanır,
    // bunun için `computeMetricHealthScore` kullanılır.
    function scoreFromLevels(levels) {
        var score = 100;
        levels.forEach(function (level) {
            if (level === "critical") score -= 20;
            else if (level === "caution") score -= 8;
        });
        return Math.max(0, Math.min(100, score));
    }

    function computeMetricHealthScore(thresholds, cpuValue, gpuValue, ramUsagePercent) {
        return scoreFromLevels([
            levelFor(cpuValue, thresholds.cpu.cautionValue, thresholds.cpu.value),
            levelFor(gpuValue, thresholds.gpu.cautionValue, thresholds.gpu.value),
            levelFor(ramUsagePercent, thresholds.ramCautionPercent, thresholds.ramUsedPercent)
        ]);
    }

    return {
        load: load, save: save, sanitize: sanitize, defaults: DEFAULTS, metrics: METRICS, storageKey: STORAGE_KEY,
        levelFor: levelFor, scoreFromLevels: scoreFromLevels, computeMetricHealthScore: computeMetricHealthScore
    };
})();

// Birim tercihi — sıcaklık (°C/°F) ve hız (MB/s/Mbps) gerçek matematiksel dönüşüm yapar. Boyut
// (GB/GiB) İSE sayıyı değiştirmez, yalnızca etiketi değiştirir: LibreHardwareMonitorLib zaten
// 1024 tabanlı (yani matematiksel olarak GiB) bir değer veriyor ve uygulama bunu her zaman "GB"
// diye etiketlemişti (çoğu Windows uygulamasının yaptığı gibi); "GiB" seçimi yalnızca bu sayının
// aslında ne olduğunu doğru etiketler, "GB" varsayılanı geçmiş davranışla birebir aynı kalır.
window.HwmonUnitPreferences = (function () {
    "use strict";

    var STORAGE_KEY = "hwmon-unit-prefs";
    var DEFAULTS = { temp: "C", speed: "MBps", size: "GB" };

    function sanitize(saved) {
        var result = Object.assign({}, DEFAULTS);
        if (saved && typeof saved === "object") {
            if (saved.temp === "C" || saved.temp === "F") result.temp = saved.temp;
            if (saved.speed === "MBps" || saved.speed === "Mbps") result.speed = saved.speed;
            if (saved.size === "GB" || saved.size === "GiB") result.size = saved.size;
        }
        return result;
    }

    function load() {
        try {
            return sanitize(JSON.parse(localStorage.getItem(STORAGE_KEY)));
        } catch (e) {
            return sanitize(null);
        }
    }

    function save(prefs) {
        try { localStorage.setItem(STORAGE_KEY, JSON.stringify(prefs)); } catch (e) { /* kota aşımı/gizli sekme: yok say */ }
    }

    window.addEventListener("storage", function (e) {
        if (e.key !== STORAGE_KEY) return;
        document.dispatchEvent(new CustomEvent("hwmon:unit-prefs-changed"));
    });

    return { load: load, save: save, sanitize: sanitize, defaults: DEFAULTS, storageKey: STORAGE_KEY };
})();

// Enerji tüketimi maliyet tahmini — yalnızca İşlemci + Ekran Kartı paket güçlerinden hesaplanır
// (anakart/depolama/ekran gibi ölçülemeyen bileşenler dahil değildir), bu yüzden gerçek toplam
// sistem tüketiminden düşük çıkması beklenen bir YAKLAŞIK değerdir; arayüzde de bu şekilde belirtilir.
window.HwmonEnergyCost = (function () {
    "use strict";

    var STORAGE_KEY = "hwmon-energy-cost";
    var DEFAULTS = { enabled: true, ratePerKwh: null };

    function sanitize(saved) {
        var result = Object.assign({}, DEFAULTS);
        if (saved && typeof saved === "object") {
            if (typeof saved.enabled === "boolean") result.enabled = saved.enabled;
            if (typeof saved.ratePerKwh === "number" && isFinite(saved.ratePerKwh) && saved.ratePerKwh > 0)
                result.ratePerKwh = saved.ratePerKwh;
        }
        return result;
    }

    function load() {
        try {
            return sanitize(JSON.parse(localStorage.getItem(STORAGE_KEY)));
        } catch (e) {
            return sanitize(null);
        }
    }

    function save(prefs) {
        try { localStorage.setItem(STORAGE_KEY, JSON.stringify(prefs)); } catch (e) { /* kota aşımı/gizli sekme: yok say */ }
    }

    window.addEventListener("storage", function (e) {
        if (e.key !== STORAGE_KEY) return;
        document.dispatchEvent(new CustomEvent("hwmon:energy-cost-changed"));
    });

    return { load: load, save: save, sanitize: sanitize, defaults: DEFAULTS, storageKey: STORAGE_KEY };
})();

// Panel/Detaylar/Geçmiş/Karşılaştırma sayfalarındaki ortak `fmt(value, unit, digits)` yardımcıları
// bu modülden geçerek gerçek birimi çözer; böylece her sayfada aynı dönüştürme mantığı tekrarlanmaz.
window.HwmonUnits = (function () {
    "use strict";

    var prefs = window.HwmonUnitPreferences.load();

    function refresh() { prefs = window.HwmonUnitPreferences.load(); }
    document.addEventListener("hwmon:unit-prefs-changed", refresh);
    window.addEventListener("storage", function (e) {
        if (e.key === window.HwmonUnitPreferences.storageKey) refresh();
    });

    // {value, unit} çifti döner; tanınmayan bir birim ya da sayı olmayan bir değer geldiğinde
    // değeri/birimi olduğu gibi geri verir (çağıran taraf zaten "--" gösterecek şekilde davranır).
    function convert(value, unit) {
        if (typeof value !== "number" || isNaN(value)) return { value: value, unit: unit };
        if (unit === "°C" && prefs.temp === "F") return { value: value * 9 / 5 + 32, unit: "°F" };
        if (unit === "MB/s" && prefs.speed === "Mbps") return { value: value * 8, unit: "Mbps" };
        if (unit === "GB" && prefs.size === "GiB") return { value: value, unit: "GiB" };
        return { value: value, unit: unit };
    }

    return { convert: convert };
})();

// Kart içindeki "45°C · %62" gibi değer satırları (.stat-value-row) HER ZAMAN yan yana kalmalı,
// hiçbir zaman alt alta düşmemeli (kullanıcı isteği). CSS'te flex-wrap:nowrap zorlanıyor; büyük
// yazı boyutu/serif gibi geniş yazı tiplerinde satır kart genişliğine sığmayabilir — bu durumda
// burada satır tek satıra sığana kadar font-size adım adım küçültülür. Sığdıktan sonra tekrar
// büyütülmeye ÇALIŞILMAZ (yalnızca küçültme yönünde), çünkü taban değer zaten CSS'ten (density
// moduna göre de değişebilen) doğru şekilde geliyor; her çağrıda önce inline font-size sıfırlanıp
// CSS'in verdiği taban boydan yeniden başlanır, böylece küçültme birikmez.
window.HwmonFitStatRows = (function () {
    "use strict";

    var MIN_FONT_PX = 12;
    var MAX_ITERATIONS = 40;

    function fitRow(row) {
        if (!row) return;
        row.style.fontSize = "";
        var size = parseFloat(getComputedStyle(row).fontSize);
        if (!size || isNaN(size)) return;
        var guard = 0;
        while (row.scrollWidth > row.clientWidth + 1 && size > MIN_FONT_PX && guard < MAX_ITERATIONS) {
            size -= 1;
            row.style.fontSize = size + "px";
            guard++;
        }
    }

    function fitAll() {
        var rows = document.querySelectorAll(".stat-value-row");
        for (var i = 0; i < rows.length; i++) fitRow(rows[i]);
    }

    window.addEventListener("resize", fitAll);
    if (document.readyState === "loading") {
        document.addEventListener("DOMContentLoaded", fitAll);
    } else {
        fitAll();
    }

    return { fitAll: fitAll, fitRow: fitRow };
})();

// Yazı tipi boyutu/ailesi — :root'taki --app-font-size (html'e uygulanır, çoğu bileşen rem
// kullandığı için tüm sayfa orantılı büyür/küçülür) ve --app-font-family (body'ye uygulanır,
// diğer elemanlar zaten font-family:inherit kullanıyor) override edilerek anında geçerli olur.
window.HwmonTypography = (function () {
    "use strict";

    var STORAGE_KEY = "hwmon-typography";
    var DEFAULTS = { fontSize: "md", fontFamily: "system" };

    var FONT_SIZE_PX = { sm: "14px", md: "16px", lg: "18px" };
    var FONT_FAMILY_STACK = {
        system: '"Segoe UI", "Inter", system-ui, -apple-system, sans-serif',
        serif: 'Georgia, "Times New Roman", serif',
        mono: '"Cascadia Code", "Consolas", "Courier New", monospace'
    };

    function sanitize(saved) {
        var result = Object.assign({}, DEFAULTS);
        if (saved && typeof saved === "object") {
            if (FONT_SIZE_PX[saved.fontSize]) result.fontSize = saved.fontSize;
            if (FONT_FAMILY_STACK[saved.fontFamily]) result.fontFamily = saved.fontFamily;
        }
        return result;
    }

    function load() {
        try {
            return sanitize(JSON.parse(localStorage.getItem(STORAGE_KEY)));
        } catch (e) {
            return sanitize(null);
        }
    }

    function save(prefs) {
        try { localStorage.setItem(STORAGE_KEY, JSON.stringify(prefs)); } catch (e) { /* kota aşımı/gizli sekme: yok say */ }
    }

    function apply(prefs) {
        document.documentElement.style.setProperty("--app-font-size", FONT_SIZE_PX[prefs.fontSize] || FONT_SIZE_PX.md);
        document.documentElement.style.setProperty("--app-font-family", FONT_FAMILY_STACK[prefs.fontFamily] || FONT_FAMILY_STACK.system);
        // Yazı boyutu/ailesi değişince kart değer satırları (45°C · %62 gibi) yeniden sığdırılır.
        if (window.HwmonFitStatRows) window.HwmonFitStatRows.fitAll();
    }

    apply(load());

    window.addEventListener("storage", function (e) {
        if (e.key !== STORAGE_KEY) return;
        apply(load());
    });

    return { load: load, save: save, sanitize: sanitize, apply: apply, defaults: DEFAULTS, storageKey: STORAGE_KEY, fontSizes: FONT_SIZE_PX, fontFamilies: FONT_FAMILY_STACK };
})();

// Cam efekti (glassmorphism) aç/kapa — <html>'e "glass-effect" sınıfı eklenip kaldırılır, gerçek
// görsel kurallar site.css'teki .glass-effect bloğunda.
window.HwmonGlassEffect = (function () {
    "use strict";

    var STORAGE_KEY = "hwmon-glass-effect";

    function load() {
        try { return localStorage.getItem(STORAGE_KEY) === "1"; } catch (e) { return false; }
    }

    function save(enabled) {
        try { localStorage.setItem(STORAGE_KEY, enabled ? "1" : "0"); } catch (e) { /* kota aşımı/gizli sekme: yok say */ }
    }

    function apply(enabled) {
        document.documentElement.classList.toggle("glass-effect", !!enabled);
    }

    apply(load());

    window.addEventListener("storage", function (e) {
        if (e.key !== STORAGE_KEY) return;
        apply(load());
    });

    return { load: load, save: save, apply: apply, storageKey: STORAGE_KEY };
})();

// Kart yoğunluk modu — <html data-density="compact|normal|detailed">, gerçek kurallar
// site.css'te. "normal" hiçbir öznitelik eklemez (varsayılan görünüm, regresyon riski yok).
window.HwmonDensity = (function () {
    "use strict";

    var STORAGE_KEY = "hwmon-density";
    var VALID = { compact: true, normal: true, detailed: true };
    var DEFAULT = "normal";

    function sanitize(value) {
        return VALID[value] ? value : DEFAULT;
    }

    function load() {
        try { return sanitize(localStorage.getItem(STORAGE_KEY)); } catch (e) { return DEFAULT; }
    }

    function save(value) {
        try { localStorage.setItem(STORAGE_KEY, sanitize(value)); } catch (e) { /* kota aşımı/gizli sekme: yok say */ }
    }

    function apply(value) {
        value = sanitize(value);
        if (value === "normal") document.documentElement.removeAttribute("data-density");
        else document.documentElement.setAttribute("data-density", value);
        // Kompakt modda kart dolgusu azalır, bu da değer satırlarının kullanılabilir genişliğini
        // değiştirir; yeniden sığdır.
        if (window.HwmonFitStatRows) window.HwmonFitStatRows.fitAll();
    }

    apply(load());

    window.addEventListener("storage", function (e) {
        if (e.key !== STORAGE_KEY) return;
        apply(load());
    });

    return { load: load, save: save, apply: apply, sanitize: sanitize, defaultValue: DEFAULT, storageKey: STORAGE_KEY };
})();

// Panel görünüm modu — <html data-panel-view="list">, gerçek kurallar site.css'te. "grid"
// (varsayılan) hiçbir öznitelik eklemez, mevcut çok sütunlu düzenle birebir aynıdır.
window.HwmonPanelView = (function () {
    "use strict";

    var STORAGE_KEY = "hwmon-panel-view";
    var VALID = { grid: true, list: true };
    var DEFAULT = "grid";

    function sanitize(value) {
        return VALID[value] ? value : DEFAULT;
    }

    function load() {
        try { return sanitize(localStorage.getItem(STORAGE_KEY)); } catch (e) { return DEFAULT; }
    }

    function save(value) {
        try { localStorage.setItem(STORAGE_KEY, sanitize(value)); } catch (e) { /* kota aşımı/gizli sekme: yok say */ }
    }

    function apply(value) {
        value = sanitize(value);
        if (value === "grid") document.documentElement.removeAttribute("data-panel-view");
        else document.documentElement.setAttribute("data-panel-view", value);
    }

    apply(load());

    window.addEventListener("storage", function (e) {
        if (e.key !== STORAGE_KEY) return;
        apply(load());
    });

    return { load: load, save: save, apply: apply, sanitize: sanitize, defaultValue: DEFAULT, storageKey: STORAGE_KEY };
})();

// Kart bazında grafik tipi (çizgi/alan/bar) — yalnızca Panel'deki ana 4 seri (İşlemci/Ekran
// Kartı/Bellek/Kare Hızı) için; ikincil seriler (İndirme/Yükleme/Kare Süresi/low'lar) hep çizgi.
window.HwmonChartTypes = (function () {
    "use strict";

    var STORAGE_KEY = "hwmon-chart-types";
    var VALID_TYPES = { line: true, area: true, bar: true };
    var KEYS = ["cpu", "gpu", "ram", "fps"];
    var DEFAULTS = { cpu: "line", gpu: "line", ram: "line", fps: "line" };

    function sanitize(saved) {
        var result = Object.assign({}, DEFAULTS);
        if (saved && typeof saved === "object") {
            KEYS.forEach(function (key) {
                if (VALID_TYPES[saved[key]]) result[key] = saved[key];
            });
        }
        return result;
    }

    function load() {
        try { return sanitize(JSON.parse(localStorage.getItem(STORAGE_KEY))); } catch (e) { return sanitize(null); }
    }

    function save(prefs) {
        try { localStorage.setItem(STORAGE_KEY, JSON.stringify(prefs)); } catch (e) { /* kota aşımı/gizli sekme: yok say */ }
    }

    window.addEventListener("storage", function (e) {
        if (e.key !== STORAGE_KEY) return;
        document.dispatchEvent(new CustomEvent("hwmon:chart-types-changed"));
    });

    return { load: load, save: save, sanitize: sanitize, defaults: DEFAULTS, keys: KEYS, storageKey: STORAGE_KEY };
})();

// Bildirim sesi — harici ses dosyası KULLANILMAZ (proje bağımlılık minimizasyonu kararına uygun,
// bkz. [[hardwaremonitor-bagimlilik-karari]]); Web Audio API ile anlık sentezlenen kısa "bip"
// dizileri kullanılır. threshold-notifier.js her gerçek bildirimde play() çağırır.
window.HwmonNotifySound = (function () {
    "use strict";

    var STORAGE_KEY = "hwmon-notify-sound";
    var DEFAULTS = { sound: "classic", volume: 70 };
    var VALID_SOUNDS = { classic: true, soft: true, alert: true, none: true };

    // [frekans Hz, süre sn, gecikme sn] üçlülerinden oluşan ton tanımları.
    // "alert" bilerek "classic"ten farklı (daha tiz, üç kısa vuruşlu) tutulur;
    // aksi hâlde ikisi kulakla ayırt edilemiyordu.
    var TONES = {
        classic: [[880, 0.12, 0]],
        soft: [[440, 0.18, 0]],
        alert: [[1568, 0.07, 0], [1568, 0.07, 0.11], [1568, 0.07, 0.22]]
    };

    function sanitize(saved) {
        var result = Object.assign({}, DEFAULTS);
        if (saved && typeof saved === "object") {
            if (VALID_SOUNDS[saved.sound]) result.sound = saved.sound;
            if (typeof saved.volume === "number" && saved.volume >= 0 && saved.volume <= 100) result.volume = saved.volume;
        }
        return result;
    }

    function load() {
        try { return sanitize(JSON.parse(localStorage.getItem(STORAGE_KEY))); } catch (e) { return sanitize(null); }
    }

    function save(prefs) {
        try { localStorage.setItem(STORAGE_KEY, JSON.stringify(prefs)); } catch (e) { /* kota aşımı/gizli sekme: yok say */ }
    }

    var audioCtx = null;
    function getContext() {
        if (!audioCtx) {
            var Ctor = window.AudioContext || window.webkitAudioContext;
            if (!Ctor) return null;
            audioCtx = new Ctor();
        }
        return audioCtx;
    }

    // Tarayıcıların otomatik oynatma politikası, kullanıcı etkileşimi olmadan AudioContext'in
    // askıya alınmış (suspended) kalmasına yol açabilir — bildirim, SignalR akışından tetiklendiği
    // için bir "kullanıcı hareketi" sayılmaz. Kullanıcı sayfada en az bir kez tıklamışsa (ör. bir
    // kartı gizlemek için) bağlam zaten açılmış olur; hiç tıklamadıysa ilk birkaç bildirimde ses
    // sessiz kalabilir — bu, JS'in aşamayacağı bir tarayıcı kısıtı (Notification izni gibi).
    function play(prefsOverride) {
        var prefs = prefsOverride || load();
        if (prefs.sound === "none" || prefs.volume <= 0) return;

        var ctx = getContext();
        if (!ctx) return;
        if (ctx.state === "suspended") ctx.resume().catch(function () { /* kullanıcı hareketi bekleniyor */ });

        var tone = TONES[prefs.sound] || TONES.classic;
        var gainValue = Math.max(0, Math.min(1, prefs.volume / 100)) * 0.3;

        tone.forEach(function (part) {
            var freq = part[0], duration = part[1], delay = part[2];
            var osc = ctx.createOscillator();
            var gain = ctx.createGain();
            osc.type = "sine";
            osc.frequency.value = freq;
            var startAt = ctx.currentTime + delay;
            gain.gain.setValueAtTime(0, startAt);
            gain.gain.linearRampToValueAtTime(gainValue, startAt + 0.01);
            gain.gain.linearRampToValueAtTime(0, startAt + duration);
            osc.connect(gain);
            gain.connect(ctx.destination);
            osc.start(startAt);
            osc.stop(startAt + duration + 0.02);
        });
    }

    window.addEventListener("storage", function (e) {
        if (e.key !== STORAGE_KEY) return;
        document.dispatchEvent(new CustomEvent("hwmon:notify-sound-changed"));
    });

    return { load: load, save: save, sanitize: sanitize, defaults: DEFAULTS, play: play, storageKey: STORAGE_KEY };
})();

// Açılışta gösterilecek sekme — gerçek yönlendirme mantığı _Layout.cshtml'in <head>'indeki
// senkron script'te (yalnızca Dashboard/Index'te render edilir); burada yalnızca Ayarlar
// formunun okuyup yazması için saklama/doğrulama var.
window.HwmonStartupTab = (function () {
    "use strict";

    var STORAGE_KEY = "hwmon-startup-tab";
    var VALID = { panel: true, details: true, comparison: true, history: true, settings: true, notifications: true, about: true };
    var DEFAULT = "panel";

    function sanitize(value) {
        return VALID[value] ? value : DEFAULT;
    }

    function load() {
        try { return sanitize(localStorage.getItem(STORAGE_KEY)); } catch (e) { return DEFAULT; }
    }

    function save(value) {
        try { localStorage.setItem(STORAGE_KEY, sanitize(value)); } catch (e) { /* kota aşımı/gizli sekme: yok say */ }
    }

    return { load: load, save: save, sanitize: sanitize, defaultValue: DEFAULT, storageKey: STORAGE_KEY };
})();

// İstatistiksel anomali tespiti aç/kapa — gerçek hesaplama (rolling ortalama/standart sapma,
// z-score) dashboard.js'te; burada yalnızca kullanıcının bu özelliği açık/kapalı tutma tercihi var.
window.HwmonAnomalyDetection = (function () {
    "use strict";

    var STORAGE_KEY = "hwmon-anomaly-detection";

    function isEnabled() {
        try {
            var raw = localStorage.getItem(STORAGE_KEY);
            return raw === null ? true : raw === "1"; // varsayılan: açık
        } catch (e) {
            return true;
        }
    }

    function setEnabled(enabled) {
        try { localStorage.setItem(STORAGE_KEY, enabled ? "1" : "0"); } catch (e) { /* kota aşımı/gizli sekme: yok say */ }
    }

    return { isEnabled: isEnabled, setEnabled: setEnabled, storageKey: STORAGE_KEY };
})();

// Bellek sızıntısı tespiti aç/kapa — gerçek hesaplama (process bazlı RAM eğilimi) dashboard.js'te;
// eskiden anomali tespitiyle aynı anahtarı paylaşıyordu (anomali kapatılınca sızıntı uyarıları da
// istemeden kapanıyordu), artık ayrı ve bağımsız bir tercih. Aynı desen: window.HwmonAnomalyDetection.
window.HwmonMemoryLeakDetection = (function () {
    "use strict";

    var STORAGE_KEY = "hwmon-memory-leak-detection";

    function isEnabled() {
        try {
            var raw = localStorage.getItem(STORAGE_KEY);
            return raw === null ? true : raw === "1"; // varsayılan: açık
        } catch (e) {
            return true;
        }
    }

    function setEnabled(enabled) {
        try { localStorage.setItem(STORAGE_KEY, enabled ? "1" : "0"); } catch (e) { /* kota aşımı/gizli sekme: yok say */ }
    }

    return { isEnabled: isEnabled, setEnabled: setEnabled, storageKey: STORAGE_KEY };
})();

// Renk özelleştirme — kullanıcının Ayarlar sayfasından seçtiği renkler, CSS'teki --accent-*
// değişkenlerini :root üzerinde override ederek hem kartlarda hem grafikte anında geçerli olur.
// Bu blok her sayfada (hwmon-chart.js'den hemen sonra, dashboard.js'den önce) çalışır; böylece
// grafik oluşturulmadan önce doğru renkler zaten uygulanmış olur.
window.HwmonAccentColors = (function () {
    "use strict";

    var STORAGE_KEY = "hwmon-accent-colors";
    var VARS = {
        cpu: "--accent-cpu", gpu: "--accent-gpu", ram: "--accent-ram", fps: "--accent-fps",
        storage: "--accent-storage", network: "--accent-network", process: "--accent-process", system: "--accent-system"
    };
    var DEFAULTS = {
        cpu: "#ff9f45", gpu: "#35d0ba", ram: "#6ea8fe", fps: "#c792ea",
        storage: "#f2c14e", network: "#4ade80", process: "#f472b6", system: "#38bdf8"
    };

    function load() {
        try {
            return JSON.parse(localStorage.getItem(STORAGE_KEY)) || {};
        } catch (e) {
            return {};
        }
    }

    function apply(colors) {
        Object.keys(VARS).forEach(function (key) {
            if (colors && colors[key]) document.documentElement.style.setProperty(VARS[key], colors[key]);
        });
    }

    function clear() {
        Object.keys(VARS).forEach(function (key) {
            document.documentElement.style.removeProperty(VARS[key]);
        });
    }

    apply(load());

    // Aynı anda açık başka bir sekmede renkler değiştirildiğinde bu sekme de anında güncellensin.
    window.addEventListener("storage", function (e) {
        if (e.key !== STORAGE_KEY) return;
        apply(load());
        document.dispatchEvent(new CustomEvent("hwmon:accent-colors-changed"));
    });

    return { load: load, apply: apply, clear: clear, defaults: DEFAULTS, storageKey: STORAGE_KEY, vars: VARS };
})();

// Kart sıralaması — İşlemci ve Ekran Kartı kartlarında hangi değerin büyük/üstte (ilk iki sırada),
// hangisinin küçük/altta (son iki sırada) göründüğünü belirler. Sıralamanın kendisi Ayarlar
// sayfasında düzenlenir; burada yalnızca saklama, doğrulama ve DOM'a uygulama mantığı var.
// Panel dışındaki sayfalarda #cpu-main-row gibi elemanlar bulunmadığı için apply() sessizce atlar.
window.HwmonCardOrder = (function () {
    "use strict";

    var STORAGE_KEY = "hwmon-card-order";
    var METRIC_IDS = {
        cpu: { usage: "cpu-usage", temp: "cpu-temp", power: "cpu-power", clock: "cpu-clock" },
        gpu: { usage: "gpu-usage", temp: "gpu-temp", power: "gpu-power", clock: "gpu-clock" }
    };
    var METRIC_LABELS = { usage: "Kullanım", temp: "Sıcaklık", power: "Güç Tüketimi", clock: "Frekans" };
    var DEFAULTS = {
        cpu: ["temp", "usage", "power", "clock"],
        gpu: ["temp", "usage", "power", "clock"]
    };

    function isValidOrder(cardKey, order) {
        if (!Array.isArray(order)) return false;
        var expected = Object.keys(METRIC_IDS[cardKey]).slice().sort();
        var actual = order.slice().sort();
        return expected.length === actual.length && expected.every(function (key, i) { return key === actual[i]; });
    }

    function load() {
        var saved = {};
        try {
            saved = JSON.parse(localStorage.getItem(STORAGE_KEY)) || {};
        } catch (e) { /* bozuk kayıt yok sayılır, varsayılanlar kullanılır */ }

        var result = {};
        Object.keys(DEFAULTS).forEach(function (cardKey) {
            result[cardKey] = isValidOrder(cardKey, saved[cardKey]) ? saved[cardKey] : DEFAULTS[cardKey].slice();
        });
        return result;
    }

    // Kaydedilmiş sıraya göre kartın ilk iki değerini büyük satıra, kalan ikisini küçük satıra taşır.
    // appendChild var olan elemanı taşır (yeniden oluşturmaz), böylece id'ler ve olay dinleyicileri korunur.
    function apply(cardKey) {
        var ids = METRIC_IDS[cardKey];
        if (!ids) return;

        var mainRow = document.getElementById(cardKey + "-main-row");
        var subRow = document.getElementById(cardKey + "-sub-row");
        if (!mainRow || !subRow) return;

        load()[cardKey].forEach(function (metricKey, index) {
            var el = document.getElementById(ids[metricKey]);
            if (!el) return;
            (index < 2 ? mainRow : subRow).appendChild(el);
        });
    }

    function applyAll() {
        Object.keys(METRIC_IDS).forEach(apply);
    }

    applyAll();

    // Aynı anda açık başka bir sekmede sıralama değiştirildiğinde bu sekme de anında güncellensin.
    window.addEventListener("storage", function (e) {
        if (e.key === STORAGE_KEY) applyAll();
    });

    return {
        load: load, apply: apply, applyAll: applyAll,
        defaults: DEFAULTS, metricIds: METRIC_IDS, metricLabels: METRIC_LABELS, storageKey: STORAGE_KEY
    };
})();

// Kart detay görünürlüğü — kullanıcı, her kartta hangi ikincil değerlerin gösterileceğini
// Ayarlar → Kart Sıralaması sayfasından tek tek seçer ("hepsi için" — CPU/GPU'ya özgü kart
// sıralamasından farklı olarak Bellek/VRAM/FPS/Ağ/Sistem Özeti dahil TÜM kartları kapsar).
// Eskiden bu iş yalnızca "Kart Yoğunluğu" (data-density) tarafından kabaca (yalnızca
// .stat-sub-row gizlenerek) yapılıyordu; yoğunluk seviyeleri artık burada birer HAZIR ŞABLON
// (PRESETS) olarak kalıyor — bir seviyeye tıklamak kutucukları o şablona göre toplu ayarlar,
// kullanıcı sonrasında istediği kutucuğu yine elle açıp kapatabilir (şablon yalnızca başlangıç
// noktasıdır, kalıcı bir kısıtlama değildir).
window.HwmonDetailVisibility = (function () {
    "use strict";

    var STORAGE_KEY = "hwmon-detail-visibility";

    // id: null olan alanlar DOM'da element değil (dinamik olarak yeniden oluşturulan Depolama
    // kartı gibi) — bunlar dashboard.js'teki ilgili render fonksiyonu tarafından okunur.
    var FIELDS = {
        cpu: [
            { key: "usage", id: "cpu-usage", label: "Kullanım" },
            { key: "temp", id: "cpu-temp", label: "Sıcaklık" },
            { key: "power", id: "cpu-power", label: "Güç Tüketimi" },
            { key: "clock", id: "cpu-clock", label: "Frekans" }
        ],
        gpu: [
            { key: "usage", id: "gpu-usage", label: "Kullanım" },
            { key: "temp", id: "gpu-temp", label: "Sıcaklık" },
            { key: "power", id: "gpu-power", label: "Güç Tüketimi" },
            { key: "clock", id: "gpu-clock", label: "Frekans" }
        ],
        vram: [
            { key: "detail", id: "vram-detail", label: "Kullanılan / Toplam MB" }
        ],
        ram: [
            { key: "detail", id: "ram-detail", label: "Kullanılan / Toplam GB" }
        ],
        fps: [
            { key: "low1", id: "fps-low1", label: "1% Low" },
            { key: "low01", id: "fps-low01", label: "0.1% Low" },
            { key: "frametime", id: "fps-frametime", label: "Kare Süresi" },
            { key: "source", id: "fps-source", label: "Kaynak İşlem" }
        ],
        network: [
            { key: "upload", id: "network-upload-row", label: "Yükleme Hızı" },
            { key: "adapter", id: "network-adapter", label: "Adaptör Adı" },
            { key: "sessionTotal", id: "network-session-total", label: "Oturum Toplamı" }
        ],
        storage: [
            { key: "temp", id: null, label: "Sıcaklık" },
            { key: "usage", id: null, label: "Kullanım Yüzdesi" },
            { key: "capacity", id: null, label: "Kapasite (Kullanılan / Toplam)" },
            { key: "rwRate", id: null, label: "Okuma / Yazma Hızı" },
            { key: "name", id: null, label: "Disk Adı" },
            { key: "smart", id: null, label: "Disk Sağlığı (SMART)" }
        ],
        system: [
            { key: "gpuNames", id: "system-gpu-names-row", label: "Ekran Kartı Adı" },
            { key: "os", id: "system-os-row", label: "İşletim Sistemi" },
            { key: "ramTotal", id: "system-ram-row", label: "Toplam Bellek" },
            { key: "storageTotal", id: "system-storage-row", label: "Toplam Depolama" },
            { key: "uptime", id: "system-uptime-row", label: "Açık Kalma Süresi" }
        ]
    };

    // "compact" hazır şablonu, eskiden yoğunluğun tek başına yaptığı gizlemeye karşılık gelir
    // (İşlemci/Ekran Kartı'nda güç+frekans, VRAM/Bellek'te alt satır, FPS'te low'lar/kaynak,
    // Ağ'da yükleme/adaptör/oturum, Depolama'da kapasite/hız/ad/SMART, Sistem Özeti'nde ek
    // satırlar gizlenir). "normal", canlı metrikleri gösterip statik/betimleyici bilgileri
    // (Sistem Özeti'nin tamamı, Kare Hızı'nın kaynak işlem adı, Ağ'ın adaptör adı, Depolama'nın
    // disk adı) gizleyen orta bir şablon; "detaylı" hâlâ hiçbir şeyi gizlemez, en dolu görünüm.
    // Aralarındaki dolgu/font boyutu farkı ayrıca (bkz. HwmonDensity, site.css `[data-density]`).
    var PRESETS = {
        compact: {
            cpu: { usage: true, temp: true, power: false, clock: false },
            gpu: { usage: true, temp: true, power: false, clock: false },
            vram: { detail: false },
            ram: { detail: false },
            fps: { low1: false, low01: false, frametime: false, source: false },
            network: { upload: false, adapter: false, sessionTotal: false },
            storage: { temp: true, usage: true, capacity: false, rwRate: false, name: false, smart: false },
            system: { gpuNames: false, os: false, ramTotal: false, storageTotal: false, uptime: false }
        },
        normal: buildNormalPreset(),
        detailed: allTrue()
    };

    function allTrue() {
        var result = {};
        Object.keys(FIELDS).forEach(function (cardKey) {
            result[cardKey] = {};
            FIELDS[cardKey].forEach(function (f) { result[cardKey][f.key] = true; });
        });
        return result;
    }

    // "normal": tüm canlı metrikler açık, yalnızca sayfa yenilenmedikçe değişmeyen betimleyici/
    // kimlik bilgileri (isim/adaptör/kaynak işlem, Sistem Özeti'nin tamamı) kapalı — bunlar zaten
    // "detaylı"da her zaman görünür.
    function buildNormalPreset() {
        var result = allTrue();
        result.fps.source = false;
        result.network.adapter = false;
        result.storage.name = false;
        result.system = { gpuNames: false, os: false, ramTotal: false, storageTotal: false, uptime: false };
        return result;
    }

    // Bu, PRESETS.normal'DAN AYRI ve sabit "her şey görünür" hâlidir: hiçbir yoğunluk şablonu
    // hiç seçilmemişken (ilk kurulum, localStorage boş) uygulanan gerçek varsayılan budur — kartlar
    // ancak kullanıcı Kart Sıralaması'ndan bir şablona (Kompakt/Normal/Detaylı) TIKLARSA o şablona
    // göre değişir. PRESETS.normal'a bağlı olsaydı, "normal" şablonunun içeriğini değiştirmek hiç
    // dokunmamış kullanıcıların varsayılan görünümünü de sessizce değiştirirdi.
    function defaults() {
        return allTrue();
    }

    function sanitize(saved) {
        var result = defaults();
        if (!saved || typeof saved !== "object") return result;
        Object.keys(FIELDS).forEach(function (cardKey) {
            if (!saved[cardKey]) return;
            FIELDS[cardKey].forEach(function (f) {
                if (typeof saved[cardKey][f.key] === "boolean") result[cardKey][f.key] = saved[cardKey][f.key];
            });
        });
        return result;
    }

    function load() {
        try { return sanitize(JSON.parse(localStorage.getItem(STORAGE_KEY))); } catch (e) { return defaults(); }
    }

    function save(state) {
        try { localStorage.setItem(STORAGE_KEY, JSON.stringify(state)); } catch (e) { /* kota aşımı/gizli sekme: yok say */ }
    }

    // Depolama kartı hariç tüm kartlar sabit DOM id'lerine sahiptir; bunlar doğrudan gizlenir/gösterilir.
    function apply(state) {
        state = sanitize(state);
        Object.keys(FIELDS).forEach(function (cardKey) {
            FIELDS[cardKey].forEach(function (f) {
                if (!f.id) return;
                var el = document.getElementById(f.id);
                if (el) el.style.display = state[cardKey][f.key] ? "" : "none";
            });
        });
        if (window.HwmonFitStatRows) window.HwmonFitStatRows.fitAll();
        return state;
    }

    // Bir yoğunluk şablonunu (compact/normal/detailed) geçerli duruma uygulayıp kaydeder; dönen
    // değer Ayarlar sayfasındaki kutucukları güncellemek için kullanılır.
    function applyPreset(densityValue) {
        var preset = PRESETS[densityValue] || PRESETS.normal;
        var state = JSON.parse(JSON.stringify(preset));
        save(state);
        apply(state);
        return state;
    }

    apply(load());

    window.addEventListener("storage", function (e) {
        if (e.key !== STORAGE_KEY) return;
        apply(load());
    });

    return {
        load: load, save: save, apply: apply, applyPreset: applyPreset,
        fields: FIELDS, presets: PRESETS, defaults: defaults, storageKey: STORAGE_KEY
    };
})();

// Arka plan rengi özelleştirme — aydınlık ve karanlık mod için ayrı ayrı seçilebilir. Aktif temaya
// göre --bg'yi override eder; tema değiştiğinde (hwmon:theme-changed) doğru renge yeniden uygulanır.
// Kart/üst yüzey/kenarlık renkleri (--card-bg, --bg-elevated, --border) sabit lacivert kalmasın diye
// seçilen --bg'den aynı ton (hue/saturation) korunarak, yalnızca açıklık (lightness) kaydırılarak
// türetilir — böylece her tema (hazır ya da elle seçilmiş) kartlara da kendi rengini yansıtır.
window.HwmonBackgroundColors = (function () {
    "use strict";

    var STORAGE_KEY = "hwmon-bg-colors";
    var DEFAULTS = { light: "#eef1f6", dark: "#0b0f14" };
    var DERIVED_VARS = ["--card-bg", "--bg-elevated", "--border"];

    function load() {
        try {
            return JSON.parse(localStorage.getItem(STORAGE_KEY)) || {};
        } catch (e) {
            return {};
        }
    }

    function currentTheme() {
        return document.documentElement.getAttribute("data-theme") === "light" ? "light" : "dark";
    }

    function hexToHsl(hex) {
        var m = /^#?([a-f\d]{2})([a-f\d]{2})([a-f\d]{2})$/i.exec(hex);
        if (!m) return null;
        var r = parseInt(m[1], 16) / 255, g = parseInt(m[2], 16) / 255, b = parseInt(m[3], 16) / 255;
        var max = Math.max(r, g, b), min = Math.min(r, g, b);
        var h = 0, s = 0, l = (max + min) / 2;
        var d = max - min;
        if (d !== 0) {
            s = d / (1 - Math.abs(2 * l - 1));
            switch (max) {
                case r: h = 60 * (((g - b) / d) % 6); break;
                case g: h = 60 * ((b - r) / d + 2); break;
                default: h = 60 * ((r - g) / d + 4); break;
            }
        }
        if (h < 0) h += 360;
        return { h: h, s: s * 100, l: l * 100 };
    }

    function hslToHex(h, s, l) {
        s = Math.max(0, Math.min(100, s)) / 100;
        l = Math.max(0, Math.min(100, l)) / 100;
        var c = (1 - Math.abs(2 * l - 1)) * s;
        var x = c * (1 - Math.abs(((h / 60) % 2) - 1));
        var m = l - c / 2;
        var r, g, b;
        if (h < 60) { r = c; g = x; b = 0; }
        else if (h < 120) { r = x; g = c; b = 0; }
        else if (h < 180) { r = 0; g = c; b = x; }
        else if (h < 240) { r = 0; g = x; b = c; }
        else if (h < 300) { r = x; g = 0; b = c; }
        else { r = c; g = 0; b = x; }
        var toHex = function (v) {
            var n = Math.round((v + m) * 255);
            return Math.max(0, Math.min(255, n)).toString(16).padStart(2, "0");
        };
        return "#" + toHex(r) + toHex(g) + toHex(b);
    }

    // Seçilen --bg rengine göre kart/üst yüzey/kenarlık tonlarını hesaplar. Karanlık modda açıklığı
    // kademeli olarak artırır (varsayılan #0b0f14 → #111823 → #141c28 → #232e3d ile aynı adımlar);
    // aydınlık modda kart/üst yüzey beyaza gider, kenarlık --bg'den biraz koyu kalır.
    function deriveShades(baseHex, isDark) {
        var hsl = hexToHsl(baseHex);
        if (!hsl) return null;
        if (isDark) {
            return {
                elevated: hslToHex(hsl.h, hsl.s, hsl.l + 4),
                card: hslToHex(hsl.h, hsl.s, hsl.l + 6),
                border: hslToHex(hsl.h, hsl.s, hsl.l + 13)
            };
        }
        return {
            elevated: hslToHex(hsl.h, Math.max(0, hsl.s - 10), Math.min(100, hsl.l + 6)),
            card: hslToHex(hsl.h, Math.max(0, hsl.s - 10), Math.min(100, hsl.l + 6)),
            border: hslToHex(hsl.h, hsl.s, Math.max(0, hsl.l - 3))
        };
    }

    function apply(colors) {
        var value = colors && colors[currentTheme()];
        if (!value) {
            document.documentElement.style.removeProperty("--bg");
            DERIVED_VARS.forEach(function (name) { document.documentElement.style.removeProperty(name); });
            return;
        }
        document.documentElement.style.setProperty("--bg", value);
        var shades = deriveShades(value, currentTheme() === "dark");
        if (shades) {
            document.documentElement.style.setProperty("--bg-elevated", shades.elevated);
            document.documentElement.style.setProperty("--card-bg", shades.card);
            document.documentElement.style.setProperty("--border", shades.border);
        }
    }

    function clear() {
        document.documentElement.style.removeProperty("--bg");
        DERIVED_VARS.forEach(function (name) { document.documentElement.style.removeProperty(name); });
    }

    apply(load());

    document.addEventListener("hwmon:theme-changed", function () { apply(load()); });

    window.addEventListener("storage", function (e) {
        if (e.key !== STORAGE_KEY) return;
        apply(load());
    });

    return { load: load, apply: apply, clear: clear, defaults: DEFAULTS, storageKey: STORAGE_KEY };
})();

// Anlık Görüntü ayarları — 📸 butonuyla oluşturulan PNG kartta hangi kartların (İşlemci, Ekran Kartı,
// Bellek, Kare Hızı, Depolama) ve her kartın içinde hangi değerlerin (sıcaklık, kullanım, güç, frekans
// vb.) hangi sırayla göründüğünü tutar. Sıralamanın kendisi Ayarlar sayfasında düzenlenir; burada
// yalnızca saklama, doğrulama ve varsayılan değerler var. Varsayılanlar önceki (sabit) görünümle
// birebir aynıdır: güç hiçbir kartta gösterilmezdi, bu yüzden varsayılan olarak kapalı başlar.
window.HwmonSnapshotSettings = (function () {
    "use strict";

    var STORAGE_KEY = "hwmon-snapshot-settings";

    var BLOCK_LABELS = { cpu: "İşlemci", gpu: "Ekran Kartı", ram: "Bellek", fps: "Kare Hızı", storage: "Depolama" };

    var METRIC_LABELS = {
        cpu: { usage: "Kullanım (%)", temp: "Sıcaklık (°C)", power: "Güç (W)", clock: "Frekans (MHz)" },
        gpu: { usage: "Kullanım (%)", temp: "Sıcaklık (°C)", power: "Güç (W)", clock: "Frekans (MHz)" },
        ram: { usage: "Kullanım (%)", detail: "Kullanılan / Toplam (GB)" },
        fps: { value: "Kare Hızı", process: "Kaynak Uygulama" },
        storage: { usage: "Kullanım (%)", temp: "Sıcaklık (°C)" }
    };

    var DEFAULT_BLOCK_ORDER = ["cpu", "gpu", "ram", "fps", "storage"];

    var DEFAULT_METRIC_ORDER = {
        cpu: ["usage", "temp", "clock", "power"],
        gpu: ["usage", "temp", "clock", "power"],
        ram: ["usage", "detail"],
        fps: ["value", "process"],
        storage: ["usage", "temp"]
    };

    var DEFAULT_ENABLED = {
        cpu: { usage: true, temp: true, clock: true, power: false },
        gpu: { usage: true, temp: true, clock: true, power: false },
        ram: { usage: true, detail: true },
        fps: { value: true, process: true },
        storage: { usage: true, temp: true }
    };

    var DEFAULT_BLOCKS_ENABLED = { cpu: true, gpu: true, ram: true, fps: true, storage: true };

    // Anlık görüntü sabit bir 1200 birimlik tasarım genişliği üzerine çizilir (buildSnapshotCanvas);
    // burada seçilen çözünürlük yalnızca bu tasarımın kaç fiziksel piksele büyütülerek dışa
    // aktarılacağını belirler (16:9 standart çözünürlükler) — düşük pikselli/bulanık PNG şikayetine
    // karşı kullanıcının netlik/dosya boyutu tercih edebilmesi için.
    var RESOLUTIONS = {
        "1080p": { w: 1920, h: 1080, label: "1080p (Varsayılan)" },
        "2k": { w: 2560, h: 1440, label: "2K (1440p)" },
        "4k": { w: 3840, h: 2160, label: "4K (2160p)" }
    };
    var DEFAULT_RESOLUTION = "1080p";

    function isValidKeyList(list, validKeys) {
        if (!Array.isArray(list)) return false;
        var expected = validKeys.slice().sort();
        var actual = list.slice().sort();
        return expected.length === actual.length && expected.every(function (key, i) { return key === actual[i]; });
    }

    function load() {
        var saved = {};
        try {
            saved = JSON.parse(localStorage.getItem(STORAGE_KEY)) || {};
        } catch (e) { /* bozuk kayıt yok sayılır, varsayılanlar kullanılır */ }

        var blockOrder = isValidKeyList(saved.blockOrder, DEFAULT_BLOCK_ORDER)
            ? saved.blockOrder.slice() : DEFAULT_BLOCK_ORDER.slice();

        var blocksEnabled = {};
        DEFAULT_BLOCK_ORDER.forEach(function (key) {
            var value = saved.blocksEnabled && saved.blocksEnabled[key];
            blocksEnabled[key] = typeof value === "boolean" ? value : DEFAULT_BLOCKS_ENABLED[key];
        });

        var metricOrder = {};
        var metricsEnabled = {};
        Object.keys(DEFAULT_METRIC_ORDER).forEach(function (blockKey) {
            var validKeys = Object.keys(METRIC_LABELS[blockKey]);
            var savedOrder = saved.metricOrder && saved.metricOrder[blockKey];
            metricOrder[blockKey] = isValidKeyList(savedOrder, validKeys) ? savedOrder.slice() : DEFAULT_METRIC_ORDER[blockKey].slice();

            metricsEnabled[blockKey] = {};
            validKeys.forEach(function (metricKey) {
                var value = saved.metricsEnabled && saved.metricsEnabled[blockKey] && saved.metricsEnabled[blockKey][metricKey];
                metricsEnabled[blockKey][metricKey] = typeof value === "boolean" ? value : DEFAULT_ENABLED[blockKey][metricKey];
            });
        });

        var resolution = RESOLUTIONS[saved.resolution] ? saved.resolution : DEFAULT_RESOLUTION;

        return { blockOrder: blockOrder, blocksEnabled: blocksEnabled, metricOrder: metricOrder, metricsEnabled: metricsEnabled, resolution: resolution };
    }

    function save(next) {
        try { localStorage.setItem(STORAGE_KEY, JSON.stringify(next)); } catch (e) { /* kota aşımı/gizli sekme: yok say */ }
    }

    // Aynı anda açık başka bir sekmede bu ayarlar değiştirildiğinde bu sekme de anında güncellensin.
    window.addEventListener("storage", function (e) {
        if (e.key !== STORAGE_KEY) return;
        document.dispatchEvent(new CustomEvent("hwmon:snapshot-settings-changed"));
    });

    return {
        load: load, save: save, storageKey: STORAGE_KEY,
        blockLabels: BLOCK_LABELS, metricLabels: METRIC_LABELS,
        defaultBlockOrder: DEFAULT_BLOCK_ORDER, defaultMetricOrder: DEFAULT_METRIC_ORDER,
        defaultBlocksEnabled: DEFAULT_BLOCKS_ENABLED, defaultEnabled: DEFAULT_ENABLED,
        resolutions: RESOLUTIONS, defaultResolution: DEFAULT_RESOLUTION
    };
})();

// Ambiyans Modu — tam ekran saat + büyük göstergeler ekranında hangi kartların görüneceğini
// tutar (Ayarlar sayfasında düzenlenir). Varsayılan olarak eskisi gibi yalnızca CPU/GPU/RAM
// açıktır; Kare Hızı ve Depolama kullanıcı isterse eklenebilir.
window.HwmonAmbientSettings = (function () {
    "use strict";

    var STORAGE_KEY = "hwmon-ambient-settings";
    var METRICS = ["cpu", "gpu", "ram", "fps", "storage"];
    var LABELS = { cpu: "İşlemci", gpu: "Ekran Kartı", ram: "Bellek", fps: "Kare Hızı", storage: "Depolama" };
    var DEFAULT_ENABLED = { cpu: true, gpu: true, ram: true, fps: false, storage: false };

    function load() {
        var saved = {};
        try { saved = JSON.parse(localStorage.getItem(STORAGE_KEY)) || {}; } catch (e) { /* bozuk kayıt yok sayılır */ }

        var enabled = {};
        METRICS.forEach(function (key) {
            var value = saved[key];
            enabled[key] = typeof value === "boolean" ? value : DEFAULT_ENABLED[key];
        });
        return enabled;
    }

    function save(enabled) {
        try { localStorage.setItem(STORAGE_KEY, JSON.stringify(enabled)); } catch (e) { /* kota aşımı/gizli sekme: yok say */ }
        document.dispatchEvent(new CustomEvent("hwmon:ambient-settings-changed"));
    }

    window.addEventListener("storage", function (e) {
        if (e.key !== STORAGE_KEY) return;
        document.dispatchEvent(new CustomEvent("hwmon:ambient-settings-changed"));
    });

    return { load: load, save: save, metrics: METRICS, labels: LABELS, defaultEnabled: DEFAULT_ENABLED, storageKey: STORAGE_KEY };
})();

// Oyun Profilleri — işlem adına (fps.sourceProcessName, ETW'den .exe uzantısız gelir) göre özel
// renk/eşik/ambiyans ayarı tanımlar. dashboard.js, ön plandaki uygulama değişip bir profille
// eşleşince bunu HwmonAccentColors.apply()/HwmonThresholds ile "önizleme" gibi uygular — kullanıcının
// asıl kayıtlı (localStorage'daki) tercihlerine dokunmadan; oyun kapanınca otomatik geri döner.
window.HwmonGameProfiles = (function () {
    "use strict";

    var STORAGE_KEY = "hwmon-game-profiles";

    function load() {
        try {
            var saved = JSON.parse(localStorage.getItem(STORAGE_KEY));
            return Array.isArray(saved) ? saved : [];
        } catch (e) {
            return [];
        }
    }

    function save(profiles) {
        try { localStorage.setItem(STORAGE_KEY, JSON.stringify(profiles)); } catch (e) { /* kota aşımı/gizli sekme: yok say */ }
        document.dispatchEvent(new CustomEvent("hwmon:game-profiles-changed"));
    }

    // Bir profile birden fazla işlem adı bağlanabilir (ör. bir oyunun hem "Oyun.exe" hem
    // "Oyun-Win64-Shipping.exe" olarak görünmesi). Yeni profiller `processNames` (dizi) kullanır;
    // eskiden kaydedilmiş tekil `processName` alanı olan profiller de (geriye dönük uyumluluk,
    // var olan kullanıcı verisi kaybolmasın diye) burada aynı şekilde okunur.
    function getProcessNames(profile) {
        if (Array.isArray(profile.processNames)) return profile.processNames.filter(Boolean);
        if (profile.processName) return [profile.processName];
        return [];
    }

    // Süreç adı büyük/küçük harf duyarsız karşılaştırılır; Windows dosya adlarında harf büyüklüğü
    // önemli olmadığı için kullanıcının "valorant" ya da "VALORANT-Win64-Shipping" yazması farketmemeli.
    function findMatch(processName) {
        if (!processName) return null;
        var lower = processName.toLowerCase();
        var profiles = load();
        for (var i = 0; i < profiles.length; i++) {
            var names = getProcessNames(profiles[i]);
            for (var j = 0; j < names.length; j++) {
                if (names[j].toLowerCase() === lower) return profiles[i];
            }
        }
        return null;
    }

    window.addEventListener("storage", function (e) {
        if (e.key !== STORAGE_KEY) return;
        document.dispatchEvent(new CustomEvent("hwmon:game-profiles-changed"));
    });

    return { load: load, save: save, findMatch: findMatch, getProcessNames: getProcessNames, storageKey: STORAGE_KEY };
})();

// Panel Düzeni — Panel'deki 9 kartın (İşlemci/Ekran Kartı/VRAM/Bellek/Kare Hızı/Depolama/Ağ/Kaynak
// Kullanımı/Sistem Özeti) hangisinin görüneceğini ve hangi sırada duracağını tutar. Sıralama
// Ayarlar sayfasında sürükle-bırak ile düzenlenir; burada yalnızca saklama + DOM'a uygulama var.
window.HwmonPanelLayout = (function () {
    "use strict";

    var STORAGE_KEY = "hwmon-panel-layout";
    var DEFAULT_ORDER = ["cpu", "gpu", "vram", "ram", "fps", "storage", "network", "processes", "system"];
    var LABELS = {
        cpu: "İşlemci", gpu: "Ekran Kartı", vram: "Video Belleği (VRAM)", ram: "Bellek", fps: "Kare Hızı",
        storage: "Depolama", network: "Ağ", processes: "Kaynak Kullanımı", system: "Sistem Özeti"
    };

    // Kaydedilmiş sıradaki TANIDIK OLMAYAN anahtarlar (ör. kaldırılmış "fans") sessizce elenir,
    // geri kalan sıralama korunur — eski katı "tam eşleşme" kontrolü (bir tek anahtar bile
    // uyuşmayınca TÜM düzeni sıfırlıyordu) hem yeni bir kart eklendiğinde hem de eskisi
    // kaldırıldığında kullanıcının özenle kurduğu sıralamayı gereksiz yere siliyordu.
    function sanitizeOrder(order) {
        if (!Array.isArray(order)) return null;
        var filtered = order.filter(function (key) { return DEFAULT_ORDER.indexOf(key) !== -1; });
        return filtered.length > 0 ? filtered : null;
    }

    function load() {
        // localStorage'da HİÇ kayıt yoksa (ilk açılış, yeni kullanıcı) VRAM varsayılan olarak
        // gizli gelir — kayıt varsa (kullanıcı en az bir kez Kaydet'e basmışsa) bu davranış hiç
        // devreye girmez, "hidden" içinde vram için ayrı bir değer yoksa görünür kalır (yeni bir
        // kart eklendiğinde sona eklenmesiyle aynı geriye dönük uyum mantığı).
        var isFirstTimeUser = localStorage.getItem(STORAGE_KEY) === null;

        var saved = {};
        try {
            saved = JSON.parse(localStorage.getItem(STORAGE_KEY)) || {};
        } catch (e) { /* bozuk kayıt yok sayılır, varsayılanlar kullanılır */ }

        var order = sanitizeOrder(saved.order);
        if (order) {
            // Kaydedilmiş düzende olmayan yeni bir kart (ör. VRAM eklendiğinde eski kayıtlar)
            // sona eklenir — aksi hâlde yeni kart hiç görünmez.
            DEFAULT_ORDER.forEach(function (key) {
                if (order.indexOf(key) === -1) order.push(key);
            });
        } else {
            order = DEFAULT_ORDER.slice();
        }

        var hidden = {};
        DEFAULT_ORDER.forEach(function (key) {
            if (saved.hidden && Object.prototype.hasOwnProperty.call(saved.hidden, key)) {
                hidden[key] = !!saved.hidden[key];
            } else {
                hidden[key] = isFirstTimeUser && key === "vram";
            }
        });

        return { order: order, hidden: hidden };
    }

    function save(layout) {
        try { localStorage.setItem(STORAGE_KEY, JSON.stringify(layout)); } catch (e) { /* kota aşımı/gizli sekme: yok say */ }
        document.dispatchEvent(new CustomEvent("hwmon:panel-layout-changed"));
        apply();
    }

    // Verilen sıraya/görünürlüğe göre kartları taşır (appendChild var olan elemanı taşır, yeniden
    // oluşturmaz) ve gizlenenleri display:none yapar. localStorage'a DOKUNMAZ — bu yüzden Oyun
    // Profilleri gibi "geçici önizleme" ihtiyaçları için de kullanılabilir (bkz. dashboard.js
    // applyGameProfile). Panel dışındaki sayfalarda .stat-grid bulunmadığı için sessizce atlanır.
    function applyLayout(layout) {
        var grid = document.querySelector(".stat-grid");
        if (!grid) return;

        layout.order.forEach(function (key) {
            var card = grid.querySelector('[data-card-key="' + key + '"]');
            if (!card) return;
            card.style.display = layout.hidden[key] ? "none" : "";
            grid.appendChild(card);
        });
    }

    function apply() {
        applyLayout(load());
    }

    apply();

    window.addEventListener("storage", function (e) {
        if (e.key !== STORAGE_KEY) return;
        apply();
    });

    return { load: load, save: save, apply: apply, applyLayout: applyLayout, defaults: DEFAULT_ORDER, labels: LABELS, storageKey: STORAGE_KEY };
})();

// Ayarlar sayfasındaki "Panel Düzeni" VE "Panel Görünümü" bölümlerinin küçük canlı önizlemesi —
// ikisi de aynı mini kart grid'ini çiziyor (biri sıra/görünürlüğü değiştirirken, diğeri Grid/Liste
// modunu değiştirirken), bu yüzden çizim mantığı burada TEK yerde — settings.js'teki iki init
// fonksiyonu da kendi güncel (order, hidden, viewMode) değerlerini bu saf fonksiyona verir.
window.HwmonPanelPreviewRenderer = (function () {
    "use strict";

    // Gerçek .stat-card'la BİREBİR AYNI vurgu rengi (bkz. Dashboard/Index.cshtml'deki
    // style="--accent: var(--accent-X)" tanımları) — önizlemenin gerçeğe ne kadar benzediği bu
    // eşlemeye bağlı.
    var ACCENT_VARS = {
        cpu: "--accent-cpu", gpu: "--accent-gpu", vram: "--accent-gpu", ram: "--accent-ram",
        fps: "--accent-fps", storage: "--accent-storage", network: "--accent-network",
        processes: "--accent-process", system: "--accent-system"
    };

    function render(container, order, hidden, labels, viewMode) {
        if (!container) return;
        container.innerHTML = "";
        container.classList.toggle("panel-layout-preview-list", viewMode === "list");

        var visible = order.filter(function (key) { return !hidden[key]; });
        if (visible.length === 0) {
            var empty = document.createElement("p");
            empty.className = "panel-layout-preview-empty";
            empty.textContent = "Tüm kartlar gizli, Panel boş görünecek.";
            container.appendChild(empty);
            return;
        }

        visible.forEach(function (key) {
            var card = document.createElement("div");
            card.className = "panel-layout-preview-card";
            card.style.setProperty("--accent", "var(" + (ACCENT_VARS[key] || "--accent-cpu") + ")");

            var dot = document.createElement("span");
            dot.className = "panel-layout-preview-dot";
            card.appendChild(dot);

            var label = document.createElement("span");
            label.textContent = labels[key] || key;
            card.appendChild(label);

            container.appendChild(card);
        });
    }

    return { render: render };
})();

// Panel Düzeni Profilleri — HwmonPanelLayout tek bir "aktif" düzeni tutar; bu modül birden fazla
// isimlendirilmiş düzeni saklayıp aralarında geçiş yapılmasını sağlar (ör. "İzleme Düzeni" ve
// "Oyun Düzeni"). Bir profil "uygulandığında" yalnızca HwmonPanelLayout.save() çağrılır — Panel'e
// yansıması zaten o modülün var olan apply() mantığıyla olur, burada tekrar edilmez.
window.HwmonPanelLayoutProfiles = (function () {
    "use strict";

    var STORAGE_KEY = "hwmon-panel-layout-profiles";
    var ACTIVE_KEY = "hwmon-panel-layout-active-profile";

    function load() {
        try {
            var arr = JSON.parse(localStorage.getItem(STORAGE_KEY));
            return Array.isArray(arr) ? arr : [];
        } catch (e) {
            return [];
        }
    }

    function save(list) {
        try { localStorage.setItem(STORAGE_KEY, JSON.stringify(list)); } catch (e) { /* kota aşımı/gizli sekme: yok say */ }
    }

    function getActiveId() {
        try { return localStorage.getItem(ACTIVE_KEY) || null; } catch (e) { return null; }
    }

    function setActiveId(id) {
        try { localStorage.setItem(ACTIVE_KEY, id || ""); } catch (e) { /* kota aşımı/gizli sekme: yok say */ }
    }

    // Şu anki (HwmonPanelLayout'taki) düzeni yeni bir isimli profil olarak kaydeder ve onu aktif yapar.
    function saveCurrentAs(name) {
        var current = window.HwmonPanelLayout.load();
        var list = load();
        var id = "p" + Date.now();
        list.push({ id: id, name: name, order: current.order, hidden: current.hidden });
        save(list);
        setActiveId(id);
        return id;
    }

    function applyProfile(id) {
        var profile = load().filter(function (p) { return p.id === id; })[0];
        if (!profile) return false;
        window.HwmonPanelLayout.save({ order: profile.order, hidden: profile.hidden });
        setActiveId(id);
        return true;
    }

    function remove(id) {
        save(load().filter(function (p) { return p.id !== id; }));
        if (getActiveId() === id) setActiveId(null);
    }

    return {
        load: load, save: save, saveCurrentAs: saveCurrentAs, applyProfile: applyProfile, remove: remove,
        getActiveId: getActiveId, setActiveId: setActiveId, storageKey: STORAGE_KEY
    };
})();

// Sekmeli sayfalarda (Ayarlar, Geçmiş, Karşılaştırma) sol kategori/alt-bölüm gezinmesi — sayfadaki
// tüm bölümler tek seferde gösterilmek yerine solda kategori altında listelenir, sağda yalnızca
// seçili bölüm görünür. `hidden` yalnızca görünürlüğü değiştirir, DOM'dan kaldırmaz; sayfaya özel
// init fonksiyonları elemanları gizliyken de normal şekilde bulup kurabilir. Üç sayfada da aynı
// HTML iskeleti (.settings-shell/.settings-sidebar/.settings-category/.settings-subnav[-nested]/
// .settings-content/.panel) ve CSS kullanıldığından mantık tek yerde toplanmıştır.
window.HwmonSectionNav = (function () {
    "use strict";

    // autoCollapseTargetId (opsiyonel): verilirse, yalnızca bu data-target'a sahip panel
    // gösterildiğinde sidebar'a "history-rows-active" sınıfı eklenir (site.css bu sınıf varken
    // mouse uzaklaşınca sidebar'ı daraltır). Verilmezse bu otomatik daralma hiç devreye girmez,
    // sidebar yalnızca en üstteki hamburger butonuyla elle daraltılıp genişletilebilir.
    function init(sidebarId, contentId, autoCollapseTargetId) {
        var sidebar = document.getElementById(sidebarId);
        var content = document.getElementById(contentId);
        if (!sidebar || !content) return null;

        // .settings-sidebar'ın mouse uzaklaşınca daralması (yalnızca history-rows-active iken,
        // bkz. site.css) :hover/:focus-within'e dayanır. Bir menü butonuna tıklamak o butona
        // klavye odağını (focus) verir; odak kalktığı sürece :focus-within sidebar'ı GENİŞ tutar
        // — kullanıcı menüden bir sayfaya geçip fareyi hemen uzaklaştırsa bile sidebar başka bir
        // yere tıklanana kadar açık kalıyordu. Tıklama sonrası odağı hemen kaldırarak yalnızca
        // gerçek :hover'ın (fare konumunun) daralma/genişlemeyi belirlemesi sağlanır.
        //
        // Sol üstteki hamburger (☰) butonu (sidebar-toggle) ise ayrı, hover'dan bağımsız kilitli
        // bir daralma sağlar: tıklanınca daralır, tekrar tıklanana KADAR (hover'a bakılmaksızın)
        // daralı kalır. Daralmışken herhangi bir sidebar butonuna (kategori/grup aç-kapa ya da bir
        // alt sekme hedefi) tıklanırsa kilidi kalkar ve genişler — kullanıcı nereye tıkladığını
        // görebilsin diye. history-rows-active panelinde (Kayıtları Görüntüle) bu buton bilerek
        // işlevsizdir; o panelde daralma tamamen hover'a bağlı otomatik mekanizmaya bırakılır.
        var toggleBtn = sidebar.querySelector(".sidebar-toggle");
        sidebar.addEventListener("click", function (e) {
            var btn = e.target.closest("button");
            if (!btn) return;
            btn.blur();

            if (btn === toggleBtn) {
                if (sidebar.classList.contains("history-rows-active")) return;
                var collapsed = sidebar.classList.toggle("is-collapsed");
                toggleBtn.setAttribute("aria-pressed", collapsed ? "true" : "false");
                return;
            }

            if (sidebar.classList.contains("is-collapsed")) {
                sidebar.classList.remove("is-collapsed");
                if (toggleBtn) toggleBtn.setAttribute("aria-pressed", "false");
            }
        });

        var categories = Array.prototype.slice.call(sidebar.querySelectorAll(".settings-category"));
        // Görünüm/Eşik Ayarları/Bildirimler gibi eskiden tek dev panel olan gruplar, sidebar'da
        // kategorinin kendisi gibi davranan bir alt seviye daha oluşturur: tıklanınca yalnızca
        // açılıp kapanır (kendi başına bir panel göstermez), altındaki gerçek hedefler nested listede.
        var groups = Array.prototype.slice.call(sidebar.querySelectorAll(".settings-subnav-group"));
        var subButtons = Array.prototype.slice.call(sidebar.querySelectorAll(".settings-subnav button[data-target]"));
        var panels = Array.prototype.slice.call(content.querySelectorAll(".panel"));

        // Bir kategorinin altında iç içe bir grup yoksa VE tam olarak tek hedefi varsa (ör. "Oyun
        // Profilleri" kategorisinin altında yalnızca "Oyun Profilleri" adlı tek bir öğe), o tek öğe
        // kategoriyle birebir aynı şeydir — açıp sonra tekrar tıklamak gereksiz bir adım. Bu
        // kategoriler `settings-category-single` işaretlenir: kategori butonu aç/kapa yerine
        // DOĞRUDAN o hedefe gider (bkz. aşağıdaki tıklama kurulumu), alt liste hiç açılmaz (CSS'te
        // varsayılan `display:none` kalır, `is-open` hiç eklenmediği için), ok işareti gizlenir.
        categories.forEach(function (cat) {
            var hasNestedGroup = !!cat.querySelector(".settings-subnav-group");
            var directTargets = Array.prototype.slice.call(cat.querySelectorAll(".settings-subnav > li > button[data-target]"));
            if (!hasNestedGroup && directTargets.length === 1) {
                cat.classList.add("settings-category-single");
                cat.dataset.singleTarget = directTargets[0].getAttribute("data-target");
            }
        });

        function showSection(targetId, updateHash) {
            var found = false;
            panels.forEach(function (panel) {
                var match = panel.id === targetId;
                panel.hidden = !match;
                if (match) found = true;
            });
            if (!found) return false;

            subButtons.forEach(function (btn) {
                btn.classList.toggle("is-active", btn.getAttribute("data-target") === targetId);
            });

            categories.forEach(function (cat) {
                if (cat.classList.contains("settings-category-single")) {
                    var singleToggle = cat.querySelector(".settings-category-toggle");
                    if (singleToggle) singleToggle.classList.toggle("is-active", cat.dataset.singleTarget === targetId);
                    return;
                }
                if (cat.querySelector('[data-target="' + targetId + '"]')) {
                    cat.classList.add("is-open");
                }
            });

            groups.forEach(function (grp) {
                if (grp.querySelector('[data-target="' + targetId + '"]')) {
                    grp.classList.add("is-open");
                }
            });

            if (autoCollapseTargetId) {
                sidebar.classList.toggle("history-rows-active", targetId === autoCollapseTargetId);
            }

            if (updateHash !== false) {
                history.replaceState(null, "", "#" + targetId);
            }
            return true;
        }

        categories.forEach(function (cat) {
            var toggle = cat.querySelector(".settings-category-toggle");
            if (!toggle) return;
            if (cat.classList.contains("settings-category-single")) {
                toggle.addEventListener("click", function () {
                    if (showSection(cat.dataset.singleTarget, false)) {
                        history.pushState(null, "", "#" + cat.dataset.singleTarget);
                    }
                });
            } else {
                toggle.addEventListener("click", function () {
                    cat.classList.toggle("is-open");
                });
            }
        });

        groups.forEach(function (grp) {
            var toggle = grp.querySelector(".settings-subnav-group-toggle");
            if (toggle) {
                toggle.addEventListener("click", function () {
                    grp.classList.toggle("is-open");
                });
            }
        });

        // Kullanıcının GERÇEK bir tıklamasıyla bölüm değişimi, tarayıcı geçmişine yeni bir adım
        // ekler (pushState) — eskiden burada da (initial-load ile aynı) replaceState kullanılıyordu,
        // bu yüzden "Geri" tuşu alt-sekmeler arasında adım adım gitmek yerine doğrudan bir önceki
        // TAM SAYFAYA atlıyordu (kullanıcı tarafından bulunan hata). `showSection`'ın kendi iç
        // `replaceState`'i burada `false` ile bastırılıp yerine gerçek bir geçmiş girdisi eklenir.
        subButtons.forEach(function (btn) {
            btn.addEventListener("click", function () {
                var target = btn.getAttribute("data-target");
                if (showSection(target, false)) {
                    history.pushState(null, "", "#" + target);
                }
            });
        });

        // Geri/İleri ile hash değiştiğinde ilgili bölümü tekrar göster (geçmiş girdisi zaten
        // tarayıcı tarafından oluşturuldu, burada yalnızca DOM'u ona senkronlarız).
        window.addEventListener("popstate", function () {
            var target = window.location.hash ? window.location.hash.slice(1) : null;
            if (target) showSection(target, false);
        });

        // Başka bir sayfadan "#bölüm-id" bağlantısıyla gelinmişse o bölüm açık gelsin; aksi hâlde
        // ilk bölüm varsayılan olur.
        var initialTarget = window.location.hash ? window.location.hash.slice(1) : null;
        if (!initialTarget || !showSection(initialTarget, false)) {
            showSection(subButtons.length ? subButtons[0].getAttribute("data-target") : null, false);
        }

        return { showSection: showSection, categories: categories, groups: groups, subButtons: subButtons, panels: panels };
    }

    return { init: init };
})();

// Geçmiş sayfasındaki 4 "Kayıt Bilgileri" panelinin (Kayıtlar/Oyun Geçmişi/Süreç/Güvenlik) hepsinde
// aynı "en eski N günü sil" kontrolü var — tek tek 4 kez kopyalamak yerine paylaşılan tek bir
// uygulama. CSRF deseni settings.js'teki Uzaktan Erişim/Başlangıç formlarıyla aynı: token'ı gizli
// input'tan okuyup URLSearchParams body'siyle POST eder. Silme sonrası tüm sayfa yenilenir — sunucu
// zaten Razor ile render ettiği için (bu panellerin hiçbiri JS-fetch değil) ayrı bir client-side
// render yolu icat etmek yerine en basit/güvenilir tazeleme budur.
window.HwmonRecordCleanup = (function () {
    "use strict";

    function wire(options) {
        var select = document.getElementById(options.selectId);
        var button = document.getElementById(options.buttonId);
        var tokenInput = document.querySelector(options.tokenSelector);
        if (!select || !button || !tokenInput) return;

        button.addEventListener("click", function () {
            var days = select.value;
            var confirmed = confirm(
                "En eski " + days + " günlük " + options.nounLabel + " verisini kalıcı olarak silmek " +
                "istediğinize emin misiniz? Bu işlem geri alınamaz."
            );
            if (!confirmed) return;

            button.disabled = true;
            var body = new URLSearchParams();
            body.set("__RequestVerificationToken", tokenInput.value);
            body.set("days", days);

            fetch(options.endpoint, { method: "POST", body: body })
                .then(function (r) {
                    if (!r.ok) throw new Error("cleanup failed");
                    return r.json();
                })
                .then(function (result) {
                    alert(result.deletedCount + " kayıt silindi.");
                    window.location.reload();
                })
                .catch(function () {
                    button.disabled = false;
                    alert("Silme işlemi başarısız oldu.");
                });
        });
    }

    return { wire: wire };
})();

// Ayarlar sayfasına başka bir sayfadan "#bölüm-id" bağlantısıyla gelindiğinde, o paneli görünür alana
// kaydırmanın (tarayıcının kendiliğinden yaptığı) yanı sıra klavye/ekran okuyucu odağını da oraya
// taşır ve kısa süreliğine çerçevesini vurgular; böylece "ilgili ayara git" bağlantıları gerçekten
// dikkat çeker, yalnızca sayfayı kaydırıp bırakmaz.
(function focusHashTargetPanel() {
    "use strict";

    var hash = window.location.hash;
    if (!hash || hash.length < 2) return;

    var target = document.getElementById(hash.slice(1));
    if (!target) return;

    target.setAttribute("tabindex", "-1");
    target.focus({ preventScroll: false });
    target.classList.add("settings-highlight");
    setTimeout(function () { target.classList.remove("settings-highlight"); }, 2200);
})();

// Sekmeler — Bootstrap'in tab bileşeni yerine.
(function () {
    "use strict";

    function activate(button) {
        var target = document.querySelector(button.getAttribute("data-tab-target"));
        if (!target) return;

        var list = button.closest("[role='tablist']");
        var content = target.parentElement;

        list.querySelectorAll("[data-tab-target]").forEach(function (peer) {
            var isCurrent = peer === button;
            peer.classList.toggle("active", isCurrent);
            peer.setAttribute("aria-selected", isCurrent ? "true" : "false");
            peer.setAttribute("tabindex", isCurrent ? "0" : "-1");
        });

        content.querySelectorAll(":scope > .tab-pane").forEach(function (pane) {
            pane.classList.toggle("is-active", pane === target);
        });
    }

    var buttons = Array.prototype.slice.call(document.querySelectorAll("[data-tab-target]"));
    buttons.forEach(function (button, index) {
        button.addEventListener("click", function () { activate(button); });

        // Sekme şeritlerinde ok tuşlarıyla gezinmek erişilebilirlik açısından beklenen davranıştır.
        button.addEventListener("keydown", function (e) {
            var step = e.key === "ArrowRight" ? 1 : e.key === "ArrowLeft" ? -1 : 0;
            if (step === 0) return;

            e.preventDefault();
            var siblings = buttons.filter(function (b) { return b.closest("[role='tablist']") === button.closest("[role='tablist']"); });
            var next = siblings[(siblings.indexOf(button) + step + siblings.length) % siblings.length];
            next.focus();
            activate(next);
        });
    });
})();

// Klavye kısayolları — tuş atamaları Ayarlar → Klavye Kısayolları'ndan değiştirilebilir. Her eylemin
// varsayılan ve kayıtlı bir tuş kombinasyonu ("combo", ör. "1" ya da "ctrl+shift+s") vardır; combo'lar
// çakışamaz (bkz. sanitize). ACTIONS listesi hem burada (gerçek tetikleme) hem de Ayarlar sayfasında
// (düzenleme listesi) kullanılır.
window.HwmonShortcuts = (function () {
    "use strict";

    var STORAGE_KEY = "hwmon-shortcuts";

    var ACTIONS = [
        { key: "nav1", label: "Panel'e git", type: "nav", path: "/", defaultCombo: "1" },
        { key: "nav2", label: "Detaylar'a git", type: "nav", path: "/Details", defaultCombo: "2" },
        { key: "nav3", label: "Karşılaştırma'ya git", type: "nav", path: "/Comparison", defaultCombo: "3" },
        { key: "nav4", label: "Geçmiş'e git", type: "nav", path: "/History", defaultCombo: "4" },
        { key: "nav5", label: "Ayarlar'a git", type: "nav", path: "/Settings", defaultCombo: "5" },
        { key: "nav6", label: "Bildirimler'e git", type: "nav", path: "/Notifications", defaultCombo: "6" },
        { key: "nav7", label: "Hakkında'ya git", type: "nav", path: "/About", defaultCombo: "7" },
        { key: "snapshot", label: "Anlık Görüntü Al (yalnızca Panel'de)", type: "click", targetId: "snapshot-share", defaultCombo: "ctrl+shift+s" }
    ];

    function defaults() {
        var result = {};
        ACTIONS.forEach(function (a) { result[a.key] = a.defaultCombo; });
        return result;
    }

    // Bir KeyboardEvent'i "ctrl+shift+s" gibi normalize edilmiş, karşılaştırılabilir bir metne çevirir.
    function comboFromEvent(e) {
        var parts = [];
        if (e.ctrlKey) parts.push("ctrl");
        if (e.altKey) parts.push("alt");
        if (e.shiftKey) parts.push("shift");
        if (e.metaKey) parts.push("meta");
        var key = e.key.toLowerCase();
        if (["control", "alt", "shift", "meta"].indexOf(key) === -1) parts.push(key === " " ? "space" : key);
        return parts.join("+");
    }

    // Geçersiz/çakışan kayıtları sessizce varsayılana döndürür; iki eylem aynı combo'yu paylaşamaz
    // (paylaşırsa hangisinin tetikleneceği belirsizleşir), bu yüzden ilk görülen kazanır, çakışan
    // ikinci eylem kendi varsayılanına döner.
    function sanitize(saved) {
        var result = defaults();
        if (!saved || typeof saved !== "object") return result;

        var usedCombos = {};
        ACTIONS.forEach(function (a) {
            var combo = saved[a.key];
            if (typeof combo === "string" && combo && !usedCombos[combo]) {
                result[a.key] = combo;
                usedCombos[combo] = a.key;
            } else {
                usedCombos[result[a.key]] = a.key;
            }
        });
        return result;
    }

    function load() {
        try { return sanitize(JSON.parse(localStorage.getItem(STORAGE_KEY))); } catch (e) { return defaults(); }
    }

    function save(state) {
        try { localStorage.setItem(STORAGE_KEY, JSON.stringify(state)); } catch (e) { /* kota aşımı/gizli sekme: yok say */ }
    }

    // Aynı combo'nun (varsa) hangi BAŞKA eyleme atanmış olduğunu döndürür — Ayarlar sayfasındaki
    // çakışma uyarısı için. excludeKey, düzenlenmekte olan eylemin kendisini hariç tutar.
    function findConflict(state, combo, excludeKey) {
        var conflictAction = null;
        ACTIONS.forEach(function (a) {
            if (a.key !== excludeKey && state[a.key] === combo) conflictAction = a;
        });
        return conflictAction;
    }

    window.addEventListener("storage", function (e) {
        if (e.key !== STORAGE_KEY) return;
        document.dispatchEvent(new CustomEvent("hwmon:shortcuts-changed"));
    });

    return {
        load: load, save: save, defaults: defaults, sanitize: sanitize,
        actions: ACTIONS, comboFromEvent: comboFromEvent, findConflict: findConflict, storageKey: STORAGE_KEY
    };
})();

// Klavye kısayollarını gerçekten tetikleyen dinleyici — her sayfada çalışır (site.js her yerde
// yüklü). Bir input/select/textarea/contenteditable odaklıyken devre dışı bırakılır ki forma yazı
// yazarken (ör. eşik değeri girerken "5" yazmak) sayfa değişmesin.
(function initKeyboardShortcuts() {
    "use strict";

    var shortcutsApi = window.HwmonShortcuts;
    if (!shortcutsApi) return;

    var current = shortcutsApi.load();
    document.addEventListener("hwmon:shortcuts-changed", function () { current = shortcutsApi.load(); });

    function isTypingContext(target) {
        if (!target) return false;
        var tag = target.tagName;
        return tag === "INPUT" || tag === "TEXTAREA" || tag === "SELECT" || target.isContentEditable;
    }

    document.addEventListener("keydown", function (e) {
        if (isTypingContext(e.target)) return;

        var combo = shortcutsApi.comboFromEvent(e);
        var action = shortcutsApi.actions.filter(function (a) { return current[a.key] === combo; })[0];
        if (!action) return;

        if (action.type === "nav") {
            e.preventDefault();
            window.location.href = action.path;
        } else if (action.type === "click") {
            var el = document.getElementById(action.targetId);
            if (el) {
                e.preventDefault();
                el.click();
            }
        }
    });
})();

(function () {
    "use strict";

    var STORAGE_KEY = "hwmon-theme";
    var button = document.getElementById("theme-toggle");
    if (!button) {
        return;
    }

    function applyIcon(theme) {
        button.textContent = theme === "light" ? "🌙" : "☀️";
        button.setAttribute("aria-label", theme === "light" ? "Karanlık moda geç" : "Aydınlık moda geç");
    }

    applyIcon(document.documentElement.getAttribute("data-theme") || "dark");

    button.addEventListener("click", function () {
        var current = document.documentElement.getAttribute("data-theme") === "light" ? "light" : "dark";
        var next = current === "light" ? "dark" : "light";
        document.documentElement.setAttribute("data-theme", next);
        try { localStorage.setItem(STORAGE_KEY, next); } catch (e) { /* kota aşımı/gizli sekme: yok say */ }
        applyIcon(next);
        document.dispatchEvent(new CustomEvent("hwmon:theme-changed", { detail: { theme: next } }));
    });
})();

// Programın açık kalma süresi — AppSession.StartedAtUtc, sayfa yüklenirken _Layout.cshtml
// tarafından data-started-at olarak gömülür; buradan itibaren saniyede bir client tarafında sayılır.
(function () {
    "use strict";

    var wrap = document.getElementById("app-uptime-value");
    if (!wrap) return;

    var container = wrap.closest(".app-uptime");
    var startedAt = Date.parse(container.getAttribute("data-started-at"));
    if (isNaN(startedAt)) return;

    function pad(n) {
        return n < 10 ? "0" + n : "" + n;
    }

    function tick() {
        var totalSeconds = Math.max(0, Math.floor((Date.now() - startedAt) / 1000));
        var days = Math.floor(totalSeconds / 86400);
        var hours = Math.floor((totalSeconds % 86400) / 3600);
        var minutes = Math.floor((totalSeconds % 3600) / 60);
        var seconds = totalSeconds % 60;
        var clock = pad(hours) + ":" + pad(minutes) + ":" + pad(seconds);
        wrap.textContent = days > 0 ? days + "g " + clock : clock;
    }

    tick();
    setInterval(tick, 1000);
})();

// Eşik bildirimleri — Panel sayfasındaki dashboard.js, hem tarayıcı izni "granted" hem de burada
// yönetilen uygulama-içi açık/kapalı tercihi true ise Notification API ile uyarı gönderir.
// Tarayıcı izni JS'ten geri alınamadığı için (yalnızca tarayıcı ayarlarından), "kapatma" burada
// ayrı bir localStorage bayrağıyla simüle edilir — zil ikonu buna göre 🔔/🔕 arasında değişir.
window.HwmonNotifyToggle = (function () {
    "use strict";

    var STORAGE_KEY = "hwmon-notify-enabled";

    function isEnabledPref() {
        try {
            var v = localStorage.getItem(STORAGE_KEY);
            return v === null ? true : v === "1";
        } catch (e) { return true; }
    }

    function setEnabledPref(value) {
        try { localStorage.setItem(STORAGE_KEY, value ? "1" : "0"); } catch (e) { /* gizli sekme: yok say */ }
    }

    function isActive() {
        return typeof Notification !== "undefined" && Notification.permission === "granted" && isEnabledPref();
    }

    return { isEnabledPref: isEnabledPref, setEnabledPref: setEnabledPref, isActive: isActive };
})();

// Bildirim ayarları — Ayarlar sayfasındaki "Bildirimler" bölümünden düzenlenir: eşik aşılı
// kaldığı sürece kaç saniyede bir tekrar bildirim gönderileceği (0 = yalnızca bir kez) ve hangi
// kartlar (İşlemci/Ekran Kartı/Bellek/Depolama) için bildirim gönderileceği. notifications.js
// (her sayfada yüklenir) bunu okuyup gerçek bildirimleri gönderir.
window.HwmonNotifySettings = (function () {
    "use strict";

    var STORAGE_KEY = "hwmon-notify-settings";
    var DEFAULTS = { repeatSeconds: 0, metrics: { cpu: true, gpu: true, ram: true, storage: true } };

    function sanitize(saved) {
        var result = { repeatSeconds: DEFAULTS.repeatSeconds, metrics: Object.assign({}, DEFAULTS.metrics) };
        if (saved && typeof saved === "object") {
            if (typeof saved.repeatSeconds === "number" && !isNaN(saved.repeatSeconds) && saved.repeatSeconds >= 0)
                result.repeatSeconds = saved.repeatSeconds;
            if (saved.metrics && typeof saved.metrics === "object") {
                Object.keys(DEFAULTS.metrics).forEach(function (key) {
                    if (typeof saved.metrics[key] === "boolean") result.metrics[key] = saved.metrics[key];
                });
            }
        }
        return result;
    }

    function load() {
        try {
            return sanitize(JSON.parse(localStorage.getItem(STORAGE_KEY)));
        } catch (e) {
            return sanitize(null);
        }
    }

    function save(settings) {
        try { localStorage.setItem(STORAGE_KEY, JSON.stringify(settings)); } catch (e) { /* kota aşımı/gizli sekme: yok say */ }
    }

    window.addEventListener("storage", function (e) {
        if (e.key !== STORAGE_KEY) return;
        document.dispatchEvent(new CustomEvent("hwmon:notify-settings-changed"));
    });

    return { load: load, save: save, sanitize: sanitize, defaults: DEFAULTS, storageKey: STORAGE_KEY };
})();

(function () {
    "use strict";

    var button = document.getElementById("notify-toggle");
    if (!button) return;

    var api = window.HwmonNotifyToggle;

    if (typeof Notification === "undefined") {
        button.disabled = true;
        button.textContent = "🔕";
        button.title = "Bu tarayıcı bildirimleri desteklemiyor";
        return;
    }

    function applyState() {
        var active = api.isActive();
        button.textContent = active ? "🔔" : "🔕";
        button.setAttribute("aria-pressed", active ? "true" : "false");
        if (Notification.permission === "denied") {
            button.title = "Bildirimlere izin verilmedi (tarayıcı ayarlarından değiştirebilirsiniz)";
        } else if (active) {
            button.title = "Eşik bildirimleri açık, kapatmak için tıklayın";
        } else if (Notification.permission === "granted") {
            button.title = "Bildirimler kapalı, açmak için tıklayın";
        } else {
            button.title = "Eşik aşıldığında bildirim almak için tıklayın";
        }
    }

    applyState();

    // Tarayıcı izni "denied" olduğunda tıklama JS'ten hiçbir şeyi değiştiremez (yalnızca tooltip
    // vardı, bu da fare üzerine gelmeden görünmüyordu — kullanıcıya "buton bozuk" hissi veriyordu).
    // Bunun yerine kısa süreliğine görünen bir ipucu balonu göstererek nedenini açıkça belirtiyoruz.
    var deniedHint = null;
    function showDeniedHint() {
        if (deniedHint) { clearTimeout(deniedHint.timer); deniedHint.el.remove(); }
        var el = document.createElement("div");
        el.className = "notify-denied-hint";
        el.textContent = "Bildirimler bu site için engellenmiş. Tarayıcının adres çubuğundaki kilit/site ayarları simgesinden izin vermeniz gerekiyor.";
        document.body.appendChild(el);
        var rect = button.getBoundingClientRect();
        el.style.top = (rect.bottom + 8) + "px";
        el.style.right = (window.innerWidth - rect.right) + "px";
        var timer = setTimeout(function () {
            el.remove();
            deniedHint = null;
        }, 6000);
        deniedHint = { el: el, timer: timer };
    }

    button.addEventListener("click", function () {
        if (Notification.permission === "denied") {
            applyState();
            showDeniedHint();
            return;
        }
        if (Notification.permission === "default") {
            Notification.requestPermission().then(function (result) {
                if (result === "granted") {
                    api.setEnabledPref(true);
                    document.dispatchEvent(new CustomEvent("hwmon:notify-enabled"));
                }
                applyState();
            });
            return;
        }
        var next = !api.isEnabledPref();
        api.setEnabledPref(next);
        applyState();
        if (next) document.dispatchEvent(new CustomEvent("hwmon:notify-enabled"));
    });
})();

// Ayarları dışa/içe aktarma — her modül kendi tercihini "hwmon-" önekiyle localStorage'a yazıyor
// (bu dosyadaki tüm window.Hwmon* modülleri); bu yüzden export TEK TEK alanları bilmek zorunda
// değil, yalnızca bu önekle başlayan anahtarları toplar. "hwmon-notification-history" İSTİSNA:
// bir tercih değil, geçmiş bir olay kaydı (veri) olduğu için ayar yedeğine dahil edilmez.
window.HwmonSettingsBackup = (function () {
    "use strict";

    var PREFIX = "hwmon-";
    var EXCLUDED_KEYS = ["hwmon-notification-history"];

    function collect() {
        var settings = {};
        for (var i = 0; i < localStorage.length; i++) {
            var key = localStorage.key(i);
            if (key && key.indexOf(PREFIX) === 0 && EXCLUDED_KEYS.indexOf(key) === -1) {
                settings[key] = localStorage.getItem(key);
            }
        }
        return settings;
    }

    function exportToFile() {
        var payload = {
            hwmonSettingsVersion: 1,
            exportedAtUtc: new Date().toISOString(),
            settings: collect()
        };
        var blob = new Blob([JSON.stringify(payload, null, 2)], { type: "application/json" });
        var url = URL.createObjectURL(blob);
        var a = document.createElement("a");
        a.href = url;
        a.download = "hardwaremonitor-ayarlar-" + new Date().toISOString().slice(0, 10) + ".json";
        document.body.appendChild(a);
        a.click();
        a.remove();
        URL.revokeObjectURL(url);
    }

    // Döner: { ok: true, count } veya { ok: false, error }. Yalnızca "hwmon-" önekiyle başlayan
    // anahtarlar yazılır — rastgele bir JSON dosyası localStorage'a keyfi anahtar enjekte edemez.
    function importFromText(text) {
        var parsed;
        try {
            parsed = JSON.parse(text);
        } catch (e) {
            return { ok: false, error: "Dosya geçerli bir JSON değil." };
        }

        if (!parsed || typeof parsed !== "object" || !parsed.settings || typeof parsed.settings !== "object")
            return { ok: false, error: "Dosya beklenen ayar yedeği biçiminde değil." };

        var count = 0;
        Object.keys(parsed.settings).forEach(function (key) {
            if (key.indexOf(PREFIX) !== 0 || EXCLUDED_KEYS.indexOf(key) !== -1) return;
            var value = parsed.settings[key];
            if (typeof value !== "string") return;
            try {
                localStorage.setItem(key, value);
                count++;
            } catch (e) { /* kota aşımı: bu anahtarı atla, devam et */ }
        });

        return { ok: true, count: count };
    }

    return { exportToFile: exportToFile, importFromText: importFromText };
})();
