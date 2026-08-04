// Eşik bildirimleri — her sayfada (Panel, Detaylar, Geçmiş, Ayarlar, ...) çalışır, kendi SignalR
// bağlantısıyla donanım verisini dinler; kullanıcı hangi sekmede olursa olsun eşik aşıldığında
// bildirim gönderilebilsin diye bu mantık dashboard.js'e değil, her sayfada yüklenen bu dosyaya
// taşındı. Panel sayfasındaki kart kırmızıya dönme/health-summary gibi görsel şeyler hâlâ
// dashboard.js'te; burada yalnızca "bildirim gönder" sorumluluğu var.
(function () {
    "use strict";

    if (typeof signalR === "undefined") return;

    var HwmonThresholds = window.HwmonThresholds;
    var HwmonNotifySettings = window.HwmonNotifySettings;
    var HwmonNotifyToggle = window.HwmonNotifyToggle;
    var HwmonNotificationHistory = window.HwmonNotificationHistory;
    if (!HwmonThresholds || !HwmonNotifySettings || !HwmonNotifyToggle) return;

    var WARNING_THRESHOLDS = HwmonThresholds.load();
    var NOTIFY_SETTINGS = HwmonNotifySettings.load();

    // key -> { isWarning: bool, lastNotifiedAt: epoch ms }
    var trackedState = {};

    document.addEventListener("hwmon:thresholds-changed", function () {
        WARNING_THRESHOLDS = HwmonThresholds.load();
    });

    document.addEventListener("hwmon:notify-settings-changed", function () {
        NOTIFY_SETTINGS = HwmonNotifySettings.load();
    });

    // Bir metrik zaten eşiği aşmışken bildirimler açılırsa, izleme sıfırlanır ki bir sonraki
    // güncellemede "yeni" bir yükseliş gibi algılanıp hemen bildirim gitsin (sekme değiştirmeye
    // gerek kalmadan).
    document.addEventListener("hwmon:notify-enabled", function () {
        trackedState = {};
    });

    var fmt = window.HwmonFormat.fmt;

    // Bildirim, "normal -> eşik üstü" geçişinde her zaman bir kez gönderilir. Ayarlar sayfasında
    // bir tekrar sıklığı seçildiyse (repeatSeconds > 0), eşik aşılı kaldığı sürece o sıklıkla
    // tekrar tekrar da gönderilir. Geçmişe kayıt, tarayıcı bildirim izni verilmemiş olsa bile
    // düşülür (Bildirimler sayfası).
    function maybeNotify(key, isWarning, title, body) {
        var state = trackedState[key] || (trackedState[key] = { isWarning: false, lastNotifiedAt: 0 });
        var wasWarning = state.isWarning;
        state.isWarning = !!isWarning;
        if (!isWarning) return;

        if (NOTIFY_SETTINGS.metrics[key] === false) return;

        var now = Date.now();
        var isRisingEdge = !wasWarning;
        var repeatDue = NOTIFY_SETTINGS.repeatSeconds > 0 && (now - state.lastNotifiedAt) >= NOTIFY_SETTINGS.repeatSeconds * 1000;
        if (!isRisingEdge && !repeatDue) return;

        state.lastNotifiedAt = now;
        if (HwmonNotificationHistory) HwmonNotificationHistory.record({ key: key, title: title, body: body });
        if (HwmonNotifyToggle.isActive()) {
            new Notification(title, { body: body });
            if (window.HwmonNotifySound) window.HwmonNotifySound.play();
        }
    }

    if (!window.HwmonHub) return;
    var connection = window.HwmonHub.get();

    connection.on("hardwareUpdate", function (message) {
        if (message.cpu) {
            var cpuMetric = HwmonThresholds.metrics.cpu[WARNING_THRESHOLDS.cpu.metric];
            var cpuValue = message.cpu[cpuMetric.field];
            var cpuWarning = typeof cpuValue === "number" && cpuValue > WARNING_THRESHOLDS.cpu.value;
            maybeNotify("cpu", cpuWarning, "İşlemci " + cpuMetric.label.toLowerCase() + " yüksek",
                cpuMetric.label + " " + fmt(cpuValue, cpuMetric.unit, 1) + " (eşik " + WARNING_THRESHOLDS.cpu.value + " " + cpuMetric.unit + ")");
        }

        if (message.gpu) {
            var gpuMetric = HwmonThresholds.metrics.gpu[WARNING_THRESHOLDS.gpu.metric];
            var gpuValue = message.gpu[gpuMetric.field];
            var gpuWarning = typeof gpuValue === "number" && gpuValue > WARNING_THRESHOLDS.gpu.value;
            maybeNotify("gpu", gpuWarning, "Ekran kartı " + gpuMetric.label.toLowerCase() + " yüksek",
                gpuMetric.label + " " + fmt(gpuValue, gpuMetric.unit, 1) + " (eşik " + WARNING_THRESHOLDS.gpu.value + " " + gpuMetric.unit + ")");
        }

        if (message.ram) {
            var ramWarning = typeof message.ram.usedPercent === "number" && message.ram.usedPercent > WARNING_THRESHOLDS.ramUsedPercent;
            maybeNotify("ram", ramWarning, "Bellek kullanımı yüksek",
                "Kullanım " + fmt(message.ram.usedPercent, "%", 1) + " (eşik " + WARNING_THRESHOLDS.ramUsedPercent + " %)");
        }

        var storages = Array.isArray(message.storages) ? message.storages : [];
        var maxDiskTemp = storages.reduce(function (max, s) {
            return typeof s.temperatureC === "number" && s.temperatureC > max ? s.temperatureC : max;
        }, -Infinity);
        // dashboard.js'teki aynı hesaplamayla tutarlı: disk yoksa (-Infinity) hiçbir zaman uyarı
        // tetiklenmemesi zaten garanti (-Infinity hiçbir eşiği geçemez), ama burada da aynı açık
        // kontrol kullanılır ki "-Infinity °C" gibi anlamsız bir dize hiç hesaplanmasın.
        var diskWarning = maxDiskTemp !== -Infinity && maxDiskTemp > WARNING_THRESHOLDS.diskTempC;
        if (diskWarning) {
            maybeNotify("storage", diskWarning, "Disk sıcaklığı yüksek",
                "En yüksek disk sıcaklığı " + maxDiskTemp.toFixed(1) + " °C (eşik " + WARNING_THRESHOLDS.diskTempC + " °C)");
        } else {
            maybeNotify("storage", false, "", "");
        }
    });

    window.HwmonHub.start().catch(function (err) {
        console.error("Bildirim izleme bağlantısı kurulamadı", err);
    });
})();
