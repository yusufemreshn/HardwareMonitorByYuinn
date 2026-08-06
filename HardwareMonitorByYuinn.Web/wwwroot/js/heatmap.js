(function () {
    "use strict";

    var daysSelect = document.getElementById("heatmap-days");
    var metricSelect = document.getElementById("heatmap-metric");
    var table = document.getElementById("heatmap-table");
    var legend = document.getElementById("heatmap-legend");
    if (!daysSelect || !table) return;

    // Tipik bir dizüstü bilgisayarın boşta/yükte gördüğü makul sıcaklık aralığı; sabit tutulur ki
    // farklı gün aralıkları arasında renkler karşılaştırılabilir kalsın.
    var MIN_TEMP = 30;
    var MAX_TEMP = 90;

    // Tek bir renk tonu (turuncu-kırmızı), yalnızca açıklık ekseninde değişir — "sequential" bir
    // ölçek: gökkuşağı değil, soğuktan sıcağa tek tonun açıktan koyuya gitmesi.
    function tempColor(temp) {
        var clamped = Math.max(MIN_TEMP, Math.min(MAX_TEMP, temp));
        var ratio = (clamped - MIN_TEMP) / (MAX_TEMP - MIN_TEMP);
        var lightness = 88 - ratio * 55; // %88 (serin) -> %33 (sıcak)
        return "hsl(14, 82%, " + lightness.toFixed(0) + "%)";
    }

    function textColorFor(temp) {
        var clamped = Math.max(MIN_TEMP, Math.min(MAX_TEMP, temp));
        var ratio = (clamped - MIN_TEMP) / (MAX_TEMP - MIN_TEMP);
        return ratio > 0.55 ? "#ffffff" : "#1a1006";
    }

    function render(buckets, metric) {
        table.innerHTML = "";

        var byDate = {};
        buckets.forEach(function (b) {
            if (!byDate[b.date]) byDate[b.date] = {};
            byDate[b.date][b.hour] = metric === "gpu" ? b.avgGpuTemp : b.avgCpuTemp;
        });

        var dates = Object.keys(byDate).sort();
        if (dates.length === 0) {
            var emptyRow = document.createElement("tr");
            var emptyCell = document.createElement("td");
            emptyCell.className = "stat-sub";
            emptyCell.textContent = "Bu aralıkta veri yok.";
            emptyRow.appendChild(emptyCell);
            table.appendChild(emptyRow);
            return;
        }

        var thead = document.createElement("thead");
        var headRow = document.createElement("tr");
        var cornerTh = document.createElement("th");
        cornerTh.textContent = "Tarih";
        headRow.appendChild(cornerTh);
        for (var h = 0; h < 24; h++) {
            var th = document.createElement("th");
            th.textContent = (h < 10 ? "0" : "") + h;
            th.style.textAlign = "center";
            headRow.appendChild(th);
        }
        thead.appendChild(headRow);
        table.appendChild(thead);

        var tbody = document.createElement("tbody");
        dates.forEach(function (date) {
            var row = document.createElement("tr");
            var dateCell = document.createElement("td");
            dateCell.textContent = date.slice(8, 10) + "." + date.slice(5, 7);
            dateCell.style.whiteSpace = "nowrap";
            row.appendChild(dateCell);

            for (var hour = 0; hour < 24; hour++) {
                var temp = byDate[date][hour];
                var cell = document.createElement("td");
                cell.style.textAlign = "center";
                cell.style.fontVariantNumeric = "tabular-nums";
                if (typeof temp === "number") {
                    cell.style.background = tempColor(temp);
                    cell.style.color = textColorFor(temp);
                    cell.textContent = temp.toFixed(0);
                    cell.title = date + " " + (hour < 10 ? "0" : "") + hour + ":00 · " + temp.toFixed(1) + " °C";
                } else {
                    cell.style.background = "var(--bg-elevated)";
                    cell.title = "Veri yok";
                }
                row.appendChild(cell);
            }
            tbody.appendChild(row);
        });
        table.appendChild(tbody);
    }

    function renderLegend() {
        legend.innerHTML = "";

        var minLabel = document.createElement("span");
        minLabel.className = "stat-sub";
        minLabel.style.marginRight = "8px";
        minLabel.textContent = MIN_TEMP + " °C";
        legend.appendChild(minLabel);

        var strip = document.createElement("span");
        strip.className = "heatmap-legend-strip";
        var steps = 10;
        for (var i = 0; i <= steps; i++) {
            var temp = MIN_TEMP + (i / steps) * (MAX_TEMP - MIN_TEMP);
            var seg = document.createElement("span");
            seg.style.background = tempColor(temp);
            strip.appendChild(seg);
        }
        legend.appendChild(strip);

        var maxLabel = document.createElement("span");
        maxLabel.className = "stat-sub";
        maxLabel.style.marginLeft = "8px";
        maxLabel.textContent = MAX_TEMP + " °C";
        legend.appendChild(maxLabel);
    }

    function load() {
        var days = daysSelect.value;
        var metric = metricSelect.value;

        fetch("/History/Heatmap?days=" + days)
            .then(function (res) { return res.json(); })
            .then(function (buckets) { render(buckets, metric); });
    }

    daysSelect.addEventListener("change", load);
    metricSelect.addEventListener("change", load);

    renderLegend();
    load();
})();
