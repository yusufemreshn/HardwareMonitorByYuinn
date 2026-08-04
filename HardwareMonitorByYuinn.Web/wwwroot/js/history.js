// Geçmiş sayfası da Ayarlar'daki gibi sol kategori/alt-bölüm gezinmesine bölündü (bkz.
// window.HwmonSectionNav, site.js) — aşağıdaki her IIFE kendi panelinin gizli olup olmamasından
// bağımsız olarak normal şekilde kurulmaya devam eder ("hidden" yalnızca görünürlüğü değiştirir).
(function initHistoryNav() {
    "use strict";
    // Sidebar'ın mouse uzaklaşınca otomatik daralması yalnızca "Kayıtları Görüntüle" paneli
    // açıkken çalışır (kullanıcı isteğiyle) — o panelin geniş tablosu için yer açmak amacıyla
    // eklendi, diğer Geçmiş sekmelerinde/sayfalarda sidebar her zaman tam genişlikte kalır.
    if (window.HwmonSectionNav) window.HwmonSectionNav.init("history-sidebar", "history-content", "history-rows-panel");
})();

// "Kayıt Bilgileri" (samples) ve Process Geçmişi'nin yeni "Kayıt Bilgileri" panelindeki "eski
// kayıtları temizle" kontrolleri — paylaşılan uygulama site.js'teki window.HwmonRecordCleanup.
(function initCleanupControls() {
    "use strict";
    if (!window.HwmonRecordCleanup) return;

    window.HwmonRecordCleanup.wire({
        selectId: "history-cleanup-days",
        buttonId: "history-cleanup-btn",
        tokenSelector: "#history-summary-panel input[name='__RequestVerificationToken']",
        endpoint: "/History/DeleteOldestSamples",
        nounLabel: "kayıt"
    });

    window.HwmonRecordCleanup.wire({
        selectId: "process-history-cleanup-days",
        buttonId: "process-history-cleanup-btn",
        tokenSelector: "#process-history-summary-panel input[name='__RequestVerificationToken']",
        endpoint: "/History/DeleteOldestProcessSamples",
        nounLabel: "kayıt"
    });
})();

(function () {
    "use strict";

    var form = document.getElementById("history-filter-form");
    var fromInput = document.getElementById("history-filter-from");
    var toInput = document.getElementById("history-filter-to");
    var body = document.getElementById("history-rows-body");
    var note = document.getElementById("history-rows-note");
    if (!form || !fromInput || !toInput || !body) return;

    var fmt = window.HwmonFormat.fmt;

    function toMb(bytesPerSec) {
        return typeof bytesPerSec === "number" ? bytesPerSec / (1024 * 1024) : null;
    }

    function cell(text) {
        var td = document.createElement("td");
        td.textContent = text;
        return td;
    }

    function renderRows(rows) {
        body.textContent = "";

        if (rows.length === 0) {
            var empty = document.createElement("tr");
            var td = document.createElement("td");
            td.colSpan = 13;
            td.className = "stat-sub";
            td.textContent = "Bu aralıkta kayıt yok.";
            empty.appendChild(td);
            body.appendChild(empty);
            return;
        }

        // En yeni kayıt en üstte görünsün diye ters sırada basılır (sunucu eskiden yeniye döndürür).
        rows.slice().reverse().forEach(function (r) {
            var tr = document.createElement("tr");
            tr.appendChild(cell(new Date(r.zaman).toLocaleString("tr-TR")));
            tr.appendChild(cell(fmt(r.cpuUsagePercent, "%", 1)));
            tr.appendChild(cell(fmt(r.cpuClockMhz, "MHz", 0)));
            tr.appendChild(cell(fmt(r.cpuPowerWatts, "W", 1)));
            tr.appendChild(cell(fmt(r.cpuTemperatureC, "°C", 1)));
            tr.appendChild(cell(fmt(r.gpuUsagePercent, "%", 1)));
            tr.appendChild(cell(fmt(r.gpuCoreClockMhz, "MHz", 0)));
            tr.appendChild(cell(fmt(r.gpuCoreTemperatureC, "°C", 1)));
            tr.appendChild(cell(fmt(r.gpuPowerWatts, "W", 1)));
            tr.appendChild(cell(fmt(r.ramUsagePercent, "%", 1)));
            tr.appendChild(cell(fmt(r.fpsValue, "", 1).trim()));
            tr.appendChild(cell(fmt(toMb(r.networkDownloadBytesPerSec), "MB/s", 2)));
            tr.appendChild(cell(fmt(toMb(r.networkUploadBytesPerSec), "MB/s", 2)));
            body.appendChild(tr);
        });
    }

    var loadSequence = 0;

    function load() {
        var requestId = ++loadSequence;
        var params = new URLSearchParams({ from: fromInput.value, to: toInput.value });

        body.innerHTML = "";
        var loadingRow = document.createElement("tr");
        var loadingCell = document.createElement("td");
        loadingCell.colSpan = 13;
        loadingCell.className = "stat-sub";
        loadingCell.textContent = "Yükleniyor…";
        loadingRow.appendChild(loadingCell);
        body.appendChild(loadingRow);
        note.style.display = "none";

        fetch("/History/Rows?" + params.toString())
            .then(function (res) { return res.json(); })
            .then(function (data) {
                if (requestId !== loadSequence) return; // daha yeni bir istek zaten sonuçlandı

                renderRows(data.rows);
                if (data.truncated) {
                    note.style.display = "block";
                    note.textContent = "Bu aralıkta " + data.totalCount + " kayıt var, yalnızca ilk " + data.rows.length + " tanesi gösteriliyor. Tamamı için Dışa Aktar'ı kullanın.";
                }
            })
            .catch(function () {
                if (requestId !== loadSequence) return;

                body.innerHTML = "";
                var errorRow = document.createElement("tr");
                var errorCell = document.createElement("td");
                errorCell.colSpan = 13;
                errorCell.className = "stat-sub";
                errorCell.textContent = "Kayıtlar okunamadı.";
                errorRow.appendChild(errorCell);
                body.appendChild(errorRow);
            });
    }

    function showFormError(message) {
        body.innerHTML = "";
        var errorRow = document.createElement("tr");
        var errorCell = document.createElement("td");
        errorCell.colSpan = 13;
        errorCell.className = "stat-sub";
        errorCell.textContent = message;
        errorRow.appendChild(errorCell);
        body.appendChild(errorRow);
    }

    form.addEventListener("submit", function (e) {
        e.preventDefault();
        if (!fromInput.value || !toInput.value) {
            showFormError("Başlangıç ve bitiş tarihi gerekli.");
            return;
        }
        if (toInput.value < fromInput.value) {
            showFormError("Bitiş tarihi başlangıçtan önce olamaz.");
            return;
        }
        load();
    });

    // Dışa Aktar, "Kayıtları Görüntüle" formundaki (kullanıcı tarafından değiştirilebilen) aynı
    // Başlangıç/Bitiş aralığını kullanır; ayrı bir form/panel gerekmez. Content-Disposition:
    // attachment döndüğü için location.href sayfadan ayrılmadan dosya indirmeyi tetikler.
    var exportBtn = document.getElementById("history-export-btn");
    var formatSelect = document.getElementById("history-export-format");
    if (exportBtn && formatSelect) {
        exportBtn.addEventListener("click", function () {
            var params = new URLSearchParams({ from: fromInput.value, to: toInput.value, format: formatSelect.value });
            window.location.href = "/History/Export?" + params.toString();
        });
    }

    // Tarih kutuları sayfa açılışında en son kaydedilen 15 dakika ile dolu gelir (bkz.
    // HistoryController.Index); bu yüzden ilk gösterim de doğrudan bu değerlerle yapılır.
    load();
})();

// Sistem Sağlığı Trendi — puan burada (sunucuda değil) hesaplanır çünkü eşikler yalnızca
// tarayıcıda (localStorage, HwmonThresholds) tutulur; sunucu yalnızca günlük CPU/GPU/RAM
// ortalamalarını döndürür (bkz. HistoryController.HealthTrend). Kalıcı geçmiş disk sıcaklığı
// tutmadığından puan yalnızca CPU/GPU/RAM'e dayanır — Panel'deki canlı puandan (disk/SMART de
// dahil) bu yüzden biraz farklı olabilir, bu kasıtlı ve dokümante edilmiş bir sınırlama.
(function initHealthTrend() {
    "use strict";

    var select = document.getElementById("health-trend-days");
    var body = document.getElementById("health-trend-body");
    if (!select || !body || !window.HwmonThresholds) return;

    // Puanlama formülü artık site.js'teki tek kaynakta (HwmonThresholds.computeMetricHealthScore) —
    // eskiden burada, report.js'te ve dashboard.js'te üç kez bağımsız kopyalanmıştı.
    function scoreForDay(day, thresholds) {
        var cpuValue = thresholds.cpu.metric === "usage" ? day.avgCpuUsage : day.avgCpuTemp;
        var gpuValue = thresholds.gpu.metric === "usage" ? day.avgGpuUsage : day.avgGpuTemp;
        return window.HwmonThresholds.computeMetricHealthScore(thresholds, cpuValue, gpuValue, day.avgRamUsage);
    }

    function render(days) {
        body.innerHTML = '<tr><td colspan="3" class="stat-sub">Yükleniyor…</td></tr>';
        var thresholds = window.HwmonThresholds.load();

        fetch("/History/HealthTrend?days=" + days)
            .then(function (r) { return r.json(); })
            .then(function (daily) {
                body.innerHTML = "";
                if (daily.length === 0) {
                    body.innerHTML = '<tr><td colspan="3" class="stat-sub">Bu aralıkta kayıt yok.</td></tr>';
                    return;
                }

                daily.forEach(function (day) {
                    var score = scoreForDay(day, thresholds);
                    var tr = document.createElement("tr");

                    var dateTd = document.createElement("td");
                    var d = new Date(day.date + "T00:00:00");
                    dateTd.textContent = d.toLocaleDateString("tr-TR", { day: "2-digit", month: "2-digit", year: "numeric" });
                    tr.appendChild(dateTd);

                    var scoreTd = document.createElement("td");
                    scoreTd.textContent = score + "/100";
                    scoreTd.style.fontWeight = "700";
                    scoreTd.style.color = score >= 80 ? "var(--accent-network)" : score >= 50 ? "var(--accent-caution)" : "var(--accent-danger)";
                    tr.appendChild(scoreTd);

                    var countTd = document.createElement("td");
                    countTd.className = "stat-sub";
                    countTd.textContent = day.sampleCount + " örnek";
                    tr.appendChild(countTd);

                    body.appendChild(tr);
                });
            })
            .catch(function () {
                body.innerHTML = '<tr><td colspan="3" class="stat-sub">Trend okunamadı.</td></tr>';
            });
    }

    select.addEventListener("change", function () { render(Number(select.value)); });
    render(Number(select.value));
})();

// Process Geçmişi — bir process seçilip seçilen aralıktaki dakikalık CPU/RAM ortalamaları
// listelenir (bkz. HistoryController.ProcessNames/ProcessHistory, SqliteHistoryStore
// process_samples tablosu).
(function initProcessHistory() {
    "use strict";

    var nameSelect = document.getElementById("process-history-name");
    var daysSelect = document.getElementById("process-history-days");
    var body = document.getElementById("process-history-body");
    var note = document.getElementById("process-history-note");
    if (!nameSelect || !daysSelect || !body) return;

    function fmtNum(value, digits) {
        return typeof value === "number" ? value.toFixed(digits) : "--";
    }

    // Not: bu dosyanın en üstteki IIFE'sindeki `cell()` yardımcısı bu kapsamdan erişilemez
    // (ayrı bir fonksiyon kapsamı) — burada kendi kopyası tanımlanır.
    function cell(text) {
        var td = document.createElement("td");
        td.textContent = text;
        return td;
    }

    function loadHistory() {
        var name = nameSelect.value;
        if (!name) {
            body.innerHTML = '<tr><td colspan="3" class="stat-sub">Bir process seçin.</td></tr>';
            return;
        }

        body.innerHTML = '<tr><td colspan="3" class="stat-sub">Yükleniyor…</td></tr>';
        if (note) note.style.display = "none";
        var params = new URLSearchParams({ processName: name, days: daysSelect.value });

        fetch("/History/ProcessHistory?" + params.toString())
            .then(function (r) { return r.json(); })
            .then(function (data) {
                var rows = data.rows || [];
                body.innerHTML = "";
                if (rows.length === 0) {
                    body.innerHTML = '<tr><td colspan="3" class="stat-sub">Bu aralıkta kayıt yok.</td></tr>';
                    return;
                }
                // Sunucu, aralık MaxDisplayRows'u aşarsa en YENİ kayıtları döner (bkz.
                // HistoryController.ProcessHistory) — burada kullanıcıya bunun neden "son N gün"ün
                // TAMAMI olmadığını açıklayan bir not göstermek gerekiyor, aksi halde eskiden olduğu
                // gibi sessizce eksik/yanlış uçtan veri gösterilmiş izlenimi doğardı.
                if (data.truncated && note) {
                    note.style.display = "block";
                    note.textContent = "Bu aralıkta " + data.totalCount + " kayıt var, yalnızca en yeni " + rows.length + " tanesi gösteriliyor.";
                }
                // En yeni en üstte gösterilir (tablo doğal olarak eskiden yeniye geliyordu).
                rows.slice().reverse().forEach(function (row) {
                    var tr = document.createElement("tr");
                    var d = new Date(row.timeLocal);
                    tr.appendChild(cell(d.toLocaleString("tr-TR", { day: "2-digit", month: "2-digit", year: "numeric", hour: "2-digit", minute: "2-digit" })));
                    tr.appendChild(cell(fmtNum(row.avgCpuPercent, 1)));
                    tr.appendChild(cell(fmtNum(row.avgRamMb, 0)));
                    body.appendChild(tr);
                });
            })
            .catch(function () {
                body.innerHTML = '<tr><td colspan="3" class="stat-sub">Geçmiş okunamadı.</td></tr>';
            });
    }

    fetch("/History/ProcessNames")
        .then(function (r) { return r.json(); })
        .then(function (names) {
            nameSelect.innerHTML = '<option value="">Bir process seçin…</option>';
            names.forEach(function (name) {
                var option = document.createElement("option");
                option.value = name;
                option.textContent = name;
                nameSelect.appendChild(option);
            });
        })
        .catch(function () {
            nameSelect.innerHTML = '<option value="">Process listesi okunamadı</option>';
        });

    nameSelect.addEventListener("change", loadHistory);
    daysSelect.addEventListener("change", loadHistory);
})();

// Sistem Olayı Zaman Çizelgesi — Windows Olay Günlüğü'nden okunan olayları listeler (bkz.
// HistoryController.SystemEvents, SystemEventReader — SQLite'a kaydedilmez, canlı okunur).
(function initSystemEvents() {
    "use strict";

    var daysSelect = document.getElementById("system-events-days");
    var body = document.getElementById("system-events-body");
    var typeChecks = document.querySelectorAll(".system-events-type-check");
    if (!daysSelect || !body) return;

    var TYPE_ICONS = {
        "Başlatma": "🟢", "Kapatma": "🔴", "Uykuya Geçiş": "💤", "Uyanma": "☀",
        "Güncelleme Yüklendi": "🔄", "Kullanıcı İsteğiyle Kapatma/Yeniden Başlatma": "⏻"
    };

    // Son fetch edilen olaylar burada tutulur — tip filtresi değiştiğinde sunucuya tekrar
    // gitmeden, yalnızca zaten indirilmiş listeyi yeniden süzüp çizeriz.
    var lastEvents = [];

    function cell(text) {
        var td = document.createElement("td");
        td.textContent = text;
        return td;
    }

    function checkedTypes() {
        var types = [];
        typeChecks.forEach(function (cb) { if (cb.checked) types.push(cb.value); });
        return types;
    }

    function renderRows() {
        var allowed = checkedTypes();
        var filtered = lastEvents.filter(function (evt) { return allowed.indexOf(evt.type) !== -1; });

        body.innerHTML = "";
        if (filtered.length === 0) {
            body.innerHTML = '<tr><td colspan="3" class="stat-sub">Bu aralıkta/filtrede olay bulunamadı.</td></tr>';
            return;
        }
        filtered.forEach(function (evt) {
            var tr = document.createElement("tr");
            var d = new Date(evt.timestamp);
            tr.appendChild(cell(d.toLocaleString("tr-TR", { day: "2-digit", month: "2-digit", year: "numeric", hour: "2-digit", minute: "2-digit" })));
            tr.appendChild(cell((TYPE_ICONS[evt.type] || "•") + " " + evt.type));
            tr.appendChild(cell(evt.description));
            body.appendChild(tr);
        });
    }

    function render(days) {
        body.innerHTML = '<tr><td colspan="3" class="stat-sub">Yükleniyor…</td></tr>';

        fetch("/History/SystemEvents?days=" + days)
            .then(function (r) { return r.json(); })
            .then(function (events) {
                lastEvents = events;
                renderRows();
            })
            .catch(function () {
                body.innerHTML = '<tr><td colspan="3" class="stat-sub">Olaylar okunamadı.</td></tr>';
            });
    }

    daysSelect.addEventListener("change", function () { render(Number(daysSelect.value)); });
    typeChecks.forEach(function (cb) { cb.addEventListener("change", renderRows); });
    render(Number(daysSelect.value));
})();
