// ═══════════════════════════════════════════════════════════════════════
// TAD.RV Teacher Dashboard — Client-side Logic
// (C) 2026 TAD Europe — https://tad-it.eu
//
// Receives student status/video from the C# host via window.chrome.webview.
// Renders a live grid of student tiles with thumbnail previews.
// Uses the WebCodecs API for hardware-accelerated H.264 decoding on
// the teacher's iGPU (i5-12400 UHD 730).
// ═══════════════════════════════════════════════════════════════════════

'use strict';

// ── State ────────────────────────────────────────────────────────────

const students = new Map();           // ip → { status, canvas, decoder, ... }
let activeRvIp = null;                // Currently viewed remote view IP
let rvDecoder = null;                 // WebCodecs VideoDecoder for fullscreen RV

// ── Message Bridge (C# → JS) ────────────────────────────────────────

window.chrome.webview.addEventListener('message', (event) => {
    const msg = event.data;
    if (!msg || !msg.type) return;

    switch (msg.type) {
        case 'status':
            handleStatusUpdate(msg.ip, msg.data);
            break;
        case 'video_frame':
            handleVideoFrame(msg.ip, msg.data, msg.keyFrame);
            break;
        case 'add_students':
            msg.ips.forEach(ip => ensureStudentTile(ip));
            break;
    }
});

// ── Status Handling ──────────────────────────────────────────────────

function handleStatusUpdate(ip, data) {
    let student = students.get(ip);
    if (!student) {
        student = createStudentEntry(ip);
        students.set(ip, student);
        renderTile(student);
    }

    student.status = data;
    student.lastSeen = Date.now();
    updateTileUI(student);
    updateStats();
}

function createStudentEntry(ip) {
    return {
        ip: ip,
        status: null,
        lastSeen: 0,
        canvas: null,       // Thumbnail canvas element
        ctx: null,          // Canvas 2D context
        decoder: null,      // WebCodecs VideoDecoder for thumbnail
        tileEl: null        // DOM element
    };
}

// ── Tile Rendering ───────────────────────────────────────────────────

function renderTile(student) {
    const grid = document.getElementById('studentGrid');
    const empty = document.getElementById('emptyState');
    if (empty) empty.style.display = 'none';

    const tile = document.createElement('div');
    tile.className = 'student-tile';
    tile.id = `tile-${student.ip.replace(/\./g, '-')}`;
    tile.innerHTML = `
        <div class="tile-thumb">
            <canvas width="480" height="270"></canvas>
            <div class="placeholder">&#xE7F4;</div>
            <div class="tile-badges"></div>
        </div>
        <div class="tile-actions">
            <button class="tile-btn" onclick="event.stopPropagation(); startRv('${student.ip}')" title="Remote View">&#xE7B3;</button>
            <button class="tile-btn danger" onclick="event.stopPropagation(); lockStudent('${student.ip}')" title="Lock">&#xE72E;</button>
            <button class="tile-btn success" onclick="event.stopPropagation(); unlockStudent('${student.ip}')" title="Unlock">&#xE785;</button>
        </div>
        <div class="tile-info">
            <span class="tile-hostname">${student.ip}</span>
            <span class="tile-user">—</span>
        </div>
        <div class="tile-window"></div>
    `;

    tile.addEventListener('click', () => startRv(student.ip));

    grid.appendChild(tile);
    student.tileEl = tile;
    student.canvas = tile.querySelector('canvas');
    student.ctx = student.canvas.getContext('2d');
}

function updateTileUI(student) {
    if (!student.tileEl || !student.status) return;
    const s = student.status;

    // Update text
    student.tileEl.querySelector('.tile-hostname').textContent = s.Hostname || student.ip;
    student.tileEl.querySelector('.tile-user').textContent = s.Username || '—';
    student.tileEl.querySelector('.tile-window').textContent = s.ActiveWindow || '';

    // Update badges
    const badges = student.tileEl.querySelector('.tile-badges');
    let html = '';
    if (s.IsLocked) {
        html += '<span class="badge badge-locked">Locked</span>';
        student.tileEl.classList.add('locked');
    } else {
        student.tileEl.classList.remove('locked');
    }
    if (s.IsStreaming) html += '<span class="badge badge-streaming">RV</span>';
    if (s.DriverLoaded) html += '<span class="badge badge-online">Online</span>';
    else html += '<span class="badge badge-offline">Offline</span>';
    badges.innerHTML = html;

    // Hide placeholder if we have video
    const placeholder = student.tileEl.querySelector('.placeholder');
    if (student.hasReceivedFrame && placeholder) placeholder.style.display = 'none';
}

function ensureStudentTile(ip) {
    if (!students.has(ip)) {
        const student = createStudentEntry(ip);
        students.set(ip, student);
        renderTile(student);
    }
}

// ── Video Frame Handling ─────────────────────────────────────────────

function handleVideoFrame(ip, base64Data, isKeyFrame) {
    const student = students.get(ip);
    if (!student) return;

    const data = Uint8Array.from(atob(base64Data), c => c.charCodeAt(0));

    // ── Thumbnail decoder (always active for grid view) ──
    if (!student.decoder) {
        student.decoder = createVideoDecoder(student.canvas, student.ctx, 480, 270);
    }
    decodeFrame(student.decoder, data, isKeyFrame);
    student.hasReceivedFrame = true;

    // ── Fullscreen RV decoder ──
    if (activeRvIp === ip && rvDecoder) {
        const rvCanvas = document.getElementById('rvCanvas');
        const rvCtx = rvCanvas.getContext('2d');
        decodeFrame(rvDecoder, data, isKeyFrame);
    }
}

// ── WebCodecs H.264 Decoder ──────────────────────────────────────────

function createVideoDecoder(canvas, ctx, width, height) {
    if (typeof VideoDecoder === 'undefined') {
        console.warn('WebCodecs API not available — falling back to static thumbnails');
        return null;
    }

    const decoder = new VideoDecoder({
        output: (frame) => {
            // Draw decoded frame to canvas
            ctx.drawImage(frame, 0, 0, width, height);
            frame.close();
        },
        error: (e) => {
            console.error('Decoder error:', e);
        }
    });

    decoder.configure({
        codec: 'avc1.42E01E',  // H.264 Baseline Level 3.0
        hardwareAcceleration: 'prefer-hardware',
        optimizeForLatency: true
    });

    return decoder;
}

function decodeFrame(decoder, data, isKeyFrame) {
    if (!decoder || decoder.state === 'closed') return;

    try {
        const chunk = new EncodedVideoChunk({
            type: isKeyFrame ? 'key' : 'delta',
            timestamp: performance.now() * 1000,
            data: data
        });
        decoder.decode(chunk);
    } catch (e) {
        // Request keyframe on error
        console.warn('Decode error, need keyframe:', e);
    }
}

// ── Remote View Modal ────────────────────────────────────────────────

function startRv(ip) {
    activeRvIp = ip;
    const student = students.get(ip);
    const modal = document.getElementById('rvModal');
    const title = document.getElementById('rvTitle');
    const canvas = document.getElementById('rvCanvas');

    title.textContent = `Remote View — ${student?.status?.Hostname || ip}`;
    modal.style.display = 'flex';

    // Create fullscreen decoder
    const ctx = canvas.getContext('2d');
    rvDecoder = createVideoDecoder(canvas, ctx, 1920, 1080);

    // Tell C# to start streaming from this student
    sendToHost({ action: 'rv_start', target: ip });
}

function closeRv() {
    if (activeRvIp) {
        sendToHost({ action: 'rv_stop', target: activeRvIp });
    }
    document.getElementById('rvModal').style.display = 'none';

    if (rvDecoder && rvDecoder.state !== 'closed') {
        rvDecoder.close();
    }
    rvDecoder = null;
    activeRvIp = null;
}

function toggleRvCensored() {
    // Toggle privacy/censored mode — blurs sensitive areas client-side
    const canvas = document.getElementById('rvCanvas');
    canvas.classList.toggle('censored');
}

function lockFromRv() {
    if (activeRvIp) lockStudent(activeRvIp);
}

// ── Student Commands (JS → C#) ──────────────────────────────────────

function lockStudent(ip) {
    sendToHost({ action: 'lock', target: ip });
}

function unlockStudent(ip) {
    sendToHost({ action: 'unlock', target: ip });
}

function sendToHost(msg) {
    if (window.chrome?.webview) {
        window.chrome.webview.postMessage(JSON.stringify(msg));
    }
}

// ── Stats Bar ────────────────────────────────────────────────────────

function updateStats() {
    const now = Date.now();
    let online = 0, locked = 0, streaming = 0, offline = 0;

    students.forEach(s => {
        const isOnline = s.status && (now - s.lastSeen < 10000);
        if (isOnline) {
            online++;
            if (s.status.IsLocked) locked++;
            if (s.status.IsStreaming) streaming++;
        } else {
            offline++;
            if (s.tileEl) s.tileEl.classList.add('offline');
        }
    });

    document.getElementById('statOnline').textContent = online;
    document.getElementById('statLocked').textContent = locked;
    document.getElementById('statStreaming').textContent = streaming;
    document.getElementById('statOffline').textContent = offline;
}

// Refresh stats every 5 seconds
setInterval(updateStats, 5000);

// ── Keyboard Shortcuts ───────────────────────────────────────────────

document.addEventListener('keydown', (e) => {
    if (e.key === 'Escape' && activeRvIp) {
        closeRv();
    }
});

console.log('[TAD.RV] Teacher Dashboard initialized');
