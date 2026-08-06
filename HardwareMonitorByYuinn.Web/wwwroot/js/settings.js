(function () {
    "use strict";

    // Ayarlar sayfası kategori/alt bölüm gezinmesi — sayfadaki tüm bölümler artık tek seferde
    // gösterilmek yerine solda kategori altında listeleniyor, sağda yalnızca seçili bölüm görünür.
    // Diğer init fonksiyonları (initThresholdsForm, initPresetThemes vb.) elemanları gizliyken de
    // normal şekilde bulup kuruyor; `hidden` yalnızca görünürlüğü değiştirir, DOM'dan kaldırmaz.
    (function initSettingsNav() {
        if (!window.HwmonSectionNav) return;
        var nav = window.HwmonSectionNav.init("settings-sidebar", "settings-content");
        if (!nav) return;

        // Sol üstteki arama kutusu — yalnızca metne göre filtreler, sayfa değiştirmez. Eşleşmeyen
        // alt menü öğeleri (hem üst düzey hem "Görünüm" gibi iç içe gruplardakiler) gizlenir; en az
        // bir eşleşmesi olan kategori/grup normal aç/kapa durumundan bağımsız olarak zorla açılır.
        // Hiç eşleşmesi olmayan kategori/grup da tamamen gizlenir. Kutu boşaltılınca (ya da hiç
        // yazılmamışken) tüm bu geçici sınıflar kaldırılır, sayfa arama öncesi hâline döner.
        var searchInput = document.getElementById("settings-search-input");
        if (searchInput) {
            searchInput.addEventListener("input", function () {
                var query = searchInput.value.trim().toLocaleLowerCase("tr");
                var isSearching = query.length > 0;

                nav.subButtons.forEach(function (btn) {
                    var li = btn.closest("li");
                    if (!li) return;
                    var matches = !isSearching || btn.textContent.toLocaleLowerCase("tr").indexOf(query) !== -1;
                    li.classList.toggle("settings-search-hidden", !matches);
                });

                function refreshContainer(container) {
                    var hasMatch = Array.prototype.some.call(
                        container.querySelectorAll("button[data-target]"),
                        function (btn) { return !btn.closest("li").classList.contains("settings-search-hidden"); }
                    );
                    container.classList.toggle("settings-search-open", isSearching && hasMatch);
                    container.classList.toggle("settings-search-hidden", isSearching && !hasMatch);
                }

                nav.groups.forEach(refreshContainer);
                nav.categories.forEach(refreshContainer);
            });
        }
    })();

    function flashSavedNote(id) {
        var note = document.getElementById(id);
        if (!note) return;
        note.style.display = "inline";
        setTimeout(function () { note.style.display = "none"; }, 2000);
    }

    // Eşik Ayarları — window.HwmonThresholds (site.js) Panel'deki kart uyarıları ve bildirimleriyle
    // (dashboard.js) aynı kaynağı paylaşır. İşlemci/Ekran Kartı için metrik (sıcaklık/kullanım) +
    // değer ikilisi, Bellek/Disk için ise doğrudan tek bir sayı okunup yazılır.
    (function initThresholdsForm() {
        var api = window.HwmonThresholds;
        if (!api) return;

        // Sıcaklık ve kullanım % için makul sınırlar farklıdır; metrik değişince değer kutusunun
        // min/max'ı da güncellenir, aksi halde "Kullanım (%)" seçiliyken 100'ün üzerinde bir değer
        // girilip hiçbir zaman tetiklenmeyecek bir eşik oluşturulabilir.
        var VALUE_LIMITS = { temp: { min: 0, max: 150 }, usage: { min: 0, max: 100 } };

        function applyLimits(cardKey) {
            var metricSelect = document.getElementById("th-" + cardKey + "-metric");
            var valueInput = document.getElementById("th-" + cardKey + "-value");
            var cautionInput = document.getElementById("th-" + cardKey + "-caution");
            if (!metricSelect || !valueInput) return;

            var limits = VALUE_LIMITS[metricSelect.value] || VALUE_LIMITS.temp;
            valueInput.min = limits.min;
            valueInput.max = limits.max;
            if (cautionInput) { cautionInput.min = limits.min; cautionInput.max = limits.max; }

            var current = Number(valueInput.value);
            if (!isNaN(current) && current > limits.max) valueInput.value = limits.max;
            if (cautionInput) {
                var currentCaution = Number(cautionInput.value);
                if (!isNaN(currentCaution) && currentCaution > limits.max) cautionInput.value = limits.max;
            }
        }

        var current = api.load();

        ["cpu", "gpu"].forEach(function (cardKey) {
            var metricSelect = document.getElementById("th-" + cardKey + "-metric");
            var valueInput = document.getElementById("th-" + cardKey + "-value");
            var cautionInput = document.getElementById("th-" + cardKey + "-caution");
            if (metricSelect) metricSelect.value = current[cardKey].metric;
            if (valueInput) valueInput.value = current[cardKey].value;
            if (cautionInput) cautionInput.value = current[cardKey].cautionValue;
            applyLimits(cardKey);

            if (metricSelect) metricSelect.addEventListener("change", function () { applyLimits(cardKey); });
        });

        var ramInput = document.getElementById("th-ram-usage");
        if (ramInput) ramInput.value = current.ramUsedPercent;
        var ramCautionInput = document.getElementById("th-ram-usage-caution");
        if (ramCautionInput) ramCautionInput.value = current.ramCautionPercent;

        var diskInput = document.getElementById("th-disk-temp");
        if (diskInput) diskInput.value = current.diskTempC;
        var diskCautionInput = document.getElementById("th-disk-temp-caution");
        if (diskCautionInput) diskCautionInput.value = current.diskCautionTempC;

        var saveButton = document.getElementById("thresholds-save");
        var errorBox = document.getElementById("thresholds-error");
        if (!saveButton) return;

        function showError(message) {
            if (!errorBox) return;
            errorBox.textContent = message;
            errorBox.style.display = message ? "block" : "none";
        }

        // Number("") === 0, NaN DEĞİL — bu yüzden bir kutu boş bırakılıp kaydedilince "geçersiz,
        // varsayılana düş" mantığı hiç tetiklenmiyor, sessizce 0 kaydediliyor (ör. boş bırakılan bir
        // Dikkat alanı 0 olup İşlemci kartının Panel'de sürekli "Dikkat" sarısında kalmasına yol
        // açıyordu — kullanıcı tarafından bulunan, canlı doğrulanmış hata). Boş/yalnızca boşluk
        // içeren bir değeri de NaN gibi ele alan bu yardımcı, aynı satırdaki isNaN kontrollerinin
        // gerçekten işe yaramasını sağlıyor.
        function parseFieldNumber(input) {
            if (!input || input.value.trim() === "") return NaN;
            return Number(input.value);
        }

        saveButton.addEventListener("click", function () {
            showError("");
            var next = {
                ramUsedPercent: api.defaults.ramUsedPercent,
                ramCautionPercent: api.defaults.ramCautionPercent,
                diskTempC: api.defaults.diskTempC,
                diskCautionTempC: api.defaults.diskCautionTempC
            };

            ["cpu", "gpu"].forEach(function (cardKey) {
                var metricSelect = document.getElementById("th-" + cardKey + "-metric");
                var valueInput = document.getElementById("th-" + cardKey + "-value");
                var cautionInput = document.getElementById("th-" + cardKey + "-caution");
                var metric = metricSelect && api.metrics[cardKey][metricSelect.value] ? metricSelect.value : api.defaults[cardKey].metric;
                var limits = VALUE_LIMITS[metric] || VALUE_LIMITS.temp;
                var value = parseFieldNumber(valueInput);
                if (isNaN(value)) value = api.defaults[cardKey].value;
                value = Math.min(Math.max(value, limits.min), limits.max);
                var cautionValue = parseFieldNumber(cautionInput);
                if (isNaN(cautionValue)) cautionValue = api.defaults[cardKey].cautionValue;
                cautionValue = Math.min(Math.max(cautionValue, limits.min), limits.max);
                next[cardKey] = { metric: metric, value: value, cautionValue: cautionValue };
            });

            // RAM/Disk için de CPU/GPU'daki gibi fiziksel sınır kırpması uygulanır — aksi halde
            // negatif ya da anlamsız derecede büyük bir değer (ör. "-10") doğrudan kaydedilebiliyordu,
            // yalnızca "Dikkat < Kritik" ilişkisi kontrol ediliyordu.
            if (ramInput) {
                var ramValue = parseFieldNumber(ramInput);
                next.ramUsedPercent = isNaN(ramValue) ? api.defaults.ramUsedPercent : Math.min(Math.max(ramValue, 0), 100);
            }
            if (ramCautionInput) {
                var ramCautionValue = parseFieldNumber(ramCautionInput);
                next.ramCautionPercent = isNaN(ramCautionValue) ? api.defaults.ramCautionPercent : Math.min(Math.max(ramCautionValue, 0), 100);
            }
            if (diskInput) {
                var diskValue = parseFieldNumber(diskInput);
                next.diskTempC = isNaN(diskValue) ? api.defaults.diskTempC : Math.min(Math.max(diskValue, 0), 150);
            }
            if (diskCautionInput) {
                var diskCautionValue = parseFieldNumber(diskCautionInput);
                next.diskCautionTempC = isNaN(diskCautionValue) ? api.defaults.diskCautionTempC : Math.min(Math.max(diskCautionValue, 0), 150);
            }

            // Dikkat, Kritik'ten küçük olmalı — aksi halde bir değer Dikkat'i geçtiği anda doğrudan
            // Kritik'i de geçmiş sayılır (levelFor önce Kritik'i kontrol eder) ve Dikkat sarısı hiçbir
            // zaman görünmez; kullanıcı arayüzün göründüğünden tamamen farklı davranmasına şaşırır.
            var invalidLabels = [];
            [["cpu", "İşlemci"], ["gpu", "Ekran Kartı"]].forEach(function (pair) {
                if (next[pair[0]].cautionValue >= next[pair[0]].value) invalidLabels.push(pair[1]);
            });
            if (next.ramCautionPercent >= next.ramUsedPercent) invalidLabels.push("Bellek");
            if (next.diskCautionTempC >= next.diskTempC) invalidLabels.push("Disk");
            if (invalidLabels.length > 0) {
                showError("Dikkat değeri Kritik değerinden küçük olmalı: " + invalidLabels.join(", ") + ". Kaydedilmedi.");
                return;
            }

            api.save(next);
            // Eksikti: HwmonThresholds yalnızca BAŞKA bir sekmede değişiklik olduğunda ("storage"
            // olayı, aynı sekmede asla tetiklenmez) hwmon:thresholds-changed yayınlıyordu. Bu yüzden
            // kullanıcı Ayarlar sayfasında yeni bir eşik (ör. RAM için 95) kaydettikten sonra O AYNI
            // sekmede kalmaya devam ederse (threshold-notifier.js her sayfada, Ayarlar dahil çalışır),
            // bildirim mantığı hâlâ kaydedilmeden ÖNCEKİ (ör. varsayılan 90) eşiği kullanmaya devam
            // ediyordu — kullanıcının "95 girdim ama 93-94'te uyarı geldi" şikâyetinin kaynağı buydu.
            document.dispatchEvent(new CustomEvent("hwmon:thresholds-changed"));
            flashSavedNote("thresholds-saved-note");
        });
    })();

    // Birim tercihi — window.HwmonUnitPreferences (site.js); gerçek dönüştürme window.HwmonUnits'te.
    (function initUnitPrefsForm() {
        var api = window.HwmonUnitPreferences;
        if (!api) return;

        var tempSelect = document.getElementById("unit-temp");
        var speedSelect = document.getElementById("unit-speed");
        var sizeSelect = document.getElementById("unit-size");
        var saveButton = document.getElementById("unit-prefs-save");
        if (!saveButton) return;

        var current = api.load();
        if (tempSelect) tempSelect.value = current.temp;
        if (speedSelect) speedSelect.value = current.speed;
        if (sizeSelect) sizeSelect.value = current.size;

        saveButton.addEventListener("click", function () {
            var next = {
                temp: tempSelect ? tempSelect.value : api.defaults.temp,
                speed: speedSelect ? speedSelect.value : api.defaults.speed,
                size: sizeSelect ? sizeSelect.value : api.defaults.size
            };
            api.save(next);
            document.dispatchEvent(new CustomEvent("hwmon:unit-prefs-changed"));
            flashSavedNote("unit-prefs-saved-note");
        });
    })();

    // Enerji maliyeti — window.HwmonEnergyCost (site.js); anlık CPU+GPU gücünden basit bir önizleme
    // hesaplanır (yalnızca kayıtlı oran girilmişse), gerçek canlı gösterim dashboard.js'te.
    (function initEnergyCostForm() {
        var api = window.HwmonEnergyCost;
        var enabledCheckbox = document.getElementById("energy-cost-enabled");
        var rateInput = document.getElementById("energy-cost-rate");
        var saveButton = document.getElementById("energy-cost-save");
        var preview = document.getElementById("energy-cost-preview");
        if (!api || !saveButton) return;

        var current = api.load();
        if (enabledCheckbox) enabledCheckbox.checked = current.enabled;
        if (rateInput) rateInput.value = current.ratePerKwh === null ? "" : String(current.ratePerKwh);

        function renderPreview(prefs) {
            if (!preview) return;
            if (!prefs.enabled) { preview.textContent = "Tahmin gizli."; return; }
            if (!prefs.ratePerKwh) { preview.textContent = "Önizleme için birim fiyat girin."; return; }
            var dailyLira = (0.05 * 24 * prefs.ratePerKwh);
            preview.textContent = "Örnek: sürekli 50 W tüketimle günlük yaklaşık ₺" + dailyLira.toFixed(2) + ".";
        }

        renderPreview(current);

        saveButton.addEventListener("click", function () {
            var rawRate = rateInput ? parseFloat(rateInput.value) : NaN;
            var next = {
                enabled: enabledCheckbox ? enabledCheckbox.checked : api.defaults.enabled,
                ratePerKwh: isFinite(rawRate) && rawRate > 0 ? rawRate : null
            };
            api.save(next);
            document.dispatchEvent(new CustomEvent("hwmon:energy-cost-changed"));
            renderPreview(next);
            flashSavedNote("energy-cost-saved-note");
        });
    })();

    // Yazı tipi — window.HwmonTypography (site.js) boyutu/aileyi hem localStorage'a yazar hem
    // anında :root'a uygular (apply() sayfa yüklenirken zaten bir kez çağrılıyor).
    (function initTypographyForm() {
        var api = window.HwmonTypography;
        if (!api) return;

        var sizeSelect = document.getElementById("typography-font-size");
        var familySelect = document.getElementById("typography-font-family");
        var saveButton = document.getElementById("typography-save");
        if (!saveButton) return;

        var current = api.load();
        if (sizeSelect) sizeSelect.value = current.fontSize;
        if (familySelect) familySelect.value = current.fontFamily;

        saveButton.addEventListener("click", function () {
            var next = {
                fontSize: sizeSelect ? sizeSelect.value : api.defaults.fontSize,
                fontFamily: familySelect ? familySelect.value : api.defaults.fontFamily
            };
            api.save(next);
            api.apply(next);
            flashSavedNote("typography-saved-note");
        });
    })();

    // Cam efekti — window.HwmonGlassEffect (site.js); checkbox değişir değişmez anında uygulanır,
    // ayrı bir "Kaydet" butonu yok (diğer aç/kapa anahtarlarıyla aynı desen: bildirim zili gibi).
    (function initGlassEffectToggle() {
        var api = window.HwmonGlassEffect;
        var checkbox = document.getElementById("glass-effect-toggle");
        if (!api || !checkbox) return;

        checkbox.checked = api.load();
        checkbox.addEventListener("change", function () {
            api.save(checkbox.checked);
            api.apply(checkbox.checked);
        });
    })();

    // Kart yoğunluğu — artık Kart Sıralaması panelinde bir "hazır şablon" düğme grubu. Tıklanınca
    // hem görsel yoğunluğu (dolgu/font, window.HwmonDensity) hem de detay kutucuklarını
    // (window.HwmonDetailVisibility) o şablona göre toplu ayarlar; initDetailVisibilityForm bu
    // olayı dinleyip kutucukları günceller (bkz. hwmon:detail-visibility-changed).
    (function initDensityPresetToggle() {
        var densityApi = window.HwmonDensity;
        var detailApi = window.HwmonDetailVisibility;
        var container = document.getElementById("density-preset-toggle");
        if (!densityApi || !container) return;

        var buttons = Array.prototype.slice.call(container.querySelectorAll("button[data-density]"));

        function refreshActive() {
            var current = densityApi.load();
            buttons.forEach(function (b) { b.classList.toggle("is-active", b.getAttribute("data-density") === current); });
        }

        buttons.forEach(function (btn) {
            btn.addEventListener("click", function () {
                var value = btn.getAttribute("data-density");
                densityApi.save(value);
                densityApi.apply(value);
                if (detailApi) detailApi.applyPreset(value);
                refreshActive();
                document.dispatchEvent(new CustomEvent("hwmon:detail-visibility-changed"));
            });
        });

        refreshActive();
    })();

    // Panel görünümü (grid/liste) — window.HwmonPanelView (site.js); anında uygulanır.
    (function initPanelViewForm() {
        var api = window.HwmonPanelView;
        var select = document.getElementById("panel-view-select");
        if (!api || !select) return;

        select.value = api.load();

        var preview = document.getElementById("panel-view-preview");
        var layoutApi = window.HwmonPanelLayout;
        // Bu sayfada değişmiyor (yalnızca Grid/Liste değişiyor) — hangi kartların görüneceği hâlâ
        // önizlemeye yansısın diye tek seferlik okunuyor.
        var layout = layoutApi ? layoutApi.load() : { order: [], hidden: {} };

        function renderPreview() {
            if (!preview || !window.HwmonPanelPreviewRenderer || !layoutApi) return;
            window.HwmonPanelPreviewRenderer.render(preview, layout.order, layout.hidden, layoutApi.labels, select.value);
        }

        renderPreview();

        select.addEventListener("change", function () {
            api.save(select.value);
            api.apply(select.value);
            renderPreview();
        });
    })();

    // Kart bazında grafik tipi — window.HwmonChartTypes (site.js); değişiklik Panel açıksa (başka
    // sekmede) "hwmon:chart-types-changed" ile anında uygulanır (dashboard.js dinliyor). Ayarlar
    // sayfasındaki küçük SVG önizlemesi ise yalnızca burada, gerçek Panel grafiğine dokunmadan.
    (function initChartTypesForm() {
        var api = window.HwmonChartTypes;
        var selects = Array.prototype.slice.call(document.querySelectorAll(".chart-type-select"));
        if (!api || selects.length === 0) return;

        var CHART_TYPE_LABELS = { cpu: "İşlemci", gpu: "Ekran Kartı", ram: "Bellek", fps: "Kare Hızı" };
        var CHART_TYPE_ACCENT_VARS = { cpu: "--accent-cpu", gpu: "--accent-gpu", ram: "--accent-ram", fps: "--accent-fps" };
        // Sabit örnek veri — gerçek ölçümle ilgisi yok, yalnızca çizgi/alan/bar şeklinin nasıl
        // göründüğünü göstermek için (0-40 SVG viewBox yüksekliğinde, 5 nokta).
        var SAMPLE_POINTS = [28, 14, 22, 8, 18];

        function svgNs(tag) { return document.createElementNS("http://www.w3.org/2000/svg", tag); }

        function renderChartTypePreviewCard(key, type) {
            var card = document.createElement("div");
            card.className = "panel-layout-preview-card chart-type-preview-card";
            var accentVar = CHART_TYPE_ACCENT_VARS[key] || "--accent-cpu";
            card.style.setProperty("--accent", "var(" + accentVar + ")");

            var svg = svgNs("svg");
            svg.setAttribute("viewBox", "0 0 100 40");
            svg.setAttribute("class", "chart-type-preview-svg");
            svg.style.color = "var(" + accentVar + ")";

            var stepX = 100 / (SAMPLE_POINTS.length - 1);

            if (type === "bar") {
                var barWidth = stepX * 0.6;
                SAMPLE_POINTS.forEach(function (value, i) {
                    var rect = svgNs("rect");
                    rect.setAttribute("x", (i * stepX - barWidth / 2).toFixed(1));
                    rect.setAttribute("y", (40 - value).toFixed(1));
                    rect.setAttribute("width", barWidth.toFixed(1));
                    rect.setAttribute("height", value.toFixed(1));
                    rect.setAttribute("fill", "currentColor");
                    svg.appendChild(rect);
                });
            } else {
                var linePoints = SAMPLE_POINTS.map(function (value, i) {
                    return (i * stepX).toFixed(1) + "," + (40 - value).toFixed(1);
                }).join(" ");

                if (type === "area") {
                    var areaPoints = "0,40 " + linePoints + " 100,40";
                    var polygon = svgNs("polygon");
                    polygon.setAttribute("points", areaPoints);
                    polygon.setAttribute("fill", "currentColor");
                    polygon.setAttribute("opacity", "0.35");
                    svg.appendChild(polygon);
                }

                var polyline = svgNs("polyline");
                polyline.setAttribute("points", linePoints);
                polyline.setAttribute("fill", "none");
                polyline.setAttribute("stroke", "currentColor");
                polyline.setAttribute("stroke-width", "2.5");
                polyline.setAttribute("stroke-linejoin", "round");
                polyline.setAttribute("stroke-linecap", "round");
                svg.appendChild(polyline);
            }

            card.appendChild(svg);
            var label = document.createElement("span");
            label.textContent = CHART_TYPE_LABELS[key] || key;
            card.appendChild(label);
            return card;
        }

        function renderChartTypesPreview() {
            var preview = document.getElementById("chart-types-preview");
            if (!preview) return;
            preview.innerHTML = "";
            selects.forEach(function (select) {
                var key = select.getAttribute("data-series-key");
                preview.appendChild(renderChartTypePreviewCard(key, select.value));
            });
        }

        var current = api.load();
        selects.forEach(function (select) {
            var key = select.getAttribute("data-series-key");
            select.value = current[key] || api.defaults[key];
            select.addEventListener("change", function () {
                var next = api.load();
                next[key] = select.value;
                api.save(next);
                document.dispatchEvent(new CustomEvent("hwmon:chart-types-changed"));
                renderChartTypesPreview();
            });
        });

        renderChartTypesPreview();
    })();

    // Açılış sekmesi — window.HwmonStartupTab (site.js); gerçek yönlendirme _Layout.cshtml'de.
    (function initStartupTabForm() {
        var api = window.HwmonStartupTab;
        var select = document.getElementById("startup-tab-select");
        var saveButton = document.getElementById("startup-tab-save");
        if (!api || !select || !saveButton) return;

        select.value = api.load();
        saveButton.addEventListener("click", function () {
            api.save(select.value);
            flashSavedNote("startup-tab-saved-note");
        });
    })();

    // Anomali tespiti aç/kapa — window.HwmonAnomalyDetection (site.js); gerçek hesaplama dashboard.js'te.
    (function initAnomalyDetectionToggle() {
        var api = window.HwmonAnomalyDetection;
        var checkbox = document.getElementById("anomaly-detection-toggle");
        if (!api || !checkbox) return;

        checkbox.checked = api.isEnabled();
        checkbox.addEventListener("change", function () {
            api.setEnabled(checkbox.checked);
        });
    })();

    // Bellek sızıntısı tespiti aç/kapa — window.HwmonMemoryLeakDetection (site.js); anomali
    // tespitinden bağımsız, kendi anahtarı var (eskiden ikisi aynı anahtarı paylaşıyordu).
    (function initMemoryLeakDetectionToggle() {
        var api = window.HwmonMemoryLeakDetection;
        var checkbox = document.getElementById("memory-leak-detection-toggle");
        if (!api || !checkbox) return;

        checkbox.checked = api.isEnabled();
        checkbox.addEventListener("change", function () {
            api.setEnabled(checkbox.checked);
        });
    })();

    // Bildirimler — window.HwmonNotifySettings (site.js) tekrar sıklığını ve hangi kartlar için
    // bildirim gönderileceğini tutar; gerçek bildirim gönderme mantığı threshold-notifier.js'te.
    (function initNotifySettingsForm() {
        var api = window.HwmonNotifySettings;
        if (!api) return;

        var repeatSelect = document.getElementById("notify-repeat-seconds");
        var metricCheckboxes = {
            cpu: document.getElementById("notify-metric-cpu"),
            gpu: document.getElementById("notify-metric-gpu"),
            ram: document.getElementById("notify-metric-ram"),
            storage: document.getElementById("notify-metric-storage")
        };
        var saveButton = document.getElementById("notify-settings-save");
        if (!repeatSelect || !saveButton) return;

        var current = api.load();
        repeatSelect.value = String(current.repeatSeconds);
        Object.keys(metricCheckboxes).forEach(function (key) {
            if (metricCheckboxes[key]) metricCheckboxes[key].checked = current.metrics[key] !== false;
        });

        saveButton.addEventListener("click", function () {
            var next = {
                repeatSeconds: Number(repeatSelect.value) || 0,
                metrics: {}
            };
            Object.keys(metricCheckboxes).forEach(function (key) {
                next.metrics[key] = metricCheckboxes[key] ? metricCheckboxes[key].checked : true;
            });
            api.save(next);
            document.dispatchEvent(new CustomEvent("hwmon:notify-settings-changed"));
            flashSavedNote("notify-settings-saved-note");
        });
    })();

    // Bildirim sesi — window.HwmonNotifySound (site.js); "Test Et" butonu kaydetmeden mevcut
    // form değerleriyle anında çalar (kullanıcı kaydetmeden önce sesi duyup ayarlayabilsin diye).
    (function initNotifySoundForm() {
        var api = window.HwmonNotifySound;
        var soundSelect = document.getElementById("notify-sound-select");
        var volumeInput = document.getElementById("notify-sound-volume");
        var saveButton = document.getElementById("notify-sound-save");
        var testButton = document.getElementById("notify-sound-test");
        if (!api || !saveButton) return;

        var current = api.load();
        if (soundSelect) soundSelect.value = current.sound;
        if (volumeInput) volumeInput.value = current.volume;

        saveButton.addEventListener("click", function () {
            var next = {
                sound: soundSelect ? soundSelect.value : api.defaults.sound,
                volume: volumeInput ? Number(volumeInput.value) : api.defaults.volume
            };
            api.save(next);
            flashSavedNote("notify-sound-saved-note");
        });

        if (testButton) {
            testButton.addEventListener("click", function () {
                api.play({
                    sound: soundSelect ? soundSelect.value : api.defaults.sound,
                    volume: volumeInput ? Number(volumeInput.value) : api.defaults.volume
                });
            });
        }
    })();

    // Renkler — window.HwmonAccentColors (site.js) hem varsayılanları hem de :root'a uygulama
    // mantığını taşır; burada yalnızca formu o API üzerinden okuyup yazıyoruz.
    var COLOR_FIELD_IDS = {
        cpu: "color-cpu", gpu: "color-gpu", ram: "color-ram", fps: "color-fps",
        storage: "color-storage", network: "color-network", process: "color-process", system: "color-system"
    };
    var COLOR_LABELS = {
        cpu: "İşlemci", gpu: "Ekran Kartı", ram: "Bellek", fps: "Kare Hızı",
        storage: "Depolama", network: "Ağ", process: "Kaynak Kullanımı", system: "Sistem Özeti"
    };

    // Renk seçiciler sürüklenirken (henüz Kaydet'e basılmadan) anında görsel geri bildirim verir —
    // yalnızca bu küçük önizleme kartlarını günceller, gerçek CSS değişkenlerine/localStorage'a
    // dokunmaz (o hâlâ yalnızca Kaydet'te olur, api.apply çağrısı değişmedi).
    function renderColorsPreview() {
        var preview = document.getElementById("colors-preview");
        if (!preview) return;
        preview.innerHTML = "";
        Object.keys(COLOR_FIELD_IDS).forEach(function (key) {
            var el = document.getElementById(COLOR_FIELD_IDS[key]);
            var card = document.createElement("div");
            card.className = "panel-layout-preview-card";
            card.style.setProperty("--accent", el ? el.value : "#888");
            var dot = document.createElement("span");
            dot.className = "panel-layout-preview-dot";
            var label = document.createElement("span");
            label.textContent = COLOR_LABELS[key];
            card.appendChild(dot);
            card.appendChild(label);
            preview.appendChild(card);
        });
    }

    // Son tıklanan Hazır Tema'nın id'si — "Varsayılanlara Dön" bir preset seçiliyken app'in genel
    // varsayılanına değil, o preset'in kendi renklerine dönsün diye. Kullanıcı Renkler/Görünüm
    // formundan elle "Kaydet"e basarsa (preset'ten sapıp kendi renklerini tanımladığı an) bu işaret
    // temizlenir — PRESET_THEMES tanımı aşağıda (initPresetThemes), ama bu yardımcılar önce
    // tanımlanıyor ki hem initColorsForm/initBackgroundForm hem initPresetThemes kullanabilsin.
    var ACTIVE_PRESET_KEY = "hwmon-active-preset-theme";

    function getActivePresetId() {
        try { return localStorage.getItem(ACTIVE_PRESET_KEY) || null; } catch (e) { return null; }
    }

    function setActivePresetId(id) {
        try {
            if (id) localStorage.setItem(ACTIVE_PRESET_KEY, id);
            else localStorage.removeItem(ACTIVE_PRESET_KEY);
        } catch (e) { /* gizli sekme: yok say */ }
    }

    function findActivePreset() {
        var id = getActivePresetId();
        if (!id) return null;
        var match = PRESET_THEMES.filter(function (p) { return p.id === id; });
        return match.length ? match[0] : null;
    }

    // Kaydedilen değerler hâlâ aktif preset'in kendi değerleriyle birebir aynıysa (kullanıcı hiçbir
    // şey değiştirmeden sadece "Kaydet"e bastıysa, ya da başka bir formdan — ör. Görünüm'den —
    // Kaydet'e bastıysa) preset bağını KOPARMA; yalnızca değerler gerçekten sapınca kopar. Aksi
    // halde her "Kaydet" tıklaması (hatta değer değişmemişken, hatta ilgisiz bir formdan) sessizce
    // ACTIVE_PRESET_KEY'i temizler ve bir sonraki "Varsayılanlara Dön" preset yerine app'in genel
    // varsayılanına döner — kullanıcının seçtiği temayı kaybettiği hissi burada oluşuyordu.
    function valuesMatchPreset(values, presetValues) {
        if (!presetValues) return false;
        return Object.keys(presetValues).every(function (key) {
            return (values[key] || "").toLowerCase() === (presetValues[key] || "").toLowerCase();
        });
    }

    (function initColorsForm() {
        var api = window.HwmonAccentColors;
        if (!api) return;

        var saved = api.load();
        Object.keys(COLOR_FIELD_IDS).forEach(function (key) {
            var el = document.getElementById(COLOR_FIELD_IDS[key]);
            if (el) {
                el.value = saved[key] || api.defaults[key];
                el.addEventListener("input", renderColorsPreview);
            }
        });
        renderColorsPreview();

        var saveButton = document.getElementById("colors-save");
        if (saveButton) {
            saveButton.addEventListener("click", function () {
                var next = {};
                Object.keys(COLOR_FIELD_IDS).forEach(function (key) {
                    var el = document.getElementById(COLOR_FIELD_IDS[key]);
                    next[key] = el ? el.value : api.defaults[key];
                });
                try { localStorage.setItem(api.storageKey, JSON.stringify(next)); } catch (e) { /* kota aşımı/gizli sekme: yok say */ }
                api.apply(next);
                // Kullanıcı elle renk kaydetti VE bu değerler aktif preset'ten gerçekten sapmışsa
                // — artık preset'in "temiz" hâli değil, kendi ince ayarı; bir sonraki "Varsayılanlara
                // Dön" preset'e değil app varsayılanına dönmeli. Değerler hâlâ preset'le aynıysa
                // (ör. hiçbir şey değiştirmeden Kaydet'e basıldıysa) bağı koparma.
                var activePresetOnSave = findActivePreset();
                if (!valuesMatchPreset(next, activePresetOnSave && activePresetOnSave.accentColors)) {
                    setActivePresetId(null);
                }
                flashSavedNote("colors-saved-note");
            });
        }

        var resetButton = document.getElementById("colors-reset");
        if (resetButton) {
            resetButton.addEventListener("click", function () {
                var activePreset = findActivePreset();
                var target = activePreset ? activePreset.accentColors : api.defaults;
                Object.keys(COLOR_FIELD_IDS).forEach(function (key) {
                    var el = document.getElementById(COLOR_FIELD_IDS[key]);
                    if (el) el.value = target[key] || api.defaults[key];
                });
                if (activePreset) {
                    try { localStorage.setItem(api.storageKey, JSON.stringify(activePreset.accentColors)); } catch (e) { /* kota aşımı/gizli sekme: yok say */ }
                    api.apply(activePreset.accentColors);
                } else {
                    try { localStorage.removeItem(api.storageKey); } catch (e) { /* gizli sekme: yok say */ }
                    api.clear();
                }
                renderColorsPreview();
                flashSavedNote("colors-saved-note");
            });
        }
    })();

    // Hazır Temalar — her biri hem 8 aksan rengini (Renkler) hem de gündüz/gece arka planını
    // (Görünüm) tek pakette tanımlar. Tıklanınca window.HwmonAccentColors/HwmonBackgroundColors
    // üzerinden hem uygulanır hem localStorage'a kaydedilir; Renkler/Görünüm formları da senkron
    // kalsın diye yeniden doldurulur.
    var PRESET_THEMES = [
        {
            id: "default", label: "Varsayılan",
            accentColors: { cpu: "#ff9f45", gpu: "#35d0ba", ram: "#6ea8fe", fps: "#c792ea", storage: "#f2c14e", network: "#4ade80", process: "#f472b6", system: "#38bdf8" },
            bg: { light: "#eef1f6", dark: "#0b0f14" }
        },
        {
            id: "amoled", label: "AMOLED Siyah",
            accentColors: { cpu: "#ff9f45", gpu: "#35d0ba", ram: "#6ea8fe", fps: "#c792ea", storage: "#f2c14e", network: "#4ade80", process: "#f472b6", system: "#38bdf8" },
            bg: { light: "#eef1f6", dark: "#000000" }
        },
        {
            id: "contrast", label: "Yüksek Kontrast",
            accentColors: { cpu: "#ff3b30", gpu: "#00e5ff", ram: "#2979ff", fps: "#ffea00", storage: "#ff9100", network: "#00e676", process: "#ff00ff", system: "#d500f9" },
            bg: { light: "#ffffff", dark: "#000000" }
        },
        {
            id: "ocean", label: "Okyanus",
            accentColors: { cpu: "#0ea5e9", gpu: "#06b6d4", ram: "#3b82f6", fps: "#818cf8", storage: "#2dd4bf", network: "#22d3ee", process: "#6366f1", system: "#0284c7" },
            bg: { light: "#eef4f8", dark: "#071019" }
        },
        {
            id: "sunset", label: "Gün Batımı",
            accentColors: { cpu: "#fb923c", gpu: "#f472b6", ram: "#f87171", fps: "#fbbf24", storage: "#fb7185", network: "#facc15", process: "#e879f9", system: "#fdba74" },
            bg: { light: "#fdf2ee", dark: "#180b0d" }
        },
        {
            id: "forest", label: "Orman",
            accentColors: { cpu: "#65a30d", gpu: "#16a34a", ram: "#059669", fps: "#84cc16", storage: "#ca8a04", network: "#22c55e", process: "#4d7c0f", system: "#15803d" },
            bg: { light: "#eef6ee", dark: "#08130c" }
        },
        {
            id: "purple", label: "Mor Rüya",
            accentColors: { cpu: "#a78bfa", gpu: "#c084fc", ram: "#818cf8", fps: "#e879f9", storage: "#f0abfc", network: "#8b5cf6", process: "#d946ef", system: "#7c3aed" },
            bg: { light: "#f5f0fb", dark: "#100a1a" }
        },
        {
            id: "pastel", label: "Pastel",
            accentColors: { cpu: "#ffb4a2", gpu: "#a2d2ff", ram: "#bde0fe", fps: "#cdb4db", storage: "#ffd6a5", network: "#b9fbc0", process: "#ffc8dd", system: "#a0c4ff" },
            bg: { light: "#fbf9fb", dark: "#161320" }
        },
        {
            id: "crimson", label: "Kızıl Gece",
            accentColors: { cpu: "#ef4444", gpu: "#f87171", ram: "#fb7185", fps: "#fca5a5", storage: "#dc2626", network: "#f43f5e", process: "#e11d48", system: "#b91c1c" },
            bg: { light: "#fdf1f1", dark: "#160707" }
        },
        {
            id: "ice", label: "Buz Mavisi",
            accentColors: { cpu: "#38bdf8", gpu: "#7dd3fc", ram: "#67e8f9", fps: "#a5f3fc", storage: "#0ea5e9", network: "#22d3ee", process: "#0891b2", system: "#0369a1" },
            bg: { light: "#eef8fc", dark: "#050f16" }
        },
        {
            id: "neon", label: "Neon Şehir",
            accentColors: { cpu: "#ff2e63", gpu: "#08d9d6", ram: "#f9ed69", fps: "#ff6ec7", storage: "#00ffab", network: "#7b2ff7", process: "#ff9f1c", system: "#2de2e6" },
            bg: { light: "#f5f0ff", dark: "#0a0014" }
        },
        {
            id: "desert", label: "Çöl Tonları",
            accentColors: { cpu: "#c2410c", gpu: "#d97706", ram: "#ca8a04", fps: "#eab308", storage: "#a16207", network: "#f59e0b", process: "#b45309", system: "#92400e" },
            bg: { light: "#fdf6ec", dark: "#1a1006" }
        },
        {
            id: "terminal", label: "Retro Terminal",
            accentColors: { cpu: "#22c55e", gpu: "#4ade80", ram: "#86efac", fps: "#16a34a", storage: "#15803d", network: "#a3e635", process: "#65a30d", system: "#059669" },
            bg: { light: "#eefdf3", dark: "#020a04" }
        },
        // Aşağıdakiler internette en popüler kod editörü/terminal renk temalarından uyarlanmıştır.
        {
            id: "dracula", label: "Dracula",
            accentColors: { cpu: "#ff5555", gpu: "#8be9fd", ram: "#bd93f9", fps: "#f1fa8c", storage: "#ffb86c", network: "#50fa7b", process: "#ff79c6", system: "#6272a4" },
            bg: { light: "#f7f7fb", dark: "#282a36" }
        },
        {
            id: "nord", label: "Nord",
            accentColors: { cpu: "#bf616a", gpu: "#88c0d0", ram: "#81a1c1", fps: "#b48ead", storage: "#d08770", network: "#a3be8c", process: "#ebcb8b", system: "#5e81ac" },
            bg: { light: "#eceff4", dark: "#2e3440" }
        },
        {
            id: "gruvbox-dark", label: "Gruvbox Koyu",
            accentColors: { cpu: "#fb4934", gpu: "#8ec07c", ram: "#83a598", fps: "#d3869b", storage: "#fabd2f", network: "#b8bb26", process: "#fe8019", system: "#a89984" },
            bg: { light: "#fbf1c7", dark: "#282828" }
        },
        {
            id: "gruvbox-light", label: "Gruvbox Açık",
            accentColors: { cpu: "#9d0006", gpu: "#427b58", ram: "#076678", fps: "#8f3f71", storage: "#b57614", network: "#79740e", process: "#af3a03", system: "#7c6f64" },
            bg: { light: "#fbf1c7", dark: "#3c3836" }
        },
        {
            id: "solarized-dark", label: "Solarized Koyu",
            accentColors: { cpu: "#dc322f", gpu: "#2aa198", ram: "#268bd2", fps: "#6c71c4", storage: "#cb4b16", network: "#859900", process: "#d33682", system: "#b58900" },
            bg: { light: "#fdf6e3", dark: "#002b36" }
        },
        {
            id: "monokai", label: "Monokai",
            accentColors: { cpu: "#f92672", gpu: "#66d9ef", ram: "#ae81ff", fps: "#e6db74", storage: "#fd971f", network: "#a6e22e", process: "#fd5ff0", system: "#75715e" },
            bg: { light: "#faf8f0", dark: "#272822" }
        },
        {
            id: "one-dark", label: "One Dark",
            accentColors: { cpu: "#e06c75", gpu: "#56b6c2", ram: "#61afef", fps: "#c678dd", storage: "#d19a66", network: "#98c379", process: "#e5c07b", system: "#abb2bf" },
            bg: { light: "#fafafa", dark: "#282c34" }
        },
        {
            id: "tokyo-night", label: "Tokyo Night",
            accentColors: { cpu: "#f7768e", gpu: "#73daca", ram: "#7aa2f7", fps: "#bb9af7", storage: "#ff9e64", network: "#9ece6a", process: "#e0af68", system: "#565f89" },
            bg: { light: "#d5d6db", dark: "#1a1b26" }
        },
        {
            id: "catppuccin-mocha", label: "Catppuccin Mocha",
            accentColors: { cpu: "#f38ba8", gpu: "#94e2d5", ram: "#89b4fa", fps: "#cba6f7", storage: "#fab387", network: "#a6e3a1", process: "#f9e2af", system: "#b4befe" },
            bg: { light: "#eff1f5", dark: "#1e1e2e" }
        },
        {
            id: "catppuccin-latte", label: "Catppuccin Latte",
            accentColors: { cpu: "#d20f39", gpu: "#179299", ram: "#1e66f5", fps: "#8839ef", storage: "#fe640b", network: "#40a02b", process: "#df8e1d", system: "#7287fd" },
            bg: { light: "#eff1f5", dark: "#303446" }
        },
        {
            id: "rose-pine", label: "Rosé Pine",
            accentColors: { cpu: "#eb6f92", gpu: "#9ccfd8", ram: "#31748f", fps: "#c4a7e7", storage: "#f6c177", network: "#ebbcba", process: "#908caa", system: "#6e6a86" },
            bg: { light: "#faf4ed", dark: "#191724" }
        },
        {
            id: "ayu-dark", label: "Ayu Koyu",
            accentColors: { cpu: "#f07171", gpu: "#59c2ff", ram: "#399ee6", fps: "#d2a6ff", storage: "#ffb454", network: "#c2d94c", process: "#ff8f40", system: "#95e6cb" },
            bg: { light: "#fafafa", dark: "#0a0e14" }
        },
        {
            id: "ayu-light", label: "Ayu Açık",
            accentColors: { cpu: "#f51818", gpu: "#4cbf99", ram: "#399ee6", fps: "#a37acc", storage: "#f2ae49", network: "#86b300", process: "#fa8d3e", system: "#5c6773" },
            bg: { light: "#fafafa", dark: "#1f2430" }
        },
        {
            id: "everforest", label: "Everforest",
            accentColors: { cpu: "#e67e80", gpu: "#83c092", ram: "#7fbbb3", fps: "#d699b6", storage: "#e69875", network: "#a7c080", process: "#dbbc7f", system: "#859289" },
            bg: { light: "#f3ead3", dark: "#2b3339" }
        },
        {
            id: "material-palenight", label: "Material Palenight",
            accentColors: { cpu: "#f07178", gpu: "#89ddff", ram: "#82aaff", fps: "#c792ea", storage: "#f78c6c", network: "#c3e88d", process: "#ffcb6b", system: "#80cbc4" },
            bg: { light: "#fafafa", dark: "#292d3e" }
        },
        {
            id: "synthwave84", label: "Synthwave '84",
            accentColors: { cpu: "#fe4450", gpu: "#36f9f6", ram: "#b893ce", fps: "#ff7edb", storage: "#fede5d", network: "#72f1b8", process: "#f97e72", system: "#836ac6" },
            bg: { light: "#f3eefc", dark: "#262335" }
        },
        {
            id: "github-dark", label: "GitHub Koyu",
            accentColors: { cpu: "#f85149", gpu: "#39c5cf", ram: "#58a6ff", fps: "#a371f7", storage: "#ffa657", network: "#3fb950", process: "#db61a2", system: "#e3b341" },
            bg: { light: "#ffffff", dark: "#0d1117" }
        },
        {
            id: "github-light", label: "GitHub Açık",
            accentColors: { cpu: "#cf222e", gpu: "#1b7c83", ram: "#0969da", fps: "#8250df", storage: "#bc4c00", network: "#1a7f37", process: "#bf3989", system: "#9a6700" },
            bg: { light: "#ffffff", dark: "#010409" }
        },
        {
            id: "night-owl", label: "Night Owl",
            accentColors: { cpu: "#ef5350", gpu: "#7fdbca", ram: "#82aaff", fps: "#c792ea", storage: "#f78c6c", network: "#addb67", process: "#ecc48d", system: "#5f7e97" },
            bg: { light: "#fbfbfb", dark: "#011627" }
        },
        {
            id: "cobalt2", label: "Cobalt2",
            accentColors: { cpu: "#ff628c", gpu: "#80fcff", ram: "#0088ff", fps: "#9effff", storage: "#ff9d00", network: "#3ad900", process: "#ffc600", system: "#8fb9c9" },
            bg: { light: "#eef3f7", dark: "#193549" }
        },
        {
            id: "horizon", label: "Horizon",
            accentColors: { cpu: "#e95678", gpu: "#59e1e3", ram: "#26bbd9", fps: "#b877db", storage: "#fab795", network: "#29d398", process: "#ee64ac", system: "#f09383" },
            bg: { light: "#fdf0ed", dark: "#1c1e26" }
        },
        {
            id: "shades-of-purple", label: "Shades of Purple",
            accentColors: { cpu: "#ff628c", gpu: "#00c5c7", ram: "#9d65ff", fps: "#a599e9", storage: "#ff9d00", network: "#3ad900", process: "#ec3a6e", system: "#fad000" },
            bg: { light: "#f1eefc", dark: "#2d2b55" }
        },
        {
            id: "zenburn", label: "Zenburn",
            accentColors: { cpu: "#cc9393", gpu: "#8cd0d3", ram: "#93e0e3", fps: "#dc8cc3", storage: "#dfaf8f", network: "#7f9f7f", process: "#f0dfaf", system: "#9fafaf" },
            bg: { light: "#ece8d8", dark: "#3f3f3f" }
        },
        {
            id: "oceanic-next", label: "Oceanic Next",
            accentColors: { cpu: "#ec5f67", gpu: "#5fb3b3", ram: "#6699cc", fps: "#c594c5", storage: "#f99157", network: "#99c794", process: "#fac863", system: "#65737e" },
            bg: { light: "#eef3f5", dark: "#1b2b34" }
        },
        {
            id: "panda", label: "Panda",
            accentColors: { cpu: "#ff6b6b", gpu: "#19f9d8", ram: "#45a9f9", fps: "#b084eb", storage: "#ffb86c", network: "#7cd992", process: "#ffe66d", system: "#ff75b5" },
            bg: { light: "#f3f3f7", dark: "#292a2b" }
        },
        // 2026 itibarıyla Neovim/VS Code topluluklarında en çok rağbet gören ek 12 tema (toplamı 50'ye çıkarır).
        {
            id: "kanagawa", label: "Kanagawa",
            accentColors: { cpu: "#c34043", gpu: "#7aa89f", ram: "#658594", fps: "#957fb8", storage: "#dca561", network: "#98bb6c", process: "#d27e99", system: "#727169" },
            bg: { light: "#f2ecbc", dark: "#1f1f28" }
        },
        {
            id: "nightfox", label: "Nightfox",
            accentColors: { cpu: "#c94f6d", gpu: "#63cdcf", ram: "#719cd6", fps: "#9d79d6", storage: "#f4a261", network: "#81b29a", process: "#dbc074", system: "#738091" },
            bg: { light: "#faf4ed", dark: "#192330" }
        },
        {
            id: "poimandres", label: "Poimandres",
            accentColors: { cpu: "#d0679d", gpu: "#5de4c7", ram: "#89ddff", fps: "#91b4d5", storage: "#fffac2", network: "#addb67", process: "#f087bd", system: "#767c9d" },
            bg: { light: "#e4f0fb", dark: "#1b1e28" }
        },
        {
            id: "vitesse-dark", label: "Vitesse Koyu",
            accentColors: { cpu: "#cb7676", gpu: "#5da994", ram: "#6394bf", fps: "#a186d5", storage: "#cf9860", network: "#4d9375", process: "#d38aa3", system: "#cccc77" },
            bg: { light: "#ffffff", dark: "#121212" }
        },
        {
            id: "vitesse-light", label: "Vitesse Açık",
            accentColors: { cpu: "#ab5959", gpu: "#428c6c", ram: "#4b81b3", fps: "#8462c9", storage: "#a65e2b", network: "#1e754f", process: "#a13865", system: "#998418" },
            bg: { light: "#ffffff", dark: "#121212" }
        },
        {
            id: "andromeda", label: "Andromeda",
            accentColors: { cpu: "#ee5d43", gpu: "#00e8c6", ram: "#6fb3d2", fps: "#c74ded", storage: "#cd960c", network: "#96e072", process: "#f92aad", system: "#ffe66d" },
            bg: { light: "#f5f5f7", dark: "#23262e" }
        },
        {
            id: "bluloco-dark", label: "Bluloco Koyu",
            accentColors: { cpu: "#ff6480", gpu: "#37c3e1", ram: "#3691ff", fps: "#ff78f8", storage: "#ff9d34", network: "#3fc56b", process: "#f9005a", system: "#ebc88d" },
            bg: { light: "#ffffff", dark: "#282c34" }
        },
        {
            id: "noctis", label: "Noctis",
            accentColors: { cpu: "#e6534b", gpu: "#49c8c8", ram: "#49a1c8", fps: "#a980cb", storage: "#eaa64b", network: "#49c853", process: "#ff8fc8", system: "#7fa8b3" },
            bg: { light: "#f7f4ef", dark: "#0d3b4f" }
        },
        {
            id: "city-lights", label: "City Lights",
            accentColors: { cpu: "#e2777a", gpu: "#41d1c1", ram: "#5ec4ff", fps: "#c792ea", storage: "#ffcb6b", network: "#8bd49c", process: "#ff6b9f", system: "#718ca1" },
            bg: { light: "#f3f7fa", dark: "#1d252c" }
        },
        {
            id: "aura", label: "Aura",
            accentColors: { cpu: "#ff6767", gpu: "#61ffca", ram: "#82e2ff", fps: "#a277ff", storage: "#ffca85", network: "#7cf29c", process: "#f694ff", system: "#6d6d6d" },
            bg: { light: "#f2eef7", dark: "#15141b" }
        },
        {
            id: "vscode-dark-plus", label: "VS Code Dark+",
            accentColors: { cpu: "#f44747", gpu: "#4ec9b0", ram: "#569cd6", fps: "#c586c0", storage: "#ce9178", network: "#6a9955", process: "#dcdcaa", system: "#9cdcfe" },
            bg: { light: "#ffffff", dark: "#1e1e1e" }
        },
        {
            id: "moonlight", label: "Moonlight",
            accentColors: { cpu: "#ff757f", gpu: "#86e1fc", ram: "#82aaff", fps: "#c099ff", storage: "#ffc777", network: "#c3e88d", process: "#f087bd", system: "#7a88cf" },
            bg: { light: "#eef0fb", dark: "#212337" }
        },
        // Aşağıdaki iki tema, renk körlüğü (deuteranopia/protanopia/tritanopia) durumlarında bile
        // birbirinden ayırt edilebilecek, veri görselleştirmede yaygın kabul görmüş kategorik
        // paletlere dayanır — rastgele "güzel görünen" renkler değil.
        {
            // Okabe & Ito (2008) — renk körlüğü için en yaygın önerilen 8 renkli palet. Orijinal
            // 8. renk siyahtır; koyu temada görünmez kalacağından burada açık gri ile değiştirildi.
            id: "colorblind-okabe-ito", label: "Renk Körlüğü Dostu (Okabe-Ito)",
            accentColors: { cpu: "#E69F00", gpu: "#56B4E9", ram: "#009E73", fps: "#F0E442", storage: "#0072B2", network: "#D55E00", process: "#CC79A7", system: "#BBBBBB" },
            bg: { light: "#f7f4ef", dark: "#0d0d0c" }
        },
        {
            // Paul Tol'un "bright" niteliksel paleti + "muted" paletinden ödünç alınan bir çivit
            // rengi (8. renk) — teknik notunda renk körlüğü için güvenli olarak belgelenmiştir.
            id: "colorblind-tol-bright", label: "Renk Körlüğü Dostu (Yüksek Ayırt Edicilik)",
            accentColors: { cpu: "#4477AA", gpu: "#66CCEE", ram: "#228833", fps: "#CCBB44", storage: "#EE6677", network: "#AA3377", process: "#BBBBBB", system: "#332288" },
            bg: { light: "#f2f4f7", dark: "#0a0d12" }
        }
    ];

    (function initPresetThemes() {
        var accentApi = window.HwmonAccentColors;
        var bgApi = window.HwmonBackgroundColors;
        var grid = document.getElementById("preset-theme-grid");
        if (!accentApi || !bgApi || !grid) return;

        // Kayıtlı bir preset bağı (ACTIVE_PRESET_KEY) varsa onu kullan; yoksa (ör. hiç preset
        // seçilmeden gelen ilk kurulum) mevcut renk+arka plan değerleri bir preset'le birebir
        // eşleşiyor mu diye bakılır — kullanıcı hangi temanın şu an uygulandığını her zaman görsün.
        function computeActivePresetId() {
            var stored = getActivePresetId();
            if (stored && PRESET_THEMES.some(function (p) { return p.id === stored; })) return stored;
            var currentAccent = accentApi.load();
            var currentBg = bgApi.load();
            var match = PRESET_THEMES.filter(function (p) {
                return valuesMatchPreset(currentAccent, p.accentColors) && valuesMatchPreset(currentBg, p.bg);
            });
            return match.length ? match[0].id : null;
        }

        function markActiveCard() {
            var activeId = computeActivePresetId();
            grid.querySelectorAll(".preset-theme-card").forEach(function (card) {
                card.classList.toggle("is-active", card.dataset.presetId === activeId);
            });
        }

        function refreshColorAndBgForms(preset) {
            Object.keys(COLOR_FIELD_IDS).forEach(function (key) {
                var el = document.getElementById(COLOR_FIELD_IDS[key]);
                if (el && preset.accentColors[key]) el.value = preset.accentColors[key];
            });
            Object.keys(BG_FIELD_IDS).forEach(function (key) {
                var el = document.getElementById(BG_FIELD_IDS[key]);
                if (el && preset.bg[key]) el.value = preset.bg[key];
            });
        }

        PRESET_THEMES.forEach(function (preset) {
            var card = document.createElement("button");
            card.type = "button";
            card.className = "preset-theme-card";
            card.dataset.label = preset.label.toLocaleLowerCase("tr-TR");
            card.dataset.presetId = preset.id;

            var activeBadge = document.createElement("span");
            activeBadge.className = "preset-theme-active-badge";
            activeBadge.textContent = "✓ Kullanılıyor";
            card.appendChild(activeBadge);

            var bgPreview = document.createElement("span");
            bgPreview.className = "preset-theme-bg-preview";
            bgPreview.style.background = "linear-gradient(90deg, " + preset.bg.light + " 50%, " + preset.bg.dark + " 50%)";
            card.appendChild(bgPreview);

            var swatches = document.createElement("span");
            swatches.className = "preset-theme-swatches";
            Object.keys(preset.accentColors).forEach(function (key) {
                var dot = document.createElement("span");
                dot.style.background = preset.accentColors[key];
                swatches.appendChild(dot);
            });
            card.appendChild(swatches);

            var label = document.createElement("span");
            label.className = "preset-theme-label";
            label.textContent = preset.label;
            card.appendChild(label);

            card.addEventListener("click", function () {
                try { localStorage.setItem(accentApi.storageKey, JSON.stringify(preset.accentColors)); } catch (e) { /* kota aşımı/gizli sekme: yok say */ }
                accentApi.apply(preset.accentColors);
                document.dispatchEvent(new CustomEvent("hwmon:accent-colors-changed"));

                try { localStorage.setItem(bgApi.storageKey, JSON.stringify(preset.bg)); } catch (e) { /* kota aşımı/gizli sekme: yok say */ }
                bgApi.apply(preset.bg);

                setActivePresetId(preset.id);
                refreshColorAndBgForms(preset);
                renderColorsPreview();
                markActiveCard();
                flashSavedNote("preset-theme-saved-note");
            });

            grid.appendChild(card);
        });

        markActiveCard();
        // Renkler/Görünüm formlarından elle "Kaydet"e basılınca aktif preset bağı kopabilir
        // (valuesMatchPreset ile korunuyor, ama sapan durumlarda) — işareti güncel tut.
        document.addEventListener("hwmon:accent-colors-changed", markActiveCard);
        var colorsSaveButton = document.getElementById("colors-save");
        if (colorsSaveButton) colorsSaveButton.addEventListener("click", markActiveCard);
        var colorsResetButton = document.getElementById("colors-reset");
        if (colorsResetButton) colorsResetButton.addEventListener("click", markActiveCard);

        // Arama kutusu — tema adına göre kartları anlık filtreler; eşleşme yoksa uyarı gösterir.
        var searchInput = document.getElementById("preset-theme-search");
        var emptyNote = document.getElementById("preset-theme-empty");
        if (searchInput) {
            searchInput.addEventListener("input", function () {
                var query = searchInput.value.trim().toLocaleLowerCase("tr-TR");
                var visibleCount = 0;
                grid.querySelectorAll(".preset-theme-card").forEach(function (card) {
                    var matches = !query || card.dataset.label.indexOf(query) !== -1;
                    card.style.display = matches ? "" : "none";
                    if (matches) visibleCount++;
                });
                if (emptyNote) emptyNote.style.display = visibleCount === 0 ? "" : "none";
            });
        }
    })();

    // Kart sıralaması — window.HwmonCardOrder (site.js) İşlemci/Ekran Kartı kartlarındaki değerlerin
    // sırasını (ilk iki = büyük satır, kalan iki = küçük satır) tutar. Yukarı/aşağı okları listedeki
    // konumu komşusuyla değiştirir; kaydetmeden önce yalnızca bu sayfadaki bellek içi kopya değişir.
    (function initOrderForm() {
        var api = window.HwmonCardOrder;
        var detailApi = window.HwmonDetailVisibility;
        if (!api) return;

        var state = api.load();
        var detailState = detailApi ? detailApi.load() : null;

        function renderList(cardKey) {
            var list = document.getElementById("order-" + cardKey);
            if (!list) return;
            list.innerHTML = "";

            state[cardKey].forEach(function (metricKey, index) {
                var li = document.createElement("li");
                li.className = "order-item";

                var labelWrap = document.createElement("label");
                labelWrap.className = "order-item-label";

                // Görünürlük kutucuğu — kapatılırsa metrik Panel'de tamamen gizlenir; sıralama
                // (yukarı/aşağı) görünür olsun ya da olmasın aynı şekilde çalışmaya devam eder.
                if (detailApi) {
                    var checkbox = document.createElement("input");
                    checkbox.type = "checkbox";
                    checkbox.checked = detailState[cardKey][metricKey] !== false;
                    checkbox.title = "Panel'de göster";
                    checkbox.addEventListener("change", function () {
                        detailState[cardKey][metricKey] = checkbox.checked;
                        detailApi.save(detailState);
                        detailApi.apply(detailState);
                    });
                    labelWrap.appendChild(checkbox);
                }

                var label = document.createElement("span");
                label.textContent = api.metricLabels[metricKey] || metricKey;
                labelWrap.appendChild(label);
                li.appendChild(labelWrap);

                var actions = document.createElement("span");
                actions.className = "order-item-actions";

                var upButton = document.createElement("button");
                upButton.type = "button";
                upButton.textContent = "▲";
                upButton.setAttribute("aria-label", "Yukarı taşı");
                upButton.disabled = index === 0;
                upButton.addEventListener("click", function () { move(cardKey, index, -1); });
                actions.appendChild(upButton);

                var downButton = document.createElement("button");
                downButton.type = "button";
                downButton.textContent = "▼";
                downButton.setAttribute("aria-label", "Aşağı taşı");
                downButton.disabled = index === state[cardKey].length - 1;
                downButton.addEventListener("click", function () { move(cardKey, index, 1); });
                actions.appendChild(downButton);

                li.appendChild(actions);
                list.appendChild(li);
            });
        }

        function move(cardKey, index, delta) {
            var order = state[cardKey];
            var target = index + delta;
            if (target < 0 || target >= order.length) return;

            var tmp = order[index];
            order[index] = order[target];
            order[target] = tmp;
            renderList(cardKey);
        }

        renderList("cpu");
        renderList("gpu");

        // Yoğunluk şablonu (Kompakt/Normal/Detaylı) tıklanınca kutucuklar da güncellensin.
        document.addEventListener("hwmon:detail-visibility-changed", function () {
            if (!detailApi) return;
            detailState = detailApi.load();
            renderList("cpu");
            renderList("gpu");
        });

        var saveButton = document.getElementById("order-save");
        if (saveButton) {
            saveButton.addEventListener("click", function () {
                try { localStorage.setItem(api.storageKey, JSON.stringify(state)); } catch (e) { /* kota aşımı/gizli sekme: yok say */ }
                api.applyAll();
                flashSavedNote("order-saved-note");
            });
        }

        var resetButton = document.getElementById("order-reset");
        if (resetButton) {
            resetButton.addEventListener("click", function () {
                state = { cpu: api.defaults.cpu.slice(), gpu: api.defaults.gpu.slice() };
                renderList("cpu");
                renderList("gpu");
                try { localStorage.removeItem(api.storageKey); } catch (e) { /* gizli sekme: yok say */ }
                api.applyAll();
                flashSavedNote("order-saved-note");
            });
        }
    })();

    // Diğer kartların (VRAM/Bellek/FPS/Ağ/Depolama/Sistem Özeti) detay görünürlüğü — sıralaması
    // olmayan, yalnızca göster/gizle kutucukları. window.HwmonDetailVisibility (site.js).
    // Depolama kartı her tick'te dashboard.js tarafından yeniden çizildiği için kutucuk değişimi
    // orada bir sonraki güncellemede otomatik yansır; diğer kartlarda display anında değişir.
    (function initDetailVisibilityForm() {
        var api = window.HwmonDetailVisibility;
        var container = document.getElementById("detail-visibility-form");
        if (!api || !container) return;

        var CARD_LABELS = {
            vram: "Video Belleği (VRAM)", ram: "Bellek (RAM)", fps: "Kare Hızı (FPS)",
            network: "Ağ", storage: "Depolama (Disk)", system: "Sistem Özeti"
        };
        var CARD_KEYS = ["vram", "ram", "fps", "network", "storage", "system"];

        var state = api.load();

        function render() {
            container.innerHTML = "";
            CARD_KEYS.forEach(function (cardKey) {
                var fields = api.fields[cardKey];
                if (!fields || !fields.length) return;

                var group = document.createElement("div");
                group.className = "order-group";

                var heading = document.createElement("h4");
                heading.textContent = CARD_LABELS[cardKey] || cardKey;
                group.appendChild(heading);

                var list = document.createElement("div");
                list.className = "notify-metrics";

                fields.forEach(function (f) {
                    var label = document.createElement("label");
                    label.className = "notify-metric-field";

                    var checkbox = document.createElement("input");
                    checkbox.type = "checkbox";
                    checkbox.checked = state[cardKey][f.key] !== false;
                    checkbox.addEventListener("change", function () {
                        state[cardKey][f.key] = checkbox.checked;
                        api.save(state);
                        api.apply(state);
                    });
                    label.appendChild(checkbox);
                    label.appendChild(document.createTextNode(" " + f.label));
                    list.appendChild(label);
                });

                group.appendChild(list);
                container.appendChild(group);
            });
        }

        render();

        // Yoğunluk şablonu tıklanınca (density-preset-toggle) kutucuklar da güncellensin.
        document.addEventListener("hwmon:detail-visibility-changed", function () {
            state = api.load();
            render();
        });
    })();

    // Klavye kısayolları — window.HwmonShortcuts (site.js). Her satırda mevcut kombinasyon
    // gösterilir; "Değiştir" gibi davranan buton bir sonraki tuş basışını yakalayıp (Esc = vazgeç)
    // hemen kaydeder ve uygular — ayrı bir "Kaydet" butonuna gerek yok, diğer anında-uygulanan
    // aç/kapa ayarlarıyla (cam efekti, anomali tespiti vb.) aynı desen.
    (function initShortcutsForm() {
        var api = window.HwmonShortcuts;
        var list = document.getElementById("shortcuts-list");
        if (!api || !list) return;

        var state = api.load();
        var conflictNote = document.getElementById("shortcuts-conflict-note");

        function comboLabel(combo) {
            if (!combo) return "(atanmamış)";
            return combo.split("+").map(function (part) {
                return part.length === 1 ? part.toUpperCase() : part.charAt(0).toUpperCase() + part.slice(1);
            }).join("+");
        }

        function showConflict(message) {
            if (!conflictNote) return;
            conflictNote.textContent = message || "";
            conflictNote.style.display = message ? "" : "none";
        }

        function startCapture(action, comboButton) {
            showConflict("");
            var originalText = comboButton.textContent;
            comboButton.textContent = "Tuşa basın… (Esc: iptal)";
            comboButton.disabled = true;

            function cleanup() {
                document.removeEventListener("keydown", onKeydown, true);
                comboButton.disabled = false;
            }

            function onKeydown(e) {
                e.preventDefault();
                e.stopPropagation();

                if (e.key === "Escape") {
                    cleanup();
                    comboButton.textContent = originalText;
                    return;
                }
                // Yalnızca bir değiştirici (Ctrl/Alt/Shift) tuşuna basılmışsa asıl tuş henüz gelmedi,
                // beklemeye devam ederiz (aksi hâlde "Ctrl'e bas" tek başına bir kombinasyon olurdu).
                if (["Control", "Alt", "Shift", "Meta"].indexOf(e.key) !== -1) return;

                var combo = api.comboFromEvent(e);
                var conflict = api.findConflict(state, combo, action.key);
                cleanup();

                if (conflict) {
                    showConflict('"' + comboLabel(combo) + '" zaten "' + conflict.label + '" eylemine atanmış, önce onu değiştirin.');
                    comboButton.textContent = originalText;
                    return;
                }

                state[action.key] = combo;
                api.save(state);
                document.dispatchEvent(new CustomEvent("hwmon:shortcuts-changed"));
                render();
            }

            document.addEventListener("keydown", onKeydown, true);
        }

        function render() {
            list.innerHTML = "";
            api.actions.forEach(function (action) {
                var li = document.createElement("li");
                li.className = "order-item";

                var label = document.createElement("span");
                label.textContent = action.label;
                li.appendChild(label);

                var actionsWrap = document.createElement("span");
                actionsWrap.className = "order-item-actions";

                var comboButton = document.createElement("button");
                comboButton.type = "button";
                comboButton.style.width = "auto";
                comboButton.style.padding = "0 10px";
                comboButton.title = "Değiştirmek için tıklayın";
                comboButton.textContent = comboLabel(state[action.key]);
                comboButton.addEventListener("click", function () { startCapture(action, comboButton); });
                actionsWrap.appendChild(comboButton);

                li.appendChild(actionsWrap);
                list.appendChild(li);
            });
        }

        render();

        var resetButton = document.getElementById("shortcuts-reset");
        if (resetButton) {
            resetButton.addEventListener("click", function () {
                state = api.defaults();
                api.save(state);
                document.dispatchEvent(new CustomEvent("hwmon:shortcuts-changed"));
                showConflict("");
                render();
            });
        }

        // Başka bir sekmede değiştirilirse (ya da az önce burada kaydedildiği için) güncelle.
        document.addEventListener("hwmon:shortcuts-changed", function () {
            state = api.load();
            render();
        });
    })();

    // Panel Düzeni — window.HwmonPanelLayout (site.js) Panel'deki 8 kartın görünürlüğünü/sırasını
    // tutar. Diğer listelerden farklı olarak burada gerçek HTML5 sürükle-bırak kullanılır (ok
    // tuşları değil); kutucuk görünürlüğü, sürükleme sırayı değiştirir.
    (function initPanelLayoutForm() {
        var api = window.HwmonPanelLayout;
        var list = document.getElementById("panel-layout-list");
        if (!api || !list) return;

        var state = api.load();
        var dragKey = null;

        var preview = document.getElementById("panel-layout-preview");
        // Bu sayfada değişmiyor (yalnızca sıra/görünürlük değişiyor) — Grid/Liste kararı hâlâ
        // önizlemeye yansısın diye tek seferlik okunuyor (bkz. window.HwmonPanelView, site.js).
        var viewMode = window.HwmonPanelView ? window.HwmonPanelView.load() : "grid";

        // Kaydetmeden ÖNCE bile (sürükleme/kutucuk her değiştiğinde) Panel'de gerçekte görünecek
        // kartları küçültülmüş bir grid'de gösterir — gizli işaretlenenler hiç çizilmez, çünkü amaç
        // "gerçekten neyi göreceksin" sorusuna cevap vermek. Çizim mantığı paylaşılan
        // window.HwmonPanelPreviewRenderer'da (site.js) — "Panel Görünümü" bölümündeki önizleme de
        // aynısını kullanıyor.
        function renderPreview() {
            if (!preview || !window.HwmonPanelPreviewRenderer) return;
            window.HwmonPanelPreviewRenderer.render(preview, state.order, state.hidden, api.labels, viewMode);
        }

        function render() {
            list.innerHTML = "";

            state.order.forEach(function (key) {
                var li = document.createElement("li");
                li.className = "order-item panel-layout-item";
                li.draggable = true;
                li.dataset.key = key;

                var handle = document.createElement("span");
                handle.className = "panel-layout-handle";
                handle.textContent = "⠿⠿";
                handle.setAttribute("aria-hidden", "true");

                var labelWrap = document.createElement("label");
                labelWrap.className = "order-item-label";

                var checkbox = document.createElement("input");
                checkbox.type = "checkbox";
                checkbox.checked = !state.hidden[key];
                checkbox.addEventListener("change", function () {
                    state.hidden[key] = !checkbox.checked;
                    renderPreview();
                });

                var text = document.createElement("span");
                text.textContent = api.labels[key] || key;

                labelWrap.appendChild(handle);
                labelWrap.appendChild(checkbox);
                labelWrap.appendChild(text);
                li.appendChild(labelWrap);

                li.addEventListener("dragstart", function (e) {
                    dragKey = key;
                    e.dataTransfer.effectAllowed = "move";
                    li.classList.add("is-dragging");
                });
                li.addEventListener("dragend", function () {
                    li.classList.remove("is-dragging");
                });
                li.addEventListener("dragover", function (e) {
                    e.preventDefault();
                    e.dataTransfer.dropEffect = "move";
                });
                li.addEventListener("dragenter", function () {
                    if (dragKey !== key) li.classList.add("is-drop-target");
                });
                li.addEventListener("dragleave", function () {
                    li.classList.remove("is-drop-target");
                });
                li.addEventListener("drop", function (e) {
                    e.preventDefault();
                    li.classList.remove("is-drop-target");
                    if (!dragKey || dragKey === key) return;

                    var fromIndex = state.order.indexOf(dragKey);
                    var toIndex = state.order.indexOf(key);
                    state.order.splice(fromIndex, 1);
                    state.order.splice(toIndex, 0, dragKey);
                    render();
                });

                list.appendChild(li);
            });

            renderPreview();
        }

        render();

        var saveButton = document.getElementById("panel-layout-save");
        if (saveButton) {
            saveButton.addEventListener("click", function () {
                api.save(state);
                flashSavedNote("panel-layout-saved-note");
            });
        }

        var resetButton = document.getElementById("panel-layout-reset");
        if (resetButton) {
            resetButton.addEventListener("click", function () {
                try { localStorage.removeItem(api.storageKey); } catch (e) { /* gizli sekme: yok say */ }
                state = api.load();
                render();
                flashSavedNote("panel-layout-saved-note");
            });
        }
    })();

    // Panel Düzeni Profilleri — window.HwmonPanelLayoutProfiles (site.js); yukarıdaki sürükle-bırak
    // sıralamasını isimli profiller olarak kaydedip aralarında geçiş yapar.
    (function initPanelLayoutProfiles() {
        var api = window.HwmonPanelLayoutProfiles;
        var list = document.getElementById("panel-layout-profiles-list");
        var nameInput = document.getElementById("panel-layout-profile-name");
        var saveAsButton = document.getElementById("panel-layout-profile-save");
        if (!api || !list || !saveAsButton) return;

        function renderProfiles() {
            list.innerHTML = "";
            var profiles = api.load();
            var activeId = api.getActiveId();

            if (profiles.length === 0) {
                var empty = document.createElement("li");
                empty.className = "stat-sub";
                empty.textContent = "Henüz kaydedilmiş profil yok.";
                list.appendChild(empty);
                return;
            }

            profiles.forEach(function (profile) {
                var li = document.createElement("li");
                li.className = "order-item";

                var label = document.createElement("span");
                label.className = "order-item-label";
                label.style.cursor = "default";
                label.textContent = profile.name + (profile.id === activeId ? "  ·  Aktif" : "");
                li.appendChild(label);

                var actions = document.createElement("span");
                actions.className = "order-item-actions";

                var applyButton = document.createElement("button");
                applyButton.type = "button";
                applyButton.textContent = "Uygula";
                applyButton.style.width = "auto";
                applyButton.style.padding = "0 8px";
                applyButton.addEventListener("click", function () {
                    api.applyProfile(profile.id);
                    // HwmonPanelLayout.save() zaten Panel'e uyguluyor; bu sayfadaki sürükle-bırak
                    // listesini de güncel göstermek için initPanelLayoutForm'un state'ini tazeleyip
                    // en basit yol olarak sayfayı yeniden çizmek yerine event tetikleriz.
                    document.dispatchEvent(new CustomEvent("hwmon:panel-layout-profile-applied"));
                    renderProfiles();
                });
                actions.appendChild(applyButton);

                var deleteButton = document.createElement("button");
                deleteButton.type = "button";
                deleteButton.textContent = "Sil";
                deleteButton.style.width = "auto";
                deleteButton.style.padding = "0 8px";
                deleteButton.addEventListener("click", function () {
                    api.remove(profile.id);
                    renderProfiles();
                });
                actions.appendChild(deleteButton);

                li.appendChild(actions);
                list.appendChild(li);
            });
        }

        renderProfiles();

        saveAsButton.addEventListener("click", function () {
            var name = (nameInput.value || "").trim();
            if (!name) return;
            api.saveCurrentAs(name);
            nameInput.value = "";
            renderProfiles();
        });

        // Başka bir profil "Uygula" ile aktif edildiğinde, yukarıdaki sürükle-bırak listesi
        // (initPanelLayoutForm) de güncel sırayı göstersin diye sayfa o kısmı yeniden oluşturur.
        document.addEventListener("hwmon:panel-layout-profile-applied", function () {
            var layoutList = document.getElementById("panel-layout-list");
            if (layoutList) location.reload();
        });
    })();

    // Anlık Görüntü — window.HwmonSnapshotSettings (site.js) 📸 butonuyla oluşturulan PNG'de hangi
    // kartların ve her kartın içinde hangi değerlerin, hangi sırayla göründüğünü tutar. Her liste
    // (kartlar ve her kartın değerleri) Kart Sıralaması ile aynı yukarı/aşağı desenini kullanır,
    // ayrıca bir kutucukla açılıp kapatılabilir.
    (function initSnapshotSettingsForm() {
        var api = window.HwmonSnapshotSettings;
        if (!api) return;

        var state = api.load();

        function buildItem(labelText, checked, onToggle, disableUp, disableDown, onUp, onDown) {
            var li = document.createElement("li");
            li.className = "order-item";

            var labelWrap = document.createElement("label");
            labelWrap.className = "order-item-label";

            var checkbox = document.createElement("input");
            checkbox.type = "checkbox";
            checkbox.checked = checked;
            checkbox.addEventListener("change", function () { onToggle(checkbox.checked); });

            var text = document.createElement("span");
            text.textContent = labelText;

            labelWrap.appendChild(checkbox);
            labelWrap.appendChild(text);
            li.appendChild(labelWrap);

            var actions = document.createElement("span");
            actions.className = "order-item-actions";

            var upButton = document.createElement("button");
            upButton.type = "button";
            upButton.textContent = "▲";
            upButton.setAttribute("aria-label", "Yukarı taşı");
            upButton.disabled = disableUp;
            upButton.addEventListener("click", onUp);
            actions.appendChild(upButton);

            var downButton = document.createElement("button");
            downButton.type = "button";
            downButton.textContent = "▼";
            downButton.setAttribute("aria-label", "Aşağı taşı");
            downButton.disabled = disableDown;
            downButton.addEventListener("click", onDown);
            actions.appendChild(downButton);

            li.appendChild(actions);
            return li;
        }

        function renderBlocks() {
            var list = document.getElementById("snapshot-blocks");
            if (!list) return;
            list.innerHTML = "";

            state.blockOrder.forEach(function (blockKey, index) {
                list.appendChild(buildItem(
                    api.blockLabels[blockKey] || blockKey,
                    !!state.blocksEnabled[blockKey],
                    function (checked) { state.blocksEnabled[blockKey] = checked; },
                    index === 0,
                    index === state.blockOrder.length - 1,
                    function () { moveBlock(index, -1); },
                    function () { moveBlock(index, 1); }
                ));
            });
        }

        function moveBlock(index, delta) {
            var target = index + delta;
            if (target < 0 || target >= state.blockOrder.length) return;
            var tmp = state.blockOrder[index];
            state.blockOrder[index] = state.blockOrder[target];
            state.blockOrder[target] = tmp;
            renderBlocks();
        }

        function renderMetrics(blockKey) {
            var list = document.getElementById("snapshot-metrics-" + blockKey);
            if (!list) return;
            list.innerHTML = "";

            state.metricOrder[blockKey].forEach(function (metricKey, index) {
                list.appendChild(buildItem(
                    (api.metricLabels[blockKey] && api.metricLabels[blockKey][metricKey]) || metricKey,
                    !!state.metricsEnabled[blockKey][metricKey],
                    function (checked) { state.metricsEnabled[blockKey][metricKey] = checked; },
                    index === 0,
                    index === state.metricOrder[blockKey].length - 1,
                    function () { moveMetric(blockKey, index, -1); },
                    function () { moveMetric(blockKey, index, 1); }
                ));
            });
        }

        function moveMetric(blockKey, index, delta) {
            var order = state.metricOrder[blockKey];
            var target = index + delta;
            if (target < 0 || target >= order.length) return;
            var tmp = order[index];
            order[index] = order[target];
            order[target] = tmp;
            renderMetrics(blockKey);
        }

        var groupsContainer = document.getElementById("snapshot-metric-groups");
        if (groupsContainer) {
            Object.keys(state.metricOrder).forEach(function (blockKey) {
                var group = document.createElement("div");
                group.className = "order-group";

                var heading = document.createElement("h4");
                heading.textContent = api.blockLabels[blockKey] || blockKey;
                group.appendChild(heading);

                var list = document.createElement("ol");
                list.className = "order-list";
                list.id = "snapshot-metrics-" + blockKey;
                group.appendChild(list);

                groupsContainer.appendChild(group);
                renderMetrics(blockKey);
            });
        }

        renderBlocks();

        var resolutionSelect = document.getElementById("snapshot-resolution");
        if (resolutionSelect) {
            resolutionSelect.value = state.resolution;
            resolutionSelect.addEventListener("change", function () {
                state.resolution = api.resolutions[resolutionSelect.value] ? resolutionSelect.value : api.defaultResolution;
            });
        }

        var saveButton = document.getElementById("snapshot-settings-save");
        if (saveButton) {
            saveButton.addEventListener("click", function () {
                api.save(state);
                flashSavedNote("snapshot-settings-saved-note");
            });
        }

        var resetButton = document.getElementById("snapshot-settings-reset");
        if (resetButton) {
            resetButton.addEventListener("click", function () {
                try { localStorage.removeItem(api.storageKey); } catch (e) { /* gizli sekme: yok say */ }
                state = api.load();
                renderBlocks();
                Object.keys(state.metricOrder).forEach(renderMetrics);
                if (resolutionSelect) resolutionSelect.value = state.resolution;
                flashSavedNote("snapshot-settings-saved-note");
            });
        }
    })();

    (function initAmbientSettingsForm() {
        var api = window.HwmonAmbientSettings;
        var container = document.getElementById("ambient-metric-checkboxes");
        if (!api || !container) return;

        var enabled = api.load();

        api.metrics.forEach(function (key) {
            var label = document.createElement("label");
            label.className = "color-field";
            label.style.flexDirection = "row";
            label.style.alignItems = "center";
            label.style.gap = "8px";

            var checkbox = document.createElement("input");
            checkbox.type = "checkbox";
            checkbox.checked = enabled[key];
            checkbox.addEventListener("change", function () {
                enabled[key] = checkbox.checked;
                api.save(enabled);
            });

            var text = document.createElement("span");
            text.textContent = api.labels[key] || key;

            label.appendChild(checkbox);
            label.appendChild(text);
            container.appendChild(label);
        });
    })();

    // Görünüm (arka plan rengi) — window.HwmonBackgroundColors (site.js) aktif temaya göre
    // --bg'yi override eder; burada yalnızca formu o API üzerinden okuyup yazıyoruz.
    var BG_FIELD_IDS = { light: "bg-light", dark: "bg-dark" };

    (function initBackgroundForm() {
        var api = window.HwmonBackgroundColors;
        if (!api) return;

        var saved = api.load();
        Object.keys(BG_FIELD_IDS).forEach(function (key) {
            var el = document.getElementById(BG_FIELD_IDS[key]);
            if (el) el.value = saved[key] || api.defaults[key];
        });

        var saveButton = document.getElementById("bg-save");
        if (saveButton) {
            saveButton.addEventListener("click", function () {
                var next = {};
                Object.keys(BG_FIELD_IDS).forEach(function (key) {
                    var el = document.getElementById(BG_FIELD_IDS[key]);
                    next[key] = el ? el.value : api.defaults[key];
                });
                try { localStorage.setItem(api.storageKey, JSON.stringify(next)); } catch (e) { /* kota aşımı/gizli sekme: yok say */ }
                api.apply(next);
                // bkz. initColorsForm'daki aynı isimli kontrol — değerler hâlâ aktif preset'le
                // aynıysa (Renkler formunda hiçbir şey değişmemişken buradan Kaydet'e basılmışsa
                // dahi) preset bağını koparma.
                var activePresetOnBgSave = findActivePreset();
                if (!valuesMatchPreset(next, activePresetOnBgSave && activePresetOnBgSave.bg)) {
                    setActivePresetId(null);
                }
                flashSavedNote("bg-saved-note");
            });
        }

        var resetButton = document.getElementById("bg-reset");
        if (resetButton) {
            resetButton.addEventListener("click", function () {
                var activePreset = findActivePreset();
                var target = activePreset ? activePreset.bg : api.defaults;
                Object.keys(BG_FIELD_IDS).forEach(function (key) {
                    var el = document.getElementById(BG_FIELD_IDS[key]);
                    if (el) el.value = target[key] || api.defaults[key];
                });
                if (activePreset) {
                    try { localStorage.setItem(api.storageKey, JSON.stringify(activePreset.bg)); } catch (e) { /* kota aşımı/gizli sekme: yok say */ }
                    api.apply(activePreset.bg);
                } else {
                    try { localStorage.removeItem(api.storageKey); } catch (e) { /* gizli sekme: yok say */ }
                    api.clear();
                }
                flashSavedNote("bg-saved-note");
            });
        }
    })();

    // Oyun Profilleri — window.HwmonGameProfiles (site.js) işlem adına göre renk/eşik/ambiyans
    // profillerini saklar; burada yalnızca liste + ekle/düzenle/sil formu var. Profilin CANLI
    // uygulanması (ön plandaki işlem değişince) dashboard.js'te yapılır.
    (function initGameProfilesForm() {
        var api = window.HwmonGameProfiles;
        var accentApi = window.HwmonAccentColors;
        var thresholdsApi = window.HwmonThresholds;
        if (!api || !accentApi || !thresholdsApi) return;

        var COLOR_KEYS = Object.keys(accentApi.vars);
        var editingIndex = -1; // -1 = yeni profil ekleniyor

        // Bilinen oyun işlem adları — /History/GameList, game_sessions tablosundaki (yalnızca tam
        // ekranda ETW'den ölçülmüş FPS ile kaydedilmiş, bkz. EtwFpsProvider) process_name'leri
        // döndürür. Bu liste findMatch()'in karşılaştıracağı GERÇEK değerlerin aynısı olduğu için
        // kullanıcının kısa/gündelik oyun adını (ör. "valorant") bu listeye karşı eşleştirmek
        // güvenilir; daha önce hiç tam ekran çalışmamış bir oyun bu listede yer almaz.
        var knownGameProcessNames = [];
        fetch("/History/GameList")
            .then(function (r) { return r.json(); })
            .then(function (names) {
                knownGameProcessNames = Array.isArray(names) ? names : [];
                updateProcessNameHint();
            })
            .catch(function () { knownGameProcessNames = []; });

        function normalizeProcessNameForMatch(name) {
            return (name || "").toLowerCase().replace(/[^a-z0-9]/g, "");
        }

        // Kullanıcının yazdığı adı bilinen işlem adlarına karşı eşleştirir: önce normalize edilmiş
        // (tire/boşluk/nokta atılmış, küçük harf) TAM eşleşme, yoksa bilinen adın bu yazıyla
        // BAŞLADIĞI en kısa eşleşme (ör. "valorant" -> "VALORANT-Win64-Shipping"). 3 karakterden kısa
        // girişler eşleştirilmez — aksi halde ör. "cs" gibi bir giriş çok fazla yanlış pozitif üretir.
        function resolveKnownProcessName(typed) {
            var typedNorm = normalizeProcessNameForMatch(typed);
            if (typedNorm.length < 3) return null;
            var best = null;
            for (var i = 0; i < knownGameProcessNames.length; i++) {
                var known = knownGameProcessNames[i];
                var knownNorm = normalizeProcessNameForMatch(known);
                if (knownNorm === typedNorm) return known;
                if (knownNorm.indexOf(typedNorm) === 0 && (!best || known.length < best.length)) best = known;
            }
            return best;
        }

        function updateProcessNameHint() {
            var hint = document.getElementById("gp-process-name-hint");
            var input = document.getElementById("gp-process-name");
            if (!hint || !input) return;
            var segments = input.value.split(",").map(function (s) { return s.trim(); }).filter(Boolean);
            var matches = [];
            segments.forEach(function (seg) {
                var resolved = resolveKnownProcessName(seg);
                if (resolved && resolved.toLowerCase() !== seg.toLowerCase()) {
                    matches.push('"' + seg + '" → "' + resolved + '"');
                }
            });
            hint.textContent = matches.length > 0 ? ("Eşleşme bulundu: " + matches.join(", ") + " (kaydedince bu ad kullanılacak)") : "";
        }

        function colorInput(key) { return document.getElementById("gp-color-" + key); }

        // Dikkat alanları (gp-th-*-caution) bu düzeltmeyle eklendi; sunucu henüz yeniden
        // başlatılmadan yalnızca bu JS dosyası dağıtılırsa (statik varlık, derleme gerektirmez)
        // eski cshtml'de bu input'lar bulunmaz — null referans hatasıyla tüm formun çökmesini
        // önlemek için var/yok kontrolü burada.
        function setIfExists(id, value) {
            var el = document.getElementById(id);
            if (el) el.value = value;
        }
        // Number("") === 0, NaN DEĞİL — alan boş bırakılınca (ki "Dikkat'i boş bırakırsanız genel
        // Eşik Ayarları'ndaki değeriniz kullanılır" diye belgelenmiş bir davranış) eskiden sessizce
        // 0 kaydediliyordu, "genel değeri kullan" hiç devreye girmiyordu. Artık boş/yalnızca boşluk
        // ya da sayı olmayan bir değer de eleman yokmuş gibi `fallback`'e düşüyor.
        function numIfExists(id, fallback) {
            var el = document.getElementById(id);
            if (!el || el.value.trim() === "") return fallback;
            var n = Number(el.value);
            return isNaN(n) ? fallback : n;
        }

        // Panel düzeni profilleri (window.HwmonPanelLayoutProfiles, Ayarlar → Panel Düzeni'nde
        // oluşturulur) her form gösteriminde yeniden okunur — kullanıcı yeni bir profil eklemiş
        // olabilir.
        function populatePanelLayoutProfileOptions() {
            var select = document.getElementById("gp-panel-layout-profile");
            if (!select || !window.HwmonPanelLayoutProfiles) return;
            var current = select.value;
            select.innerHTML = '<option value="">Yok (genel düzen kullanılır)</option>';
            HwmonPanelLayoutProfiles.load().forEach(function (p) {
                var option = document.createElement("option");
                option.value = p.id;
                option.textContent = p.name;
                select.appendChild(option);
            });
            select.value = current;
        }

        function resetForm() {
            editingIndex = -1;
            document.getElementById("gp-process-name").value = "";
            updateProcessNameHint();
            document.getElementById("gp-auto-ambient").checked = false;
            document.getElementById("gp-auto-snapshot").checked = false;
            populatePanelLayoutProfileOptions();
            document.getElementById("gp-panel-layout-profile").value = "";
            COLOR_KEYS.forEach(function (key) {
                var el = colorInput(key);
                if (el) el.value = accentApi.defaults[key];
            });

            var thresholds = thresholdsApi.load();
            document.getElementById("gp-th-cpu-metric").value = thresholds.cpu.metric;
            setIfExists("gp-th-cpu-caution", thresholds.cpu.cautionValue);
            document.getElementById("gp-th-cpu-value").value = thresholds.cpu.value;
            document.getElementById("gp-th-gpu-metric").value = thresholds.gpu.metric;
            setIfExists("gp-th-gpu-caution", thresholds.gpu.cautionValue);
            document.getElementById("gp-th-gpu-value").value = thresholds.gpu.value;
            setIfExists("gp-th-ram-usage-caution", thresholds.ramCautionPercent);
            document.getElementById("gp-th-ram-usage").value = thresholds.ramUsedPercent;
            setIfExists("gp-th-disk-temp-caution", thresholds.diskCautionTempC);
            document.getElementById("gp-th-disk-temp").value = thresholds.diskTempC;
        }

        function fillFormFromProfile(profile) {
            document.getElementById("gp-process-name").value = api.getProcessNames(profile).join(", ");
            updateProcessNameHint();
            document.getElementById("gp-auto-ambient").checked = !!profile.autoAmbientMode;
            document.getElementById("gp-auto-snapshot").checked = !!profile.autoSnapshotOnExit;
            populatePanelLayoutProfileOptions();
            document.getElementById("gp-panel-layout-profile").value = profile.panelLayoutProfileId || "";
            COLOR_KEYS.forEach(function (key) {
                var el = colorInput(key);
                if (el) el.value = (profile.colors && profile.colors[key]) || accentApi.defaults[key];
            });

            // Eski profillerde (bu düzeltmeden önce kaydedilmiş) Dikkat alanı hiç yoktu; o durumda
            // sabit bir varsayılana değil, kullanıcının O ANKİ genel Eşik Ayarları'ndaki Dikkat
            // değerine düşülür — sabit varsayılana düşmek kullanıcının tercihini görmezden gelirdi.
            // Ama genel Dikkat değeri o profilin Kritik değerine eşit ya da büyükse (ör. genel
            // Dikkat=95, bu profilin Kritik'i de 95), formu OLDUĞU GİBİ (hiç dokunmadan) kaydetmeye
            // çalışmak yeni Dikkat<Kritik doğrulamasına takılıp reddediliyordu — kullanıcı hiçbir şey
            // değiştirmediği hâlde. Yalnızca bu OTOMATİK DÜŞÜLEN değer Kritik'in altına çekilir;
            // profilin kendi kaydettiği açık bir Dikkat değerine asla dokunulmaz.
            function safeCautionFallback(fallbackValue, criticalValue, min) {
                return fallbackValue < criticalValue ? fallbackValue : Math.max(min, criticalValue - 1);
            }

            var general = thresholdsApi.load();
            var t = profile.thresholds || {};
            var cpuCriticalValue = (t.cpu && t.cpu.value) || 85;
            var gpuCriticalValue = (t.gpu && t.gpu.value) || 85;
            var ramCriticalValue = typeof t.ramUsedPercent === "number" ? t.ramUsedPercent : 90;
            var diskCriticalValue = typeof t.diskTempC === "number" ? t.diskTempC : 60;

            document.getElementById("gp-th-cpu-metric").value = (t.cpu && t.cpu.metric) || "temp";
            setIfExists("gp-th-cpu-caution", (t.cpu && typeof t.cpu.cautionValue === "number")
                ? t.cpu.cautionValue : safeCautionFallback(general.cpu.cautionValue, cpuCriticalValue, 0));
            document.getElementById("gp-th-cpu-value").value = cpuCriticalValue;
            document.getElementById("gp-th-gpu-metric").value = (t.gpu && t.gpu.metric) || "temp";
            setIfExists("gp-th-gpu-caution", (t.gpu && typeof t.gpu.cautionValue === "number")
                ? t.gpu.cautionValue : safeCautionFallback(general.gpu.cautionValue, gpuCriticalValue, 0));
            document.getElementById("gp-th-gpu-value").value = gpuCriticalValue;
            setIfExists("gp-th-ram-usage-caution", typeof t.ramCautionPercent === "number"
                ? t.ramCautionPercent : safeCautionFallback(general.ramCautionPercent, ramCriticalValue, 0));
            document.getElementById("gp-th-ram-usage").value = ramCriticalValue;
            setIfExists("gp-th-disk-temp-caution", typeof t.diskCautionTempC === "number"
                ? t.diskCautionTempC : safeCautionFallback(general.diskCautionTempC, diskCriticalValue, 0));
            document.getElementById("gp-th-disk-temp").value = diskCriticalValue;
        }

        function renderList() {
            var list = document.getElementById("game-profiles-list");
            if (!list) return;
            list.innerHTML = "";

            var profiles = api.load();
            if (profiles.length === 0) {
                var empty = document.createElement("li");
                empty.className = "stat-sub";
                empty.textContent = "Henüz profil eklenmedi.";
                list.appendChild(empty);
                return;
            }

            profiles.forEach(function (profile, index) {
                var li = document.createElement("li");
                li.className = "order-item";

                var labelWrap = document.createElement("span");
                labelWrap.className = "order-item-label";
                labelWrap.style.cursor = "default";
                labelWrap.style.minWidth = "0"; // flex çocuğunun aşağıdaki ellipsis için küçülebilmesi için

                var swatches = document.createElement("span");
                swatches.style.display = "inline-flex";
                swatches.style.gap = "3px";
                ["cpu", "gpu", "ram", "fps"].forEach(function (key) {
                    var dot = document.createElement("span");
                    dot.style.width = "10px";
                    dot.style.height = "10px";
                    dot.style.borderRadius = "50%";
                    dot.style.display = "inline-block";
                    dot.style.background = (profile.colors && profile.colors[key]) || accentApi.defaults[key];
                    swatches.appendChild(dot);
                });
                labelWrap.appendChild(swatches);

                // Çok uzun bir işlem adı listesi (ör. 150+ karakter) satırı taşırıp yatay scrollbar
                // oluşturmasın diye kırpılır — girdi tarafında maxlength olsa da savunma katmanı.
                var text = document.createElement("span");
                text.textContent = api.getProcessNames(profile).join(", ") + (profile.autoAmbientMode ? "   ·   Ambiyans: Açık" : "");
                text.style.overflow = "hidden";
                text.style.textOverflow = "ellipsis";
                text.style.whiteSpace = "nowrap";
                text.style.display = "inline-block";
                text.style.maxWidth = "100%";
                text.style.verticalAlign = "middle";
                labelWrap.appendChild(text);
                li.appendChild(labelWrap);

                var actions = document.createElement("span");
                actions.className = "order-item-actions";

                var editButton = document.createElement("button");
                editButton.type = "button";
                editButton.textContent = "Düzenle";
                editButton.style.width = "auto";
                editButton.style.padding = "0 8px";
                editButton.addEventListener("click", function () {
                    editingIndex = index;
                    fillFormFromProfile(profile);
                });
                actions.appendChild(editButton);

                var deleteButton = document.createElement("button");
                deleteButton.type = "button";
                deleteButton.textContent = "Sil";
                deleteButton.style.width = "auto";
                deleteButton.style.padding = "0 8px";
                deleteButton.addEventListener("click", function () {
                    var label = api.getProcessNames(profile).join(", ") || "bu profil";
                    if (!confirm("\"" + label + "\" profilini silmek istediğinize emin misiniz?")) return;
                    var current = api.load();
                    current.splice(index, 1);
                    api.save(current);
                    renderList();
                    if (editingIndex === index) resetForm();
                });
                actions.appendChild(deleteButton);

                li.appendChild(actions);
                list.appendChild(li);
            });
        }

        resetForm();
        renderList();

        var processNameInput = document.getElementById("gp-process-name");
        if (processNameInput) processNameInput.addEventListener("input", updateProcessNameHint);

        var newButton = document.getElementById("gp-new");
        if (newButton) newButton.addEventListener("click", function () { resetForm(); updateProcessNameHint(); });

        var saveButton = document.getElementById("gp-save");
        if (saveButton) {
            saveButton.addEventListener("click", function () {
                // Virgülle ayrılmış birden fazla işlem adı desteklenir (ör. bir oyunun hem
                // "Oyun.exe" hem "Oyun-Win64-Shipping" olarak görünen iki farklı süreç adı olması).
                var gpErrorBox = document.getElementById("gp-th-error");
                if (gpErrorBox) { gpErrorBox.textContent = ""; gpErrorBox.style.display = "none"; }

                var processNames = document.getElementById("gp-process-name").value
                    .split(",")
                    .map(function (s) { return s.trim(); })
                    .filter(Boolean);
                if (processNames.length === 0) return;

                // Kaydetmeden önce her yazılan adı bilinen gerçek işlem adına çözümle (ör. "valorant"
                // -> "VALORANT-Win64-Shipping"); bilinen listede karşılığı yoksa yazılan ad olduğu gibi
                // kalır (runtime'daki findMatch() zaten tam bu adla karşılaştıracak).
                var autoMatchedPairs = [];
                processNames = processNames.map(function (typed) {
                    var resolved = resolveKnownProcessName(typed);
                    if (resolved && resolved.toLowerCase() !== typed.toLowerCase()) {
                        autoMatchedPairs.push('"' + typed + '" → "' + resolved + '"');
                        return resolved;
                    }
                    return typed;
                });

                // Aynı işlem adıyla iki profil oluşturulmasını engelle — aksi halde hangisinin
                // uygulanacağı belirsiz kalırdı (canlı testte bulunan hata). Eşleşen profilin index'i
                // `api.load()`'un DÖNDÜRDÜĞÜ AYNI dizi üzerinden bulunmalı — `api.findMatch` kendi
                // içinde ayrı bir `load()` çağrısı yapıp localStorage'ı yeniden JSON.parse ettiğinden,
                // döndürdüğü nesne referansı buradaki `existingProfiles` dizisindeki hiçbir öğeyle asla
                // eşleşmez (`indexOf` her zaman -1 döner); bu da hem yeni-kayıtta çakışmayı hiç
                // yakalamıyor hem de düzenlemede (editingIndex de -1 değilse) adı değiştirmeden
                // kaydetmeyi yanlışlıkla çakışma sayıyordu. Düzenlenmekte olan profilin kendisiyle
                // çakışması (değişiklik yapmadan tekrar kaydetme) hata sayılmaz.
                var existingProfiles = api.load();
                for (var pnIdx = 0; pnIdx < processNames.length; pnIdx++) {
                    var lowerName = processNames[pnIdx].toLowerCase();
                    var matchIndex = -1;
                    for (var epIdx = 0; epIdx < existingProfiles.length; epIdx++) {
                        var epNames = api.getProcessNames(existingProfiles[epIdx]);
                        if (epNames.some(function (n) { return n.toLowerCase() === lowerName; })) {
                            matchIndex = epIdx;
                            break;
                        }
                    }
                    if (matchIndex !== -1 && matchIndex !== editingIndex) {
                        if (gpErrorBox) {
                            gpErrorBox.textContent = "\"" + processNames[pnIdx] + "\" işlem adıyla zaten başka bir profil var.";
                            gpErrorBox.style.display = "block";
                        }
                        return;
                    }
                }

                var colors = {};
                COLOR_KEYS.forEach(function (key) {
                    var el = colorInput(key);
                    colors[key] = el ? el.value : accentApi.defaults[key];
                });

                // Dikkat kutusu boş bırakılırsa (UI'da belgelendiği gibi) kullanıcının O ANKİ genel
                // Eşik Ayarları değeri kullanılır — sabit bir yer tutucu (ör. -Infinity) DEĞİL, çünkü
                // öyle bir değer olduğu gibi kaydedilirse JSON.stringify onu `null`'a çevirir ve
                // profil bozulurdu.
                // Ana Eşik Ayarları formundaki gibi: fiziksel olarak anlamsız (negatif, aşırı büyük)
                // değerler kaydedilmeden önce metriğe uygun sınıra kırpılır — bu form şu ana kadar
                // hiç kırpma yapmıyordu.
                function clamp(x, min, max) { return Math.min(Math.max(x, min), max); }
                function clampByMetric(x, metric) { return metric === "usage" ? clamp(x, 0, 100) : clamp(x, 0, 150); }

                var cpuMetric = document.getElementById("gp-th-cpu-metric").value;
                var gpuMetric = document.getElementById("gp-th-gpu-metric").value;
                var generalForSave = thresholdsApi.load();
                var cpuCaution = clampByMetric(numIfExists("gp-th-cpu-caution", generalForSave.cpu.cautionValue), cpuMetric);
                var cpuCritical = clampByMetric(Number(document.getElementById("gp-th-cpu-value").value), cpuMetric);
                var gpuCaution = clampByMetric(numIfExists("gp-th-gpu-caution", generalForSave.gpu.cautionValue), gpuMetric);
                var gpuCritical = clampByMetric(Number(document.getElementById("gp-th-gpu-value").value), gpuMetric);
                var ramCaution = clamp(numIfExists("gp-th-ram-usage-caution", generalForSave.ramCautionPercent), 0, 100);
                var ramCritical = clamp(Number(document.getElementById("gp-th-ram-usage").value), 0, 100);
                var diskCaution = clamp(numIfExists("gp-th-disk-temp-caution", generalForSave.diskCautionTempC), 0, 150);
                var diskCritical = clamp(Number(document.getElementById("gp-th-disk-temp").value), 0, 150);

                var invalidLabels = [];
                if (cpuCaution >= cpuCritical) invalidLabels.push("İşlemci");
                if (gpuCaution >= gpuCritical) invalidLabels.push("Ekran Kartı");
                if (ramCaution >= ramCritical) invalidLabels.push("Bellek");
                if (diskCaution >= diskCritical) invalidLabels.push("Disk");
                if (invalidLabels.length > 0) {
                    if (gpErrorBox) {
                        gpErrorBox.textContent = "Dikkat değeri Kritik değerinden küçük olmalı: " + invalidLabels.join(", ") + ". Kaydedilmedi.";
                        gpErrorBox.style.display = "block";
                    }
                    return;
                }

                var profile = {
                    processNames: processNames,
                    autoAmbientMode: document.getElementById("gp-auto-ambient").checked,
                    autoSnapshotOnExit: document.getElementById("gp-auto-snapshot").checked,
                    panelLayoutProfileId: document.getElementById("gp-panel-layout-profile").value || null,
                    colors: colors,
                    thresholds: {
                        cpu: { metric: document.getElementById("gp-th-cpu-metric").value, value: cpuCritical, cautionValue: cpuCaution },
                        gpu: { metric: document.getElementById("gp-th-gpu-metric").value, value: gpuCritical, cautionValue: gpuCaution },
                        ramUsedPercent: ramCritical,
                        ramCautionPercent: ramCaution,
                        diskTempC: diskCritical,
                        diskCautionTempC: diskCaution
                    }
                };

                var profiles = api.load();
                if (editingIndex >= 0 && editingIndex < profiles.length) {
                    profiles[editingIndex] = profile;
                } else {
                    profiles.push(profile);
                }
                api.save(profiles);
                renderList();
                flashSavedNote("gp-saved-note");

                var automatchNote = document.getElementById("gp-automatch-note");
                if (automatchNote) {
                    if (autoMatchedPairs.length > 0) {
                        automatchNote.textContent = "Otomatik eşleşti: " + autoMatchedPairs.join(", ");
                        automatchNote.style.display = "inline";
                        setTimeout(function () { automatchNote.style.display = "none"; }, 6000);
                    } else {
                        automatchNote.style.display = "none";
                    }
                }
                updateProcessNameHint();
            });
        }
    })();

    // Yerel Ağa Açma — diğer bölümlerden farklı olarak bu ayar tarayıcıda değil sunucuda
    // (remote-access.json) saklanır, çünkü Kestrel'in hangi adrese bağlanacağına uygulama
    // başlarken karar verilir. Bu yüzden localStorage yerine RemoteAccessController'a fetch
    // ile GET/POST yapılıyor; değişiklik yalnızca yeniden başlatınca etkili olur.
    (function initRemoteAccessForm() {
        var enabledCheckbox = document.getElementById("ra-enabled");
        var pinInput = document.getElementById("ra-pin");
        var pinHint = document.getElementById("ra-pin-hint");
        var httpsCheckbox = document.getElementById("ra-https-enabled");
        var httpsHint = document.getElementById("ra-https-hint");
        var regenCertButton = document.getElementById("ra-regen-cert");
        var lanInfo = document.getElementById("ra-lan-info");
        var restartWarning = document.getElementById("ra-restart-warning");
        var errorBox = document.getElementById("ra-error");
        var saveButton = document.getElementById("ra-save");
        var restartButton = document.getElementById("ra-restart");
        var restartNote = document.getElementById("ra-restart-note");
        var tokenInput = document.querySelector("#remote-access input[name='__RequestVerificationToken']");
        if (!enabledCheckbox || !pinInput || !saveButton || !tokenInput) return;

        if (httpsCheckbox && httpsHint) {
            httpsCheckbox.addEventListener("change", function () {
                httpsHint.style.display = httpsCheckbox.checked ? "block" : "none";
            });
        }

        // "Yerel ağdan erişime izin ver" kapatılırken "HTTPS kullan" işaretli kalabiliyordu; bu,
        // kaydedilen ham HttpsEnabled=true'nun etkin olmayan bir ayarla (Enabled=false) birlikte
        // saklanmasına ve "yeniden başlatma gerekli" uyarısının hiç kapanmamasına yol açıyordu
        // (bkz. RemoteAccessController.Status). HTTPS zaten yalnızca Yerel Ağa Açma etkinken bir
        // anlam taşıdığından, kapatılınca otomatik işareti kaldırılır ve tekrar açılana kadar
        // tıklanamaz hâle getirilir.
        function syncHttpsAvailability() {
            if (!httpsCheckbox) return;
            if (!enabledCheckbox.checked) {
                httpsCheckbox.checked = false;
                httpsCheckbox.disabled = true;
                if (httpsHint) httpsHint.style.display = "none";
            } else {
                httpsCheckbox.disabled = false;
            }
        }
        enabledCheckbox.addEventListener("change", syncHttpsAvailability);

        if (regenCertButton) {
            regenCertButton.addEventListener("click", function () {
                fetch("/RemoteAccess/RegenerateCertificate", {
                    method: "POST",
                    headers: { "Content-Type": "application/x-www-form-urlencoded" },
                    body: "__RequestVerificationToken=" + encodeURIComponent(tokenInput.value)
                }).then(function () {
                    httpsHint.textContent = "Sertifika silindi. Bir sonraki yeniden başlatmada güncel ağ adresleriyle yenisi üretilecek.";
                });
            });
        }

        function showError(message) {
            if (!errorBox) return;
            errorBox.textContent = message;
            errorBox.style.display = message ? "block" : "none";
        }

        function renderStatus(status) {
            enabledCheckbox.checked = status.savedEnabled;
            pinHint.textContent = status.savedPinConfigured ? "(değiştirmek için yeni PIN girin, korumak için boş bırakın)" : "(gerekli)";

            // HTTPS yalnızca "Yerel ağdan erişime izin ver" AÇIKKEN bir anlam taşır; ham
            // savedHttpsEnabled'ı tek başına kullanmak (Enabled=false iken bile true kalabilir —
            // bkz. RemoteAccessController.Status'taki aynı düzeltme) hem kutunun hem de aşağıdaki
            // adres metninin yanlış görünmesine yol açardı.
            var effectiveHttpsEnabled = status.savedEnabled && !!status.savedHttpsEnabled;

            if (httpsCheckbox) {
                httpsCheckbox.checked = effectiveHttpsEnabled;
                syncHttpsAvailability();
                if (httpsHint) httpsHint.style.display = httpsCheckbox.checked ? "block" : "none";
            }

            if (status.lanAddresses && status.lanAddresses.length > 0) {
                var scheme = effectiveHttpsEnabled ? "https" : "http";
                var port = effectiveHttpsEnabled ? status.httpsPort : status.port;
                lanInfo.textContent = "Yerel ağdan erişim adresi: " +
                    status.lanAddresses.map(function (ip) { return scheme + "://" + ip + ":" + port; }).join(", ");
            } else {
                lanInfo.textContent = "Bu bilgisayar için yerel ağ adresi bulunamadı.";
            }

            if (status.restartRequired) {
                restartWarning.textContent = "Kaydedilen ayar henüz etkin değil. Etkili olması için uygulamayı yeniden başlatın.";
                restartWarning.style.display = "block";
                if (restartButton) restartButton.style.display = "";
            } else {
                restartWarning.style.display = "none";
                if (restartButton) restartButton.style.display = "none";
            }
        }

        fetch("/RemoteAccess/Status")
            .then(function (r) { return r.json(); })
            .then(renderStatus)
            .catch(function () { /* durum okunamadıysa form varsayılan (kapalı) görünümde kalır */ });

        saveButton.addEventListener("click", function () {
            if (saveButton.disabled) return; // istek zaten sürüyor, çift tıklamayı yok say
            showError("");
            var enabled = enabledCheckbox.checked;
            var pin = pinInput.value.trim();

            if (enabled && !pin && pinHint.textContent.indexOf("gerekli") !== -1) {
                showError("Yerel ağa açmak için bir PIN belirlemelisiniz.");
                return;
            }
            if (pin && pin.length < 4) {
                showError("PIN en az 4 karakter olmalı.");
                return;
            }
            if (pin && pin.length > 32) {
                showError("PIN en fazla 32 karakter olabilir.");
                return;
            }

            var body = new URLSearchParams();
            body.set("__RequestVerificationToken", tokenInput.value);
            body.set("enabled", enabled ? "true" : "false");
            if (pin) body.set("pin", pin);
            body.set("httpsEnabled", (httpsCheckbox && httpsCheckbox.checked) ? "true" : "false");

            saveButton.disabled = true;
            fetch("/RemoteAccess/Configure", {
                method: "POST",
                headers: { "Content-Type": "application/x-www-form-urlencoded" },
                body: body.toString()
            }).then(function (r) {
                if (!r.ok) return r.json().then(function (data) { throw new Error(data.error || "Kaydedilemedi."); });
                return r.json();
            }).then(function () {
                pinInput.value = "";
                flashSavedNote("ra-saved-note");
                return fetch("/RemoteAccess/Status").then(function (r) { return r.json(); }).then(renderStatus);
            }).catch(function (err) {
                showError(err.message || "Kaydedilemedi.");
            }).finally(function () {
                saveButton.disabled = false;
            });
        });

        // Restart tetiklendikten sonra sayfayı sabit bir süre (ör. 10 sn) sonra yenilemek riskli:
        // UAC penceresi kullanıcı tarafından o kadar hızlı onaylanmayabilir, bu durumda sayfa tam
        // sunucu henüz ayakta değilken yenilenir ve tarayıcının kendi "siteye ulaşılamıyor"
        // hatasına düşer (kendiliğinden tekrar denemez, mevcut durumdan daha kötü olur). Bunun
        // yerine ESKİ sunucunun gerçekten kapandığı (bir isteğin başarısız olduğu) görülene kadar,
        // sonra YENİ sunucu ayağa kalkana (bir isteğin başarılı olduğu) kadar birkaç saniyede bir
        // hafif bir istek denenir; ilk başarılı yanıtta sayfa otomatik yenilenir. UAC 5 sn'de de
        // onaylansa 40 sn'de de sonuç aynıdır. Üst sınır aşılırsa (UAC reddedilmiş olabilir) bugünkü
        // "elle yenileyin" mesajına düşülür.
        function waitForRestartAndReload() {
            var pollIntervalMs = 2000;
            var maxWaitMs = 120000;
            var startedAt = Date.now();
            var sawServerGoDown = false;

            function poll() {
                if (Date.now() - startedAt > maxWaitMs) {
                    if (restartNote) {
                        restartNote.textContent = "Sunucu beklenenden uzun sürede geri gelmedi. UAC penceresini onayladıysanız sayfayı elle yenileyin.";
                    }
                    return;
                }

                fetch("/RemoteAccess/Status", { cache: "no-store" })
                    .then(function (r) {
                        if (sawServerGoDown && r.ok) {
                            location.reload();
                            return;
                        }
                        // Eski sunucu (kapanmadan hemen önceki ~300ms içinde) hâlâ yanıt veriyor
                        // olabilir; bunu "geri geldi" saymadan önce gerçekten kapandığını görmemiz
                        // gerekir — aksi hâlde sayfa yeniden başlamadan önce kendini yenileyip aynı
                        // (kapanmakta olan) sürece bağlanabilir.
                        setTimeout(poll, pollIntervalMs);
                    })
                    .catch(function () {
                        sawServerGoDown = true;
                        setTimeout(poll, pollIntervalMs);
                    });
            }

            setTimeout(poll, pollIntervalMs);
        }

        if (restartButton) {
            restartButton.addEventListener("click", function () {
                if (restartButton.disabled) return;
                if (!confirm("Uygulama şimdi kapatılıp yeniden başlatılacak. Windows bir yönetici izni (UAC) penceresi gösterecek, onaylamanız gerekir. Devam edilsin mi?")) {
                    return;
                }

                var body = new URLSearchParams();
                body.set("__RequestVerificationToken", tokenInput.value);

                restartButton.disabled = true;
                fetch("/RemoteAccess/Restart", {
                    method: "POST",
                    headers: { "Content-Type": "application/x-www-form-urlencoded" },
                    body: body.toString()
                }).then(function (r) {
                    if (!r.ok) throw new Error("Yeniden başlatma tetiklenemedi.");
                    if (restartNote) {
                        restartNote.textContent = "Yeniden başlatılıyor. Birkaç saniye içinde UAC penceresi açılacak, onaylayın. Sunucu geri gelince sayfa kendiliğinden yenilenecek.";
                        restartNote.style.display = "inline";
                    }
                    waitForRestartAndReload();
                }).catch(function () {
                    restartButton.disabled = false;
                    showError("Yeniden başlatma tetiklenemedi. Uygulamayı elle kapatıp tekrar açabilirsiniz.");
                });
            });
        }
    })();

    // Ayarları dışa/içe aktar — window.HwmonSettingsBackup (site.js). İçe aktarma sonrası sayfa
    // yeniden yüklenir: onlarca modülün her birine ayrı ayrı "değişti" olayı göndermek yerine, her
    // modül zaten sayfa yüklenirken kendi load()'unu çağırıyor — bu en basit ve hatasız yol.
    (function initSettingsBackup() {
        var api = window.HwmonSettingsBackup;
        var exportButton = document.getElementById("settings-export");
        var importTrigger = document.getElementById("settings-import-trigger");
        var importFile = document.getElementById("settings-import-file");
        var status = document.getElementById("settings-backup-status");
        if (!api || !exportButton || !importTrigger || !importFile) return;

        exportButton.addEventListener("click", function () {
            api.exportToFile();
            if (status) status.textContent = "Dosya indirildi.";
        });

        importTrigger.addEventListener("click", function () {
            importFile.value = "";
            importFile.click();
        });

        importFile.addEventListener("change", function () {
            var file = importFile.files && importFile.files[0];
            if (!file) return;

            var reader = new FileReader();
            reader.onload = function () {
                var result = api.importFromText(String(reader.result));
                if (!result.ok) {
                    if (status) status.textContent = "İçe aktarılamadı: " + result.error;
                    return;
                }
                if (status) status.textContent = result.count + " ayar içe aktarıldı, sayfa yenileniyor…";
                setTimeout(function () { window.location.reload(); }, 800);
            };
            reader.onerror = function () {
                if (status) status.textContent = "Dosya okunamadı.";
            };
            reader.readAsText(file);
        });
    })();

    // Otomatik başlatma — durum HKCU\...\Run kayıt defteri anahtarının kendisinden okunur
    // (RemoteAccess'teki ayrı JSON dosyasının aksine, burada başka bir "tek doğru kaynak"a gerek
    // yok: anahtarın var/yok olması zaten gerçek durumdur).
    (function initStartupForm() {
        var enabledCheckbox = document.getElementById("startup-enabled");
        var pathInfo = document.getElementById("startup-path-info");
        var mismatchWarning = document.getElementById("startup-mismatch-warning");
        var errorBox = document.getElementById("startup-error");
        var saveButton = document.getElementById("startup-save");
        var tokenInput = document.querySelector("#startup-form input[name='__RequestVerificationToken']");
        if (!enabledCheckbox || !saveButton || !tokenInput) return;

        function showError(message) {
            if (!errorBox) return;
            errorBox.textContent = message;
            errorBox.style.display = message ? "block" : "none";
        }

        function renderStatus(status) {
            enabledCheckbox.checked = status.enabled;
            if (pathInfo) pathInfo.textContent = "Başlangıç yolu: " + status.exePath;
            if (mismatchWarning) mismatchWarning.style.display = status.pathMismatch ? "block" : "none";
        }

        fetch("/Startup/Status")
            .then(function (r) { return r.json(); })
            .then(renderStatus)
            .catch(function () { /* durum okunamadıysa form varsayılan (kapalı) görünümde kalır */ });

        saveButton.addEventListener("click", function () {
            if (saveButton.disabled) return;
            showError("");

            var body = new URLSearchParams();
            body.set("__RequestVerificationToken", tokenInput.value);
            body.set("enabled", enabledCheckbox.checked ? "true" : "false");

            saveButton.disabled = true;
            fetch("/Startup/Configure", {
                method: "POST",
                headers: { "Content-Type": "application/x-www-form-urlencoded" },
                body: body.toString()
            }).then(function (r) {
                if (!r.ok) return r.json().then(function (data) { throw new Error(data.error || "Kaydedilemedi."); });
                return r.json();
            }).then(function () {
                flashSavedNote("startup-saved-note");
                return fetch("/Startup/Status").then(function (r) { return r.json(); }).then(renderStatus);
            }).catch(function (err) {
                showError(err.message || "Kaydedilemedi.");
            }).finally(function () {
                saveButton.disabled = false;
            });
        });
    })();
})();
