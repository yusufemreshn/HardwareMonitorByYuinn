(function () {
    "use strict";

    var WINDOW_MS = 5 * 60 * 1000;
    // Grafiğin bellek retention'ı sabit 1 saat — toggle butonları (1dk/5dk/15dk/1sa) artık veri
    // çekmez/değiştirmez, yalnızca zaten yüklü olan veriye zoom yapar (bkz. initChartRangeToggle).
    var MAX_WINDOW_MS = 60 * 60 * 1000;
    var initialHistory = window.__dashboardInitialHistory || {};

    // İstatistiksel anomali tespiti — sabit eşiklerden (Dikkat/Kritik) BAĞIMSIZ, tamamlayıcı bir
    // katman: eşiği hiç aşmasa bile son ~2 dakikaya göre ANİ bir sıçrama yaşayan bir metriği
    // yakalar (ör. normalde %20 CPU kullanan bir sistemin aniden %60'a fırlaması). En az 20 örnek
    // birikmeden hesaplama yapılmaz (aksi hâlde standart sapma güvenilmez/gürültülü olur).
    function createRollingStat(maxSamples) {
        var values = [];
        return {
            add: function (v) {
                if (typeof v !== "number" || isNaN(v)) return;
                values.push(v);
                if (values.length > maxSamples) values.shift();
            },
            zScoreFor: function (v) {
                if (values.length < 20 || typeof v !== "number" || isNaN(v)) return null;
                var mean = values.reduce(function (a, b) { return a + b; }, 0) / values.length;
                var variance = values.reduce(function (a, b) { return a + (b - mean) * (b - mean); }, 0) / values.length;
                var std = Math.sqrt(variance);
                if (std < 0.001) return 0;
                return (v - mean) / std;
            }
        };
    }

    var ANOMALY_Z_THRESHOLD = 3;
    var anomalyStats = { cpu: createRollingStat(120), gpu: createRollingStat(120), ram: createRollingStat(120) };

    // Process bazlı bellek sızıntısı tespiti — bir process'in RAM kullanımı listede göründüğü
    // sürece SÜREKLİ artıyorsa (ilk yarı ortalamasına göre son yarı ortalaması en az %50 VE
    // en az 100 MB fazlaysa) bir not düşülür. Anomali tespiti gibi bu da sabit eşiklerden bağımsız,
    // sağlık puanını ETKİLEMEZ (yalnızca bilgilendirme amaçlı).
    var PROCESS_RAM_HISTORY_MAX = 30;
    var MEMORY_LEAK_MIN_SAMPLES = 20;
    var MEMORY_LEAK_GROWTH_RATIO = 1.5;
    var MEMORY_LEAK_MIN_GROWTH_MB = 100;
    // Bir process, CPU/GPU/RAM sıralamalarının hiçbirinde net üst-N'de değilse (ör. yavaş sızan ama
    // orta seviyede kalan bir process) bir tur listeden düşüp geri gelebilir. Eskiden bu, geçmişin
    // ANINDA silinmesine yol açıyordu — bir process hiçbir zaman MEMORY_LEAK_MIN_SAMPLES'a
    // ulaşamıyor, tespit fiilen hiç çalışmıyordu (kullanıcı tarafından bulunan, canlı doğrulanmış
    // hata). Artık bir process yalnızca bu süre boyunca HİÇ görünmezse geçmişi silinir.
    var MEMORY_LEAK_ABSENCE_GRACE_MS = 5 * 60 * 1000;
    var processRamHistory = {}; // name -> { values: number[], lastSeenAt: epoch ms }

    function checkMemoryLeaks(processes) {
        if (!Array.isArray(processes)) return [];
        var seenNames = {};
        var warnings = [];
        var now = Date.now();

        processes.forEach(function (p) {
            if (typeof p.ramMb !== "number") return;
            var name = p.name || ("PID " + p.pid);
            seenNames[name] = true;

            var entry = processRamHistory[name] || (processRamHistory[name] = { values: [], lastSeenAt: now });
            entry.lastSeenAt = now;
            var hist = entry.values;
            hist.push(p.ramMb);
            if (hist.length > PROCESS_RAM_HISTORY_MAX) hist.shift();

            if (hist.length >= MEMORY_LEAK_MIN_SAMPLES) {
                var half = Math.floor(hist.length / 2);
                var firstHalfAvg = hist.slice(0, half).reduce(function (a, b) { return a + b; }, 0) / half;
                var secondHalfAvg = hist.slice(half).reduce(function (a, b) { return a + b; }, 0) / (hist.length - half);
                var growthMb = secondHalfAvg - firstHalfAvg;
                if (firstHalfAvg > 0 && secondHalfAvg > firstHalfAvg * MEMORY_LEAK_GROWTH_RATIO && growthMb > MEMORY_LEAK_MIN_GROWTH_MB) {
                    warnings.push("⚡ " + name + " bellek kullanımı sürekli artıyor (" + firstHalfAvg.toFixed(0) + " MB → " + secondHalfAvg.toFixed(0) + " MB)");
                    hist.length = 0; // aynı uyarı sürekli tekrarlanmasın; yeni bir eğilim birikmeye başlar
                }
            }
        });

        // Yalnızca uzun süredir (5+ dk) hiç görünmeyen process'lerin geçmişini sil — kendi
        // belleğimizde sınırsız birikmesin, ama üst-N listesinden bir-iki tur için düşen bir
        // process'in trendi kaybolmasın.
        Object.keys(processRamHistory).forEach(function (name) {
            if (!seenNames[name] && (now - processRamHistory[name].lastSeenAt) > MEMORY_LEAK_ABSENCE_GRACE_MS) {
                delete processRamHistory[name];
            }
        });

        return warnings;
    }

    // z-score eşiği aşan bir sıçrama olduğunda healthConcerns'e bir not eklenir; ekleme, YENİ
    // değer rolling istatistiğe dahil edilmeden ÖNCE yapılır (yoksa sıçramanın kendisi bir sonraki
    // turda kendi ortalamasını/sapmasını kaydırıp anomaliyi "normalleştirir").
    function checkAnomaly(key, value, label, unit, digits) {
        if (!window.HwmonAnomalyDetection || !HwmonAnomalyDetection.isEnabled()) {
            anomalyStats[key].add(value);
            return null;
        }
        var z = anomalyStats[key].zScoreFor(value);
        anomalyStats[key].add(value);
        if (z !== null && Math.abs(z) > ANOMALY_Z_THRESHOLD) {
            return "⚡ " + label + " son 2 dakikaya göre ani bir sıçrama gösterdi (" + value.toFixed(digits) + " " + unit + ")";
        }
        return null;
    }
    var chartTypes = window.HwmonChartTypes ? window.HwmonChartTypes.load() : { cpu: "line", gpu: "line", ram: "line", fps: "line" };

    var chart = HwmonChart.create(document.getElementById("usage-chart"), {
        windowMs: WINDOW_MS,
        minWindowMs: 15 * 1000,
        maxWindowMs: MAX_WINDOW_MS,
        leftAxis: "percent",
        rightAxis: "fps",
        axes: {
            percent: { min: 0, max: 100, format: function (v) { return v.toFixed(0) + "%"; } },
            // Bu eksen yüzde değil; FPS ve ağ hızı (MB/s) gibi "saniyede bir değer" serilerini
            // paylaşır, kendi ölçeğinde serbestçe büyür, boşken 60'a kadar gösterilir.
            fps: { min: 0, max: "auto", fallbackMax: 60, format: function (v) { return v.toFixed(0); } }
        },
        series: [
            { key: "cpu", label: "İşlemci", colorVar: "--accent-cpu", axis: "percent", unit: "%", digits: 1, chartType: chartTypes.cpu },
            { key: "gpu", label: "Ekran Kartı", colorVar: "--accent-gpu", axis: "percent", unit: "%", digits: 1, chartType: chartTypes.gpu },
            { key: "vram", label: "VRAM", colorVar: "--accent-gpu", axis: "percent", unit: "%", digits: 1, visible: false, dashed: true },
            { key: "ram", label: "Bellek", colorVar: "--accent-ram", axis: "percent", unit: "%", digits: 1, chartType: chartTypes.ram },
            { key: "fps", label: "Kare Hızı", colorVar: "--accent-fps", axis: "fps", unit: "FPS", digits: 1, chartType: chartTypes.fps },
            { key: "download", label: "İndirme", colorVar: "--accent-network", axis: "fps", unit: "MB/s", digits: 1, visible: false },
            { key: "upload", label: "Yükleme", colorVar: "--accent-network", axis: "fps", unit: "MB/s", digits: 1, visible: false, dashed: true },
            // Kare süresi (ms), FPS ile aynı eksende paylaşılır; tipik oyun FPS aralığında (30-240)
            // sayısal olarak da benzer büyüklükte kaldığı için ayrı bir eksen açmaya gerek kalmaz.
            { key: "frametime", label: "Kare Süresi", colorVar: "--accent-fps", axis: "fps", unit: "ms", digits: 1, visible: false, dashed: true },
            { key: "fpsLow1", label: "1% Low", colorVar: "--accent-fps", axis: "fps", unit: "FPS", digits: 0, visible: false, dashed: true },
            { key: "fpsLow01", label: "0.1% Low", colorVar: "--accent-fps", axis: "fps", unit: "FPS", digits: 0, visible: false, dashed: true }
        ]
    });

    // Ayarlar sayfasından (başka bir sekmede) grafik tipi değiştirildiğinde anında uygular.
    document.addEventListener("hwmon:chart-types-changed", function () {
        if (!window.HwmonChartTypes) return;
        var next = window.HwmonChartTypes.load();
        window.HwmonChartTypes.keys.forEach(function (key) { chart.setChartType(key, next[key]); });
        chart.render();
    });

    ["cpu", "gpu", "vram", "ram", "fps", "frametime", "fpsLow1", "fpsLow01"].forEach(function (key) {
        chart.seed(key, initialHistory[key]);
    });

    // Geçmiş, ağ hızını bayt/sn olarak taşır (canlı mesajlarla aynı birim); grafik MB/s beklediği
    // için burada da aynı dönüşüm uygulanır.
    ["download", "upload"].forEach(function (key) {
        var points = initialHistory[key];
        if (!Array.isArray(points)) return;
        chart.seed(key, points.map(function (p) {
            return { timestampUtc: p.timestampUtc, value: typeof p.value === "number" ? p.value / (1024 * 1024) : p.value };
        }));
    });
    chart.render();

    // Sunucudaki TimeSeriesStore yalnızca son 15 dk'yı ince (saniyelik) çözünürlükte bellekte tutuyor
    // (initialHistory de o kadarını içerir) — "1 sa" penceresinin geri kalan 45 dk'lık kısmı için
    // burada BİR KEZ, sayfa açılışında, /History/Rows'tan (dakikalık ortalama) geçmiş veri çekilip
    // ince veriden ÖNCEYE eklenir. Bundan sonra 1dk/5dk/15dk/1sa arasında geçiş yapmak artık hiç
    // ağ isteği yapmaz (bkz. initChartRangeToggle) — sadece zaten yüklü olan bu veriye zoom yapar.
    (function seedOlderHistoryOnce() {
        var oldestNeeded = new Date(Date.now() - MAX_WINDOW_MS);
        var fineStart = new Date(Date.now() - 15 * 60 * 1000);
        if (oldestNeeded >= fineStart) return; // MAX_WINDOW_MS zaten ince veri kapsamının içinde

        function pad(n) { return n < 10 ? "0" + n : String(n); }
        function toLocalParam(d) {
            return d.getFullYear() + "-" + pad(d.getMonth() + 1) + "-" + pad(d.getDate()) + "T" +
                pad(d.getHours()) + ":" + pad(d.getMinutes()) + ":" + pad(d.getSeconds());
        }

        var url = "/History/Rows?from=" + encodeURIComponent(toLocalParam(oldestNeeded)) + "&to=" + encodeURIComponent(toLocalParam(fineStart));

        fetch(url).then(function (r) { return r.json(); }).then(function (data) {
            var rows = Array.isArray(data.rows) ? data.rows : [];
            var coarse = { cpu: [], gpu: [], ram: [], fps: [], download: [], upload: [] };

            rows.forEach(function (r) {
                var t = r.zaman;
                if (typeof r.cpuUsagePercent === "number") coarse.cpu.push({ timestampUtc: t, value: r.cpuUsagePercent });
                if (typeof r.gpuUsagePercent === "number") coarse.gpu.push({ timestampUtc: t, value: r.gpuUsagePercent });
                if (typeof r.ramUsagePercent === "number") coarse.ram.push({ timestampUtc: t, value: r.ramUsagePercent });
                if (typeof r.fpsValue === "number") coarse.fps.push({ timestampUtc: t, value: r.fpsValue });
                if (typeof r.networkDownloadBytesPerSec === "number") coarse.download.push({ timestampUtc: t, value: r.networkDownloadBytesPerSec / (1024 * 1024) });
                if (typeof r.networkUploadBytesPerSec === "number") coarse.upload.push({ timestampUtc: t, value: r.networkUploadBytesPerSec / (1024 * 1024) });
            });

            Object.keys(coarse).forEach(function (key) {
                if (!coarse[key].length) return;
                var fine = Array.isArray(initialHistory[key]) ? initialHistory[key] : [];
                var fineNormalized = (key === "download" || key === "upload")
                    ? fine.map(function (p) { return { timestampUtc: p.timestampUtc, value: typeof p.value === "number" ? p.value / (1024 * 1024) : p.value }; })
                    : fine;
                // Kaba (dakikalık) noktalar ince (saniyelik) noktalardan ESKİ olduğu için önce onlar
                // eklenir; drawSeries aradaki ~15 dk'lık boşluğu (GAP_BREAK_MS) otomatik olarak
                // çizgiyle birleştirmeyip boş bırakır.
                chart.seed(key, coarse[key].concat(fineNormalized));
            });
            chart.render();
        }).catch(function () {
            // Geçmiş genişletme opsiyoneldir; başarısız olursa yalnızca "1 sa" görünümünün ilk
            // 15 dk'lık kısmı boş kalır, mevcut ince veri etkilenmez.
        });
    })();

    document.addEventListener("hwmon:theme-changed", function () {
        chart.refreshColors();
        chart.render();
    });

    // Ayarlar sayfasından (başka bir sekmede) renk değiştirildiğinde grafiği yeniden boyar.
    document.addEventListener("hwmon:accent-colors-changed", function () {
        chart.refreshColors();
        chart.render();
    });

    document.querySelectorAll("[data-toggle-series]").forEach(function (el) {
        el.addEventListener("click", function () {
            var key = el.getAttribute("data-toggle-series");
            var visible = !chart.isVisible(key);
            chart.setVisible(key, visible);
            chart.render();

            document.querySelectorAll('[data-toggle-series="' + key + '"]').forEach(function (peer) {
                peer.classList.toggle("is-muted", !visible);
                if (peer.hasAttribute("aria-pressed")) {
                    peer.setAttribute("aria-pressed", visible ? "true" : "false");
                }
            });
        });
    });

    // Grafik zaman aralığı toggle'ı (1dk/5dk/15dk/1sa) — fare tekerleğiyle yakınlaştırmaya ek,
    // daha keşfedilebilir bir alternatif. Veri zaten (yukarıdaki initialHistory seed'i +
    // seedOlderHistoryOnce ile) tam 1 saatlik retention'a kadar yüklü olduğundan, toggle burada
    // hiçbir zaman ağ isteği yapmaz — yalnızca zoom yapar (bkz. hwmon-chart.js setWindowMs).
    (function initChartRangeToggle() {
        var container = document.getElementById("chart-range-toggle");
        if (!container) return;

        container.querySelectorAll("button[data-range-ms]").forEach(function (btn) {
            btn.addEventListener("click", function () {
                var ms = Number(btn.getAttribute("data-range-ms"));
                container.querySelectorAll("button").forEach(function (b) { b.classList.toggle("is-active", b === btn); });
                chart.setWindowMs(ms);
            });
        });
    })();

    // Grafiğin kullanım ipucu ("fare tekerleğiyle yakınlaştır...") eskiden native `title`
    // niteliğiyle gösteriliyordu (tarayıcının kendi zamanlaması/görünürlüğü, kontrol edilemez).
    // Artık: fare grafiğe her girdiğinde (sayfa yenilenene kadar en fazla 3 kez) 2 sn görünüp
    // yavaşça kaybolan özel bir baloncuk gösteriyoruz — 3. gösterimden sonra sayfa yenilenene
    // kadar bir daha çıkmıyor.
    (function initChartHint() {
        var canvas = document.getElementById("usage-chart");
        var hint = document.getElementById("chart-hint");
        if (!canvas || !hint) return;

        var MAX_SHOWS = 3;
        var VISIBLE_MS = 2000;
        var shownCount = 0;
        var hideTimer = null;

        canvas.addEventListener("mouseenter", function () {
            if (shownCount >= MAX_SHOWS) return;
            shownCount++;

            clearTimeout(hideTimer);
            hint.style.transition = "opacity .15s ease";
            hint.classList.add("is-visible");

            hideTimer = setTimeout(function () {
                hint.style.transition = "opacity 1.2s ease";
                hint.classList.remove("is-visible");
            }, VISIBLE_MS);
        });
    })();

    // Değer okunamadığında birimi de gizleriz; "-- MHz" okuyucuya sıfır ölçüldüğü izlenimi verir.
    // Tanım artık site.js'te (HwmonFormat) — burada yalnızca kısa bir takma ad.
    var fmt = window.HwmonFormat.fmt;

    function setText(id, text) {
        var el = document.getElementById(id);
        if (el) el.textContent = text;
    }

    function formatRamDetail(usedGb, totalGb) {
        if (typeof usedGb !== "number" || typeof totalGb !== "number") return "--";
        var used = window.HwmonUnits.convert(usedGb, "GB");
        var total = window.HwmonUnits.convert(totalGb, "GB");
        return used.value.toFixed(1) + " / " + total.value.toFixed(1) + " " + used.unit;
    }

    // Kaynak Kullanımı listesindeki bir sürecin bellek kullanımı 4 haneye (1000 MB/~1 GB) ulaşınca
    // MB olarak yazmak sütunu gereksiz genişletir; bu eşikten sonra GB'ye geçeriz.
    function formatProcessRam(ramMb) {
        if (typeof ramMb !== "number") return "--";
        if (ramMb < 1000) return ramMb.toFixed(0) + " MB";
        var gb = window.HwmonUnits.convert(ramMb / 1024, "GB");
        return gb.value.toFixed(1) + " " + gb.unit;
    }

    function formatRateMBs(bytesPerSec) {
        if (typeof bytesPerSec !== "number") return "--";
        var c = window.HwmonUnits.convert(bytesPerSec / (1024 * 1024), "MB/s");
        return c.value.toFixed(1) + " " + c.unit;
    }

    function toMBs(bytesPerSec) {
        return typeof bytesPerSec === "number" ? bytesPerSec / (1024 * 1024) : undefined;
    }

    // DOM node'ları innerHTML yerine elle kurarak disk adının (donanımdan gelen metin) kaçışsız
    // basılmasını garantiye alırız.
    function renderStorageCard(storages) {
        var container = document.getElementById("storage-body");
        if (!container) return;
        container.textContent = "";

        if (!Array.isArray(storages) || storages.length === 0) {
            var empty = document.createElement("div");
            empty.className = "stat-sub";
            empty.textContent = "Disk bulunamadı";
            container.appendChild(empty);
            return;
        }

        // Ayarlar → Kart Sıralaması'nda kullanıcının Depolama kartı için seçtiği detaylar
        // (Depolama kartı her tick'te yeniden oluşturulduğu için CSS/display yerine burada okunur).
        var visible = window.HwmonDetailVisibility ? window.HwmonDetailVisibility.load().storage : null;
        function isOn(key) { return !visible || visible[key] !== false; }

        storages.forEach(function (s) {
            var row = document.createElement("div");
            row.className = "storage-row";

            if (isOn("temp") || isOn("usage")) {
                var mainRow = document.createElement("div");
                mainRow.className = "stat-value-row";
                [isOn("temp") ? fmt(s.temperatureC, "°C", 1) : null, isOn("usage") ? fmt(s.usedPercent, "%", 1) : null]
                    .filter(function (text) { return text !== null; })
                    .forEach(function (text) {
                        var span = document.createElement("span");
                        span.textContent = text;
                        mainRow.appendChild(span);
                    });
                row.appendChild(mainRow);
            }

            if (isOn("capacity") && typeof s.totalGb === "number" && typeof s.usedPercent === "number") {
                var capacity = document.createElement("div");
                capacity.className = "stat-sub";
                var usedGb = window.HwmonUnits.convert(s.totalGb * s.usedPercent / 100, "GB");
                var totalGb = window.HwmonUnits.convert(s.totalGb, "GB");
                capacity.textContent = usedGb.value.toFixed(0) + " / " + totalGb.value.toFixed(0) + " " + totalGb.unit;
                row.appendChild(capacity);
            }

            if (isOn("rwRate")) {
                var subRow = document.createElement("div");
                subRow.className = "stat-sub-row";
                ["↓ " + formatRateMBs(s.readRateBytesPerSec), "↑ " + formatRateMBs(s.writeRateBytesPerSec)]
                    .forEach(function (text) {
                        var span = document.createElement("span");
                        span.textContent = text;
                        subRow.appendChild(span);
                    });
                row.appendChild(subRow);
            }

            if (isOn("name")) {
                var name = document.createElement("div");
                name.className = "stat-sub";
                name.textContent = s.name || "";
                row.appendChild(name);
            }

            if (isOn("smart") && s.smartStatus) {
                var smartRow = document.createElement("div");
                smartRow.className = "stat-sub";
                var smartText = "Disk Sağlığı: " + s.smartStatus;
                if (typeof s.smartLifePercent === "number") smartText += " · Ömür %" + s.smartLifePercent;
                smartRow.textContent = smartText;
                if (s.smartStatus === "Kötü" || s.smartStatus === "Dikkat") smartRow.style.color = "var(--accent-danger)";
                row.appendChild(smartRow);
            }

            container.appendChild(row);
        });
    }

    // Kaynak Kullanımı sıralama ölçütü — CPU/GPU/RAM arasında seçilebilir, tarayıcıda saklanır.
    // Sunucu zaten her ölçüte göre en üstteki adayları birleştirip gönderiyor (ProcessMetricsProvider),
    // burada yalnızca seçilen ölçüte göre yeniden sıralayıp ilk 8'i gösteriyoruz.
    var PROCESS_SORT_KEY = "hwmon-process-sort";
    var PROCESS_SORT_FIELDS = { cpu: "cpuPercent", gpu: "gpuPercent", ram: "ramMb" };
    var processSortMetric = (function () {
        try {
            var saved = localStorage.getItem(PROCESS_SORT_KEY);
            return PROCESS_SORT_FIELDS[saved] ? saved : "cpu";
        } catch (e) { return "cpu"; }
    })();
    var lastProcesses = null;

    // DOM node'ları innerHTML yerine elle kurarak süreç adının (kullanıcı tarafından değiştirilebilir
    // bir metin olan process adı) kaçışsız basılmasını garantiye alırız.
    function renderProcessesCard(processes) {
        lastProcesses = processes;

        var container = document.getElementById("processes-body");
        if (!container) return;
        container.textContent = "";

        if (!Array.isArray(processes) || processes.length === 0) {
            var empty = document.createElement("div");
            empty.className = "stat-sub";
            empty.textContent = "Veri bekleniyor…";
            container.appendChild(empty);
            return;
        }

        var field = PROCESS_SORT_FIELDS[processSortMetric];
        var top = processes.slice()
            .sort(function (a, b) { return (b[field] || 0) - (a[field] || 0); })
            .slice(0, 5);

        top.forEach(function (p) {
            var row = document.createElement("div");
            row.className = "process-row";

            var name = document.createElement("span");
            name.className = "process-name";
            name.textContent = p.name || ("PID " + p.pid);
            row.appendChild(name);

            // Her metrik kendi (sabit genişlikli) hücresine yazılır; tek bir birleşik metin olsaydı
            // satırdan satıra rakam sayısı değiştikçe sütunlar birbirine göre kayardı.
            var metrics = document.createElement("span");
            metrics.className = "process-metrics";
            [fmt(p.cpuPercent, "%", 1), fmt(p.gpuPercent, "%", 1), formatProcessRam(p.ramMb)].forEach(function (text) {
                var cell = document.createElement("span");
                cell.textContent = text;
                metrics.appendChild(cell);
            });
            row.appendChild(metrics);

            container.appendChild(row);
        });
    }

    // Sıralama ölçütü artık ayrı bir açılır menü yerine başlık satırındaki CPU/GPU/RAM
    // yazılarına tıklanarak seçiliyor; seçili olan beyaza dönüyor (.is-active).
    var processSortToggles = document.querySelectorAll(".process-sort-toggle");
    function updateProcessSortToggleStyles() {
        processSortToggles.forEach(function (btn) {
            btn.classList.toggle("is-active", btn.dataset.sortMetric === processSortMetric);
        });
    }
    processSortToggles.forEach(function (btn) {
        btn.addEventListener("click", function () {
            var metric = btn.dataset.sortMetric;
            if (!PROCESS_SORT_FIELDS[metric]) return;
            processSortMetric = metric;
            try { localStorage.setItem(PROCESS_SORT_KEY, processSortMetric); } catch (e) { /* localStorage kullanılamıyorsa yok sayılır */ }
            updateProcessSortToggleStyles();
            renderProcessesCard(lastProcesses);
        });
    });
    updateProcessSortToggleStyles();

    // Windows'un açık kalma süresi (program değil, işletim sistemi) — sayfa yüklendiğinde
    // sunucudan gelen sabit bir açılış zamanından itibaren saniyede bir client tarafında sayılır.
    (function initSystemUptime() {
        var el = document.getElementById("system-uptime");
        if (!el) return;

        var bootAt = Date.parse(el.getAttribute("data-boot-at"));
        if (isNaN(bootAt)) return;

        function pad(n) { return n < 10 ? "0" + n : "" + n; }

        function tick() {
            var totalSeconds = Math.max(0, Math.floor((Date.now() - bootAt) / 1000));
            var days = Math.floor(totalSeconds / 86400);
            var hours = Math.floor((totalSeconds % 86400) / 3600);
            var minutes = Math.floor((totalSeconds % 3600) / 60);
            var seconds = totalSeconds % 60;
            var clock = pad(hours) + ":" + pad(minutes) + ":" + pad(seconds);
            el.textContent = days > 0 ? days + "g " + clock : clock;
        }

        tick();
        setInterval(tick, 1000);
    })();

    // Eşik değerleri; aşıldığında kart kırmızıya döner. Değerler "Ayarlar" sayfasından değiştirilir
    // (settings.js); burada yalnızca okunur. Gerçek bildirim gönderme sorumluluğu artık her
    // sayfada çalışan threshold-notifier.js'te — böylece Panel açık olmasa da bildirim gelir.
    var HwmonThresholds = window.HwmonThresholds;
    var WARNING_THRESHOLDS = HwmonThresholds.load();

    // Başka bir sekmede (Ayarlar) eşikler değiştirildiğinde bu sekmeyi yenilemeden anında uygula.
    document.addEventListener("hwmon:thresholds-changed", function () {
        WARNING_THRESHOLDS = HwmonThresholds.load();
    });

    // Enerji maliyeti — yalnızca İşlemci + Ekran Kartı paket güçlerinden hesaplanan bir tahmindir.
    var ENERGY_COST_PREFS = window.HwmonEnergyCost.load();
    document.addEventListener("hwmon:energy-cost-changed", function () {
        ENERGY_COST_PREFS = window.HwmonEnergyCost.load();
    });

    function updateEnergyCost(cpuWatts, gpuWatts) {
        var row = document.getElementById("energy-cost-row");
        var display = document.getElementById("energy-cost-display");
        if (!row || !display) return;

        var hasPower = typeof cpuWatts === "number" || typeof gpuWatts === "number";
        if (!ENERGY_COST_PREFS.enabled || !ENERGY_COST_PREFS.ratePerKwh || !hasPower) {
            row.style.display = "none";
            return;
        }

        var totalWatts = (typeof cpuWatts === "number" ? cpuWatts : 0) + (typeof gpuWatts === "number" ? gpuWatts : 0);
        var kwhPerHour = totalWatts / 1000;
        var costPerHour = kwhPerHour * ENERGY_COST_PREFS.ratePerKwh;
        row.style.display = "";
        display.textContent = "Enerji maliyeti (tahmini): ₺" + costPerHour.toFixed(2) + "/sa · ₺" +
            (costPerHour * 24).toFixed(2) + "/gün · ₺" + (costPerHour * 24 * 30).toFixed(2) + "/ay";
    }

    // level: "normal" | "caution" | "critical" — iki kademeli eşik renklendirmesi (Dikkat sarı /
    // Kritik kırmızı) için kartın sınıflarını buna göre ayarlar.
    function setCardWarningBySeries(seriesKey, level) {
        document.querySelectorAll('.stat-card[data-toggle-series="' + seriesKey + '"]').forEach(function (card) {
            card.classList.toggle("is-caution", level === "caution");
            card.classList.toggle("is-warning", level === "critical");
        });
    }

    // Kullanıcı başka bir sekmedeyken bile göz ucuyla takip edebilsin diye sekme başlığı canlı güncellenir.
    var ORIGINAL_TITLE = document.title;
    function updateTabTitle(cpuPercent, gpuPercent) {
        if (typeof cpuPercent === "number" && typeof gpuPercent === "number") {
            document.title = "CPU " + cpuPercent.toFixed(0) + "% · GPU " + gpuPercent.toFixed(0) + "% — HardwareMonitorByYuinn";
        } else {
            document.title = ORIGINAL_TITLE;
        }
    }

    // Ambiyans modu — Panel'in tamamını kaplayan saat + büyük gösterge ekranı.
    // Dışarıya (Oyun Profilleri'nin otomatik ambiyans geçişi için) enter/exit/isActive açar.
    var ambientMode = (function initAmbientMode() {
        var overlay = document.getElementById("ambient-overlay");
        var enterButton = document.getElementById("ambient-enter");
        var exitButton = document.getElementById("ambient-exit");
        if (!overlay || !enterButton) return { enter: function () { }, exit: function () { }, isActive: function () { return false; } };

        var clockInterval = null;

        function updateClock() {
            var now = new Date();
            setText("ambient-clock", now.toLocaleTimeString("tr-TR"));
            setText("ambient-date", now.toLocaleDateString("tr-TR", { weekday: "long", day: "numeric", month: "long" }));
        }

        // Ayarlar sayfasında hangi kartların (İşlemci/Ekran Kartı/Bellek/Kare Hızı/Depolama)
        // Ambiyans Modu'nda görüneceği seçilir; burada yalnızca ilgili kutunun gösterilip
        // gösterilmeyeceğine karar verilir, değerler yine hardwareUpdate akışından gelir.
        function applyVisibility() {
            var api = window.HwmonAmbientSettings;
            var enabled = api ? api.load() : null;
            if (!enabled) return;
            api.metrics.forEach(function (key) {
                var el = document.getElementById("ambient-stat-" + key);
                if (el) el.style.display = enabled[key] === false ? "none" : "";
            });
        }
        applyVisibility();
        document.addEventListener("hwmon:ambient-settings-changed", applyVisibility);

        function enter() {
            overlay.hidden = false;
            applyVisibility();
            updateClock();
            clockInterval = setInterval(updateClock, 1000);
            // Tarayıcı gerçek tam ekranı reddedebilir (ör. otomasyon/kullanıcı hareketi yoksa,
            // Oyun Profilleri'nin otomatik tetiklemesinde olduğu gibi); overlay bu durumda da
            // normal pencere içinde çalışmaya devam eder.
            if (document.documentElement.requestFullscreen) {
                document.documentElement.requestFullscreen().catch(function () { });
            }
        }

        function exit() {
            overlay.hidden = true;
            if (clockInterval) { clearInterval(clockInterval); clockInterval = null; }
            if (document.fullscreenElement) document.exitFullscreen();
        }

        enterButton.addEventListener("click", enter);
        if (exitButton) exitButton.addEventListener("click", exit);
        document.addEventListener("keydown", function (e) {
            if (e.key === "Escape" && !overlay.hidden) exit();
        });
        document.addEventListener("fullscreenchange", function () {
            if (!document.fullscreenElement && !overlay.hidden) exit();
        });

        return { enter: enter, exit: exit, isActive: function () { return !overlay.hidden; } };
    })();

    // Oyun Profilleri — ön plandaki uygulama (fps.sourceProcessName, Kare Hızı ölçümüyle aynı
    // kaynak) Ayarlar sayfasında tanımlı bir profille eşleşince renkleri/eşikleri "önizleme" gibi
    // uygular (localStorage'daki asıl tercihlere dokunmadan); işlem değişince (oyun kapanınca ya da
    // başka bir profile geçince) otomatik olarak geri döner.
    var activeGameProfileProcessName = null;
    var activeGameProfileForExit = null;
    var ambientAutoEnteredByProfile = false;

    function applyGameProfile(sourceProcessName) {
        var api = window.HwmonGameProfiles;
        if (!api) return;

        var normalizedName = sourceProcessName || null;
        if (normalizedName === activeGameProfileProcessName) return;

        // Önceki profil artık geçerli değilse (işlem değişti/kapandı) genel ayarlara dön.
        if (activeGameProfileProcessName !== null) {
            // Otomatik anlık görüntü, ayarlar genel duruma döndürülmeden ÖNCE alınır ki PNG,
            // oyunun kendi renkleriyle (aktifken görülen renklerle) uyumlu olsun.
            if (activeGameProfileForExit && activeGameProfileForExit.autoSnapshotOnExit) {
                autoDownloadSnapshot();
            }
            HwmonAccentColors.apply(HwmonAccentColors.load());
            WARNING_THRESHOLDS = HwmonThresholds.load();
            if (window.HwmonPanelLayout) HwmonPanelLayout.apply(); // gerçek kayıtlı düzene döner
            if (ambientAutoEnteredByProfile) {
                ambientMode.exit();
                ambientAutoEnteredByProfile = false;
            }
            activeGameProfileProcessName = null;
            activeGameProfileForExit = null;
        }

        var profile = api.findMatch(normalizedName);
        if (!profile) return;

        activeGameProfileProcessName = normalizedName;
        activeGameProfileForExit = profile;
        if (profile.colors) HwmonAccentColors.apply(profile.colors);
        if (profile.thresholds) WARNING_THRESHOLDS = HwmonThresholds.sanitize(profile.thresholds);

        // Panel düzeni de renkler/eşikler gibi "önizleme" olarak uygulanır — localStorage'daki
        // asıl kayıtlı düzene dokunulmaz, yalnızca DOM'a geçici olarak uygulanır (applyLayout).
        if (profile.panelLayoutProfileId && window.HwmonPanelLayoutProfiles && window.HwmonPanelLayout) {
            var layoutProfiles = HwmonPanelLayoutProfiles.load();
            var targetLayout = layoutProfiles.filter(function (p) { return p.id === profile.panelLayoutProfileId; })[0];
            if (targetLayout) HwmonPanelLayout.applyLayout({ order: targetLayout.order, hidden: targetLayout.hidden });
        }

        // Tarayıcılar, kullanıcı hareketi olmadan (bu otomatik geçiş gibi) tam ekranı reddeder;
        // ambientMode.enter() bunu zaten sessizce yutar, overlay yine de normal pencerede açılır.
        if (profile.autoAmbientMode && !ambientMode.isActive()) {
            ambientMode.enter();
            ambientAutoEnteredByProfile = true;
        }
    }

    document.addEventListener("hwmon:game-profiles-changed", function () {
        // Profil listesi değiştiğinde (ör. Ayarlar'da düzenlendi) bir sonraki tick'te yeniden
        // değerlendirilsin diye zorla "değişti" gibi davranırız.
        activeGameProfileProcessName = undefined;
    });

    // Paylaşılabilir "kartvizit" görüntüsü — o anki tüm değerleri tek bir PNG karta çizer.
    var lastMessage = null;

    // Genel Sağlık Özeti — Panel'in en üstünde, mevcut eşik ayarlarına (HwmonThresholds) ve SMART
    // disk durumuna göre canlı güncellenen bir özet. Kullanıcı × ile kapatınca sayfa yeniden
    // yüklenene kadar (yani bir sonraki açılışa kadar) tekrar gösterilmez.
    var healthSummaryDismissed = false;

    // 100'den başlar; her "kritik" seviye -20, her "dikkat" seviye -8 (0'ın altına inmez). Basit
    // ama iki kademeli eşik sistemiyle (bkz. levelFor) tutarlı bir özet puan — SMART "Dikkat"/"Kötü"
    // durumları da concerns listesine zaten "kritik" gibi eklendiği için ayrıca sayılmıyor.
    function renderHealthSummary(concerns, score) {
        var container = document.getElementById("health-summary");
        if (!container || healthSummaryDismissed) return;

        container.textContent = "";
        container.style.display = "flex";
        container.classList.toggle("is-warning", concerns.length > 0);

        var icon = document.createElement("span");
        icon.className = "health-summary-icon";
        icon.textContent = concerns.length > 0 ? "⚠" : "✓";
        container.appendChild(icon);

        if (typeof score === "number") {
            var scoreEl = document.createElement("span");
            scoreEl.className = "health-summary-score";
            scoreEl.classList.toggle("is-good", score >= 80);
            scoreEl.classList.toggle("is-caution", score >= 50 && score < 80);
            scoreEl.classList.toggle("is-bad", score < 50);
            scoreEl.textContent = score + "/100";
            scoreEl.title = "Sistem sağlığı puanı";
            container.appendChild(scoreEl);
        }

        var text = document.createElement("div");
        text.className = "health-summary-text";
        text.textContent = concerns.length > 0 ? concerns.join(" · ") : "Her şey normal görünüyor.";
        container.appendChild(text);

        var closeButton = document.createElement("button");
        closeButton.type = "button";
        closeButton.className = "health-summary-close";
        closeButton.setAttribute("aria-label", "Kapat");
        closeButton.textContent = "×";
        closeButton.addEventListener("click", function () {
            healthSummaryDismissed = true;
            container.style.display = "none";
        });
        container.appendChild(closeButton);
    }

    function cssRootVar(name, fallback) {
        var value = getComputedStyle(document.documentElement).getPropertyValue(name).trim();
        return value || fallback;
    }

    // Kartın altındaki değerler (kullanıcının Ayarlar sayfasında seçtiği metrikler) her biri kendi
    // satırına yazılır — " · " ile yan yana birleştirmek, üç veya dört metrik birden seçildiğinde
    // kart genişliğini aşıp bir sonraki karta taşıyordu. Satır başına tek metrik olduğunda kart
    // genişliğini hiçbir zaman aşmaz; kart yüksekliği de satır sayısına göre çağıran taraf ayarlar.
    var SNAPSHOT_SUB_LINE_HEIGHT = 22;

    function snapshotBlockHeight(subLineCount) {
        return Math.max(150, 116 + subLineCount * SNAPSHOT_SUB_LINE_HEIGHT);
    }

    function drawSnapshotBlock(ctx, x, y, w, h, accent, cardBg, textColor, mutedColor, label, value, subLines) {
        ctx.fillStyle = cardBg;
        ctx.beginPath();
        ctx.roundRect(x, y, w, h, 14);
        ctx.fill();

        ctx.fillStyle = accent;
        ctx.beginPath();
        ctx.roundRect(x, y, w, 5, [14, 14, 0, 0]);
        ctx.fill();

        ctx.textAlign = "left";
        ctx.fillStyle = mutedColor;
        ctx.font = "700 15px 'Segoe UI', sans-serif";
        ctx.textBaseline = "top";
        ctx.fillText(label.toUpperCase(), x + 20, y + 20);

        ctx.fillStyle = textColor;
        ctx.font = "800 40px 'Segoe UI', sans-serif";
        ctx.fillText(value, x + 20, y + 44);

        ctx.fillStyle = mutedColor;
        ctx.font = "500 16px 'Segoe UI', sans-serif";
        subLines.forEach(function (line, index) {
            ctx.fillText(line, x + 20, y + 100 + index * SNAPSHOT_SUB_LINE_HEIGHT);
        });
    }

    // Anlık Görüntü ayarlarında (Ayarlar sayfası) seçilen bir metriğin o anki değerini metne çevirir.
    // Hangi kartların/metriklerin gösterileceği ve sırası window.HwmonSnapshotSettings'te tutulur;
    // burada yalnızca metrik anahtarını gerçek mesaj alanına eşleriz.
    function snapshotMetricText(blockKey, metricKey) {
        var cpu = lastMessage.cpu || {};
        var gpu = lastMessage.gpu || {};
        var ram = lastMessage.ram || {};
        var fps = lastMessage.fps || {};
        var storage = (lastMessage.storages || [])[0] || {};

        switch (blockKey + ":" + metricKey) {
            case "cpu:usage": return fmt(cpu.totalLoadPercent, "%", 1);
            case "cpu:temp": return fmt(cpu.packageTemperatureC, "°C", 1);
            case "cpu:power": return fmt(cpu.packagePowerWatts, "W", 1);
            case "cpu:clock": return fmt(cpu.packageClockMhz, "MHz", 0);
            case "gpu:usage": return fmt(gpu.loadPercent, "%", 1);
            case "gpu:temp": return fmt(gpu.coreTemperatureC, "°C", 1);
            case "gpu:power": return fmt(gpu.powerWatts, "W", 1);
            case "gpu:clock": return fmt(gpu.coreClockMhz, "MHz", 0);
            case "ram:usage": return fmt(ram.usedPercent, "%", 1);
            case "ram:detail": return formatRamDetail(ram.usedGb, ram.totalGb);
            case "fps:value": return typeof fps.framesPerSecond === "number" ? fps.framesPerSecond.toFixed(0) : "--";
            case "fps:process": return fps.sourceProcessName || "Ölçülemiyor";
            case "storage:usage": return fmt(storage.usedPercent, "%", 1);
            case "storage:temp": return fmt(storage.temperatureC, "°C", 1);
            default: return "--";
        }
    }

    function buildSnapshotCanvas() {
        var settingsApi = window.HwmonSnapshotSettings;
        var snapSettings = settingsApi ? settingsApi.load() : null;

        // Tasarım her zaman 1200 birimlik mantıksal genişlik üzerine çizilir; Ayarlar'da seçilen
        // çözünürlük (varsayılan 1080p, ayrıca 2K/4K) yalnızca bunun kaç fiziksel piksele
        // büyütülerek dışa aktarılacağını belirler — sabit 1200x630'luk küçük tuval bulanık PNG'ye
        // yol açıyordu.
        var resKey = (snapSettings && snapSettings.resolution) || (settingsApi && settingsApi.defaultResolution) || "1080p";
        var res = (settingsApi && settingsApi.resolutions[resKey]) || { w: 1920, h: 1080 };
        var scale = res.w / 1200;
        var designHeight = res.h / scale;

        var canvas = document.createElement("canvas");
        canvas.width = res.w;
        canvas.height = res.h;
        var ctx = canvas.getContext("2d");
        ctx.scale(scale, scale);

        // Anlık görüntü, o an ekranda seçili olan temanın (gündüz/gece) renklerini kullanır;
        // sabit renkler kullanılırsa gündüz modunda alınan görüntü de gece modu gibi çıkar.
        var bg = cssRootVar("--bg", "#0b0f14");
        var bgElevated = cssRootVar("--bg-elevated", "#151b26");
        var cardBg = cssRootVar("--card-bg", "#141c28");
        var textColor = cssRootVar("--text", "#e7edf6");
        var mutedColor = cssRootVar("--text-muted", "#8ea0b8");

        var gradient = ctx.createLinearGradient(0, 0, 1200, designHeight);
        gradient.addColorStop(0, bg);
        gradient.addColorStop(1, bgElevated);
        ctx.fillStyle = gradient;
        ctx.fillRect(0, 0, 1200, designHeight);

        ctx.textAlign = "left";
        ctx.textBaseline = "top";
        ctx.fillStyle = textColor;
        ctx.font = "800 34px 'Segoe UI', sans-serif";
        ctx.fillText("HardwareMonitorByYuinn", 48, 44);

        var systemInfo = window.__systemInfo || {};
        ctx.fillStyle = mutedColor;
        ctx.font = "500 17px 'Segoe UI', sans-serif";
        ctx.fillText(new Date().toLocaleString("tr-TR") + (systemInfo.machineName ? "  ·  " + systemInfo.machineName : ""), 48, 88);

        var specLine = [systemInfo.cpuName].concat(systemInfo.gpuNames || []).filter(Boolean).join("   ·   ");
        ctx.fillText(specLine, 48, 112);

        var accentVars = {
            cpu: "--accent-cpu", gpu: "--accent-gpu", ram: "--accent-ram",
            fps: "--accent-fps", storage: "--accent-storage"
        };
        var accentFallbacks = { cpu: "#ff9f45", gpu: "#35d0ba", ram: "#6ea8fe", fps: "#c792ea", storage: "#f2c14e" };
        var blockLabelFallbacks = { cpu: "İşlemci", gpu: "Ekran Kartı", ram: "Bellek", fps: "Kare Hızı", storage: "Depolama" };

        var blockOrder = snapSettings ? snapSettings.blockOrder : Object.keys(accentVars);

        var blocks = blockOrder
            .filter(function (key) { return !snapSettings || snapSettings.blocksEnabled[key]; })
            .map(function (key) {
                var metricOrder = snapSettings ? snapSettings.metricOrder[key] : [];
                var texts = metricOrder
                    .filter(function (metricKey) { return !snapSettings || snapSettings.metricsEnabled[key][metricKey]; })
                    .map(function (metricKey) { return snapshotMetricText(key, metricKey); });

                return {
                    accent: cssRootVar(accentVars[key], accentFallbacks[key]),
                    label: (settingsApi && settingsApi.blockLabels[key]) || blockLabelFallbacks[key],
                    value: texts[0] || "--",
                    subLines: texts.slice(1)
                };
            });

        if (blocks.length > 0) {
            var gap = 20;
            var blockW = (1200 - 48 * 2 - gap * (blocks.length - 1)) / blocks.length;

            // Tüm kartlar aynı satırda göründüğü için hizalı durmaları adına ortak bir yükseklik
            // kullanılır; yükseklik, en çok alt satıra sahip kartın ihtiyacına göre belirlenir.
            var maxSubLines = blocks.reduce(function (max, b) { return Math.max(max, b.subLines.length); }, 0);
            var blockH = snapshotBlockHeight(maxSubLines);

            blocks.forEach(function (b, i) {
                drawSnapshotBlock(ctx, 48 + i * (blockW + gap), 170, blockW, blockH, b.accent, cardBg, textColor, mutedColor, b.label, b.value, b.subLines);
            });
        }

        return canvas;
    }

    // Anlık görüntüyü doğrudan indirmek yerine önce ekranın ortasında önizleme olarak gösteririz;
    // kullanıcı beğenirse indirir, beğenmezse kapatır. Ayrıca renk/düzen değişikliklerini test ederken
    // her seferinde indirilenler klasörünü açmaya gerek kalmaz.
    function showSnapshotPreview(canvas) {
        var dataUrl = canvas.toDataURL("image/png");

        var backdrop = document.createElement("div");
        backdrop.className = "snapshot-modal-backdrop";

        var modal = document.createElement("div");
        modal.className = "snapshot-modal";

        // Font glyph'i ("×") kullanan bir buton, fontun satır kutusu içindeki asimetrik konumu
        // yüzünden flex ile ortalansa bile hafif kaymış durur. SVG çizgileriyle çizilen bir çarpı,
        // viewBox koordinatlarına göre piksel bazında gerçekten ortalanır.
        var closeButton = document.createElement("button");
        closeButton.type = "button";
        closeButton.className = "snapshot-modal-close";
        closeButton.innerHTML = '<svg width="16" height="16" viewBox="0 0 16 16" aria-hidden="true">' +
            '<line x1="3" y1="3" x2="13" y2="13" stroke="currentColor" stroke-width="2" stroke-linecap="round"></line>' +
            '<line x1="13" y1="3" x2="3" y2="13" stroke="currentColor" stroke-width="2" stroke-linecap="round"></line>' +
            '</svg>';
        closeButton.setAttribute("aria-label", "Kapat");

        var img = document.createElement("img");
        img.className = "snapshot-modal-image";
        img.src = dataUrl;
        img.alt = "Anlık görüntü önizlemesi";

        var downloadLink = document.createElement("a");
        downloadLink.className = "snapshot-modal-download";
        downloadLink.href = dataUrl;
        downloadLink.download = "hardwaremonitor-" + Date.now() + ".png";
        downloadLink.textContent = "⬇ İndir";

        function close() {
            document.removeEventListener("keydown", onKeydown);
            backdrop.remove();
        }

        function onKeydown(e) {
            if (e.key === "Escape") close();
        }

        closeButton.addEventListener("click", close);
        downloadLink.addEventListener("click", function () { setTimeout(close, 50); });
        backdrop.addEventListener("click", function (e) {
            if (e.target === backdrop) close();
        });
        document.addEventListener("keydown", onKeydown);

        modal.appendChild(closeButton);
        modal.appendChild(img);
        modal.appendChild(downloadLink);
        backdrop.appendChild(modal);
        document.body.appendChild(backdrop);
    }

    function shareSnapshot() {
        if (!lastMessage) return;
        showSnapshotPreview(buildSnapshotCanvas());
    }

    // Oyun Profilleri'nde "Oyun kapanınca otomatik anlık görüntü al" açıksa çağrılır — önizleme
    // penceresi göstermeden (kullanıcı o an ekranda olmayabilir) doğrudan PNG'yi indirir.
    function autoDownloadSnapshot() {
        if (!lastMessage) return;
        var canvas = buildSnapshotCanvas();
        var link = document.createElement("a");
        link.href = canvas.toDataURL("image/png");
        link.download = "hardwaremonitor-oyun-sonu-" + Date.now() + ".png";
        document.body.appendChild(link);
        link.click();
        link.remove();
    }

    var snapshotButton = document.getElementById("snapshot-share");
    if (snapshotButton) snapshotButton.addEventListener("click", shareSnapshot);

    var connection = window.HwmonHub.get();

    connection.on("hardwareUpdate", function (message) {
        lastMessage = message;
        var t = new Date(message.timestampUtc).getTime();
        var healthConcerns = [];
        var healthLevels = [];

        if (message.cpu) {
            setText("cpu-usage", fmt(message.cpu.totalLoadPercent, "%", 1));
            setText("cpu-clock", fmt(message.cpu.packageClockMhz, "MHz", 0));
            setText("cpu-temp", fmt(message.cpu.packageTemperatureC, "°C", 1));
            setText("cpu-power", fmt(message.cpu.packagePowerWatts, "W", 1));
            chart.push("cpu", t, message.cpu.totalLoadPercent);

            setText("ambient-cpu", fmt(message.cpu.totalLoadPercent, "%", 1));
            setText("ambient-cpu-sub", fmt(message.cpu.packageTemperatureC, "°C", 1) + " · " + fmt(message.cpu.packagePowerWatts, "W", 1));

            var cpuMetric = HwmonThresholds.metrics.cpu[WARNING_THRESHOLDS.cpu.metric];
            var cpuMetricValue = message.cpu[cpuMetric.field];
            var cpuLevel = HwmonThresholds.levelFor(cpuMetricValue, WARNING_THRESHOLDS.cpu.cautionValue, WARNING_THRESHOLDS.cpu.value);
            setCardWarningBySeries("cpu", cpuLevel);
            healthLevels.push(cpuLevel);
            if (cpuLevel === "critical") healthConcerns.push("İşlemci " + cpuMetric.label.toLowerCase() + " yüksek (" + fmt(cpuMetricValue, cpuMetric.unit, 1).trim() + ")");
            var cpuAnomaly = checkAnomaly("cpu", cpuMetricValue, "İşlemci " + cpuMetric.label.toLowerCase() + "ı", cpuMetric.unit, 1);
            if (cpuAnomaly) healthConcerns.push(cpuAnomaly);
        }

        if (message.gpu) {
            setText("gpu-usage", fmt(message.gpu.loadPercent, "%", 1));
            setText("gpu-clock", fmt(message.gpu.coreClockMhz, "MHz", 0));
            setText("gpu-temp", fmt(message.gpu.coreTemperatureC, "°C", 1));
            setText("gpu-power", fmt(message.gpu.powerWatts, "W", 1));
            chart.push("gpu", t, message.gpu.loadPercent);

            setText("ambient-gpu", fmt(message.gpu.loadPercent, "%", 1));
            setText("ambient-gpu-sub", fmt(message.gpu.coreTemperatureC, "°C", 1) + " · " + fmt(message.gpu.powerWatts, "W", 1));

            var gpuMetric = HwmonThresholds.metrics.gpu[WARNING_THRESHOLDS.gpu.metric];
            var gpuMetricValue = message.gpu[gpuMetric.field];
            var gpuLevel = HwmonThresholds.levelFor(gpuMetricValue, WARNING_THRESHOLDS.gpu.cautionValue, WARNING_THRESHOLDS.gpu.value);
            setCardWarningBySeries("gpu", gpuLevel);
            healthLevels.push(gpuLevel);
            if (gpuLevel === "critical") healthConcerns.push("Ekran kartı " + gpuMetric.label.toLowerCase() + " yüksek (" + fmt(gpuMetricValue, gpuMetric.unit, 1).trim() + ")");
            var gpuAnomaly = checkAnomaly("gpu", gpuMetricValue, "Ekran kartı " + gpuMetric.label.toLowerCase() + "ı", gpuMetric.unit, 1);
            if (gpuAnomaly) healthConcerns.push(gpuAnomaly);

            // VRAM — bazı GPU'larda (ör. LibreHardwareMonitorLib desteklemiyorsa) bellek verisi
            // gelmeyebilir; bu durumda kart "--" göstermeye devam eder, hata vermez.
            if (typeof message.gpu.memoryUsedMb === "number" && typeof message.gpu.memoryTotalMb === "number" && message.gpu.memoryTotalMb > 0) {
                var vramPercent = (message.gpu.memoryUsedMb / message.gpu.memoryTotalMb) * 100;
                setText("vram-usage", fmt(vramPercent, "%", 1));
                setText("vram-detail", fmt(message.gpu.memoryUsedMb, "", 0).trim() + " / " + fmt(message.gpu.memoryTotalMb, "", 0).trim() + " MB");
                chart.push("vram", t, vramPercent);
            } else {
                setText("vram-usage", "-- %");
                setText("vram-detail", "-- / -- MB");
            }
        }

        if (message.ram) {
            setText("ram-usage", fmt(message.ram.usedPercent, "%", 1));
            setText("ram-detail", formatRamDetail(message.ram.usedGb, message.ram.totalGb));
            chart.push("ram", t, message.ram.usedPercent);

            setText("ambient-ram", fmt(message.ram.usedPercent, "%", 1));
            setText("ambient-ram-sub", formatRamDetail(message.ram.usedGb, message.ram.totalGb));

            var ramLevel = HwmonThresholds.levelFor(message.ram.usedPercent, WARNING_THRESHOLDS.ramCautionPercent, WARNING_THRESHOLDS.ramUsedPercent);
            setCardWarningBySeries("ram", ramLevel);
            healthLevels.push(ramLevel);
            if (ramLevel === "critical") healthConcerns.push("Bellek kullanımı yüksek (" + fmt(message.ram.usedPercent, "%", 1).trim() + ")");
            var ramAnomaly = checkAnomaly("ram", message.ram.usedPercent, "Bellek kullanımı", "%", 1);
            if (ramAnomaly) healthConcerns.push(ramAnomaly);
        }

        updateEnergyCost(
            message.cpu ? message.cpu.packagePowerWatts : null,
            message.gpu ? message.gpu.powerWatts : null);

        var fps = message.fps || {};
        applyGameProfile(fps.sourceProcessName);
        setText("fps-value", typeof fps.framesPerSecond === "number" ? fps.framesPerSecond.toFixed(1) : "--");
        setText("fps-source", fps.sourceProcessName || "Ölçülemiyor");
        chart.push("fps", t, fps.framesPerSecond);
        chart.push("frametime", t, fps.frameTimeMs);
        chart.push("fpsLow1", t, fps.low1PercentFps);
        chart.push("fpsLow01", t, fps.lowPoint1PercentFps);

        // Başlıklar veri olmasa da (FPS kartındaki büyük değer gibi) hep görünür kalır, yalnızca
        // sayı "--" olur; %1/%0.1 low en az ~10 saniyelik kare geçmişi birikene kadar null döner
        // (bkz. ProcessPresentCounter).
        setText("fps-low1", "1% Low " + fmt(fps.low1PercentFps, "", 0).trim());
        setText("fps-low01", "0.1% Low " + fmt(fps.lowPoint1PercentFps, "", 0).trim());
        setText("fps-frametime", "Kare Süresi " + fmt(fps.frameTimeMs, "ms", 1));

        setText("ambient-fps", typeof fps.framesPerSecond === "number" ? fps.framesPerSecond.toFixed(0) : "--");
        setText("ambient-fps-sub", fps.sourceProcessName || "Ölçülemiyor");

        updateTabTitle(message.cpu && message.cpu.totalLoadPercent, message.gpu && message.gpu.loadPercent);

        renderStorageCard(message.storages);

        var primaryStorage = (Array.isArray(message.storages) ? message.storages : [])[0] || {};
        setText("ambient-storage", fmt(primaryStorage.usedPercent, "%", 1));
        setText("ambient-storage-sub", fmt(primaryStorage.temperatureC, "°C", 1));

        renderProcessesCard(message.topProcesses);
        if (!window.HwmonMemoryLeakDetection || HwmonMemoryLeakDetection.isEnabled()) {
            checkMemoryLeaks(message.topProcesses).forEach(function (w) { healthConcerns.push(w); });
        }

        var storages = Array.isArray(message.storages) ? message.storages : [];
        var maxDiskTemp = storages.reduce(function (max, s) {
            return typeof s.temperatureC === "number" && s.temperatureC > max ? s.temperatureC : max;
        }, -Infinity);
        var diskLevel = HwmonThresholds.levelFor(maxDiskTemp === -Infinity ? null : maxDiskTemp, WARNING_THRESHOLDS.diskCautionTempC, WARNING_THRESHOLDS.diskTempC);
        var storageCard = document.getElementById("storage-card");
        if (storageCard) {
            storageCard.classList.toggle("is-caution", diskLevel === "caution");
            storageCard.classList.toggle("is-warning", diskLevel === "critical");
        }
        if (maxDiskTemp !== -Infinity) healthLevels.push(diskLevel);
        if (diskLevel === "critical") healthConcerns.push("Disk sıcaklığı yüksek (" + maxDiskTemp.toFixed(1) + " °C)");

        storages.forEach(function (s) {
            if (s.smartStatus === "Kötü") healthLevels.push("critical");
            else if (s.smartStatus === "Dikkat") healthLevels.push("caution");
            if (s.smartStatus === "Dikkat" || s.smartStatus === "Kötü") {
                healthConcerns.push((s.name || "Disk") + " disk sağlığı durumu: " + s.smartStatus);
            }
        });

        renderHealthSummary(healthConcerns, HwmonThresholds.scoreFromLevels(healthLevels));

        var network = message.network;
        setText("network-download", "↓ " + formatRateMBs(network && network.downloadBytesPerSec));
        setText("network-upload", "↑ " + formatRateMBs(network && network.uploadBytesPerSec));
        chart.push("download", t, toMBs(network && network.downloadBytesPerSec));
        chart.push("upload", t, toMBs(network && network.uploadBytesPerSec));

        if (network && network.adapterName) {
            var speedText = typeof network.linkSpeedMbps === "number"
                ? (network.linkSpeedMbps >= 1000 ? (network.linkSpeedMbps / 1000).toFixed(0) + " Gbps" : network.linkSpeedMbps.toFixed(0) + " Mbps")
                : "";
            setText("network-adapter", network.adapterName + (speedText ? " · " + speedText : ""));
        } else {
            setText("network-adapter", "--");
        }

        setText("network-session-total", "Bu oturum: ↓ " + fmt(network && network.totalDownloadedGb, "GB", 1) +
            " · ↑ " + fmt(network && network.totalUploadedGb, "GB", 1));

        chart.render();
        // Değer basamak sayısı değişince (ör. 9°C → 105°C) satır kart genişliğine sığmayabilir.
        if (window.HwmonFitStatRows) window.HwmonFitStatRows.fitAll();
    });

    window.HwmonHub.start().catch(function (err) {
        console.error("SignalR bağlantısı kurulamadı", err);
    });
})();
