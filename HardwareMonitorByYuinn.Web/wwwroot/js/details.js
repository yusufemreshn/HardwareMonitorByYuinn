(function () {
    "use strict";

    var fmt = window.HwmonFormat.fmt;

    function buildRow(label, avg, unit, digits) {
        avg = avg || {};
        var tr = document.createElement("tr");
        [label,
            fmt(avg.last1Min, unit, digits), fmt(avg.last2Min, unit, digits), fmt(avg.last5Min, unit, digits),
            fmt(avg.last10Min, unit, digits), fmt(avg.last15Min, unit, digits), fmt(avg.lifetime, unit, digits)
        ].forEach(function (text) {
            var td = document.createElement("td");
            td.textContent = text;
            tr.appendChild(td);
        });
        return tr;
    }

    function setRows(bodyId, rows) {
        var body = document.getElementById(bodyId);
        if (!body) return;
        body.textContent = "";
        rows.forEach(function (row) { body.appendChild(row); });
    }

    function renderAverages(averages) {
        setRows("cpu-aggregate-averages-body", [
            buildRow("Kullanım", averages.cpu.usagePercent, "%", 1),
            buildRow("Frekans", averages.cpu.clockMhz, "MHz", 0),
            buildRow("Güç Tüketimi", averages.cpu.powerWatts, "W", 1),
            buildRow("Sıcaklık", averages.cpu.temperatureC, "°C", 1)
        ]);

        if (averages.cpu.cores) {
            var coreRows = [];
            averages.cpu.cores
                .slice()
                .sort(function (a, b) { return a.coreIndex - b.coreIndex; })
                .forEach(function (core) {
                    coreRows.push(buildRow("Çekirdek " + core.coreIndex + " - Frekans", core.clockMhz, "MHz", 0));
                    coreRows.push(buildRow("Çekirdek " + core.coreIndex + " - Kullanım", core.loadPercent, "%", 1));
                    coreRows.push(buildRow("Çekirdek " + core.coreIndex + " - Güç", core.powerWatts, "W", 1));
                });
            setRows("cpu-core-averages-body", coreRows);
        }

        setRows("gpu-averages-body", [
            buildRow("Kullanım", averages.gpu.usagePercent, "%", 1),
            buildRow("Çekirdek Frekansı", averages.gpu.coreClockMhz, "MHz", 0),
            buildRow("Normal Sıcaklık", averages.gpu.coreTemperatureC, "°C", 1),
            buildRow("Hotspot Sıcaklık", averages.gpu.hotSpotTemperatureC, "°C", 1),
            buildRow("Güç Tüketimi", averages.gpu.powerWatts, "W", 1)
        ]);

        setRows("ram-averages-body", [buildRow("Kullanım", averages.ram.usagePercent, "%", 1)]);

        if (averages.fps) {
            setRows("fps-averages-body", [
                buildRow("Kare Hızı", averages.fps.framesPerSecond, "FPS", 1),
                buildRow("%1 Low", averages.fps.low1Percent, "FPS", 1),
                buildRow("%0.1 Low", averages.fps.lowPoint1Percent, "FPS", 1)
            ]);
        }

        setRows("general-averages-body", [
            buildRow("İşlemci Kullanımı", averages.cpu.usagePercent, "%", 1),
            buildRow("İşlemci Frekansı", averages.cpu.clockMhz, "MHz", 0),
            buildRow("İşlemci Güç Tüketimi", averages.cpu.powerWatts, "W", 1),
            buildRow("İşlemci Sıcaklığı", averages.cpu.temperatureC, "°C", 1),
            buildRow("Ekran Kartı Kullanımı", averages.gpu.usagePercent, "%", 1),
            buildRow("Ekran Kartı Frekansı", averages.gpu.coreClockMhz, "MHz", 0),
            buildRow("Ekran Kartı Sıcaklığı", averages.gpu.coreTemperatureC, "°C", 1),
            buildRow("Ekran Kartı Güç Tüketimi", averages.gpu.powerWatts, "W", 1),
            buildRow("Bellek Kullanımı", averages.ram.usagePercent, "%", 1),
            buildRow("Kare Hızı", averages.fps.framesPerSecond, "FPS", 1),
            buildRow("Ağ İndirme Hızı", toMbs(averages.network.downloadBytesPerSec), "MB/s", 1),
            buildRow("Ağ Yükleme Hızı", toMbs(averages.network.uploadBytesPerSec), "MB/s", 1)
        ]);
    }

    // Ortalamalar bayt/sn olarak gelir; kart MB/s beklediği için her pencereyi tek tek dönüştürür.
    function toMbs(avg) {
        avg = avg || {};
        function conv(v) { return typeof v === "number" ? v / 1048576 : v; }
        return {
            last1Min: conv(avg.last1Min), last2Min: conv(avg.last2Min), last5Min: conv(avg.last5Min),
            last10Min: conv(avg.last10Min), last15Min: conv(avg.last15Min), lifetime: conv(avg.lifetime)
        };
    }

    function refreshAverages() {
        fetch("/Details/Averages")
            .then(function (r) { return r.json(); })
            .then(renderAverages)
            .catch(function (err) { console.error("Ortalamalar alınamadı", err); });
    }

    setInterval(refreshAverages, 5000);

    function setText(id, text) {
        var el = document.getElementById(id);
        if (el) {
            el.textContent = text;
        }
    }

    // Değer okunamadığında birimi de gizleriz; "-- W" okuyucuya sıfır watt ölçüldüğü izlenimi verir.
    function setValue(id, value, unit, digits) {
        setText(id, fmt(value, unit, digits));
    }

    var connection = window.HwmonHub.get();

    connection.on("hardwareUpdate", function (message) {
        if (message.cpu) {
            setValue("cpu-live-usage", message.cpu.totalLoadPercent, "%", 1);
            setValue("cpu-live-clock", message.cpu.packageClockMhz, "MHz", 0);
            setValue("cpu-live-power", message.cpu.packagePowerWatts, "W", 1);
            setValue("cpu-live-temp", message.cpu.packageTemperatureC, "°C", 1);
            setText("cpu-clock-source", message.cpu.clockSource || "");
            setText("cpu-temp-source", message.cpu.temperatureSource || "");

            (message.cpu.cores || []).forEach(function (core) {
                setValue("cpu-core-" + core.coreIndex + "-clock", core.clockMhz, "MHz", 0);
                setValue("cpu-core-" + core.coreIndex + "-load", core.loadPercent, "%", 1);
                setValue("cpu-core-" + core.coreIndex + "-power", core.powerWatts, "W", 1);
            });
        }

        if (message.gpu) {
            setValue("gpu-live-usage", message.gpu.loadPercent, "%", 1);
            setValue("gpu-live-core-clock", message.gpu.coreClockMhz, "MHz", 0);
            setValue("gpu-live-mem-clock", message.gpu.memoryClockMhz, "MHz", 0);
            setValue("gpu-live-core-temp", message.gpu.coreTemperatureC, "°C", 1);
            setValue("gpu-live-hotspot", message.gpu.hotSpotTemperatureC, "°C", 1);
            setValue("gpu-live-power", message.gpu.powerWatts, "W", 1);
            setValue("gpu-live-vram", message.gpu.memoryUsedMb, "MB", 0);

            var vramTotal = document.getElementById("gpu-live-vram-total");
            if (vramTotal) {
                vramTotal.textContent = typeof message.gpu.memoryTotalMb === "number"
                    ? "toplam " + message.gpu.memoryTotalMb.toFixed(0) + " MB"
                    : "";
            }
        }

        if (message.ram) {
            setValue("ram-live-usage", message.ram.usedPercent, "%", 1);
            setValue("ram-live-clock", message.ram.clockMhz, "MHz", 0);
        }

        setText("fps-live-value", typeof (message.fps || {}).framesPerSecond === "number"
            ? message.fps.framesPerSecond.toFixed(1)
            : "--");
        setText("fps-live-source", (message.fps || {}).sourceProcessName || "Ölçülemiyor");
        setValue("fps-live-frametime", (message.fps || {}).frameTimeMs, "ms", 1);
        setValue("fps-live-low1", (message.fps || {}).low1PercentFps, "FPS", 0);
        setValue("fps-live-low01", (message.fps || {}).lowPoint1PercentFps, "FPS", 0);
    });

    window.HwmonHub.start().catch(function (err) {
        console.error("SignalR bağlantısı kurulamadı", err);
    });
})();
