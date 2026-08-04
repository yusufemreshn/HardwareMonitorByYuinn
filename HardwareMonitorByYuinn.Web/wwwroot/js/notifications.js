(function () {
    "use strict";

    var api = window.HwmonNotificationHistory;
    if (!api) return;

    // Sabit sırada gösterilir ki eşik hiç aşılmamış olsa bile listede "0 kez" olarak görünsün;
    // kullanıcı böylece hangi kategorinin hiç sorun çıkarmadığını da görebilir.
    var KEY_ORDER = ["cpu", "gpu", "ram", "storage"];
    var KEY_LABELS = { cpu: "İşlemci", gpu: "Ekran Kartı", ram: "Bellek", storage: "Depolama" };
    var KEY_ACCENT_VARS = { cpu: "--accent-cpu", gpu: "--accent-gpu", ram: "--accent-ram", storage: "--accent-storage" };

    function formatTimestamp(iso) {
        var date = new Date(iso);
        return isNaN(date.getTime()) ? "--" : date.toLocaleString("tr-TR");
    }

    function renderSummary(list) {
        var container = document.getElementById("notification-summary");
        if (!container) return;
        container.textContent = "";

        KEY_ORDER.forEach(function (key) {
            var entries = list.filter(function (e) { return e.key === key; });

            var row = document.createElement("div");
            row.className = "order-item";

            var labelWrap = document.createElement("span");
            labelWrap.className = "order-item-label";

            var dot = document.createElement("span");
            dot.className = "stat-dot";
            dot.style.background = "var(" + KEY_ACCENT_VARS[key] + ")";
            labelWrap.appendChild(dot);

            var label = document.createElement("span");
            label.textContent = KEY_LABELS[key] || key;
            labelWrap.appendChild(label);
            row.appendChild(labelWrap);

            var detail = document.createElement("span");
            detail.className = "stat-sub";
            detail.style.margin = "0";
            detail.textContent = entries.length === 0
                ? "Hiç aşılmadı"
                : entries.length + " kez · son " + formatTimestamp(entries[0].timestampIso);
            row.appendChild(detail);

            container.appendChild(row);
        });
    }

    // Liste (en yeni en üstte) varsayılan olarak yalnızca son 15 kaydı gösterir; "Daha fazla göster"
    // tümünü açar ve butonu "Daha az göster"e çevirir, tekrar tıklanınca son 15'e geri döner.
    var VISIBLE_LIMIT = 15;
    var showAll = false;

    function renderList(list) {
        var container = document.getElementById("notification-history-list");
        if (!container) return;
        container.textContent = "";

        if (list.length === 0) {
            var empty = document.createElement("div");
            empty.className = "stat-sub";
            empty.textContent = "Henüz bir eşik aşılmadı.";
            container.appendChild(empty);
            return;
        }

        var visibleList = showAll ? list : list.slice(0, VISIBLE_LIMIT);

        visibleList.forEach(function (entry) {
            var row = document.createElement("div");
            row.className = "notification-entry";

            var dot = document.createElement("span");
            dot.className = "stat-dot notification-entry-dot";
            dot.style.background = "var(" + (KEY_ACCENT_VARS[entry.key] || "--accent-danger") + ")";
            row.appendChild(dot);

            var text = document.createElement("div");

            var title = document.createElement("div");
            title.className = "notification-entry-title";
            title.textContent = entry.title;
            text.appendChild(title);

            var body = document.createElement("div");
            body.className = "stat-sub";
            body.textContent = entry.body;
            text.appendChild(body);

            row.appendChild(text);

            var time = document.createElement("div");
            time.className = "notification-entry-time";
            time.textContent = formatTimestamp(entry.timestampIso);
            row.appendChild(time);

            container.appendChild(row);
        });

        if (list.length > VISIBLE_LIMIT) {
            var toggleWrap = document.createElement("div");
            toggleWrap.style.textAlign = "center";
            toggleWrap.style.marginTop = "10px";

            var toggleButton = document.createElement("button");
            toggleButton.type = "button";
            toggleButton.className = "legend-item";
            toggleButton.textContent = showAll ? "Daha az göster" : "Daha fazla göster (" + (list.length - VISIBLE_LIMIT) + " kayıt daha)";
            toggleButton.addEventListener("click", function () {
                showAll = !showAll;
                renderList(list);
            });

            toggleWrap.appendChild(toggleButton);
            container.appendChild(toggleWrap);
        }
    }

    function renderAll() {
        var list = api.load();
        renderSummary(list);
        renderList(list);
    }

    renderAll();
    document.addEventListener("hwmon:notification-history-changed", renderAll);

    var clearButton = document.getElementById("notification-history-clear");
    if (clearButton) {
        clearButton.addEventListener("click", function () {
            api.clear();
            renderAll();
        });
    }
})();
