// Karşılaştırma sayfası da Ayarlar/Geçmiş gibi sol kategori/alt-bölüm gezinmesine bölündü (bkz.
// window.HwmonSectionNav, site.js).
(function initComparisonNav() {
    "use strict";
    if (window.HwmonSectionNav) window.HwmonSectionNav.init("comparison-sidebar", "comparison-content");
})();

(function () {
    "use strict";

    var currentReport = null;
    var loadedReport = null;

    var fmt = window.HwmonFormat.fmt;

    function metricRows() {
        return [
            { label: "İşlemci Kullanımı", path: "cpu.usagePercent", unit: "%", digits: 1 },
            { label: "İşlemci Frekansı", path: "cpu.clockMhz", unit: "MHz", digits: 0 },
            { label: "İşlemci Güç Tüketimi", path: "cpu.powerWatts", unit: "W", digits: 1 },
            { label: "İşlemci Sıcaklığı", path: "cpu.temperatureC", unit: "°C", digits: 1 },
            { label: "Ekran Kartı Kullanımı", path: "gpu.usagePercent", unit: "%", digits: 1 },
            { label: "Ekran Kartı Frekansı", path: "gpu.coreClockMhz", unit: "MHz", digits: 0 },
            { label: "Ekran Kartı Normal Sıcaklığı", path: "gpu.coreTemperatureC", unit: "°C", digits: 1 },
            { label: "Ekran Kartı Hotspot Sıcaklığı", path: "gpu.hotSpotTemperatureC", unit: "°C", digits: 1 },
            { label: "Ekran Kartı Güç Tüketimi", path: "gpu.powerWatts", unit: "W", digits: 1 },
            { label: "Bellek Kullanımı", path: "ram.usagePercent", unit: "%", digits: 1 },
            { label: "Kare Hızı", path: "fps.framesPerSecond", unit: "FPS", digits: 1 },
            { label: "Kare Hızı %1 Low", path: "fps.low1Percent", unit: "FPS", digits: 1 },
            { label: "Kare Hızı %0.1 Low", path: "fps.lowPoint1Percent", unit: "FPS", digits: 1 }
        ];
    }

    function getByPath(obj, path) {
        return path.split(".").reduce(function (acc, key) {
            return acc && acc[key] !== undefined ? acc[key] : null;
        }, obj);
    }

    function cell(text) {
        var td = document.createElement("td");
        td.textContent = text;
        return td;
    }

    function renderComparison() {
        var body = document.getElementById("comparison-body");
        body.textContent = "";

        if (!currentReport) {
            var loadingRow = document.createElement("tr");
            var loadingCell = document.createElement("td");
            loadingCell.colSpan = 4;
            loadingCell.style.color = "var(--text-muted)";
            loadingCell.textContent = "Şu anki oturum verileri yükleniyor...";
            loadingRow.appendChild(loadingCell);
            body.appendChild(loadingRow);
            return;
        }

        metricRows().forEach(function (row) {
            var currentAvg = getByPath(currentReport.averages, row.path);
            var currentValue = currentAvg ? currentAvg.lifetime : null;
            var loadedAvg = loadedReport ? getByPath(loadedReport.averages, row.path) : null;
            var loadedValue = loadedAvg ? loadedAvg.lifetime : null;

            var deltaText = "--";
            if (typeof currentValue === "number" && typeof loadedValue === "number") {
                var delta = currentValue - loadedValue;
                var arrow = delta > 0 ? "▲" : (delta < 0 ? "▼" : "―");
                deltaText = arrow + " " + Math.abs(delta).toFixed(row.digits) + " " + row.unit;
            }

            var tr = document.createElement("tr");
            tr.appendChild(cell(row.label));
            tr.appendChild(cell(fmt(currentValue, row.unit, row.digits)));
            tr.appendChild(cell(fmt(loadedValue, row.unit, row.digits)));
            tr.appendChild(cell(deltaText));
            body.appendChild(tr);
        });

        document.getElementById("current-session-label").textContent =
            "Bu Oturum" + (currentReport.machineName ? " (" + currentReport.machineName + ")" : "");
        document.getElementById("loaded-session-label").textContent = loadedReport
            ? "Yüklenen" + (loadedReport.exportedAtUtc ? " (" + new Date(loadedReport.exportedAtUtc).toLocaleString("tr-TR") + ")" : "")
            : "Yüklenen Kayıt";
    }

    function refreshCurrent() {
        fetch("/Comparison/Current")
            .then(function (r) { return r.json(); })
            .then(function (data) {
                currentReport = data;
                renderComparison();
            })
            .catch(function (err) { console.error("Şu anki oturum verileri alınamadı", err); });
    }

    refreshCurrent();
    setInterval(refreshCurrent, 5000);

    // .txt raporu tamamen tarayıcıda ayrıştırılır; dosya içeriği hiçbir zaman sunucuya gönderilmez.
    function parseComparisonText(text) {
        var lines = text.split(/\r?\n/);
        var machineName = null, cpuName = null, gpuName = null, sessionStartedAtUtc = null, exportedAtUtc = null;
        var sections = {};
        var currentSection = null;

        lines.forEach(function (rawLine) {
            var line = rawLine.trim();
            if (!line || line.indexOf("#") === 0) {
                return;
            }

            if (line.charAt(0) === "[" && line.charAt(line.length - 1) === "]") {
                currentSection = line.slice(1, -1);
                sections[currentSection] = {};
                return;
            }

            var eq = line.indexOf("=");
            if (eq < 0) {
                return;
            }

            var key = line.slice(0, eq);
            var rawValue = line.slice(eq + 1);

            if (currentSection === null) {
                if (key === "MachineName") machineName = rawValue;
                else if (key === "CpuName") cpuName = rawValue;
                else if (key === "GpuName") gpuName = rawValue;
                else if (key === "SessionStartedAtUtc") sessionStartedAtUtc = rawValue;
                else if (key === "ExportedAtUtc") exportedAtUtc = rawValue;
            } else {
                var value = rawValue === "" ? null : parseFloat(rawValue);
                sections[currentSection][key] = (value === null || Number.isNaN(value)) ? null : value;
            }
        });

        function readSection(name) {
            var s = sections[name] || {};
            return {
                last1Min: s.Last1Min === undefined ? null : s.Last1Min,
                last2Min: s.Last2Min === undefined ? null : s.Last2Min,
                last5Min: s.Last5Min === undefined ? null : s.Last5Min,
                last10Min: s.Last10Min === undefined ? null : s.Last10Min,
                last15Min: s.Last15Min === undefined ? null : s.Last15Min,
                lifetime: s.Lifetime === undefined ? null : s.Lifetime
            };
        }

        var coreIndexes = [];
        Object.keys(sections).forEach(function (key) {
            var m = /^Cpu\.Core\.(\d+)\./.exec(key);
            if (m) {
                var idx = parseInt(m[1], 10);
                if (coreIndexes.indexOf(idx) < 0) {
                    coreIndexes.push(idx);
                }
            }
        });
        coreIndexes.sort(function (a, b) { return a - b; });

        var cores = coreIndexes.map(function (i) {
            return {
                coreIndex: i,
                clockMhz: readSection("Cpu.Core." + i + ".ClockMhz"),
                loadPercent: readSection("Cpu.Core." + i + ".LoadPercent"),
                powerWatts: readSection("Cpu.Core." + i + ".PowerWatts")
            };
        });

        return {
            machineName: machineName || "Bilinmeyen",
            cpuName: cpuName || "Bilinmeyen",
            gpuName: gpuName || "Bilinmeyen",
            sessionStartedAtUtc: sessionStartedAtUtc,
            exportedAtUtc: exportedAtUtc,
            averages: {
                cpu: {
                    usagePercent: readSection("Cpu.UsagePercent"),
                    clockMhz: readSection("Cpu.ClockMhz"),
                    powerWatts: readSection("Cpu.PowerWatts"),
                    temperatureC: readSection("Cpu.TemperatureC"),
                    cores: cores
                },
                gpu: {
                    usagePercent: readSection("Gpu.UsagePercent"),
                    coreClockMhz: readSection("Gpu.CoreClockMhz"),
                    coreTemperatureC: readSection("Gpu.CoreTemperatureC"),
                    hotSpotTemperatureC: readSection("Gpu.HotSpotTemperatureC"),
                    powerWatts: readSection("Gpu.PowerWatts")
                },
                ram: {
                    usagePercent: readSection("Ram.UsagePercent")
                },
                fps: {
                    framesPerSecond: readSection("Fps.FramesPerSecond"),
                    low1Percent: readSection("Fps.Low1Percent"),
                    lowPoint1Percent: readSection("Fps.LowPoint1Percent")
                }
            }
        };
    }

    var fileInput = document.getElementById("load-file-input");
    var fileInfo = document.getElementById("load-file-info");
    var fileTrigger = document.getElementById("load-file-trigger");
    if (fileTrigger && fileInput) {
        fileTrigger.addEventListener("click", function () { fileInput.click(); });
    }
    if (fileInput) {
        fileInput.addEventListener("change", function () {
            var file = fileInput.files[0];
            if (!file) {
                return;
            }

            if (file.size > 200 * 1024) {
                fileInfo.textContent = "Dosya çok büyük görünüyor, bu bir HardwareMonitorByYuinn raporu olmayabilir.";
                return;
            }

            var reader = new FileReader();
            reader.onload = function () {
                try {
                    loadedReport = parseComparisonText(String(reader.result));
                    fileInfo.textContent = "Yüklendi: " + file.name;
                    renderComparison();
                } catch (err) {
                    console.error("Dosya ayrıştırılamadı", err);
                    fileInfo.textContent = "Dosya okunamadı, geçerli bir HardwareMonitorByYuinn raporu olduğundan emin olun.";
                }
            };
            reader.readAsText(file);
        });
    }
})();

// İki Zaman Aralığını Karşılaştır — Kalıcı Geçmiş'teki (HistoryController.RangeAverage) iki
// aralığın ortalamalarını yan yana gösterir; oyun bazlı olmayan, genel bir karşılaştırma.
(function initRangeCompare() {
    "use strict";

    var button = document.getElementById("range-compare-btn");
    var body = document.getElementById("range-compare-body");
    if (!button || !body) return;

    var METRICS = [
        { label: "İşlemci Kullanımı", key: "avgCpuUsage", unit: "%", digits: 1 },
        { label: "İşlemci Sıcaklığı", key: "avgCpuTemp", unit: "°C", digits: 1 },
        { label: "İşlemci Güç Tüketimi", key: "avgCpuPower", unit: "W", digits: 1 },
        { label: "Ekran Kartı Kullanımı", key: "avgGpuUsage", unit: "%", digits: 1 },
        { label: "Ekran Kartı Sıcaklığı", key: "avgGpuTemp", unit: "°C", digits: 1 },
        { label: "Ekran Kartı Güç Tüketimi", key: "avgGpuPower", unit: "W", digits: 1 },
        { label: "Bellek Kullanımı", key: "avgRamUsage", unit: "%", digits: 1 },
        { label: "Kare Hızı", key: "avgFps", unit: "FPS", digits: 1 }
    ];

    function cell(text) {
        var td = document.createElement("td");
        td.textContent = text;
        return td;
    }

    var fmt = window.HwmonFormat.fmt;

    // Bir FARKI (delta) birim dönüştürürken °C→°F'nin +32 ofseti uygulanamaz (5°C'lik bir fark
    // 9°F'liktir, 41°F değil) — bu yüzden "Fark" sütunu ayrı, ofsetsiz bir dönüştürme kullanır.
    function fmtDiff(diff, unit, digits) {
        if (typeof diff !== "number" || isNaN(diff)) return "--";
        var prefs = window.HwmonUnitPreferences ? window.HwmonUnitPreferences.load() : null;
        var scaled = diff, outUnit = unit;
        if (unit === "°C" && prefs && prefs.temp === "F") { scaled = diff * 9 / 5; outUnit = "°F"; }
        else if (unit === "MB/s" && prefs && prefs.speed === "Mbps") { scaled = diff * 8; outUnit = "Mbps"; }
        var sign = scaled > 0 ? "+" : "";
        return sign + scaled.toFixed(digits) + " " + outUnit;
    }

    function fetchRangeAverage(fromEl, toEl) {
        var params = new URLSearchParams({ from: fromEl.value, to: toEl.value });
        return fetch("/History/RangeAverage?" + params.toString()).then(function (r) { return r.json(); });
    }

    button.addEventListener("click", function () {
        var fromA = document.getElementById("range-a-from");
        var toA = document.getElementById("range-a-to");
        var fromB = document.getElementById("range-b-from");
        var toB = document.getElementById("range-b-to");

        if (!fromA.value || !toA.value || !fromB.value || !toB.value) return;

        if (toA.value < fromA.value || toB.value < fromB.value) {
            body.innerHTML = '<tr><td colspan="4" class="stat-sub">Bitiş tarihi başlangıçtan önce olamaz.</td></tr>';
            return;
        }

        body.innerHTML = '<tr><td colspan="4" class="stat-sub">Yükleniyor…</td></tr>';
        document.getElementById("range-a-label").textContent = "Aralık A (" + fromA.value.replace("T", " ") + ")";
        document.getElementById("range-b-label").textContent = "Aralık B (" + fromB.value.replace("T", " ") + ")";

        Promise.all([fetchRangeAverage(fromA, toA), fetchRangeAverage(fromB, toB)])
            .then(function (results) {
                var a = results[0], b = results[1];
                body.innerHTML = "";

                if (a.sampleCount === 0 || b.sampleCount === 0) {
                    body.innerHTML = '<tr><td colspan="4" class="stat-sub">Bir veya iki aralıkta hiç kayıt yok.</td></tr>';
                    return;
                }

                METRICS.forEach(function (metric) {
                    var tr = document.createElement("tr");
                    var aValue = a[metric.key];
                    var bValue = b[metric.key];
                    tr.appendChild(cell(metric.label));
                    tr.appendChild(cell(fmt(aValue, metric.unit, metric.digits)));
                    tr.appendChild(cell(fmt(bValue, metric.unit, metric.digits)));

                    var diffCell = cell("--");
                    if (typeof aValue === "number" && typeof bValue === "number") {
                        diffCell.textContent = fmtDiff(bValue - aValue, metric.unit, metric.digits);
                    }
                    tr.appendChild(diffCell);
                    body.appendChild(tr);
                });

                var noteRow = document.createElement("tr");
                var noteCell = document.createElement("td");
                noteCell.colSpan = 4;
                noteCell.className = "stat-sub";
                noteCell.textContent = "Aralık A: " + a.sampleCount + " örnek · Aralık B: " + b.sampleCount + " örnek";
                noteRow.appendChild(noteCell);
                body.appendChild(noteRow);
            })
            .catch(function () {
                body.innerHTML = '<tr><td colspan="4" class="stat-sub">Karşılaştırma okunamadı.</td></tr>';
            });
    });
})();
