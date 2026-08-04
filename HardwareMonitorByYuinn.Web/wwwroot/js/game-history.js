(function () {
    "use strict";

    var select = document.getElementById("game-history-select");
    var body = document.getElementById("game-history-body");
    if (!select || !body) return;

    var fmt = window.HwmonFormat.fmt;

    function emptyRow(text) {
        body.textContent = "";
        var tr = document.createElement("tr");
        var td = document.createElement("td");
        td.colSpan = 5;
        td.className = "stat-sub";
        td.textContent = text;
        tr.appendChild(td);
        body.appendChild(tr);
    }

    function cell(text) {
        var td = document.createElement("td");
        td.textContent = text;
        return td;
    }

    var loadSequence = 0;

    function loadSessions(processName) {
        var requestId = ++loadSequence;

        if (!processName) {
            emptyRow("Henüz kayıtlı oyun oturumu yok.");
            return;
        }

        fetch("/History/GameSessions?processName=" + encodeURIComponent(processName) + "&limit=10")
            .then(function (res) { return res.json(); })
            .then(function (sessions) {
                if (requestId !== loadSequence) return; // daha yeni bir istek zaten sonuçlandı

                if (!Array.isArray(sessions) || sessions.length === 0) {
                    emptyRow("Bu oyun için kayıt yok.");
                    return;
                }

                body.textContent = "";
                sessions.forEach(function (s) {
                    var tr = document.createElement("tr");
                    tr.appendChild(cell(new Date(s.startLocal).toLocaleString("tr-TR")));
                    tr.appendChild(cell(fmt(s.durationMinutes, "dk", 0)));
                    tr.appendChild(cell(fmt(s.averageFps, "FPS", 1)));
                    tr.appendChild(cell(fmt(s.averageCpuTemperatureC, "°C", 1)));
                    tr.appendChild(cell(fmt(s.averageGpuTemperatureC, "°C", 1)));
                    body.appendChild(tr);
                });
            })
            .catch(function () {
                if (requestId === loadSequence) emptyRow("Oturumlar yüklenemedi.");
            });
    }

    fetch("/History/GameList")
        .then(function (res) { return res.json(); })
        .then(function (names) {
            select.textContent = "";

            if (!Array.isArray(names) || names.length === 0) {
                var placeholder = document.createElement("option");
                placeholder.textContent = "Henüz oyun oturumu yok";
                select.appendChild(placeholder);
                emptyRow("Henüz kayıtlı oyun oturumu yok.");
                return;
            }

            names.forEach(function (name) {
                var option = document.createElement("option");
                option.value = name;
                option.textContent = name;
                select.appendChild(option);
            });

            loadSessions(names[0]);
        });

    select.addEventListener("change", function () { loadSessions(select.value); });
})();

// "Oyun Geçmişi → Kayıt Bilgileri" panelindeki "eski kayıtları temizle" kontrolü — paylaşılan
// uygulama site.js'teki window.HwmonRecordCleanup.
(function initGameHistoryCleanup() {
    "use strict";
    if (!window.HwmonRecordCleanup) return;

    window.HwmonRecordCleanup.wire({
        selectId: "game-history-summary-cleanup-days",
        buttonId: "game-history-summary-cleanup-btn",
        tokenSelector: "#game-history-summary-panel input[name='__RequestVerificationToken']",
        endpoint: "/History/DeleteOldestGameSessions",
        nounLabel: "oturum"
    });
})();
