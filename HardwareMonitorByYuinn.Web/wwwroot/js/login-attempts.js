(function () {
    "use strict";

    var body = document.getElementById("login-attempts-body");
    if (!body) return;

    function emptyRow(text) {
        body.textContent = "";
        var tr = document.createElement("tr");
        var td = document.createElement("td");
        td.colSpan = 3;
        td.className = "stat-sub";
        td.textContent = text;
        tr.appendChild(td);
        body.appendChild(tr);
    }

    function resultCell(entry) {
        var td = document.createElement("td");
        if (entry.success) {
            td.textContent = t("Başarılı");
            td.style.color = "#4ade80";
        } else if (entry.causedLockout) {
            td.textContent = t("Başarısız (5. denemede kilitlendi)");
            td.style.color = "var(--accent-danger)";
        } else {
            td.textContent = t("Başarısız");
            td.style.color = "var(--accent-danger)";
        }
        td.style.fontWeight = "600";
        return td;
    }

    fetch("/History/LoginAttempts?limit=100")
        .then(function (res) { return res.json(); })
        .then(function (attempts) {
            if (!Array.isArray(attempts) || attempts.length === 0) {
                emptyRow(t("Henüz hiç giriş denemesi kaydedilmedi."));
                return;
            }

            body.textContent = "";
            attempts.forEach(function (entry) {
                var tr = document.createElement("tr");

                var timeCell = document.createElement("td");
                timeCell.textContent = new Date(entry.timeLocal).toLocaleString(window.HwmonLocaleTag());
                tr.appendChild(timeCell);

                var ipCell = document.createElement("td");
                ipCell.textContent = entry.ipAddress;
                tr.appendChild(ipCell);

                tr.appendChild(resultCell(entry));
                body.appendChild(tr);
            });
        })
        .catch(function () {
            emptyRow(t("Giriş denemeleri yüklenemedi."));
        });
})();

// "Giriş Denemesi Özeti" panelindeki "eski kayıtları temizle" kontrolü, paylaşılan uygulama
// site.js'teki window.HwmonRecordCleanup.
(function initLoginAttemptsCleanup() {
    "use strict";
    if (!window.HwmonRecordCleanup) return;

    window.HwmonRecordCleanup.wire({
        selectId: "login-attempts-cleanup-days",
        buttonId: "login-attempts-cleanup-btn",
        tokenSelector: "#history-login-summary-panel input[name='__RequestVerificationToken']",
        endpoint: "/History/DeleteOldestLoginAttempts",
        nounLabel: t("deneme")
    });
})();
