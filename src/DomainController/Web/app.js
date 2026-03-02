// ═══════════════════════════════════════════════════════════════════════════
// TAD.RV Management Console — Application Logic
// C# ↔ WebView2 bridge + SPA routing + UI state management
// ═══════════════════════════════════════════════════════════════════════════

// ── State ──────────────────────────────────────────────────────────────────
let allEvents = [];
let currentFilter = 'all';
let policyVersion = 0;
let deploying = false;

// ── Internationalization ───────────────────────────────────────────────────
const translationPacks = {
  en: TAD_LANG_EN,
  de: TAD_LANG_DE,
  fr: TAD_LANG_FR,
  nl: TAD_LANG_NL,
  es: TAD_LANG_ES,
  it: TAD_LANG_IT,
  pl: TAD_LANG_PL
};

// Initialize i18n on page load
document.addEventListener('DOMContentLoaded', () => {
  const currentLang = TAD_I18N.init(translationPacks);
  updateLanguageIndicator(currentLang);
  setupLanguageSelector();
});

function updateLanguageIndicator(lang) {
  const langCode = document.getElementById('langCode');
  if (langCode) {
    langCode.textContent = lang.toUpperCase();
  }
  
  // Update active state in dropdown
  document.querySelectorAll('.lang-option').forEach(opt => {
    opt.classList.toggle('active', opt.dataset.lang === lang);
  });
}

function setupLanguageSelector() {
  const langBtn = document.getElementById('langBtn');
  const langDropdown = document.getElementById('langDropdown');
  
  // Toggle dropdown
  langBtn?.addEventListener('click', (e) => {
    e.stopPropagation();
    langDropdown?.classList.toggle('active');
  });
  
  // Close dropdown when clicking outside
  document.addEventListener('click', () => {
    langDropdown?.classList.remove('active');
  });
  
  // Handle language selection
  document.querySelectorAll('.lang-option').forEach(opt => {
    opt.addEventListener('click', (e) => {
      e.stopPropagation();
      const newLang = opt.dataset.lang;
      TAD_I18N.setLanguage(newLang, translationPacks);
      updateLanguageIndicator(newLang);
      langDropdown?.classList.remove('active');
      
      // Re-render dynamic content with new language
      const activePage = document.querySelector('.page.active')?.id;
      if (activePage === 'page-dashboard') refreshDashboard();
    });
  });
}

// ── Bridge Helpers ─────────────────────────────────────────────────────────
function send(action, data) {
  if (window.chrome && window.chrome.webview) {
    window.chrome.webview.postMessage({ action, ...data });
  } else {
    console.log('[Bridge→C#]', action, data);
  }
}

// C# → JS messages
if (window.chrome && window.chrome.webview) {
  window.chrome.webview.addEventListener('message', e => {
    try {
      const msg = typeof e.data === 'string' ? JSON.parse(e.data) : e.data;
      handleMessage(msg);
    } catch (err) {
      console.error('Bridge parse error:', err);
    }
  });
}

function handleMessage(msg) {
  switch (msg.type) {
    case 'systemInfo':      renderSystemInfo(msg.data); break;
    case 'servicesStatus':  renderServicesStatus(msg.data); break;
    case 'registryConfig':  renderRegistryConfig(msg.data); break;
    case 'healthChecks':    renderHealthChecks(msg.data); break;
    case 'events':          receiveEvents(msg.data); break;
    case 'deployProgress':  updateDeployProgress(msg.data); break;
    case 'deployStep':      addDeployStep(msg.data); break;
    case 'deployLog':       appendDeployLog(msg.data); break;
    case 'deployComplete':  onDeployComplete(msg.data); break;
    case 'browseResult':    onBrowseResult(msg.data); break;
    case 'policyLoaded':    onPolicyLoaded(msg.data); break;
    case 'toast':           showToast(msg.data?.level || msg.level || 'info', msg.data?.text || msg.text); break;
    case 'elevated':        onElevatedStatus(msg.data); break;
    case 'admin_rooms_loaded':
    case 'admin_layout_imported':
      handleAdminMessage(msg); break;
    default:
      console.log('[Bridge←C#] Unknown:', msg);
  }
}

// ── Navigation ────────────────────────────────────────────────────────────
const titles = {
  dashboard: 'Dashboard',
  deploy: 'Deploy',
  policy: 'Policy',
  alerts: 'Alerts',
  classrooms: 'Classrooms'
};

document.querySelectorAll('.nav-btn').forEach(btn => {
  btn.addEventListener('click', () => navigate(btn.dataset.page));
});

function navigate(page) {
  // Update nav
  document.querySelectorAll('.nav-btn').forEach(b => b.classList.toggle('active', b.dataset.page === page));
  // Update pages
  document.querySelectorAll('.page').forEach(p => {
    p.classList.toggle('active', p.id === 'page-' + page);
  });
  // Title (i18n)
  const titleKey = 'nav.' + page;
  document.getElementById('pageTitle').textContent = t(titleKey);

  // Lazy load data
  if (page === 'dashboard') refreshDashboard();
  if (page === 'alerts') refreshAlerts();
  if (page === 'policy') send('loadPolicy');
  if (page === 'classrooms') loadRoomsFromHost();
}

// ── Clock ─────────────────────────────────────────────────────────────────
function updateClock() {
  const now = new Date();
  const h = String(now.getHours()).padStart(2, '0');
  const m = String(now.getMinutes()).padStart(2, '0');
  const s = String(now.getSeconds()).padStart(2, '0');
  document.getElementById('clock').textContent = `${h}:${m}:${s}`;
}
setInterval(updateClock, 1000);
updateClock();

// ── Dashboard ─────────────────────────────────────────────────────────────
function refreshDashboard() {
  send('queryServices');
  send('getSystemInfo');
  send('queryRegistry');
  send('runHealthChecks');
}

function renderServicesStatus(data) {
  renderServiceCard('driver', data.driver);
  renderServiceCard('bridge', data.bridge);
}

function renderServiceCard(id, svc) {
  const card = document.getElementById(id + 'Card');
  const statusEl = document.getElementById(id + 'Status');
  const detailEl = document.getElementById(id + 'Detail');

  if (!svc) return;

  const isRunning = svc.status === 'RUNNING';
  const isStopped = svc.status === 'STOPPED';
  const notInstalled = !svc.exists;

  let dotClass = 'unknown';
  let text = svc.status || 'Unknown';

  if (notInstalled) {
    dotClass = 'stopped';
    text = 'Not Installed';
  } else if (isRunning) {
    dotClass = 'running';
    text = 'Running';
  } else if (isStopped) {
    dotClass = 'stopped';
    text = 'Stopped';
  } else {
    dotClass = 'warning';
  }

  statusEl.innerHTML = `
    <span class="status-dot ${dotClass}"></span>
    <span class="status-text">${text}</span>
  `;

  let detail = svc.name || '';
  if (svc.pid > 0) detail += ` · PID ${svc.pid}`;
  detailEl.textContent = detail;
}

function renderSystemInfo(data) {
  const el = document.getElementById('sysInfoTable');
  if (!data) return;

  const card = document.getElementById('systemCard');
  const statusEl = document.getElementById('systemStatus');
  const detailEl = document.getElementById('systemDetail');

  statusEl.innerHTML = `
    <span class="status-dot running"></span>
    <span class="status-text">${data.hostname || 'N/A'}</span>
  `;
  detailEl.textContent = data.osVersion || '';

  const rows = [
    [t('sysinfo.hostname'), data.hostname],
    [t('sysinfo.osVersion'), data.osVersion],
    [t('sysinfo.domain'), data.userDomain],
    [t('sysinfo.currentUser'), data.currentUser],
    [t('sysinfo.dotNet'), data.dotNetVersion],
    [t('sysinfo.processors'), data.processorCount],
    [t('sysinfo.uptime'), data.systemUptime],
    [t('sysinfo.memory'), data.memoryUsage],
  ];

  el.innerHTML = rows.map(([k, v]) =>
    `<div class="info-row">
      <span class="info-key">${k}</span>
      <span class="info-val">${v || '—'}</span>
    </div>`
  ).join('');
}

function renderRegistryConfig(data) {
  const el = document.getElementById('regInfoTable');
  if (!data) return;

  if (!data.keyExists) {
    el.innerHTML = `<div class="info-row"><span class="info-key" style="color:var(--warning)">${t('registry.keyNotFound')}</span></div>`;
    return;
  }

  const rows = [
    [t('registry.installDir'), data.installDir],
    [t('registry.domainController'), data.domainController],
    [t('registry.deployedAt'), data.deployedAt],
    [t('registry.provisioned'), data.provisioned ? '✓ ' + t('common.yes') : '✗ ' + t('common.no')],
    [t('registry.machineDN'), data.machineDN],
    [t('registry.ou'), data.organizationalUnit],
    [t('registry.policyVersion'), 'v' + (data.policyVersion || 0)],
  ];

  el.innerHTML = rows.map(([k, v]) =>
    `<div class="info-row">
      <span class="info-key">${k}</span>
      <span class="info-val">${v || '—'}</span>
    </div>`
  ).join('');
}

function renderHealthChecks(data) {
  const el = document.getElementById('healthGrid');
  if (!data || !data.length) {
    el.innerHTML = `<div style="color:var(--text-muted);padding:20px">${t('dashboard.noHealthChecks')}</div>`;
    return;
  }

  el.innerHTML = data.map(h => {
    let dotClass = 'wait';
    let icon = '⋯';
    if (h.passed === true) { dotClass = 'pass'; icon = '✓'; }
    else if (h.passed === false) { dotClass = 'fail'; icon = '✗'; }

    return `<div class="health-item">
      <span class="health-dot ${dotClass}"></span>
      <span class="health-name">${h.name || ''}</span>
      <span class="health-result">${h.detail || icon}</span>
    </div>`;
  }).join('');
}

// ── Deploy ────────────────────────────────────────────────────────────────
function startDeploy() {
  if (deploying) return;
  deploying = true;

  const config = {
    driverPath:     document.getElementById('deployDriverPath').value,
    servicePath:    document.getElementById('deployServicePath').value,
    targetDir:      document.getElementById('deployTargetDir').value,
    domainController: document.getElementById('deployDC').value,
    installDriver:  document.getElementById('deployInstallDriver').checked,
    installService: document.getElementById('deployInstallService').checked,
  };

  document.getElementById('btnDeploy').style.display = 'none';
  document.getElementById('btnCancelDeploy').style.display = '';
  document.getElementById('deployProgress').style.display = '';
  document.getElementById('deployLogSection').style.display = '';
  document.getElementById('deploySteps').innerHTML = '';
  document.getElementById('deployLog').textContent = '';
  document.getElementById('deployProgressFill').style.width = '0%';
  document.getElementById('deployPct').textContent = '0%';

  send('deploy', { config });
}

function cancelDeploy() {
  send('cancelDeploy');
  deploying = false;
  document.getElementById('btnDeploy').style.display = '';
  document.getElementById('btnCancelDeploy').style.display = 'none';
}

function updateDeployProgress(data) {
  const pct = data.percent || 0;
  document.getElementById('deployProgressFill').style.width = pct + '%';
  document.getElementById('deployPct').textContent = pct + '%';
}

function addDeployStep(data) {
  const list = document.getElementById('deploySteps');
  const cls = data.success ? 'success' : (data.success === false ? 'fail' : 'running');
  const icon = data.success ? '&#xE73E;' : (data.success === false ? '&#xE711;' : '&#xE895;');
  const dur = data.durationMs ? (data.durationMs / 1000).toFixed(1) + 's' : '';

  const div = document.createElement('div');
  div.className = 'step-item ' + cls;
  div.innerHTML = `
    <span class="step-icon">${icon}</span>
    <span class="step-name">${data.name || ''}</span>
    <span class="step-msg">${data.message || ''}</span>
    <span class="step-duration">${dur}</span>
  `;
  list.appendChild(div);
  list.scrollTop = list.scrollHeight;
}

function appendDeployLog(data) {
  const log = document.getElementById('deployLog');
  log.textContent += (data.text || '') + '\n';
  log.scrollTop = log.scrollHeight;
}

function onDeployComplete(data) {
  deploying = false;
  document.getElementById('btnDeploy').style.display = '';
  document.getElementById('btnCancelDeploy').style.display = 'none';

  if (data.success) {
    showToast('success', t('deploy.completedSuccess'));
  } else {
    showToast('error', t('deploy.completedErrors'));
  }
}

function browseFile(inputId) {
  send('browseFile', { inputId });
}

function browseFolder(inputId) {
  send('browseFolder', { inputId });
}

function onBrowseResult(data) {
  if (data.inputId && data.path) {
    const el = document.getElementById(data.inputId);
    if (el) el.value = data.path;
  }
}

// ── Policy ────────────────────────────────────────────────────────────────
function onPolicyChange() {
  updatePolicyPreview();
}

function updatePolicyPreview() {
  const flags = {};
  document.querySelectorAll('#policyFlags input[data-flag]').forEach(cb => {
    flags[cb.dataset.flag] = cb.checked;
  });

  const policy = {
    version: policyVersion + 1,
    updatedAt: new Date().toISOString(),
    flags
  };

  document.getElementById('policyPreview').textContent = JSON.stringify(policy, null, 2);
}

function onPolicyLoaded(data) {
  if (!data) return;
  policyVersion = data.version || 0;
  document.getElementById('policyVersion').textContent = 'v' + policyVersion;

  if (data.flags) {
    document.querySelectorAll('#policyFlags input[data-flag]').forEach(cb => {
      if (cb.dataset.flag in data.flags) {
        cb.checked = data.flags[cb.dataset.flag];
      }
    });
  }
  updatePolicyPreview();
}

function savePolicy() {
  const flags = {};
  document.querySelectorAll('#policyFlags input[data-flag]').forEach(cb => {
    flags[cb.dataset.flag] = cb.checked;
  });

  const policy = {
    version: policyVersion + 1,
    updatedAt: new Date().toISOString(),
    flags
  };

  send('savePolicy', { json: JSON.stringify(policy, null, 2), version: policy.version });
  policyVersion = policy.version;
  document.getElementById('policyVersion').textContent = 'v' + policyVersion;
  showToast('success', t('policy.savedToRegistry'));
}

function importPolicy() {
  send('importPolicy');
}

function exportPolicy() {
  const json = document.getElementById('policyPreview').textContent;
  send('exportPolicy', { json });
}

function resetProvisioning() {
  if (confirm(t('policy.resetConfirm'))) {
    send('resetProvisioning');
    showToast('warning', t('policy.provisioningReset'));
  }
}

// ── Alerts ────────────────────────────────────────────────────────────────
function refreshAlerts() {
  send('getEvents');
}

function receiveEvents(data) {
  allEvents = data || [];
  const badge = document.getElementById('alertBadge');
  const errorCount = allEvents.filter(e => e.level === 'Error').length;
  if (errorCount > 0) {
    badge.textContent = errorCount;
    badge.style.display = '';
  } else {
    badge.style.display = 'none';
  }
  renderAlerts();
}

function filterAlerts() {
  renderAlerts();
}

function setAlertFilter(filter) {
  currentFilter = filter;
  document.querySelectorAll('.filter-pills .pill').forEach(p =>
    p.classList.toggle('active', p.dataset.filter === filter)
  );
  renderAlerts();
}

function renderAlerts() {
  const search = (document.getElementById('alertSearch').value || '').toLowerCase();
  const body = document.getElementById('alertsBody');

  let filtered = allEvents;

  if (currentFilter !== 'all') {
    filtered = filtered.filter(e => {
      const lvl = (e.level || '').toLowerCase();
      if (currentFilter === 'error') return lvl === 'error';
      if (currentFilter === 'warning') return lvl === 'warning';
      if (currentFilter === 'info') return lvl === 'information' || lvl === 'info';
      return true;
    });
  }

  if (search) {
    filtered = filtered.filter(e =>
      (e.message || '').toLowerCase().includes(search) ||
      (e.source || '').toLowerCase().includes(search)
    );
  }

  if (filtered.length === 0) {
    body.innerHTML = `<tr class="empty-row"><td colspan="5">${t('alerts.noEvents')}</td></tr>`;
    return;
  }

  body.innerHTML = filtered.map((e, i) => {
    const lvl = (e.level || '').toLowerCase();
    let badgeClass = 'info';
    if (lvl === 'error') badgeClass = 'error';
    else if (lvl === 'warning') badgeClass = 'warning';

    const ts = e.timeStamp ? new Date(e.timeStamp).toLocaleString() : '';
    const msgShort = (e.message || '').substring(0, 120);

    return `<tr onclick="showAlertDetail(${i})" data-idx="${i}">
      <td><span class="level-badge ${badgeClass}">${e.level || '?'}</span></td>
      <td style="font-family:var(--font-mono);font-size:12px;color:var(--text-secondary)">${ts}</td>
      <td style="font-family:var(--font-mono)">${e.eventId || 0}</td>
      <td>${e.source || ''}</td>
      <td>${msgShort}</td>
    </tr>`;
  }).join('');
}

function showAlertDetail(idx) {
  const events = allEvents.filter(e => {
    if (currentFilter === 'all') return true;
    const lvl = (e.level || '').toLowerCase();
    if (currentFilter === 'error') return lvl === 'error';
    if (currentFilter === 'warning') return lvl === 'warning';
    if (currentFilter === 'info') return lvl === 'information' || lvl === 'info';
    return true;
  });
  const ev = events[idx];
  if (!ev) return;

  document.getElementById('alertDetail').style.display = '';
  document.getElementById('alertDetailText').textContent =
    `Level:     ${ev.level}\n` +
    `Timestamp: ${ev.timeStamp ? new Date(ev.timeStamp).toLocaleString() : ''}\n` +
    `Event ID:  ${ev.eventId}\n` +
    `Source:    ${ev.source}\n` +
    `\n${ev.message || ''}`;

  // Highlight row
  document.querySelectorAll('#alertsBody tr').forEach(r => r.classList.remove('selected'));
  const row = document.querySelector(`#alertsBody tr[data-idx="${idx}"]`);
  if (row) row.classList.add('selected');
}

function closeAlertDetail() {
  document.getElementById('alertDetail').style.display = 'none';
  document.querySelectorAll('#alertsBody tr').forEach(r => r.classList.remove('selected'));
}

// ── Toast ─────────────────────────────────────────────────────────────────
function showToast(level, text) {
  let container = document.querySelector('.toast-container');
  if (!container) {
    container = document.createElement('div');
    container.className = 'toast-container';
    document.body.appendChild(container);
  }

  const icons = { success: '\uE73E', error: '\uE711', warning: '\uE7BA', info: '\uE946' };

  const toast = document.createElement('div');
  toast.className = 'toast ' + level;
  toast.innerHTML = `
    <span class="toast-icon">${icons[level] || icons.info}</span>
    <span class="toast-text">${text}</span>
  `;
  container.appendChild(toast);

  setTimeout(() => {
    toast.style.opacity = '0';
    toast.style.transform = 'translateX(20px)';
    toast.style.transition = 'all 300ms ease';
    setTimeout(() => toast.remove(), 300);
  }, 4000);
}

// ── Elevated Status ───────────────────────────────────────────────────────
function onElevatedStatus(data) {
  if (data && data.isElevated) {
    document.getElementById('adminBadge').style.display = '';
  }
}

// ── Init ──────────────────────────────────────────────────────────────────
document.addEventListener('DOMContentLoaded', () => {
  refreshDashboard();
  updatePolicyPreview();
});

// Auto-refresh dashboard every 10 seconds if visible
setInterval(() => {
  if (document.querySelector('#page-dashboard.active')) {
    send('queryServices');
  }
}, 10000);

// ═══════════════════════════════════════════════════════════════════════════
// ══ CLASSROOM DESIGNER (Admin Panel) ═══════════════════════════════════
// ═══════════════════════════════════════════════════════════════════════════

// ── State ────────────────────────────────────────────────────────────

let rooms = [];               // Array of room objects
let activeRoomId = null;      // Currently selected room ID
let designerTool = 'select';  // Current tool
let canvasObjects = [];       // Objects on the designer canvas
let selectedObject = null;    // Currently selected canvas object
let nextObjectId = 1;
let isDragging = false;
let dragTarget = null;
let dragOffsetX = 0;
let dragOffsetY = 0;
let showGrid = true;
let zoomLevel = 1.0;
let canvasW = 1200;
let canvasH = 800;

const GRID_SIZE = 40;
const DESK_W = 80;
const DESK_H = 50;
const TEACHER_DESK_W = 120;
const TEACHER_DESK_H = 60;

// ── Room Management ────────────────────────────────────────────────

function createRoom() {
    const name = prompt('Enter classroom name:', `Room ${rooms.length + 1}`);
    if (!name || !name.trim()) return;

    const roomId = 'room_' + Date.now();
    const room = {
        id: roomId,
        name: name.trim(),
        roomCode: name.trim().replace(/\s+/g, '').substring(0, 10).toUpperCase(),
        objects: [],
        createdAt: new Date().toISOString()
    };

    rooms.push(room);
    renderRoomList();
    selectRoom(roomId);
    saveRoomsToHost();
}

function selectRoom(roomId) {
    activeRoomId = roomId;
    const room = rooms.find(r => r.id === roomId);

    document.querySelectorAll('.room-item').forEach(el =>
        el.classList.toggle('active', el.dataset.roomId === roomId));

    if (room) {
        document.getElementById('designerRoomName').textContent = room.name;
        document.getElementById('btnRenameRoom').style.display = '';
        document.getElementById('btnSaveRoom').style.display = '';
        document.getElementById('btnDeleteRoom').style.display = '';
        document.getElementById('designerEmpty').style.display = 'none';
        document.getElementById('designerCanvas').style.display = '';

        canvasObjects = JSON.parse(JSON.stringify(room.objects || []));
        nextObjectId = canvasObjects.reduce((max, o) => Math.max(max, o.id + 1), 1);
        selectedObject = null;
        closePropsPanel();
        redrawCanvas();
    }
}

function renameActiveRoom() {
    const room = rooms.find(r => r.id === activeRoomId);
    if (!room) return;

    const name = prompt('Rename classroom:', room.name);
    if (!name || !name.trim()) return;

    room.name = name.trim();
    room.roomCode = name.trim().replace(/\s+/g, '').substring(0, 10).toUpperCase();
    document.getElementById('designerRoomName').textContent = room.name;
    renderRoomList();
    saveRoomsToHost();
}

function deleteActiveRoom() {
    if (!activeRoomId) return;
    const room = rooms.find(r => r.id === activeRoomId);
    if (!room) return;
    if (!confirm(t('classrooms.deleteConfirm', { name: room.name }))) return;

    rooms = rooms.filter(r => r.id !== activeRoomId);
    activeRoomId = null;
    canvasObjects = [];
    selectedObject = null;

    document.getElementById('designerRoomName').textContent = 'Select a classroom';
    document.getElementById('btnRenameRoom').style.display = 'none';
    document.getElementById('btnSaveRoom').style.display = 'none';
    document.getElementById('btnDeleteRoom').style.display = 'none';
    document.getElementById('designerEmpty').style.display = '';
    document.getElementById('designerCanvas').style.display = 'none';
    closePropsPanel();
    renderRoomList();
    saveRoomsToHost();
}

function saveActiveRoom() {
    const room = rooms.find(r => r.id === activeRoomId);
    if (!room) return;

    room.objects = JSON.parse(JSON.stringify(canvasObjects));
    saveRoomsToHost();
    showToast('success', t('classrooms.roomSaved', { name: room.name }));
}

function renderRoomList() {
    const list = document.getElementById('roomList');

    if (rooms.length === 0) {
        list.innerHTML = `
            <div style="padding:24px 12px;text-align:center;color:var(--text-muted);font-size:13px">
                No classrooms yet.<br>Click + to create one.
            </div>`;
        return;
    }

    list.innerHTML = rooms.map(r => {
        const deskCount = (r.objects || []).filter(o => o.type === 'desk').length;
        return `
            <div class="room-item ${r.id === activeRoomId ? 'active' : ''}"
                 data-room-id="${r.id}" onclick="selectRoom('${r.id}')">
                <span class="room-item-icon">&#xE912;</span>
                <div class="room-item-info">
                    <div class="room-item-name">${r.name}</div>
                    <div class="room-item-meta">${r.roomCode}</div>
                </div>
                <span class="room-item-count">${deskCount}</span>
            </div>`;
    }).join('');
}

// ── Designer Tools ───────────────────────────────────────────────────

function setDesignerTool(tool) {
    designerTool = tool;
    document.querySelectorAll('.tool-btn').forEach(b =>
        b.classList.toggle('active', b.dataset.tool === tool));

    const canvas = document.getElementById('designerCanvas');
    canvas.style.cursor = tool === 'select' ? 'default' : 'crosshair';
}

function toggleGrid() {
    showGrid = !showGrid;
    redrawCanvas();
}

function zoomIn() {
    zoomLevel = Math.min(zoomLevel + 0.1, 2.0);
    applyZoom();
}

function zoomOut() {
    zoomLevel = Math.max(zoomLevel - 0.1, 0.4);
    applyZoom();
}

function fitToView() {
    zoomLevel = 1.0;
    applyZoom();
}

function applyZoom() {
    const canvas = document.getElementById('designerCanvas');
    canvas.style.transform = `translate(-50%, -50%) scale(${zoomLevel})`;
}

// ── Canvas Drawing ───────────────────────────────────────────────────

function redrawCanvas() {
    const canvas = document.getElementById('designerCanvas');
    const ctx = canvas.getContext('2d');

    ctx.clearRect(0, 0, canvasW, canvasH);

    // Background
    ctx.fillStyle = '#0D1117';
    ctx.fillRect(0, 0, canvasW, canvasH);

    // Grid
    if (showGrid) {
        ctx.strokeStyle = 'rgba(48, 54, 61, 0.5)';
        ctx.lineWidth = 1;
        for (let x = 0; x <= canvasW; x += GRID_SIZE) {
            ctx.beginPath(); ctx.moveTo(x, 0); ctx.lineTo(x, canvasH); ctx.stroke();
        }
        for (let y = 0; y <= canvasH; y += GRID_SIZE) {
            ctx.beginPath(); ctx.moveTo(0, y); ctx.lineTo(canvasW, y); ctx.stroke();
        }
    }

    // Draw objects sorted by z-order
    const sorted = [...canvasObjects].sort((a, b) => (a.z || 0) - (b.z || 0));
    for (const obj of sorted) {
        drawObject(ctx, obj, obj === selectedObject);
    }
}

function drawObject(ctx, obj, isSelected) {
    ctx.save();
    switch (obj.type) {
        case 'desk':         drawDesk(ctx, obj, isSelected); break;
        case 'teacher-desk': drawTeacherDesk(ctx, obj, isSelected); break;
        case 'wall':         drawWall(ctx, obj, isSelected); break;
        case 'door':         drawDoor(ctx, obj, isSelected); break;
        case 'label':        drawLabel(ctx, obj, isSelected); break;
    }
    ctx.restore();
}

function drawDesk(ctx, obj, sel) {
    const x = obj.x, y = obj.y, w = obj.w || DESK_W, h = obj.h || DESK_H;

    ctx.fillStyle = sel ? '#1C3A5E' : '#161B22';
    ctx.strokeStyle = sel ? '#58A6FF' : '#30363D';
    ctx.lineWidth = sel ? 2 : 1;
    roundRect(ctx, x, y, w, h, 4);
    ctx.fill();
    ctx.stroke();

    // Monitor icon
    ctx.fillStyle = '#21262D';
    const monW = 24, monH = 16;
    const monX = x + (w - monW) / 2, monY = y + 6;
    roundRect(ctx, monX, monY, monW, monH, 2);
    ctx.fill();
    ctx.strokeStyle = '#30363D';
    ctx.lineWidth = 1;
    ctx.stroke();

    // Stand
    ctx.fillStyle = '#30363D';
    ctx.fillRect(monX + monW / 2 - 2, monY + monH, 4, 4);
    ctx.fillRect(monX + monW / 2 - 6, monY + monH + 4, 12, 2);

    // Label
    ctx.fillStyle = sel ? '#58A6FF' : '#8B949E';
    ctx.font = '10px "Segoe UI", sans-serif';
    ctx.textAlign = 'center';
    const label = obj.label || obj.hostname || `Desk ${obj.id}`;
    ctx.fillText(label, x + w / 2, y + h - 5, w - 8);

    // Online indicator
    if (obj.hostname) {
        ctx.fillStyle = obj.online ? '#3FB950' : '#F85149';
        ctx.beginPath();
        ctx.arc(x + w - 8, y + 8, 4, 0, Math.PI * 2);
        ctx.fill();
    }
}

function drawTeacherDesk(ctx, obj, sel) {
    const x = obj.x, y = obj.y;
    const w = obj.w || TEACHER_DESK_W, h = obj.h || TEACHER_DESK_H;

    ctx.fillStyle = sel ? '#2D1B4E' : '#1C2128';
    ctx.strokeStyle = sel ? '#BC8CFF' : '#30363D';
    ctx.lineWidth = sel ? 2 : 1;
    roundRect(ctx, x, y, w, h, 6);
    ctx.fill();
    ctx.stroke();

    ctx.fillStyle = sel ? '#BC8CFF' : '#8B949E';
    ctx.font = '16px "Segoe MDL2 Assets"';
    ctx.textAlign = 'center';
    ctx.fillText('\uE7EF', x + w / 2, y + h / 2 + 2);

    ctx.font = '11px "Segoe UI", sans-serif';
    ctx.fillText(obj.label || 'Teacher', x + w / 2, y + h - 6);
}

function drawWall(ctx, obj, sel) {
    const x = obj.x, y = obj.y;
    const w = obj.w || 200, h = obj.h || 8;

    ctx.fillStyle = sel ? '#58A6FF' : '#6E7681';
    ctx.strokeStyle = sel ? '#58A6FF' : 'transparent';
    ctx.lineWidth = 1;
    ctx.fillRect(x, y, w, h);
    if (sel) ctx.strokeRect(x - 1, y - 1, w + 2, h + 2);
}

function drawDoor(ctx, obj, sel) {
    const x = obj.x, y = obj.y;
    const w = obj.w || 60, h = obj.h || 8;

    ctx.fillStyle = '#D29922';
    ctx.fillRect(x, y, w, h);

    ctx.strokeStyle = sel ? '#D29922' : 'rgba(210,153,34,0.3)';
    ctx.lineWidth = 1;
    ctx.setLineDash([4, 4]);
    ctx.beginPath();
    ctx.arc(x, y + h / 2, w * 0.7, -Math.PI / 2, 0);
    ctx.stroke();
    ctx.setLineDash([]);

    if (sel) {
        ctx.strokeStyle = '#D29922';
        ctx.lineWidth = 2;
        ctx.strokeRect(x - 1, y - 1, w + 2, h + 2);
    }
}

function drawLabel(ctx, obj, sel) {
    const x = obj.x, y = obj.y;
    const text = obj.label || 'Label';
    const fontSize = obj.fontSize || 14;

    ctx.font = `${fontSize}px "Segoe UI", sans-serif`;
    ctx.fillStyle = sel ? '#58A6FF' : (obj.color || '#8B949E');
    ctx.textAlign = 'left';
    ctx.fillText(text, x, y + fontSize);

    if (sel) {
        const metrics = ctx.measureText(text);
        ctx.strokeStyle = '#58A6FF';
        ctx.lineWidth = 1;
        ctx.setLineDash([3, 3]);
        ctx.strokeRect(x - 2, y - 2, metrics.width + 4, fontSize + 6);
        ctx.setLineDash([]);
    }

    const metrics = ctx.measureText(text);
    obj.w = metrics.width + 4;
    obj.h = fontSize + 6;
}

function roundRect(ctx, x, y, w, h, r) {
    ctx.beginPath();
    ctx.moveTo(x + r, y);
    ctx.lineTo(x + w - r, y);
    ctx.quadraticCurveTo(x + w, y, x + w, y + r);
    ctx.lineTo(x + w, y + h - r);
    ctx.quadraticCurveTo(x + w, y + h, x + w - r, y + h);
    ctx.lineTo(x + r, y + h);
    ctx.quadraticCurveTo(x, y + h, x, y + h - r);
    ctx.lineTo(x, y + r);
    ctx.quadraticCurveTo(x, y, x + r, y);
    ctx.closePath();
}

// ── Snap to grid ─────────────────────────────────────────────────────

function snap(val) {
    return Math.round(val / GRID_SIZE) * GRID_SIZE;
}

// ── Hit Testing ──────────────────────────────────────────────────────

function hitTest(mx, my) {
    for (let i = canvasObjects.length - 1; i >= 0; i--) {
        const obj = canvasObjects[i];
        const w = obj.w || getDefaultW(obj.type);
        const h = obj.h || getDefaultH(obj.type);
        if (mx >= obj.x && mx <= obj.x + w && my >= obj.y && my <= obj.y + h) {
            return obj;
        }
    }
    return null;
}

function getDefaultW(type) {
    switch (type) {
        case 'desk': return DESK_W;
        case 'teacher-desk': return TEACHER_DESK_W;
        case 'wall': return 200;
        case 'door': return 60;
        case 'label': return 80;
        default: return 60;
    }
}

function getDefaultH(type) {
    switch (type) {
        case 'desk': return DESK_H;
        case 'teacher-desk': return TEACHER_DESK_H;
        case 'wall': return 8;
        case 'door': return 8;
        case 'label': return 20;
        default: return 40;
    }
}

// ── Canvas Event Handlers ────────────────────────────────────────────

function initDesignerCanvas() {
    const canvas = document.getElementById('designerCanvas');
    if (!canvas) return;

    canvas.addEventListener('mousedown', onCanvasMouseDown);
    canvas.addEventListener('mousemove', onCanvasMouseMove);
    canvas.addEventListener('mouseup', onCanvasMouseUp);
    canvas.addEventListener('dblclick', onCanvasDblClick);

    document.addEventListener('keydown', onDesignerKeyDown);
}

function getCanvasCoords(e) {
    const canvas = document.getElementById('designerCanvas');
    const rect = canvas.getBoundingClientRect();
    const scaleX = canvasW / rect.width;
    const scaleY = canvasH / rect.height;
    return {
        x: (e.clientX - rect.left) * scaleX,
        y: (e.clientY - rect.top) * scaleY
    };
}

function onCanvasMouseDown(e) {
    const { x, y } = getCanvasCoords(e);

    if (designerTool === 'select') {
        const hit = hitTest(x, y);
        if (hit) {
            selectedObject = hit;
            isDragging = true;
            dragTarget = hit;
            dragOffsetX = x - hit.x;
            dragOffsetY = y - hit.y;
            showPropsPanel(hit);
        } else {
            selectedObject = null;
            closePropsPanel();
        }
    } else {
        const obj = createDesignerObject(designerTool, snap(x), snap(y));
        if (obj) {
            canvasObjects.push(obj);
            selectedObject = obj;
            showPropsPanel(obj);
            setDesignerTool('select');
        }
    }

    redrawCanvas();
}

function onCanvasMouseMove(e) {
    if (!isDragging || !dragTarget) return;

    const { x, y } = getCanvasCoords(e);
    dragTarget.x = snap(x - dragOffsetX);
    dragTarget.y = snap(y - dragOffsetY);

    dragTarget.x = Math.max(0, Math.min(canvasW - (dragTarget.w || 40), dragTarget.x));
    dragTarget.y = Math.max(0, Math.min(canvasH - (dragTarget.h || 20), dragTarget.y));

    redrawCanvas();
}

function onCanvasMouseUp(e) {
    isDragging = false;
    dragTarget = null;
}

function onCanvasDblClick(e) {
    const { x, y } = getCanvasCoords(e);
    const hit = hitTest(x, y);
    if (hit && (hit.type === 'label' || hit.type === 'desk')) {
        const newLabel = prompt('Edit label:', hit.label || '');
        if (newLabel !== null) {
            hit.label = newLabel;
            redrawCanvas();
            if (selectedObject === hit) showPropsPanel(hit);
        }
    }
}

function onDesignerKeyDown(e) {
    // Only handle keys when classrooms page is active and not in an input
    if (!document.getElementById('page-classrooms')?.classList.contains('active')) return;
    if (e.target.tagName === 'INPUT' || e.target.tagName === 'TEXTAREA' || e.target.tagName === 'SELECT') return;

    if (e.key === 'Delete' || e.key === 'Backspace') {
        deleteSelected();
        e.preventDefault();
    }
    if (e.key === 'Escape') {
        selectedObject = null;
        closePropsPanel();
        redrawCanvas();
    }
    if (e.ctrlKey && e.key === 's') {
        saveActiveRoom();
        e.preventDefault();
    }
    if (e.ctrlKey && e.key === 'd' && selectedObject) {
        const clone = JSON.parse(JSON.stringify(selectedObject));
        clone.id = nextObjectId++;
        clone.x += GRID_SIZE;
        clone.y += GRID_SIZE;
        canvasObjects.push(clone);
        selectedObject = clone;
        showPropsPanel(clone);
        redrawCanvas();
        e.preventDefault();
    }
}

// ── Object Creation ──────────────────────────────────────────────────

function createDesignerObject(type, x, y) {
    const id = nextObjectId++;
    switch (type) {
        case 'desk':
            return { id, type: 'desk', x, y, w: DESK_W, h: DESK_H, label: '', hostname: '', z: 10 };
        case 'teacher-desk':
            return { id, type: 'teacher-desk', x, y, w: TEACHER_DESK_W, h: TEACHER_DESK_H, label: 'Teacher', z: 10 };
        case 'wall':
            return { id, type: 'wall', x, y, w: 200, h: 8, z: 1 };
        case 'door':
            return { id, type: 'door', x, y, w: 60, h: 8, z: 2 };
        case 'label': {
            const text = prompt('Enter label text:', 'Label');
            if (!text) return null;
            return { id, type: 'label', x, y, w: 80, h: 20, label: text, fontSize: 14, color: '#8B949E', z: 5 };
        }
        default:
            return null;
    }
}

function deleteSelected() {
    if (!selectedObject) return;
    canvasObjects = canvasObjects.filter(o => o !== selectedObject);
    selectedObject = null;
    closePropsPanel();
    redrawCanvas();
}

// ── Properties Panel ─────────────────────────────────────────────────

function showPropsPanel(obj) {
    const panel = document.getElementById('propsPanel');
    const body = document.getElementById('propsBody');
    panel.style.display = '';

    let html = `
        <div class="prop-group">
            <div class="prop-label">Type</div>
            <input class="prop-input" value="${obj.type}" disabled />
        </div>
        <div class="prop-row">
            <div class="prop-group">
                <div class="prop-label">X</div>
                <input class="prop-input" type="number" value="${obj.x}" step="${GRID_SIZE}"
                       onchange="updateProp(${obj.id}, 'x', parseInt(this.value))" />
            </div>
            <div class="prop-group">
                <div class="prop-label">Y</div>
                <input class="prop-input" type="number" value="${obj.y}" step="${GRID_SIZE}"
                       onchange="updateProp(${obj.id}, 'y', parseInt(this.value))" />
            </div>
        </div>
        <div class="prop-row">
            <div class="prop-group">
                <div class="prop-label">Width</div>
                <input class="prop-input" type="number" value="${obj.w || getDefaultW(obj.type)}" step="${GRID_SIZE}"
                       onchange="updateProp(${obj.id}, 'w', parseInt(this.value))" />
            </div>
            <div class="prop-group">
                <div class="prop-label">Height</div>
                <input class="prop-input" type="number" value="${obj.h || getDefaultH(obj.type)}" step="${GRID_SIZE}"
                       onchange="updateProp(${obj.id}, 'h', parseInt(this.value))" />
            </div>
        </div>`;

    if (obj.type === 'desk') {
        html += `
            <div class="prop-group">
                <div class="prop-label">Label</div>
                <input class="prop-input" value="${obj.label || ''}" placeholder="e.g. Seat 1"
                       onchange="updateProp(${obj.id}, 'label', this.value)" />
            </div>
            <div class="prop-group">
                <div class="prop-label">Assigned Hostname</div>
                <input class="prop-input" value="${obj.hostname || ''}" placeholder="e.g. LAB1-PC04"
                       onchange="updateProp(${obj.id}, 'hostname', this.value)" />
            </div>
            <div class="prop-group">
                <div class="prop-label">Assigned IP</div>
                <input class="prop-input" value="${obj.ip || ''}" placeholder="e.g. 10.0.1.40"
                       onchange="updateProp(${obj.id}, 'ip', this.value)" />
            </div>`;
    }

    if (obj.type === 'teacher-desk' || obj.type === 'label') {
        html += `
            <div class="prop-group">
                <div class="prop-label">Label</div>
                <input class="prop-input" value="${obj.label || ''}"
                       onchange="updateProp(${obj.id}, 'label', this.value)" />
            </div>`;
    }

    if (obj.type === 'label') {
        html += `
            <div class="prop-group">
                <div class="prop-label">Font Size</div>
                <input class="prop-input" type="number" value="${obj.fontSize || 14}" min="8" max="48"
                       onchange="updateProp(${obj.id}, 'fontSize', parseInt(this.value))" />
            </div>
            <div class="prop-group">
                <div class="prop-label">Color</div>
                <input class="prop-input" type="color" value="${obj.color || '#8B949E'}"
                       onchange="updateProp(${obj.id}, 'color', this.value)" />
            </div>`;
    }

    if (obj.type === 'wall') {
        html += `
            <div class="prop-group">
                <div class="prop-label">Tip</div>
                <div style="font-size:11px;color:var(--text-muted);padding:4px 0">
                    Adjust Width and Height to create horizontal or vertical walls.
                </div>
            </div>`;
    }

    body.innerHTML = html;
}

function closePropsPanel() {
    const p = document.getElementById('propsPanel');
    if (p) p.style.display = 'none';
}

function updateProp(objId, key, value) {
    const obj = canvasObjects.find(o => o.id === objId);
    if (obj) {
        obj[key] = value;
        redrawCanvas();
    }
}

// ── Import / Export ──────────────────────────────────────────────────

function importLayout() {
    send('admin_import_layout');
}

function exportAllLayouts() {
    send('admin_export_layouts', { data: JSON.stringify(rooms) });
}

// ── C# Bridge Helpers ────────────────────────────────────────────────

function saveRoomsToHost() {
    send('admin_save_rooms', { data: JSON.stringify(rooms) });
}

function loadRoomsFromHost() {
    send('admin_load_rooms');
}

function handleAdminMessage(msg) {
    switch (msg.type) {
        case 'admin_rooms_loaded':
            onRoomsLoaded(msg.data);
            break;
        case 'admin_layout_imported':
            onLayoutImported(msg.data);
            break;
    }
}

function onRoomsLoaded(data) {
    try {
        rooms = typeof data === 'string' ? JSON.parse(data) : (data || []);
    } catch { rooms = []; }
    renderRoomList();
    if (rooms.length > 0 && !activeRoomId) {
        selectRoom(rooms[0].id);
    }
}

function onLayoutImported(data) {
    try {
        const imported = typeof data === 'string' ? JSON.parse(data) : data;
        if (Array.isArray(imported)) {
            rooms = [...rooms, ...imported];
        } else if (imported && imported.id) {
            rooms.push(imported);
        }
        renderRoomList();
        saveRoomsToHost();
        showToast('success', 'Layout imported');
    } catch (e) {
        console.error('Import failed:', e);
        showToast('error', 'Failed to import layout');
    }
}

// ── Init Designer ────────────────────────────────────────────────────

initDesignerCanvas();
