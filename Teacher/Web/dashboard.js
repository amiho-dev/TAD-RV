// ═══════════════════════════════════════════════════════════════════════
// TAD.RV Teacher Dashboard — Client-side Logic
// (C) 2026 TAD Europe — https://tad-it.eu
// TAD.RV — The Greater Brother of the mighty te.comp NET.FX
//
// Receives student status/video from the C# host via window.chrome.webview.
// Renders a live grid of student tiles with thumbnail previews.
// Uses the WebCodecs API for hardware-accelerated H.264 decoding on
// the teacher's iGPU (i5-12400 UHD 730).
//
// Features: Remote View, Lock/Unlock, Freeze, Blank Screen, Hand Raise,
//           Broadcast Message, Search/Filter, Main-Stream focus decoding,
//           Per-tile CPU/RAM, Demo mode support.
// ═══════════════════════════════════════════════════════════════════════

'use strict';

// ── State ────────────────────────────────────────────────────────────

const students = new Map();           // ip → { status, canvas, decoder, ... }
let activeRvIp = null;                // Currently viewed remote view IP
let rvDecoder = null;                 // WebCodecs VideoDecoder for fullscreen RV
let rvMainDecoder = null;             // Main-stream decoder (30fps 720p)
let isDemoMode = false;               // Set by config message from C#
let currentFilter = '';               // Search filter string
let appVersion = '26200.172';         // Updated by config message

// ── Message Bridge (C# → JS) ────────────────────────────────────────

window.chrome.webview.addEventListener('message', (event) => {
    let msg = event.data;
    if (!msg) return;

    // PostWebMessageAsString sends a raw string — parse it
    if (typeof msg === 'string') {
        try { msg = JSON.parse(msg); } catch { return; }
    }
    if (!msg.type) return;

    switch (msg.type) {
        case 'config':
            isDemoMode = !!msg.demoMode;
            if (msg.version) {
                appVersion = msg.version;
                const verEl = document.getElementById('aboutVersion');
                if (verEl) verEl.textContent = 'v' + appVersion;
            }
            if (isDemoMode) {
                const hint = document.getElementById('emptyHint');
                if (hint) hint.textContent = 'Demo mode — synthetic students will appear shortly.';
            }
            break;

        case 'status':
            handleStatusUpdate(msg.ip, msg.data);
            break;

        case 'video_frame':
            handleVideoFrame(msg.ip, msg.data, msg.keyFrame);
            break;

        case 'main_frame':
            handleMainFrame(msg.ip, msg.data, msg.keyFrame);
            break;

        case 'demo_frame':
            handleDemoFrame(msg.ip, msg.frame);
            break;

        case 'add_students':
            msg.ips.forEach(ip => ensureStudentTile(ip));
            break;

        case 'freeze_all':
            showAnnouncement(msg.frozen
                ? 'All screens frozen — Eyes on the teacher!'
                : null);
            break;

        case 'blank_all':
            showAnnouncement(msg.blanked
                ? 'All screens blanked — Attention mode active'
                : null);
            break;

        case 'show_message_dialog':
            openMessageDialog();
            break;

        case 'updateAvailable':
            showUpdateBanner(msg.version, msg.releaseNotes, msg.htmlUrl);
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
        canvas: null,
        ctx: null,
        decoder: null,
        tileEl: null,
        hasReceivedFrame: false
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
    tile.dataset.ip = student.ip;
    tile.innerHTML = `
        <div class="tile-thumb">
            <canvas width="480" height="270"></canvas>
            <div class="placeholder">&#xE7F4;</div>
            <div class="tile-badges"></div>
            <div class="tile-hand-indicator" style="display:none">&#xE768;</div>
        </div>
        <div class="tile-actions">
            <button class="tile-btn" onclick="event.stopPropagation(); startRv('${student.ip}')" title="Remote View">&#xE7B3;</button>
            <button class="tile-btn danger" onclick="event.stopPropagation(); lockStudent('${student.ip}')" title="Lock">&#xE72E;</button>
            <button class="tile-btn success" onclick="event.stopPropagation(); unlockStudent('${student.ip}')" title="Unlock">&#xE785;</button>
            <button class="tile-btn" onclick="event.stopPropagation(); freezeStudent('${student.ip}')" title="Freeze">&#xE7AD;</button>
        </div>
        <div class="tile-info">
            <span class="tile-hostname">${student.ip}</span>
            <span class="tile-user">—</span>
        </div>
        <div class="tile-metrics">
            <span class="tile-cpu">CPU —</span>
            <span class="tile-ram">RAM —</span>
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
    const tile = student.tileEl;

    // Update text
    tile.querySelector('.tile-hostname').textContent = s.Hostname || student.ip;
    tile.querySelector('.tile-user').textContent = s.Username || '—';
    tile.querySelector('.tile-window').textContent = s.ActiveWindow || '';

    // CPU / RAM metrics
    const cpu = tile.querySelector('.tile-cpu');
    const ram = tile.querySelector('.tile-ram');
    if (s.CpuUsage !== undefined) {
        cpu.textContent = `CPU ${Math.round(s.CpuUsage)}%`;
        cpu.className = 'tile-cpu' + (s.CpuUsage > 80 ? ' high' : '');
    }
    if (s.RamUsedMb !== undefined) {
        const gb = (s.RamUsedMb / 1024).toFixed(1);
        ram.textContent = `RAM ${gb} GB`;
    }

    // Hand raise indicator
    const handEl = tile.querySelector('.tile-hand-indicator');
    if (handEl) {
        handEl.style.display = s.IsHandRaised ? 'flex' : 'none';
        if (s.IsHandRaised) tile.classList.add('hand-raised');
        else tile.classList.remove('hand-raised');
    }

    // Update badges
    const badges = tile.querySelector('.tile-badges');
    let html = '';

    if (s.IsLocked) {
        html += '<span class="badge badge-locked">Locked</span>';
        tile.classList.add('locked');
    } else {
        tile.classList.remove('locked');
    }

    if (s.IsFrozen) {
        html += '<span class="badge badge-frozen">Frozen</span>';
        tile.classList.add('frozen');
    } else {
        tile.classList.remove('frozen');
    }

    if (s.IsBlankScreen) {
        html += '<span class="badge badge-blank">Blank</span>';
    }

    if (s.IsStreaming) html += '<span class="badge badge-streaming">RV</span>';

    if (s.IsHandRaised) html += '<span class="badge badge-hand">Hand</span>';

    if (s.DriverLoaded) html += '<span class="badge badge-online">Online</span>';
    else html += '<span class="badge badge-offline">Offline</span>';

    badges.innerHTML = html;

    // Frozen overlay on thumbnail
    const thumb = tile.querySelector('.tile-thumb');
    let frozenOverlay = thumb.querySelector('.frozen-overlay');
    if (s.IsFrozen && !frozenOverlay) {
        frozenOverlay = document.createElement('div');
        frozenOverlay.className = 'frozen-overlay';
        frozenOverlay.innerHTML = '&#xE7AD; Frozen';
        thumb.appendChild(frozenOverlay);
    } else if (!s.IsFrozen && frozenOverlay) {
        frozenOverlay.remove();
    }

    // Hide placeholder if we have video
    const placeholder = tile.querySelector('.placeholder');
    if (student.hasReceivedFrame && placeholder) placeholder.style.display = 'none';

    // Apply search filter visibility
    applyFilter(student);
}

function ensureStudentTile(ip) {
    if (!students.has(ip)) {
        const student = createStudentEntry(ip);
        students.set(ip, student);
        renderTile(student);
    }
}

// ── Search / Filter ──────────────────────────────────────────────────

function filterStudents(query) {
    currentFilter = query.toLowerCase().trim();
    students.forEach(s => applyFilter(s));
}

function applyFilter(student) {
    if (!student.tileEl) return;
    if (!currentFilter) {
        student.tileEl.style.display = '';
        return;
    }
    const s = student.status;
    const searchable = [
        student.ip,
        s?.Hostname || '',
        s?.Username || '',
        s?.ActiveWindow || ''
    ].join(' ').toLowerCase();

    student.tileEl.style.display = searchable.includes(currentFilter) ? '' : 'none';
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

    // ── Fullscreen RV sub-stream decoder (fallback if no main-stream) ──
    if (activeRvIp === ip && rvDecoder && !rvMainDecoder) {
        decodeFrame(rvDecoder, data, isKeyFrame);
    }
}

function handleMainFrame(ip, base64Data, isKeyFrame) {
    // Main-stream: 30fps 720p — only decode if this student's RV is open
    if (activeRvIp !== ip) return;

    const data = Uint8Array.from(atob(base64Data), c => c.charCodeAt(0));

    if (!rvMainDecoder) {
        const canvas = document.getElementById('rvCanvas');
        const ctx = canvas.getContext('2d');
        rvMainDecoder = createVideoDecoder(canvas, ctx, 1920, 1080);
        document.getElementById('rvStreamInfo').textContent = 'Main-stream 720p 30fps';
    }

    decodeFrame(rvMainDecoder, data, isKeyFrame);
}

// ── Demo Frame Rendering (synthetic desktop thumbnails) ──────────────

function handleDemoFrame(ip, frame) {
    const student = students.get(ip);
    if (!student) return;

    // Mark as having frames so placeholder hides
    student.hasReceivedFrame = true;
    const placeholder = student.tileEl?.querySelector('.placeholder');
    if (placeholder) placeholder.style.display = 'none';

    // Render synthetic desktop on the tile canvas
    if (student.canvas && student.ctx) {
        renderDemoDesktop(student.ctx, 480, 270, frame);
    }

    // If this student's RV modal is open, also render on the big canvas
    if (activeRvIp === ip) {
        const rvCanvas = document.getElementById('rvCanvas');
        const rvCtx = rvCanvas.getContext('2d');
        renderDemoDesktop(rvCtx, 1920, 1080, frame);
    }
}

/**
 * Renders a convincing synthetic Windows desktop on a canvas.
 * Draws: wallpaper → application window with title bar → content area
 *        with text lines → taskbar → clock → cursor.
 *
 * All coordinates scale to the given width/height so it works for both
 * the 480×270 grid thumbnail and the 1920×1080 RV modal.
 */
function renderDemoDesktop(ctx, w, h, f) {
    const sx = w / 480;   // scale factor relative to thumbnail size
    const sy = h / 270;

    // ── Wallpaper ────────────────────────────────────────────────
    ctx.fillStyle = f.wallpaper || '#1a1a2e';
    ctx.fillRect(0, 0, w, h);

    // Subtle gradient overlay
    const grad = ctx.createLinearGradient(0, 0, w, h);
    grad.addColorStop(0, 'rgba(255,255,255,0.03)');
    grad.addColorStop(1, 'rgba(0,0,0,0.15)');
    ctx.fillStyle = grad;
    ctx.fillRect(0, 0, w, h);

    // Desktop icons (small squares in top-left)
    const iconColors = ['#58A6FF44', '#3FB95044', '#D2992244', '#F8514944'];
    for (let i = 0; i < 4; i++) {
        ctx.fillStyle = iconColors[i];
        ctx.fillRect(12 * sx, (12 + i * 42) * sy, 28 * sx, 28 * sy);
        ctx.fillStyle = 'rgba(255,255,255,0.3)';
        ctx.font = `${8 * sx}px sans-serif`;
        ctx.fillText(['Files', 'Code', 'Web', 'Mail'][i], 12 * sx, (48 + i * 42) * sy);
    }

    if (f.locked) {
        // ── Lock Screen ──────────────────────────────────────────
        ctx.fillStyle = 'rgba(0,0,0,0.85)';
        ctx.fillRect(0, 0, w, h);
        ctx.fillStyle = '#F85149';
        ctx.font = `bold ${24 * sx}px Segoe UI, sans-serif`;
        ctx.textAlign = 'center';
        ctx.fillText('🔒 LOCKED', w / 2, h / 2 - 10 * sy);
        ctx.fillStyle = '#8B949E';
        ctx.font = `${11 * sx}px Segoe UI, sans-serif`;
        ctx.fillText('This workstation is locked by the teacher', w / 2, h / 2 + 18 * sy);
        ctx.textAlign = 'left';
        return;
    }

    if (f.frozen) {
        // ── Frozen overlay (still show desktop beneath) ──────────
        // Draw the app window first, then overlay
    }

    // ── Application Window ───────────────────────────────────────
    const winX = 55 * sx, winY = 8 * sy;
    const winW = w - 68 * sx, winH = h - 48 * sy;

    // Window shadow
    ctx.fillStyle = 'rgba(0,0,0,0.4)';
    ctx.fillRect(winX + 3 * sx, winY + 3 * sy, winW, winH);

    // Window body
    ctx.fillStyle = '#1e1e2e';
    ctx.fillRect(winX, winY, winW, winH);

    // Title bar
    const tbH = 24 * sy;
    ctx.fillStyle = '#2d2d3d';
    ctx.fillRect(winX, winY, winW, tbH);

    // Title bar accent stripe
    ctx.fillStyle = f.accent || '#58A6FF';
    ctx.fillRect(winX, winY, 3 * sx, tbH);

    // Window title text
    ctx.fillStyle = '#C9D1D9';
    ctx.font = `${10 * sx}px Segoe UI, sans-serif`;
    ctx.fillText(truncText(f.app || 'Application', 45), winX + 10 * sx, winY + 16 * sy);

    // Window control buttons (minimize, maximize, close)
    const btnY = winY + 5 * sy;
    const btnSize = 14 * sx;
    ctx.fillStyle = '#3FB950'; ctx.fillRect(winW + winX - 52 * sx, btnY, btnSize, btnSize);
    ctx.fillStyle = '#D29922'; ctx.fillRect(winW + winX - 34 * sx, btnY, btnSize, btnSize);
    ctx.fillStyle = '#F85149'; ctx.fillRect(winW + winX - 16 * sx, btnY, btnSize, btnSize);

    // ── Window Content Area ──────────────────────────────────────
    const contentX = winX + 6 * sx;
    const contentY = winY + tbH + 6 * sy;
    const contentW = winW - 12 * sx;
    const contentH = winH - tbH - 12 * sy;

    // Simulated content based on app type
    const app = (f.app || '').toLowerCase();
    ctx.save();
    ctx.beginPath();
    ctx.rect(contentX, contentY, contentW, contentH);
    ctx.clip();

    if (app.includes('word') || app.includes('notepad') || app.includes('essay')) {
        renderDocContent(ctx, contentX, contentY, contentW, contentH, sx, sy, f);
    } else if (app.includes('chrome') || app.includes('firefox') || app.includes('web')) {
        renderBrowserContent(ctx, contentX, contentY, contentW, contentH, sx, sy, f);
    } else if (app.includes('code') || app.includes('visual studio')) {
        renderCodeContent(ctx, contentX, contentY, contentW, contentH, sx, sy, f);
    } else if (app.includes('excel') || app.includes('calc')) {
        renderSpreadsheetContent(ctx, contentX, contentY, contentW, contentH, sx, sy, f);
    } else if (app.includes('powerpoint') || app.includes('presentation')) {
        renderSlideContent(ctx, contentX, contentY, contentW, contentH, sx, sy, f);
    } else if (app.includes('paint') || app.includes('drawing')) {
        renderPaintContent(ctx, contentX, contentY, contentW, contentH, sx, sy, f);
    } else if (app.includes('youtube')) {
        renderVideoContent(ctx, contentX, contentY, contentW, contentH, sx, sy, f);
    } else {
        renderFileExplorerContent(ctx, contentX, contentY, contentW, contentH, sx, sy, f);
    }

    ctx.restore();

    // ── Frozen Overlay (after window content) ────────────────────
    if (f.frozen) {
        ctx.fillStyle = 'rgba(0, 120, 215, 0.35)';
        ctx.fillRect(0, 0, w, h);
        ctx.fillStyle = '#58A6FF';
        ctx.font = `bold ${18 * sx}px Segoe UI, sans-serif`;
        ctx.textAlign = 'center';
        ctx.fillText('❄ FROZEN', w / 2, h / 2);
        ctx.textAlign = 'left';
    }

    // ── Taskbar ──────────────────────────────────────────────────
    const tbY = h - 28 * sy;
    ctx.fillStyle = '#1a1a2e';
    ctx.fillRect(0, tbY, w, 28 * sy);
    ctx.fillStyle = '#30363D';
    ctx.fillRect(0, tbY, w, 1);

    // Start button
    ctx.fillStyle = '#30363D';
    ctx.fillRect(4 * sx, tbY + 4 * sy, 20 * sx, 20 * sy);
    ctx.fillStyle = f.accent || '#58A6FF';
    ctx.fillRect(8 * sx, tbY + 8 * sy, 12 * sx, 12 * sy);

    // Active app indicator
    ctx.fillStyle = '#2d2d3d';
    ctx.fillRect(30 * sx, tbY + 4 * sy, 120 * sx, 20 * sy);
    ctx.fillStyle = f.accent || '#58A6FF';
    ctx.fillRect(30 * sx, tbY + 22 * sy, 120 * sx, 2 * sy);
    ctx.fillStyle = '#C9D1D9';
    ctx.font = `${8 * sx}px Segoe UI, sans-serif`;
    ctx.fillText(truncText(f.app || '', 20), 34 * sx, tbY + 17 * sy);

    // System tray — clock
    ctx.fillStyle = '#8B949E';
    ctx.font = `${9 * sx}px Segoe UI, sans-serif`;
    ctx.textAlign = 'right';
    ctx.fillText(f.time || '00:00', w - 8 * sx, tbY + 17 * sy);
    ctx.textAlign = 'left';

    // ── Cursor ───────────────────────────────────────────────────
    if (!f.frozen && !f.locked) {
        const cx = f.cursorX * sx, cy = f.cursorY * sy;
        ctx.fillStyle = '#FFFFFF';
        ctx.beginPath();
        ctx.moveTo(cx, cy);
        ctx.lineTo(cx, cy + 14 * sy);
        ctx.lineTo(cx + 5 * sx, cy + 10 * sy);
        ctx.lineTo(cx + 9 * sx, cy + 10 * sy);
        ctx.closePath();
        ctx.fill();
        ctx.strokeStyle = '#000';
        ctx.lineWidth = 1;
        ctx.stroke();
    }
}

// ── Content Renderers ────────────────────────────────────────────────

function renderDocContent(ctx, x, y, w, h, sx, sy, f) {
    // White page with text lines
    ctx.fillStyle = '#FFFFFF';
    ctx.fillRect(x + 10 * sx, y, w - 20 * sx, h);
    ctx.fillStyle = '#333';
    ctx.font = `bold ${9 * sx}px serif`;
    ctx.fillText('Essay — Modern Technology in Education', x + 20 * sx, y + 18 * sy);
    ctx.font = `${7 * sx}px serif`;
    const lines = [
        'The integration of technology in modern classrooms has',
        'transformed the educational landscape significantly.',
        'Students now have access to digital tools that enhance',
        'collaboration and interactive learning experiences.',
        '', 'Key benefits include real-time feedback, personalized',
        'learning paths, and improved engagement metrics.',
        'Teachers can monitor progress and adapt curriculum.'
    ];
    lines.forEach((line, i) => {
        ctx.fillStyle = line ? '#444' : 'transparent';
        ctx.fillText(line, x + 20 * sx, y + 34 * sy + i * 12 * sy);
    });
}

function renderBrowserContent(ctx, x, y, w, h, sx, sy, f) {
    // Address bar
    ctx.fillStyle = '#2d2d3d';
    ctx.fillRect(x, y, w, 18 * sy);
    ctx.fillStyle = '#1e1e2e';
    ctx.fillRect(x + 30 * sx, y + 3 * sy, w - 60 * sx, 12 * sy);
    ctx.fillStyle = '#8B949E';
    ctx.font = `${7 * sx}px sans-serif`;
    const url = f.app.includes('YouTube') ? 'youtube.com/watch?v=...'
        : f.app.includes('Khan') ? 'khanacademy.org/math'
        : f.app.includes('Wiki') ? 'en.wikipedia.org/wiki/...'
        : 'classroom.google.com';
    ctx.fillText('🔒 ' + url, x + 36 * sx, y + 12 * sy);

    // Page content
    ctx.fillStyle = '#f0f0f0';
    ctx.fillRect(x, y + 18 * sy, w, h - 18 * sy);
    ctx.fillStyle = '#1a73e8';
    ctx.font = `bold ${10 * sx}px sans-serif`;
    ctx.fillText(f.app.includes('YouTube') ? '▶ Video Player' : 'Web Content', x + 12 * sx, y + 40 * sy);

    // Content blocks
    for (let i = 0; i < 4; i++) {
        ctx.fillStyle = '#e0e0e0';
        ctx.fillRect(x + 12 * sx, y + (52 + i * 22) * sy, w - 24 * sx, 16 * sy);
        ctx.fillStyle = '#666';
        ctx.font = `${7 * sx}px sans-serif`;
        ctx.fillText('Content block ' + (i + 1), x + 16 * sx, y + (63 + i * 22) * sy);
    }
}

function renderCodeContent(ctx, x, y, w, h, sx, sy, f) {
    // Dark editor with line numbers and syntax colouring
    ctx.fillStyle = '#1e1e1e';
    ctx.fillRect(x, y, w, h);

    // Line number gutter
    ctx.fillStyle = '#2d2d2d';
    ctx.fillRect(x, y, 24 * sx, h);

    const codeLines = [
        { num: '1', code: 'def calculate_average(scores):', color: '#569cd6' },
        { num: '2', code: '    """Calculate the average score."""', color: '#6A9955' },
        { num: '3', code: '    if not scores:', color: '#c586c0' },
        { num: '4', code: '        return 0.0', color: '#b5cea8' },
        { num: '5', code: '', color: '' },
        { num: '6', code: '    total = sum(scores)', color: '#dcdcaa' },
        { num: '7', code: '    count = len(scores)', color: '#dcdcaa' },
        { num: '8', code: '    return total / count', color: '#c586c0' },
        { num: '9', code: '', color: '' },
        { num: '10', code: '# Main program', color: '#6A9955' },
        { num: '11', code: 'grades = [85, 92, 78, 95, 88]', color: '#ce9178' },
        { num: '12', code: 'avg = calculate_average(grades)', color: '#9cdcfe' },
    ];

    codeLines.forEach((line, i) => {
        const ly = y + 10 * sy + i * 10 * sy;
        ctx.fillStyle = '#858585';
        ctx.font = `${7 * sx}px Consolas, monospace`;
        ctx.fillText(line.num, x + 4 * sx, ly);
        ctx.fillStyle = line.color || '#d4d4d4';
        ctx.fillText(line.code, x + 28 * sx, ly);
    });
}

function renderSpreadsheetContent(ctx, x, y, w, h, sx, sy, f) {
    ctx.fillStyle = '#FFFFFF';
    ctx.fillRect(x, y, w, h);

    // Column headers
    const cols = ['A', 'B', 'C', 'D', 'E'];
    const colW = (w - 24 * sx) / cols.length;
    ctx.fillStyle = '#f0f0f0';
    ctx.fillRect(x + 24 * sx, y, w - 24 * sx, 14 * sy);
    cols.forEach((c, i) => {
        ctx.fillStyle = '#666';
        ctx.font = `bold ${7 * sx}px sans-serif`;
        ctx.fillText(c, x + 28 * sx + i * colW + colW / 3, y + 10 * sy);
        ctx.strokeStyle = '#ddd';
        ctx.strokeRect(x + 24 * sx + i * colW, y, colW, 14 * sy);
    });

    // Rows
    for (let r = 0; r < 8; r++) {
        const ry = y + 14 * sy + r * 14 * sy;
        ctx.fillStyle = '#858585';
        ctx.font = `${7 * sx}px sans-serif`;
        ctx.fillText(String(r + 1), x + 6 * sx, ry + 10 * sy);
        cols.forEach((_, ci) => {
            ctx.strokeStyle = '#e0e0e0';
            ctx.strokeRect(x + 24 * sx + ci * colW, ry, colW, 14 * sy);
            if (r < 6 && ci < 3) {
                ctx.fillStyle = '#333';
                ctx.font = `${7 * sx}px sans-serif`;
                ctx.fillText(String(Math.round(Math.random() * 100)), x + 28 * sx + ci * colW, ry + 10 * sy);
            }
        });
    }
}

function renderSlideContent(ctx, x, y, w, h, sx, sy, f) {
    // Slide area
    const margin = 8 * sx;
    ctx.fillStyle = '#1a1a2e';
    ctx.fillRect(x, y, w, h);

    // Slide
    const slideW = w - margin * 2;
    const slideH = h - margin * 2 - 16 * sy;
    ctx.fillStyle = '#FFFFFF';
    ctx.fillRect(x + margin, y + margin, slideW, slideH);

    // Slide title
    ctx.fillStyle = f.accent || '#1a73e8';
    ctx.font = `bold ${12 * sx}px sans-serif`;
    ctx.fillText('Modern Technology', x + margin + 16 * sx, y + margin + 28 * sy);

    // Bullet points
    ctx.fillStyle = '#333';
    ctx.font = `${8 * sx}px sans-serif`;
    ['• Interactive Learning Tools', '• Real-time Collaboration', '• Digital Assessment', '• Cloud-based Resources'].forEach((t, i) => {
        ctx.fillText(t, x + margin + 20 * sx, y + margin + 50 * sy + i * 16 * sy);
    });

    // Slide number
    ctx.fillStyle = '#8B949E';
    ctx.font = `${7 * sx}px sans-serif`;
    ctx.fillText('Slide 3 of 12', x + margin + slideW - 60 * sx, y + margin + slideH - 6 * sy);
}

function renderPaintContent(ctx, x, y, w, h, sx, sy, f) {
    ctx.fillStyle = '#FFFFFF';
    ctx.fillRect(x, y, w, h);

    // Toolbar
    ctx.fillStyle = '#f0f0f0';
    ctx.fillRect(x, y, w, 16 * sy);
    ['#FF0000', '#00AA00', '#0000FF', '#FFD700', '#FF6600', '#8B00FF'].forEach((c, i) => {
        ctx.fillStyle = c;
        ctx.fillRect(x + 6 * sx + i * 14 * sx, y + 3 * sy, 10 * sx, 10 * sy);
    });

    // Random shapes
    const seed = f.index || 0;
    for (let i = 0; i < 6; i++) {
        ctx.fillStyle = ['#FF6B6B', '#4ECDC4', '#45B7D1', '#96CEB4', '#FFEAA7', '#DDA0DD'][i];
        ctx.beginPath();
        ctx.arc(
            x + (60 + ((seed * 37 + i * 67) % 300)) * sx * (w / 480) / sx,
            y + (40 + ((seed * 41 + i * 53) % 100)) * sy * (h / 270) / sy,
            (12 + i * 4) * sx, 0, Math.PI * 2
        );
        ctx.fill();
    }
}

function renderVideoContent(ctx, x, y, w, h, sx, sy, f) {
    // Dark player background
    ctx.fillStyle = '#000';
    ctx.fillRect(x, y, w, h);

    // Video area with play button
    const vw = w * 0.9, vh = h * 0.7;
    const vx = x + (w - vw) / 2, vy = y + 8 * sy;
    ctx.fillStyle = '#1a1a1a';
    ctx.fillRect(vx, vy, vw, vh);

    // Play triangle
    ctx.fillStyle = 'rgba(255,255,255,0.8)';
    const cx = vx + vw / 2, cy = vy + vh / 2;
    ctx.beginPath();
    ctx.moveTo(cx - 14 * sx, cy - 16 * sy);
    ctx.lineTo(cx - 14 * sx, cy + 16 * sy);
    ctx.lineTo(cx + 16 * sx, cy);
    ctx.closePath();
    ctx.fill();

    // Progress bar
    const pby = vy + vh + 4 * sy;
    ctx.fillStyle = '#333';
    ctx.fillRect(vx, pby, vw, 4 * sy);
    ctx.fillStyle = '#FF0000';
    ctx.fillRect(vx, pby, vw * 0.35, 4 * sy);

    // Title
    ctx.fillStyle = '#FFF';
    ctx.font = `${8 * sx}px sans-serif`;
    ctx.fillText('Educational Video — Introduction to Algorithms', vx, pby + 16 * sy);
}

function renderFileExplorerContent(ctx, x, y, w, h, sx, sy, f) {
    ctx.fillStyle = '#FFFFFF';
    ctx.fillRect(x, y, w, h);

    // Address bar
    ctx.fillStyle = '#f5f5f5';
    ctx.fillRect(x, y, w, 16 * sy);
    ctx.fillStyle = '#666';
    ctx.font = `${7 * sx}px sans-serif`;
    ctx.fillText('📁 C:\\Users\\' + (f.username || 'Student') + '\\Documents', x + 6 * sx, y + 11 * sy);

    // File list
    const files = ['📁 Homework', '📁 Projects', '📄 Notes.docx', '📄 Report.pdf', '📊 Data.xlsx', '🖼️ Photo.png'];
    files.forEach((file, i) => {
        const fy = y + 20 * sy + i * 16 * sy;
        ctx.fillStyle = i % 2 === 0 ? '#FFFFFF' : '#f9f9f9';
        ctx.fillRect(x, fy, w, 16 * sy);
        ctx.fillStyle = '#333';
        ctx.font = `${8 * sx}px sans-serif`;
        ctx.fillText(file, x + 12 * sx, fy + 11 * sy);
    });
}

function truncText(text, maxLen) {
    return text.length > maxLen ? text.substring(0, maxLen - 1) + '…' : text;
}

// ── WebCodecs H.264 Decoder ──────────────────────────────────────────

function createVideoDecoder(canvas, ctx, width, height) {
    if (typeof VideoDecoder === 'undefined') {
        console.warn('WebCodecs API not available — falling back to static thumbnails');
        return null;
    }

    const decoder = new VideoDecoder({
        output: (frame) => {
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
    const info = document.getElementById('rvStudentInfo');
    const streamInfo = document.getElementById('rvStreamInfo');

    const name = student?.status?.Hostname || ip;
    const user = student?.status?.Username || '';
    title.textContent = `Remote View — ${name}`;
    info.textContent = user ? `${user} @ ${ip}` : ip;
    streamInfo.textContent = isDemoMode ? 'Demo stream (synthetic)' : 'Sub-stream 480p';
    modal.style.display = 'flex';

    // In demo mode, the demo_frame handler will paint the RV canvas directly
    if (!isDemoMode) {
        // Create sub-stream fallback decoder for real mode
        const ctx = canvas.getContext('2d');
        rvDecoder = createVideoDecoder(canvas, ctx, 1920, 1080);
        rvMainDecoder = null;
    }

    // Tell C# to start focus (main-stream) + RV
    sendToHost({ action: 'focus_start', target: ip });
    sendToHost({ action: 'rv_start', target: ip });
}

function closeRv() {
    if (activeRvIp) {
        sendToHost({ action: 'focus_stop', target: activeRvIp });
        sendToHost({ action: 'rv_stop', target: activeRvIp });
    }
    document.getElementById('rvModal').style.display = 'none';

    if (rvDecoder && rvDecoder.state !== 'closed') rvDecoder.close();
    if (rvMainDecoder && rvMainDecoder.state !== 'closed') rvMainDecoder.close();
    rvDecoder = null;
    rvMainDecoder = null;
    activeRvIp = null;
}

function toggleRvCensored() {
    const canvas = document.getElementById('rvCanvas');
    canvas.classList.toggle('censored');
}

function lockFromRv() {
    if (activeRvIp) lockStudent(activeRvIp);
}

function freezeFromRv() {
    if (activeRvIp) freezeStudent(activeRvIp);
}

// ── Student Commands (JS → C#) ──────────────────────────────────────

function lockStudent(ip) {
    sendToHost({ action: 'lock', target: ip });
}

function unlockStudent(ip) {
    sendToHost({ action: 'unlock', target: ip });
}

function freezeStudent(ip) {
    sendToHost({ action: 'freeze', target: ip });
}

function unfreezeStudent(ip) {
    sendToHost({ action: 'unfreeze', target: ip });
}

function sendToHost(msg) {
    if (window.chrome?.webview) {
        window.chrome.webview.postMessage(JSON.stringify(msg));
    }
}

// ── Announcement Banner ──────────────────────────────────────────────

function showAnnouncement(text) {
    const bar = document.getElementById('announcementBar');
    const textEl = document.getElementById('announcementText');
    if (text) {
        textEl.textContent = text;
        bar.style.display = 'flex';
    } else {
        bar.style.display = 'none';
    }
}

function dismissAnnouncement() {
    document.getElementById('announcementBar').style.display = 'none';
}

// ── Broadcast Message Dialog ─────────────────────────────────────────

function openMessageDialog() {
    document.getElementById('messageModal').style.display = 'flex';
    const ta = document.getElementById('messageText');
    ta.value = '';
    ta.focus();
}

function closeMessageDialog() {
    document.getElementById('messageModal').style.display = 'none';
}

function sendBroadcastMessage() {
    const text = document.getElementById('messageText').value.trim();
    if (!text) return;
    sendToHost({ action: 'message', target: '', payload: text });
    closeMessageDialog();
    showAnnouncement(`Message sent: "${text.substring(0, 60)}${text.length > 60 ? '...' : ''}"`);
    setTimeout(() => showAnnouncement(null), 5000);
}

// ── About Modal ──────────────────────────────────────────────────────

function closeAbout(event) {
    if (event.target.id === 'aboutModal') {
        document.getElementById('aboutModal').style.display = 'none';
    }
}

// ── Stats Bar ────────────────────────────────────────────────────────

function updateStats() {
    const now = Date.now();
    let online = 0, locked = 0, frozen = 0, streaming = 0, handRaised = 0, offline = 0;

    students.forEach(s => {
        const isOnline = s.status && (now - s.lastSeen < 10000);
        if (isOnline) {
            online++;
            if (s.status.IsLocked) locked++;
            if (s.status.IsFrozen) frozen++;
            if (s.status.IsStreaming) streaming++;
            if (s.status.IsHandRaised) handRaised++;
        } else {
            offline++;
            if (s.tileEl) s.tileEl.classList.add('offline');
        }
    });

    document.getElementById('statOnline').textContent = online;
    document.getElementById('statLocked').textContent = locked;
    document.getElementById('statFrozen').textContent = frozen;
    document.getElementById('statStreaming').textContent = streaming;
    document.getElementById('statHandRaised').textContent = handRaised;
    document.getElementById('statOffline').textContent = offline;
}

// Refresh stats every 5 seconds
setInterval(updateStats, 5000);

// ── Changelog ────────────────────────────────────────────────────────

function showChangelog() {
    document.getElementById('aboutModal').style.display = 'none';
    document.getElementById('changelogModal').style.display = 'flex';
}

// ── Keyboard Shortcuts ───────────────────────────────────────────────

document.addEventListener('keydown', (e) => {
    if (e.key === 'Escape') {
        if (document.getElementById('changelogModal')?.style.display !== 'none')
            document.getElementById('changelogModal').style.display = 'none';
        else if (activeRvIp) closeRv();
        else if (document.getElementById('messageModal').style.display !== 'none')
            closeMessageDialog();
        else if (document.getElementById('aboutModal').style.display !== 'none')
            document.getElementById('aboutModal').style.display = 'none';
    }

    // Ctrl+F → focus search
    if (e.ctrlKey && e.key === 'f') {
        e.preventDefault();
        document.getElementById('searchInput').focus();
    }
});

// ═══════════════════════════════════════════════════════════════════════════
// Software Update Banner
// ═══════════════════════════════════════════════════════════════════════════

function showUpdateBanner(version, releaseNotes, htmlUrl) {
    // Remove existing banner if any
    let existing = document.getElementById('updateBanner');
    if (existing) existing.remove();

    const banner = document.createElement('div');
    banner.id = 'updateBanner';
    banner.style.cssText = `
        position: fixed; top: 0; left: 0; right: 0; z-index: 10000;
        background: linear-gradient(135deg, #1a5276, #2e86c1);
        color: #fff; padding: 10px 20px; display: flex;
        align-items: center; justify-content: space-between;
        font-family: 'Segoe UI', sans-serif; font-size: 14px;
        box-shadow: 0 2px 8px rgba(0,0,0,0.3); animation: slideDown 0.3s ease;
    `;

    const text = document.createElement('span');
    text.innerHTML = `<strong>Update available:</strong> v${version}`;
    if (releaseNotes) {
        const preview = releaseNotes.split('\n')[0].substring(0, 80);
        text.innerHTML += ` — ${preview}`;
    }

    const actions = document.createElement('div');
    actions.style.cssText = 'display: flex; gap: 8px; align-items: center;';

    if (htmlUrl) {
        const viewBtn = document.createElement('a');
        viewBtn.href = htmlUrl;
        viewBtn.target = '_blank';
        viewBtn.textContent = 'View Release';
        viewBtn.style.cssText = `
            background: rgba(255,255,255,0.2); color: #fff; border: 1px solid rgba(255,255,255,0.4);
            padding: 4px 12px; border-radius: 4px; text-decoration: none; font-size: 13px;
            cursor: pointer;
        `;
        actions.appendChild(viewBtn);
    }

    const dismissBtn = document.createElement('button');
    dismissBtn.textContent = '✕';
    dismissBtn.title = 'Dismiss';
    dismissBtn.style.cssText = `
        background: none; border: none; color: #fff; font-size: 18px;
        cursor: pointer; padding: 0 4px; opacity: 0.8;
    `;
    dismissBtn.onclick = () => banner.remove();
    actions.appendChild(dismissBtn);

    banner.appendChild(text);
    banner.appendChild(actions);
    document.body.prepend(banner);

    // Auto-dismiss after 30 seconds
    setTimeout(() => { if (banner.parentNode) banner.remove(); }, 30000);
}
});

console.log('[TAD.RV] Teacher Dashboard initialized — The Greater Brother of te.comp NET.FX');
