// High-Performance Lightweight Canvas Chart Renderer for SCADA Industrial Light Theme
// drawLineChart(canvasId, values, color, startLabel, endLabel, minFixed, maxFixed, times)
//   startLabel/endLabel  - optional bottom axis time labels ("now-1h" / "now")
//   minFixed/maxFixed    - optional y-axis min/max to keep charts stable across refreshes
//   times                - optional array of epoch-ms x positions aligned 1:1 with `values`;
//                          when provided the x-axis becomes a real time scale with
//                          time-of-day tick labels under the canvas
window.drawLineChart = function (canvasId, values, color, startLabel, endLabel, minFixed, maxFixed, times) {
    const canvas = document.getElementById(canvasId);
    if (!canvas || !values || values.length === 0) return;

    const hasTimeScale = Array.isArray(times) && times.length === values.length;
    let tMin = 0, tMax = 0;
    if (hasTimeScale) {
        tMin = Math.min(...times);
        tMax = Math.max(...times);
    }

    const ctx = canvas.getContext('2d');
    const dpr = window.devicePixelRatio || 1;
    const width = canvas.clientWidth;
    const height = canvas.clientHeight;
    if (width === 0 || height === 0) return;

    canvas.width = width * dpr;
    canvas.height = height * dpr;
    ctx.scale(dpr, dpr);
    ctx.clearRect(0, 0, width, height);

    let min = (typeof minFixed === 'number' && !isNaN(minFixed)) ? minFixed : Math.min(...values);
    let max = (typeof maxFixed === 'number' && !isNaN(maxFixed)) ? maxFixed : Math.max(...values);
    if (min === max) { min -= 1; max += 1; }
    const range = (max - min) || 1;
    const padTop = 18;
    const padBottom = hasTimeScale ? 28 : 22;
    const padSide = 12;

    const chartHeight = height - padTop - padBottom;
    const chartWidth = width - padSide * 2;

    const spanMs = hasTimeScale ? (tMax - tMin) : 0;
    const axisFormat = function (ms) {
        const d = new Date(ms);
        const pad = function (n) { return (n < 10 ? '0' : '') + n; };
        const hhmm = pad(d.getHours()) + ':' + pad(d.getMinutes());
        return spanMs > 24 * 3600 * 1000 ? pad(d.getMonth() + 1) + '-' + pad(d.getDate()) + ' ' + hhmm : hhmm;
    };

    // Draw Subtle Slate Grid Lines
    ctx.strokeStyle = 'rgba(15, 23, 42, 0.06)';
    ctx.lineWidth = 1;
    for (let i = 0; i <= 3; i++) {
        const y = padTop + (chartHeight / 3) * i;
        ctx.beginPath();
        ctx.moveTo(padSide, y);
        ctx.lineTo(width - padSide, y);
        ctx.stroke();
    }

    // Real-time x positions when a timeline is supplied, otherwise even spacing.
    const xOf = function (i) {
        if (hasTimeScale && (tMax - tMin) > 1) {
            return padSide + ((times[i] - tMin) / (tMax - tMin)) * chartWidth;
        }
        return padSide + (i / (values.length - 1 || 1)) * chartWidth;
    };

    const points = values.map((v, i) => {
        const x = xOf(i);
        const y = padTop + chartHeight - ((v - min) / range) * chartHeight;
        return [x, y];
    });

    // Soft Filled Gradient Area
    const grad = ctx.createLinearGradient(0, padTop, 0, height - padBottom);
    grad.addColorStop(0, color + '33');
    grad.addColorStop(1, color + '00');

    ctx.beginPath();
    ctx.moveTo(points[0][0], height - padBottom);
    points.forEach(p => ctx.lineTo(p[0], p[1]));
    ctx.lineTo(points[points.length - 1][0], height - padBottom);
    ctx.closePath();
    ctx.fillStyle = grad;
    ctx.fill();

    // Chart Line
    ctx.beginPath();
    points.forEach((p, i) => (i === 0 ? ctx.moveTo(p[0], p[1]) : ctx.lineTo(p[0], p[1])));
    ctx.strokeStyle = color;
    ctx.lineWidth = 2.5;
    ctx.lineJoin = 'round';
    ctx.stroke();

    // Latest Value Indicator Dot
    const lastP = points[points.length - 1];
    ctx.beginPath();
    ctx.arc(lastP[0], lastP[1], 4.5, 0, Math.PI * 2);
    ctx.fillStyle = '#ffffff';
    ctx.fill();
    ctx.strokeStyle = color;
    ctx.lineWidth = 2.5;
    ctx.stroke();

    // Latest Value Text (Crisp Dark Text)
    const latestVal = values[values.length - 1];
    ctx.fillStyle = '#0f172a';
    ctx.font = 'bold 11px -apple-system, BlinkMacSystemFont, Segoe UI, monospace';
    ctx.textAlign = 'right';
    ctx.fillText(latestVal.toFixed(1), width - padSide, padTop - 5);

    // Bottom time axis - end labels
    ctx.font = '10px -apple-system, BlinkMacSystemFont, Segoe UI, monospace';
    ctx.fillStyle = '#64748b';
    ctx.textAlign = 'left';
    ctx.fillText(startLabel || '', padSide, height - 6);
    ctx.textAlign = 'right';
    ctx.fillText(endLabel || '', width - padSide, height - 6);

    // Real time-of-day ticks: vertical gridlines + labels spread across the span.
    if (hasTimeScale && (tMax - tMin) > 1) {
        const tickCount = 4; // labels at 0%, 25%, 50%, 75%, 100%
        for (let k = 0; k <= tickCount; k++) {
            const frac = k / tickCount;
            const target = tMin + frac * (tMax - tMin);
            let i = 0;
            let best = Infinity;
            for (let j = 0; j < times.length; j++) {
                const d = Math.abs(times[j] - target);
                if (d < best) { best = d; i = j; }
            }
            const x = xOf(i);
            ctx.strokeStyle = 'rgba(15, 23, 42, 0.05)';
            ctx.beginPath();
            ctx.moveTo(x, padTop);
            ctx.lineTo(x, height - padBottom);
            ctx.stroke();
            ctx.fillStyle = '#94a3b8';
            ctx.font = '9.5px -apple-system, BlinkMacSystemFont, Segoe UI, monospace';
            ctx.textAlign = k === 0 ? 'left' : (k === tickCount ? 'right' : 'center');
            ctx.fillText(axisFormat(times[i]), x, height - 4);
        }
    }
};

window.drawComparisonChart = function (canvasId, currentVals, prevVals) {
    const canvas = document.getElementById(canvasId);
    if (!canvas || !currentVals || currentVals.length === 0) return;

    const ctx = canvas.getContext('2d');
    const dpr = window.devicePixelRatio || 1;
    const width = canvas.clientWidth;
    const height = canvas.clientHeight;
    if (width === 0 || height === 0) return;

    canvas.width = width * dpr;
    canvas.height = height * dpr;
    ctx.scale(dpr, dpr);
    ctx.clearRect(0, 0, width, height);

    const all = [...currentVals, ...(prevVals || [])];
    const min = Math.min(...all);
    const max = Math.max(...all);
    const range = (max - min) || 1;
    const padTop = 24;
    const padBottom = 24;
    const padSide = 20;

    const chartHeight = height - padTop - padBottom;
    const chartWidth = width - padSide * 2;

    // Grid lines
    ctx.strokeStyle = 'rgba(15, 23, 42, 0.06)';
    ctx.lineWidth = 1;
    for (let i = 0; i <= 4; i++) {
        const y = padTop + (chartHeight / 4) * i;
        ctx.beginPath();
        ctx.moveTo(padSide, y);
        ctx.lineTo(width - padSide, y);
        ctx.stroke();
    }

    // Previous Period Line (Dashed Slate)
    if (prevVals && prevVals.length > 0) {
        const prevPoints = prevVals.map((v, i) => [
            padSide + (i / (prevVals.length - 1 || 1)) * chartWidth,
            padTop + chartHeight - ((v - min) / range) * chartHeight
        ]);

        ctx.setLineDash([5, 5]);
        ctx.beginPath();
        prevPoints.forEach((p, i) => (i === 0 ? ctx.moveTo(p[0], p[1]) : ctx.lineTo(p[0], p[1])));
        ctx.strokeStyle = '#94a3b8';
        ctx.lineWidth = 2;
        ctx.stroke();
        ctx.setLineDash([]); // reset
    }

    // Current Period Line (Solid Blue Accent)
    const curPoints = currentVals.map((v, i) => [
        padSide + (i / (currentVals.length - 1 || 1)) * chartWidth,
        padTop + chartHeight - ((v - min) / range) * chartHeight
    ]);

    // Gradient fill
    const grad = ctx.createLinearGradient(0, padTop, 0, height - padBottom);
    grad.addColorStop(0, 'rgba(2, 132, 199, 0.20)');
    grad.addColorStop(1, 'rgba(2, 132, 199, 0.00)');

    ctx.beginPath();
    ctx.moveTo(curPoints[0][0], height - padBottom);
    curPoints.forEach(p => ctx.lineTo(p[0], p[1]));
    ctx.lineTo(curPoints[curPoints.length - 1][0], height - padBottom);
    ctx.closePath();
    ctx.fillStyle = grad;
    ctx.fill();

    ctx.beginPath();
    curPoints.forEach((p, i) => (i === 0 ? ctx.moveTo(p[0], p[1]) : ctx.lineTo(p[0], p[1])));
    ctx.strokeStyle = '#0284c7';
    ctx.lineWidth = 3;
    ctx.lineJoin = 'round';
    ctx.stroke();
};
window.downloadTextFile = function (filename, content, mime) {
    const blob = new Blob([content], { type: mime || 'text/plain;charset=utf-8' });
    const url = URL.createObjectURL(blob);
    const a = document.createElement('a');
    a.href = url;
    a.download = filename;
    document.body.appendChild(a);
    a.click();
    document.body.removeChild(a);
    setTimeout(() => URL.revokeObjectURL(url), 2000);
};

// Binary download (PDF / Excel / images) from a base64 string the server returned.
window.downloadBlob = function (filename, base64, mime) {
    if (!base64) return false;
    try {
        const binary = atob(base64);
        const bytes = new Uint8Array(binary.length);
        for (let i = 0; i < binary.length; i++) bytes[i] = binary.charCodeAt(i);
        const blob = new Blob([bytes], { type: mime || 'application/octet-stream' });
        const url = URL.createObjectURL(blob);
        const a = document.createElement('a');
        a.href = url;
        a.download = filename;
        document.body.appendChild(a);
        a.click();
        document.body.removeChild(a);
        setTimeout(() => URL.revokeObjectURL(url), 2000);
        return true;
    } catch (e) {
        console.error('downloadBlob failed', e);
        return false;
    }
};

// Smooth arc-gauge animation for the Plant Energy page (CSS transition on stroke-dashoffset).
// updates = [{ id, offset, value }]  ->  #pe-fill-{id} stroke-dashoffset + #pe-val-{id} textContent
window.animPeGauge = function (updates) {
    if (!Array.isArray(updates) || updates.length === 0) return;
    updates.forEach(function (u) {
        var fill = document.getElementById('pe-fill-' + u.id);
        var val = document.getElementById('pe-val-' + u.id);
        if (fill) fill.style.strokeDashoffset = u.offset.toFixed(1);
        if (val) val.textContent = u.value;
    });
};
