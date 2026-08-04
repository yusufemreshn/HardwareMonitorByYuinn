/*
 * HardwareMonitorByYuinn — bağımlılıksız çoklu seri zaman grafiği.
 *
 * Chart.js yerine yazıldı. İhtiyacımız olan kadarını yapar: kayan zaman penceresi,
 * biri yüzde biri serbest ölçekli iki Y ekseni, seri gizle/göster, imleç ipucu,
 * tema değişiminde renk yenileme ve yüksek DPI'lı ekranlarda net çizim.
 */
window.HwmonChart = (function () {
    "use strict";

    var AXIS_PAD_LEFT = 46;
    var AXIS_PAD_RIGHT = 46;
    var AXIS_PAD_TOP = 12;
    var AXIS_PAD_BOTTOM = 26;

    function cssVar(name) {
        return getComputedStyle(document.documentElement).getPropertyValue(name).trim();
    }

    function niceCeil(value) {
        if (value <= 0) return 10;
        var magnitude = Math.pow(10, Math.floor(Math.log10(value)));
        var normalized = value / magnitude;
        var step = normalized <= 1 ? 1 : normalized <= 2 ? 2 : normalized <= 5 ? 5 : 10;
        return step * magnitude;
    }

    function formatClock(ms) {
        return new Date(ms).toLocaleTimeString("tr-TR", { hour: "2-digit", minute: "2-digit", second: "2-digit" });
    }

    function create(canvas, options) {
        var ctx = canvas.getContext("2d");
        // windowMs görünen aralığın uzunluğudur; fare tekerleğiyle yakınlaştırıldığında değişir.
        // retentionMs ise verinin belleğe alındığı üst sınırdır — yakınlaşınca eski veri silinmez,
        // sonradan uzaklaşıldığında hâlâ orada olur.
        var windowMs = options.windowMs;
        var retentionMs = Math.max(options.maxWindowMs || options.windowMs, options.windowMs);
        var minWindowMs = options.minWindowMs || 15000;
        // null: pencere her zaman gerçek "şimdi"de biter (canlı takip). Sayı: görünüm o ana
        // sabitlenmiş (yakınlaştırma sırasında donduruldu); çift tıklayınca canlıya döner.
        var viewEndMs = null;
        var axes = options.axes;

        var series = options.series.map(function (def) {
            return {
                key: def.key,
                label: def.label,
                colorVar: def.colorVar,
                axis: def.axis,
                unit: def.unit || "",
                digits: typeof def.digits === "number" ? def.digits : 1,
                dashed: !!def.dashed,
                color: cssVar(def.colorVar),
                visible: def.visible !== false,
                chartType: def.chartType || "line", // "line" | "area" | "bar"
                points: []
            };
        });

        var hover = null;
        var plot = { x: 0, y: 0, w: 0, h: 0 };

        function effectiveNow() {
            return viewEndMs === null ? Date.now() : viewEndMs;
        }

        function isLive() {
            return viewEndMs === null;
        }

        function seriesByKey(key) {
            for (var i = 0; i < series.length; i++) {
                if (series[i].key === key) return series[i];
            }
            return null;
        }

        function resize() {
            var dpr = window.devicePixelRatio || 1;
            var cssWidth = canvas.clientWidth;
            var cssHeight = canvas.clientHeight;
            if (cssWidth === 0 || cssHeight === 0) return false;

            var targetW = Math.round(cssWidth * dpr);
            var targetH = Math.round(cssHeight * dpr);
            if (canvas.width !== targetW || canvas.height !== targetH) {
                canvas.width = targetW;
                canvas.height = targetH;
            }

            ctx.setTransform(dpr, 0, 0, dpr, 0, 0);
            plot.x = AXIS_PAD_LEFT;
            plot.y = AXIS_PAD_TOP;
            plot.w = cssWidth - AXIS_PAD_LEFT - AXIS_PAD_RIGHT;
            plot.h = cssHeight - AXIS_PAD_TOP - AXIS_PAD_BOTTOM;
            return plot.w > 0 && plot.h > 0;
        }

        // Her eksenin o anki değer aralığı. "auto" olan üst sınır, yalnızca o an GÖRÜNEN pencere
        // içindeki değerlere göre yuvarlanarak büyür (retentionMs kadar saklanan tüm geçmişe göre
        // değil) — aksi hâlde yakınlaştırınca çizgi pencereye sığmasına rağmen dikey ölçek
        // görünüm dışındaki eski bir tepe noktasına göre sabit kalır ve düz görünür.
        function axisRange(axisKey, nowMs) {
            var axis = axes[axisKey];
            if (axis.max !== "auto") return { min: axis.min, max: axis.max };

            var windowStart = nowMs - windowMs;
            var peak = 0;
            for (var i = 0; i < series.length; i++) {
                var s = series[i];
                if (s.axis !== axisKey || !s.visible) continue;
                for (var j = 0; j < s.points.length; j++) {
                    if (s.points[j].t < windowStart) continue;
                    if (s.points[j].v > peak) peak = s.points[j].v;
                }
            }

            return { min: axis.min, max: Math.max(niceCeil(peak * 1.1), axis.fallbackMax || 10) };
        }

        function toX(tMs, nowMs) {
            return plot.x + ((tMs - (nowMs - windowMs)) / windowMs) * plot.w;
        }

        function toY(value, range) {
            var ratio = (value - range.min) / (range.max - range.min);
            return plot.y + plot.h - Math.max(0, Math.min(1, ratio)) * plot.h;
        }

        function drawFrame(nowMs, ranges) {
            var gridColor = cssVar("--grid-line");
            var textColor = cssVar("--text-muted");

            ctx.font = "11px 'Segoe UI', system-ui, sans-serif";
            ctx.strokeStyle = gridColor;
            ctx.fillStyle = textColor;
            ctx.lineWidth = 1;

            // Yatay ızgara + her iki eksenin etiketleri
            var steps = 5;
            ctx.textBaseline = "middle";
            for (var i = 0; i <= steps; i++) {
                var y = plot.y + (plot.h * i) / steps;
                ctx.beginPath();
                ctx.moveTo(plot.x, Math.round(y) + 0.5);
                ctx.lineTo(plot.x + plot.w, Math.round(y) + 0.5);
                ctx.stroke();

                var frac = 1 - i / steps;
                ctx.textAlign = "right";
                var left = ranges.left.min + (ranges.left.max - ranges.left.min) * frac;
                ctx.fillText(axes[options.leftAxis].format(left), plot.x - 6, y);

                if (options.rightAxis) {
                    ctx.textAlign = "left";
                    var right = ranges.right.min + (ranges.right.max - ranges.right.min) * frac;
                    ctx.fillText(axes[options.rightAxis].format(right), plot.x + plot.w + 6, y);
                }
            }

            // Dikey ızgara: pencereyi eşit zaman dilimlerine böleriz
            var slices = 5;
            ctx.textAlign = "center";
            ctx.textBaseline = "top";
            for (var k = 0; k <= slices; k++) {
                var t = nowMs - windowMs + (windowMs * k) / slices;
                var x = Math.round(toX(t, nowMs)) + 0.5;
                ctx.beginPath();
                ctx.moveTo(x, plot.y);
                ctx.lineTo(x, plot.y + plot.h);
                ctx.stroke();
                ctx.fillText(formatClock(t), x, plot.y + plot.h + 6);
            }
        }

        // Yakınlaştırma sonrası görünüm donmuşsa kullanıcının fark edip canlıya dönebilmesi için
        // sağ üst köşede küçük bir durum etiketi gösterilir.
        function drawLiveBadge() {
            ctx.textAlign = "right";
            ctx.textBaseline = "top";
            ctx.font = "700 11px 'Segoe UI', system-ui, sans-serif";
            if (isLive()) {
                ctx.fillStyle = "#4ade80";
                ctx.fillText("● CANLI", plot.x + plot.w - 4, plot.y + 4);
            } else {
                ctx.fillStyle = cssVar("--text-muted");
                ctx.fillText("⏸ Geçmiş — çift tıkla", plot.x + plot.w - 4, plot.y + 4);
            }
        }

        function hexToRgba(hex, alpha) {
            var h = String(hex).replace("#", "");
            if (h.length === 3) h = h.split("").map(function (c) { return c + c; }).join("");
            var r = parseInt(h.substring(0, 2), 16) || 0;
            var g = parseInt(h.substring(2, 4), 16) || 0;
            var b = parseInt(h.substring(4, 6), 16) || 0;
            return "rgba(" + r + "," + g + "," + b + "," + alpha + ")";
        }

        // "Bar" tipi: her seri kendi bar genişliğini örnekler arası medyan zaman aralığından
        // hesaplar. Birden fazla seri aynı anda "bar" seçilirse üst üste biner — bu kasıtlı bir
        // basitleştirme, bar modu tek seferde bir seriye bakmak için düşünülmüştür.
        function drawBarSeries(s, pts, nowMs, range) {
            var barWidth = 6;
            if (pts.length > 1) {
                var intervals = [];
                for (var k = 1; k < pts.length; k++) intervals.push(pts[k].t - pts[k - 1].t);
                intervals.sort(function (a, b) { return a - b; });
                var medianMs = intervals[Math.floor(intervals.length / 2)] || 1000;
                var pxPerMs = plot.w / windowMs;
                barWidth = Math.max(2, Math.min(24, medianMs * pxPerMs * 0.7));
            }

            ctx.fillStyle = s.color;
            var baseY = toY(range.min, range);
            for (var i = 0; i < pts.length; i++) {
                var x = toX(pts[i].t, nowMs);
                if (x < plot.x - barWidth || x > plot.x + plot.w + barWidth) continue;
                var y = toY(pts[i].v, range);
                ctx.fillRect(x - barWidth / 2, y, barWidth, baseY - y);
            }
        }

        // İki nokta arası bu süreden fazla açılırsa (ör. uygulama o aralıkta kapalıydı, geçmiş
        // sorgusunda o dilim boştu) aradaki çizgiyi ÇİZMEYİZ — noktaları düz bir çizgiyle birleştirmek
        // orada veri varmış izlenimi verir ("yamuk" görünüm). Dakikalık geçmiş verisiyle ince
        // (saniyelik) canlı veri aynı seride yan yana durabildiğinden eşik, normal dakikalık
        // aralıktan (60 sn) belirgin şekilde büyük tutulur.
        var GAP_BREAK_MS = 90 * 1000;

        function splitSegments(pts) {
            var segments = [];
            var current = [];
            for (var i = 0; i < pts.length; i++) {
                if (current.length > 0 && pts[i].t - current[current.length - 1].t > GAP_BREAK_MS) {
                    segments.push(current);
                    current = [];
                }
                current.push(pts[i]);
            }
            if (current.length > 0) segments.push(current);
            return segments;
        }

        function drawSeries(s, nowMs, range) {
            var pts = s.points;
            if (pts.length === 0) return;

            if (s.chartType === "bar") {
                drawBarSeries(s, pts, nowMs, range);
                return;
            }

            ctx.strokeStyle = s.color;
            ctx.lineWidth = 2;
            ctx.lineJoin = "round";
            ctx.lineCap = "round";
            ctx.setLineDash(s.dashed ? [6, 4] : []);

            var segments = splitSegments(pts);
            for (var seg = 0; seg < segments.length; seg++) {
                var segPts = segments[seg];
                ctx.beginPath();
                for (var i = 0; i < segPts.length; i++) {
                    var x = toX(segPts[i].t, nowMs);
                    var y = toY(segPts[i].v, range);
                    if (i === 0) ctx.moveTo(x, y);
                    else ctx.lineTo(x, y);
                }
                ctx.stroke();

                // "Alan" tipi: aynı çizginin altını, ekseni "range.min" tabanına kadar yarı saydam
                // seri rengiyle doldurur. Her parça (segment) kendi tabanına kadar ayrı doldurulur,
                // aksi halde boşluğun altı da yanlışlıkla boyanırdı.
                if (s.chartType === "area") {
                    var lastX = toX(segPts[segPts.length - 1].t, nowMs);
                    var firstX = toX(segPts[0].t, nowMs);
                    var baseY = toY(range.min, range);
                    ctx.lineTo(lastX, baseY);
                    ctx.lineTo(firstX, baseY);
                    ctx.closePath();
                    ctx.fillStyle = hexToRgba(s.color, 0.2);
                    ctx.fill();
                }
            }

            ctx.setLineDash([]);
        }

        function drawHover(nowMs, ranges) {
            if (!hover) return;

            var x = hover.x;
            if (x < plot.x || x > plot.x + plot.w) return;

            var tAtCursor = nowMs - windowMs + ((x - plot.x) / plot.w) * windowMs;

            // İmleç zamanına en yakın örneği her görünür seri için ayrı buluruz.
            var rows = [];
            for (var i = 0; i < series.length; i++) {
                var s = series[i];
                if (!s.visible || s.points.length === 0) continue;

                var best = null, bestDist = Infinity;
                for (var j = 0; j < s.points.length; j++) {
                    var dist = Math.abs(s.points[j].t - tAtCursor);
                    if (dist < bestDist) { bestDist = dist; best = s.points[j]; }
                }

                // Örnek çok uzaksa (veri boşluğu) o seriyi ipucunda göstermeyiz.
                if (best && bestDist <= 4000) {
                    rows.push({ color: s.color, text: s.label + ": " + best.v.toFixed(s.digits) + " " + s.unit, point: best, axis: s.axis });
                }
            }

            if (rows.length === 0) return;

            ctx.strokeStyle = cssVar("--text-muted");
            ctx.lineWidth = 1;
            ctx.setLineDash([3, 3]);
            ctx.beginPath();
            ctx.moveTo(Math.round(x) + 0.5, plot.y);
            ctx.lineTo(Math.round(x) + 0.5, plot.y + plot.h);
            ctx.stroke();
            ctx.setLineDash([]);

            for (var r = 0; r < rows.length; r++) {
                var range = rows[r].axis === options.leftAxis ? ranges.left : ranges.right;
                ctx.fillStyle = rows[r].color;
                ctx.beginPath();
                ctx.arc(toX(rows[r].point.t, nowMs), toY(rows[r].point.v, range), 3, 0, Math.PI * 2);
                ctx.fill();
            }

            var lineHeight = 16;
            var boxW = 0;
            ctx.font = "11px 'Segoe UI', system-ui, sans-serif";
            for (var m = 0; m < rows.length; m++) {
                boxW = Math.max(boxW, ctx.measureText(rows[m].text).width);
            }
            boxW += 26;
            var boxH = lineHeight * rows.length + 20;

            var boxX = x + 12;
            if (boxX + boxW > plot.x + plot.w) boxX = x - boxW - 12;
            var boxY = Math.min(plot.y + 8, plot.y + plot.h - boxH);

            ctx.fillStyle = cssVar("--bg-elevated");
            ctx.strokeStyle = cssVar("--border");
            ctx.lineWidth = 1;
            ctx.beginPath();
            ctx.roundRect(boxX, boxY, boxW, boxH, 8);
            ctx.fill();
            ctx.stroke();

            ctx.fillStyle = cssVar("--text-muted");
            ctx.textAlign = "left";
            ctx.textBaseline = "middle";
            ctx.fillText(formatClock(tAtCursor), boxX + 10, boxY + 12);

            for (var n = 0; n < rows.length; n++) {
                var rowY = boxY + 24 + n * lineHeight;
                ctx.fillStyle = rows[n].color;
                ctx.fillRect(boxX + 10, rowY - 4, 8, 8);
                ctx.fillStyle = cssVar("--text");
                ctx.fillText(rows[n].text, boxX + 24, rowY);
            }
        }

        // Pencerenin dışına düşen örnekleri atar. Her serinin en eski noktası kasıtlı olarak bir
        // adım fazladan tutulur, böylece kesilmesi gereken çizgi kadrajın tam kenarına kadar uzar.
        // Budama burada, çizimin kullandığı nowMs (gerçek "şimdi") ile aynı referansla yapılır;
        // daha önce push() sırasında sunucudan gelen zaman damgasına göre yapılıyordu, bu da
        // yayın gecikmesi kadar render'dan geride kalıyordu. Pencere ilk dolana kadar hiç budama
        // tetiklenmediği için bu fark görünmüyordu; tam dolduğu anda en eski nokta sürekli sınırın
        // az solunda kalıp çizginin kesilmek yerine taşmasına yol açıyordu.
        function trimToWindow(nowMs) {
            var cutoff = nowMs - retentionMs;
            for (var i = 0; i < series.length; i++) {
                var pts = series[i].points;
                while (pts.length > 1 && pts[1].t < cutoff) {
                    pts.shift();
                }
            }
        }

        function render() {
            if (!resize()) return;

            // Budama her zaman gerçek "şimdi"ye göre yapılır (retentionMs kadar), donmuş/yakınlaştırılmış
            // bir görünümde bile eski veri kalıcı olarak silinmesin; sadece çizim effectiveNow'a göre olur.
            trimToWindow(Date.now());
            var nowMs = effectiveNow();

            var ranges = {
                left: axisRange(options.leftAxis, nowMs),
                right: options.rightAxis ? axisRange(options.rightAxis, nowMs) : null
            };

            ctx.clearRect(0, 0, canvas.clientWidth, canvas.clientHeight);
            drawFrame(nowMs, ranges);
            drawLiveBadge();

            // Budama bir noktayı kasıtlı olarak fazladan tutuyor; o nokta pencerenin biraz
            // solunda kalabilir. Çizim alanını kırparak (clip) bu ve benzeri sınır durumlarının
            // çizgiyi asla eksen/ızgara alanına taşırmamasını garanti ederiz.
            ctx.save();
            ctx.beginPath();
            ctx.rect(plot.x, plot.y, plot.w, plot.h);
            ctx.clip();
            for (var i = 0; i < series.length; i++) {
                if (!series[i].visible) continue;
                drawSeries(series[i], nowMs, series[i].axis === options.leftAxis ? ranges.left : ranges.right);
            }
            ctx.restore();

            drawHover(nowMs, ranges);
        }

        function push(key, tMs, value) {
            if (typeof value !== "number" || !isFinite(value)) return;

            var s = seriesByKey(key);
            if (!s) return;

            s.points.push({ t: tMs, v: value });
        }

        function setVisible(key, visible) {
            var s = seriesByKey(key);
            if (s) s.visible = visible;
        }

        function isVisible(key) {
            var s = seriesByKey(key);
            return s ? s.visible : false;
        }

        function setChartType(key, chartType) {
            var s = seriesByKey(key);
            if (s) s.chartType = chartType;
        }

        function refreshColors() {
            for (var i = 0; i < series.length; i++) {
                series[i].color = cssVar(series[i].colorVar);
            }
        }

        // Zaman aralığı toggle butonları (1dk/5dk/15dk/1sa) için: fare tekerleğiyle yakınlaştırmanın
        // yaptığı gibi pencereyi doğrudan bir değere ayarlar ve canlı takibe döner (viewEndMs sıfırlanır).
        // retentionMs sabittir (options.maxWindowMs, bkz. dashboard.js) — toggle hiçbir zaman veriyi
        // yeniden çekmez/değiştirmez, yalnızca zaten bellekte olan veriye zoom yapar. Eskiden her
        // toggle tıklamasında retention de değişip veri sunucudan farklı bir çözünürlükte yeniden
        // seed ediliyordu; bu, dar bir pencereye (ör. "5 dk") geri dönüldüğünde ince çözünürlüklü
        // veri asla geri gelmediği için grafiğin birkaç kaba noktayla "yamuk" görünmesine yol açıyordu.
        function setWindowMs(ms) {
            windowMs = Math.min(retentionMs, Math.max(minWindowMs, ms));
            viewEndMs = null;
            render();
        }

        canvas.addEventListener("mousemove", function (e) {
            var rect = canvas.getBoundingClientRect();
            hover = { x: e.clientX - rect.left, y: e.clientY - rect.top };
            render();
        });

        canvas.addEventListener("mouseleave", function () {
            hover = null;
            render();
        });

        // Fare tekerleği: imlecin altındaki zaman noktası aynı piksel oranında kalacak şekilde
        // yakınlaştırır/uzaklaştırır. Yeni pencere sonu gerçek "şimdi"ye çok yakınsa canlı takibe
        // geri döner; aksi halde görünüm o ana sabitlenir (üstteki rozet bunu belirtir).
        canvas.addEventListener("wheel", function (e) {
            var rect = canvas.getBoundingClientRect();
            var cursorX = e.clientX - rect.left;
            if (cursorX < plot.x || cursorX > plot.x + plot.w) return;

            e.preventDefault();

            var nowMs = effectiveNow();
            var fraction = (cursorX - plot.x) / plot.w;
            var cursorTimeMs = (nowMs - windowMs) + fraction * windowMs;

            var zoomFactor = e.deltaY < 0 ? 0.85 : 1 / 0.85;
            var newWindowMs = Math.min(retentionMs, Math.max(minWindowMs, windowMs * zoomFactor));
            var newEndMs = cursorTimeMs + (1 - fraction) * newWindowMs;
            var realNowMs = Date.now();

            windowMs = newWindowMs;
            viewEndMs = (realNowMs - newEndMs) < 1000 ? null : Math.min(newEndMs, realNowMs);

            render();
        }, { passive: false });

        canvas.addEventListener("dblclick", function () {
            viewEndMs = null;
            render();
        });

        window.addEventListener("resize", render);

        return {
            push: push,
            render: render,
            setVisible: setVisible,
            isVisible: isVisible,
            setChartType: setChartType,
            refreshColors: refreshColors,
            setWindowMs: setWindowMs,
            seed: function (key, points) {
                var s = seriesByKey(key);
                if (!s || !points) return;
                s.points = points.map(function (p) {
                    return { t: new Date(p.timestampUtc).getTime(), v: p.value };
                });
            }
        };
    }

    return { create: create };
})();
