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
let teacherRooms = [];                // Rooms loaded from Console's TAD_Classrooms.json
let selectedRoomId = null;            // Currently selected room ID
let showOffline = true;               // Show offline tiles in the grid

// ── Message Bridge (C# → JS) ────────────────────────────────────────

if (window.chrome?.webview) {
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
        case 'rooms_loaded':
            onRoomsLoaded(msg.data);
            break;
    }
  });
}

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
            <button class="tile-btn freeze-btn-tile" onclick="event.stopPropagation(); freezeSingleStudent('${student.ip}')" title="Freeze Timer">&#xE768;</button>
        </div>
        <div class="tile-info">
            <div class="tile-info-left">
                <span class="tile-hostname">${student.ip}</span>
                <span class="tile-user">—</span>
            </div>
            <span class="tile-role role-unknown">—</span>
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

    // Update AD role badge
    const roleEl = student.tileEl.querySelector('.tile-role');
    if (roleEl) {
        const role = (s.Role || 'Unknown').toLowerCase();
        roleEl.textContent = s.Role || '—';
        roleEl.className = `tile-role role-${role}`;
    }

    // Update badges
    const badges = student.tileEl.querySelector('.tile-badges');
    let html = '';
    if (s.IsLocked) {
        html += '<span class="badge badge-locked">Locked</span>';
        student.tileEl.classList.add('locked');
    } else {
        student.tileEl.classList.remove('locked');
    }
    if (s.IsFrozen) {
        html += '<span class="badge badge-frozen">Frozen</span>';
        student.tileEl.classList.add('frozen');
    } else {
        student.tileEl.classList.remove('frozen');
        // Remove freeze timer display if present
        const timer = student.tileEl.querySelector('.tile-freeze-timer');
        if (timer) timer.remove();
    }
    if (s.IsStreaming) html += '<span class="badge badge-streaming">RV</span>';
    if (s.DriverLoaded) html += '<span class="badge badge-online">Online</span>';
    else html += '<span class="badge badge-offline">Offline</span>';
    badges.innerHTML = html;

    // Freeze countdown overlay
    if (s.IsFrozen && s.FreezeSecondsRemaining > 0) {
        let timer = student.tileEl.querySelector('.tile-freeze-timer');
        if (!timer) {
            timer = document.createElement('div');
            timer.className = 'tile-freeze-timer';
            student.tileEl.querySelector('.tile-thumb').appendChild(timer);
        }
        const mins = Math.floor(s.FreezeSecondsRemaining / 60);
        const secs = s.FreezeSecondsRemaining % 60;
        timer.textContent = `${mins}:${secs.toString().padStart(2, '0')}`;
    }

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
        window.chrome.webview.postMessage(msg);
    }
}

// ── Stats Bar ────────────────────────────────────────────────────────

function updateStats() {
    const now = Date.now();
    let online = 0, locked = 0, streaming = 0, frozen = 0, offline = 0;

    students.forEach(s => {
        const isOnline = s.status && (now - s.lastSeen < 10000);
        if (isOnline) {
            online++;
            if (s.status.IsLocked) locked++;
            if (s.status.IsStreaming) streaming++;
            if (s.status.IsFrozen) frozen++;
        } else {
            offline++;
            if (s.tileEl) s.tileEl.classList.add('offline');
        }
    });

    document.getElementById('statOnline').textContent = online;
    document.getElementById('statLocked').textContent = locked;
    document.getElementById('statStreaming').textContent = streaming;
    document.getElementById('statFrozen').textContent = frozen;
    document.getElementById('statOffline').textContent = offline;

    // Apply offline visibility filter
    applyOfflineVisibility();
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

// ══════════════════════════════════════════════════════════════════════
// ══ ROOM SELECTOR ═══════════════════════════════════════════════════
// ══════════════════════════════════════════════════════════════════════

function loadRoomsFromHost() {
    sendToHost({ action: 'teacher_load_rooms' });
}

function onRoomsLoaded(data) {
    try {
        teacherRooms = typeof data === 'string' ? JSON.parse(data) : (data || []);
    } catch {
        teacherRooms = [];
    }
    renderRoomDropdown();
    updateRoomSwitcherLabel();

    // Auto-select the previously selected room if still present
    if (selectedRoomId) {
        const still = teacherRooms.find(r => r.id === selectedRoomId);
        if (!still) {
            selectedRoomId = null;
            document.getElementById('currentRoomLabel').textContent = 'No Room Selected';
        }
    }
    // If only one room, auto-select it
    if (!selectedRoomId && teacherRooms.length === 1) {
        selectTeacherRoom(teacherRooms[0].id);
    }
    updateRoomSwitcherLabel();
}

function renderRoomDropdown() {
    const list = document.getElementById('roomDropdownList');
    if (!list) return;

    if (teacherRooms.length === 0) {
        list.innerHTML = '<div class="room-dropdown-empty">No classrooms configured.<br>Create rooms in the Console app.</div>';
        return;
    }

    list.innerHTML = teacherRooms.map(r => {
        const deskCount = (r.objects || []).filter(o => o.type === 'desk').length;
        const isActive = r.id === selectedRoomId;
        return `
            <div class="room-dropdown-item${isActive ? ' active' : ''}" onclick="selectTeacherRoom('${r.id}')">
                <span class="room-dropdown-item-icon">${isActive ? '\uE73E' : '\uE80F'}</span>
                <div class="room-dropdown-item-info">
                    <div class="room-dropdown-item-name">${escHtml(r.name)}</div>
                    <div class="room-dropdown-item-meta">${deskCount} desk${deskCount !== 1 ? 's' : ''}${r.roomCode ? ' · ' + r.roomCode : ''}</div>
                </div>
            </div>`;
    }).join('');
}

function selectTeacherRoom(roomId) {
    selectedRoomId = roomId;
    const room = teacherRooms.find(r => r.id === roomId);
    if (room) {
        document.getElementById('currentRoomLabel').textContent = room.name;
        updateRoomSwitcherLabel();
    }
    renderRoomDropdown();
    closeRoomDropdown();
}

function toggleRoomDropdown() {
    const sel = document.getElementById('roomSelector');
    const isOpen = sel.classList.contains('open');
    if (isOpen) {
        closeRoomDropdown();
    } else {
        loadRoomsFromHost(); // Refresh rooms every time dropdown opens
        sel.classList.add('open');
    }
}

function closeRoomDropdown() {
    document.getElementById('roomSelector')?.classList.remove('open');
}

function escHtml(s) {
    const d = document.createElement('div');
    d.textContent = s;
    return d.innerHTML;
}

// Close dropdown when clicking outside
document.addEventListener('click', (e) => {
    if (!e.target.closest('.room-selector')) {
        closeRoomDropdown();
    }
});

// Load rooms on startup
setTimeout(loadRoomsFromHost, 500);

// ══════════════════════════════════════════════════════════════════════
// ══ CUSTOM ACTION — FREEZE TIMER ════════════════════════════════════
// ══════════════════════════════════════════════════════════════════════

let freezeDuration = 300; // seconds
let freezeSelectedStudents = new Set(); // IPs selected for freeze

function openFreezeModal() {
    document.getElementById('freezeModal').style.display = 'flex';
    populateFreezeStudentList();

    // Listen for target radio changes
    document.querySelectorAll('input[name="freezeTarget"]').forEach(r => {
        r.addEventListener('change', () => {
            document.getElementById('freezeSelectedList').style.display =
                r.value === 'selected' && r.checked ? '' : 'none';
        });
    });
}

function closeFreezeModal() {
    document.getElementById('freezeModal').style.display = 'none';
}

function setFreezeDuration(secs) {
    freezeDuration = secs;
    document.getElementById('freezeSeconds').value = secs;
    document.querySelectorAll('.freeze-chip').forEach(c =>
        c.classList.toggle('active', parseInt(c.dataset.secs) === secs));
}

// Sync custom input back to state
document.addEventListener('DOMContentLoaded', () => {
    const inp = document.getElementById('freezeSeconds');
    if (inp) inp.addEventListener('change', () => {
        freezeDuration = parseInt(inp.value) || 300;
        document.querySelectorAll('.freeze-chip').forEach(c => c.classList.remove('active'));
    });
});

function populateFreezeStudentList() {
    const list = document.getElementById('freezeSelectedList');
    freezeSelectedStudents.clear();

    let html = '';
    students.forEach((s, ip) => {
        const name = s.status?.Hostname || ip;
        const role = s.status?.Role || 'Unknown';
        html += `
            <label class="freeze-selected-item">
                <input type="checkbox" value="${ip}" onchange="toggleFreezeStudent('${ip}', this.checked)" checked />
                ${name} <span style="color:var(--text-muted);font-size:10px">(${role})</span>
            </label>`;
        freezeSelectedStudents.add(ip);
    });

    list.innerHTML = html || '<div style="padding:8px;color:var(--text-muted);font-size:12px">No students online</div>';
}

function toggleFreezeStudent(ip, checked) {
    if (checked) freezeSelectedStudents.add(ip);
    else freezeSelectedStudents.delete(ip);
}

function executeFreeze() {
    const secs = parseInt(document.getElementById('freezeSeconds').value) || 300;
    const msg = document.getElementById('freezeMessage').value || 'Your screen has been frozen by the teacher.';
    const target = document.querySelector('input[name="freezeTarget"]:checked')?.value || 'all';

    if (target === 'all') {
        sendToHost({ action: 'freeze_all', data: String(secs), extra: msg });
    } else {
        // Freeze only selected students
        freezeSelectedStudents.forEach(ip => {
            sendToHost({ action: 'freeze_student', target: ip, data: String(secs), extra: msg });
        });
    }

    closeFreezeModal();
}

function cancelActiveFreeze() {
    const target = document.querySelector('input[name="freezeTarget"]:checked')?.value || 'all';

    if (target === 'all') {
        sendToHost({ action: 'unfreeze_all' });
    } else {
        freezeSelectedStudents.forEach(ip => {
            sendToHost({ action: 'unfreeze_student', target: ip });
        });
    }

    closeFreezeModal();
}

/** Freeze a single student from their tile button */
function freezeSingleStudent(ip) {
    const secs = 300; // Default 5 min from tile
    sendToHost({ action: 'freeze_student', target: ip, data: String(secs), extra: 'Your screen has been frozen by the teacher.' });
}

// ══════════════════════════════════════════════════════════════════════
// ══ ROOM SWITCHER (prev / next arrows in stats bar) ═════════════════
// ══════════════════════════════════════════════════════════════════════

function updateRoomSwitcherLabel() {
    const el = document.getElementById('roomSwLabel');
    if (!el) return;
    const room = teacherRooms.find(r => r.id === selectedRoomId);
    el.textContent = room ? room.name : 'No Room';
}

function switchRoomPrev() {
    if (teacherRooms.length === 0) return;
    const idx = teacherRooms.findIndex(r => r.id === selectedRoomId);
    const prev = idx <= 0 ? teacherRooms.length - 1 : idx - 1;
    selectTeacherRoom(teacherRooms[prev].id);
}

function switchRoomNext() {
    if (teacherRooms.length === 0) return;
    const idx = teacherRooms.findIndex(r => r.id === selectedRoomId);
    const next = idx < 0 || idx >= teacherRooms.length - 1 ? 0 : idx + 1;
    selectTeacherRoom(teacherRooms[next].id);
}

// ══════════════════════════════════════════════════════════════════════
// ══ SHOW/HIDE OFFLINE COMPUTERS ═════════════════════════════════════
// ══════════════════════════════════════════════════════════════════════

function onToggleShowOffline() {
    showOffline = document.getElementById('toggleShowOffline').checked;
    applyOfflineVisibility();
}

function applyOfflineVisibility() {
    const now = Date.now();
    students.forEach(s => {
        if (!s.tileEl) return;
        const isOnline = s.status && (now - s.lastSeen < 10000);
        if (!isOnline) {
            s.tileEl.style.display = showOffline ? '' : 'none';
        }
    });

    // Show empty state if no visible tiles
    const grid = document.getElementById('studentGrid');
    const empty = document.getElementById('emptyState');
    const visibleTiles = grid.querySelectorAll('.student-tile:not([style*="display: none"])');
    if (empty) {
        empty.style.display = visibleTiles.length === 0 ? '' : 'none';
    }
}
