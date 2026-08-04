// Sistem Raporu (yazdırılabilir/PDF'e çevrilebilir HTML sayfa) — mevcut RangeAverage/HealthTrend/
// SystemEvents endpoint'lerini (Geçmiş sayfasındakiyle aynı) yeniden kullanır, sunucu tarafında
// yeni bir agregasyon yazılmaz. Harici bir PDF kütüphanesi kullanılmaz; kullanıcı tarayıcının
// "Yazdır → PDF olarak kaydet" özelliğiyle bu sayfayı PDF'e çevirir.
(function () {
    "use strict";

    var daysSelect = document.getElementById("report-days");
    var printBtn = document.getElementById("report-print-btn");
    var summaryBody = document.getElementById("report-summary-body");
    var healthBody = document.getElementById("report-health-body");
    var eventsBody = document.getElementById("report-events-body");
    if (!daysSelect || !summaryBody || !healthBody || !eventsBody) return;

    var fmt = window.HwmonFormat.fmt;

    function cell(text) {
        var td = document.createElement("td");
        td.textContent = text;
        return td;
    }

    function pad(n) { return n < 10 ? "0" + n : String(n); }
    function toLocalParam(d) {
        return d.getFullYear() + "-" + pad(d.getMonth() + 1) + "-" + pad(d.getDate()) + "T" +
            pad(d.getHours()) + ":" + pad(d.getMinutes()) + ":" + pad(d.getSeconds());
    }

    function loadSummary(days) {
        var to = new Date();
        var from = new Date(to.getTime() - days * 24 * 60 * 60 * 1000);
        var params = new URLSearchParams({ from: toLocalParam(from), to: toLocalParam(to) });

        fetch("/History/RangeAverage?" + params.toString())
            .then(function (r) { return r.json(); })
            .then(function (avg) {
                if (!avg || avg.sampleCount === 0) {
                    summaryBody.innerHTML = '<p class="stat-sub">Bu aralıkta kayıt yok.</p>';
                    return;
                }
                var rows = [
                    ["İşlemci Kullanımı (ort.)", fmt(avg.avgCpuUsage, "%", 1)],
                    ["İşlemci Sıcaklığı (ort.)", fmt(avg.avgCpuTemp, "°C", 1)],
                    ["Ekran Kartı Kullanımı (ort.)", fmt(avg.avgGpuUsage, "%", 1)],
                    ["Ekran Kartı Sıcaklığı (ort.)", fmt(avg.avgGpuTemp, "°C", 1)],
                    ["Bellek Kullanımı (ort.)", fmt(avg.avgRamUsage, "%", 1)],
                    ["Örnek Sayısı", avg.sampleCount + " dakikalık örnek"]
                ];
                summaryBody.innerHTML = "";
                var table = document.createElement("table");
                table.className = "data-table";
                var tbody = document.createElement("tbody");
                rows.forEach(function (r) {
                    var tr = document.createElement("tr");
                    tr.appendChild(cell(r[0]));
                    var valueCell = cell(r[1]);
                    valueCell.style.fontWeight = "700";
                    tr.appendChild(valueCell);
                    tbody.appendChild(tr);
                });
                table.appendChild(tbody);
                summaryBody.appendChild(table);
            })
            .catch(function () {
                summaryBody.innerHTML = '<p class="stat-sub">Özet okunamadı.</p>';
            });
    }

    function loadHealthTrend(days) {
        fetch("/History/HealthTrend?days=" + days)
            .then(function (r) { return r.json(); })
            .then(function (daily) {
                if (!window.HwmonThresholds || daily.length === 0) {
                    healthBody.innerHTML = '<tr><td colspan="3" class="stat-sub">Bu aralıkta kayıt yok.</td></tr>';
                    return;
                }
                var thresholds = window.HwmonThresholds.load();
                // Puanlama formülü artık site.js'teki tek kaynakta (computeMetricHealthScore) —
                // eskiden burada, history.js'te ve dashboard.js'te üç kez bağımsız kopyalanmıştı.
                healthBody.innerHTML = "";
                daily.forEach(function (day) {
                    var cpuValue = thresholds.cpu.metric === "usage" ? day.avgCpuUsage : day.avgCpuTemp;
                    var gpuValue = thresholds.gpu.metric === "usage" ? day.avgGpuUsage : day.avgGpuTemp;
                    var score = window.HwmonThresholds.computeMetricHealthScore(thresholds, cpuValue, gpuValue, day.avgRamUsage);
                    var d = new Date(day.date + "T00:00:00");
                    var tr = document.createElement("tr");
                    tr.appendChild(cell(d.toLocaleDateString("tr-TR")));
                    var scoreCell = cell(score + "/100");
                    scoreCell.style.fontWeight = "700";
                    tr.appendChild(scoreCell);
                    tr.appendChild(cell(day.sampleCount + " örnek"));
                    healthBody.appendChild(tr);
                });
            })
            .catch(function () {
                healthBody.innerHTML = '<tr><td colspan="3" class="stat-sub">Trend okunamadı.</td></tr>';
            });
    }

    function loadSystemEvents(days) {
        fetch("/History/SystemEvents?days=" + days)
            .then(function (r) { return r.json(); })
            .then(function (events) {
                if (events.length === 0) {
                    eventsBody.innerHTML = '<tr><td colspan="3" class="stat-sub">Bu aralıkta olay yok.</td></tr>';
                    return;
                }
                // Rapor uzayıp gitmesin diye en fazla ilk 50 olay gösterilir.
                eventsBody.innerHTML = "";
                events.slice(0, 50).forEach(function (evt) {
                    var d = new Date(evt.timestamp);
                    var tr = document.createElement("tr");
                    tr.appendChild(cell(d.toLocaleString("tr-TR", { day: "2-digit", month: "2-digit", year: "numeric", hour: "2-digit", minute: "2-digit" })));
                    tr.appendChild(cell(evt.type));
                    tr.appendChild(cell(evt.description));
                    eventsBody.appendChild(tr);
                });
            })
            .catch(function () {
                eventsBody.innerHTML = '<tr><td colspan="3" class="stat-sub">Olaylar okunamadı.</td></tr>';
            });
    }

    function loadAll(days) {
        summaryBody.innerHTML = '<p class="stat-sub">Yükleniyor…</p>';
        healthBody.innerHTML = '<tr><td colspan="3" class="stat-sub">Yükleniyor…</td></tr>';
        eventsBody.innerHTML = '<tr><td colspan="3" class="stat-sub">Yükleniyor…</td></tr>';
        loadSummary(days);
        loadHealthTrend(days);
        loadSystemEvents(days);
    }

    daysSelect.value = String(window.__reportDays || 7);
    daysSelect.addEventListener("change", function () { loadAll(Number(daysSelect.value)); });
    if (printBtn) printBtn.addEventListener("click", function () { window.print(); });

    loadAll(Number(daysSelect.value));
})();
