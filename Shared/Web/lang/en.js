// TAD.RV — English (en) translation pack
// Base language — all keys defined here.

const TAD_LANG_EN = {
    // ── Common ──────────────────────────────────────────────────
    'common.save': 'Save',
    'common.cancel': 'Cancel',
    'common.delete': 'Delete',
    'common.close': 'Close',
    'common.refresh': 'Refresh',
    'common.import': 'Import',
    'common.export': 'Export',
    'common.browse': 'Browse',
    'common.yes': 'Yes',
    'common.no': 'No',
    'common.loading': 'Loading…',
    'common.checking': 'Checking…',
    'common.unknown': 'Unknown',
    'common.admin': 'Admin',

    // ── Console: Sidebar / Nav ──────────────────────────────────
    'nav.dashboard': 'Dashboard',
    'nav.deploy': 'Deploy',
    'nav.policy': 'Policy',
    'nav.alerts': 'Alerts',
    'nav.classrooms': 'Classrooms',

    // ── Console: Dashboard ──────────────────────────────────────
    'dashboard.kernelDriver': 'Kernel Driver',
    'dashboard.bridgeService': 'Bridge Service',
    'dashboard.system': 'System',
    'dashboard.healthChecks': 'Health Checks',
    'dashboard.systemInformation': 'System Information',
    'dashboard.registryConfiguration': 'Registry Configuration',
    'dashboard.noHealthChecks': 'No health checks available',

    // Driver/service statuses
    'status.running': 'Running',
    'status.stopped': 'Stopped',
    'status.notInstalled': 'Not Installed',

    // System info labels
    'sysinfo.hostname': 'Hostname',
    'sysinfo.osVersion': 'OS Version',
    'sysinfo.domain': 'Domain',
    'sysinfo.currentUser': 'Current User',
    'sysinfo.dotNet': '.NET Runtime',
    'sysinfo.processors': 'Processors',
    'sysinfo.uptime': 'System Uptime',
    'sysinfo.memory': 'Memory (Console)',

    // Registry labels
    'registry.installDir': 'Install Directory',
    'registry.domainController': 'Domain Controller',
    'registry.deployedAt': 'Deployed At',
    'registry.provisioned': 'Provisioned',
    'registry.machineDN': 'Machine DN',
    'registry.ou': 'Organizational Unit',
    'registry.policyVersion': 'Policy Version',
    'registry.keyNotFound': 'Registry key HKLM\\SOFTWARE\\TAD_RV not found',

    // ── Console: Deploy ─────────────────────────────────────────
    'deploy.title': 'Deployment Configuration',
    'deploy.driverBinary': 'Driver Binary (TAD.RV.sys)',
    'deploy.driverPlaceholder': 'Path to TAD.RV.sys',
    'deploy.serviceFolder': 'Service Publish Folder',
    'deploy.servicePlaceholder': 'Path to TadBridgeService publish output',
    'deploy.targetDir': 'Target Install Directory',
    'deploy.domainController': 'Domain Controller',
    'deploy.installDriver': 'Install Kernel Driver',
    'deploy.installService': 'Install Bridge Service',
    'deploy.beginDeployment': 'Begin Deployment',
    'deploy.progress': 'Deployment Progress',
    'deploy.log': 'Deployment Log',
    'deploy.completedSuccess': 'Deployment completed successfully',
    'deploy.completedErrors': 'Deployment completed with errors',
    'deploy.cancelled': 'Deployment cancelled',

    // ── Console: Policy ─────────────────────────────────────────
    'policy.flags': 'Policy Flags',
    'policy.jsonPreview': 'Policy JSON Preview',
    'policy.savePolicy': 'Save Policy',
    'policy.resetProvisioning': 'Reset Provisioning',
    'policy.resetConfirm': 'Reset provisioning flag? The service will re-provision on next start.',
    'policy.provisioningReset': 'Provisioning flag reset',
    'policy.savedToRegistry': 'Policy saved to registry',

    // Policy flag labels
    'policy.flag.ScreenCapture': 'Screen Capture',
    'policy.flag.ScreenCapture.desc': 'Capture student screens at configured interval',
    'policy.flag.WebFilter': 'Web Filtering',
    'policy.flag.WebFilter.desc': 'Block access to restricted websites',
    'policy.flag.AppBlock': 'Application Blocking',
    'policy.flag.AppBlock.desc': 'Prevent launch of blacklisted executables',
    'policy.flag.UsbBlock': 'USB Storage Block',
    'policy.flag.UsbBlock.desc': 'Block removable USB storage devices',
    'policy.flag.PrintBlock': 'Print Blocking',
    'policy.flag.PrintBlock.desc': 'Disable print spooler during class hours',
    'policy.flag.NetworkAudit': 'Network Audit',
    'policy.flag.NetworkAudit.desc': 'Log all network connections made by endpoints',
    'policy.flag.Heartbeat': 'Heartbeat',
    'policy.flag.Heartbeat.desc': 'Send periodic health heartbeats to management',
    'policy.flag.Stealth': 'Stealth Mode',
    'policy.flag.Stealth.desc': 'Hide TAD.RV tray icon from end users',

    // ── Console: Alerts ─────────────────────────────────────────
    'alerts.eventLog': 'Event Log',
    'alerts.searchPlaceholder': 'Search events…',
    'alerts.all': 'All',
    'alerts.error': 'Error',
    'alerts.warning': 'Warning',
    'alerts.info': 'Info',
    'alerts.level': 'Level',
    'alerts.timestamp': 'Timestamp',
    'alerts.id': 'ID',
    'alerts.source': 'Source',
    'alerts.message': 'Message',
    'alerts.noEvents': 'No events found',
    'alerts.loadingEvents': 'Loading events…',
    'alerts.eventDetail': 'Event Detail',

    // ── Console: Classrooms ─────────────────────────────────────
    'classrooms.title': 'Classrooms',
    'classrooms.selectRoom': 'Select a classroom',
    'classrooms.noRooms': 'No classrooms yet.',
    'classrooms.clickToCreate': 'Click + to create one.',
    'classrooms.createPrompt': 'Enter classroom name:',
    'classrooms.renamePrompt': 'Rename classroom:',
    'classrooms.deleteConfirm': 'Delete "{name}"? This cannot be undone.',
    'classrooms.roomSaved': 'Room "{name}" saved',
    'classrooms.layoutImported': 'Layout imported',
    'classrooms.importFailed': 'Failed to import layout',
    'classrooms.exportAll': 'Export All',
    'classrooms.classroomDesigner': 'Classroom Designer',
    'classrooms.designerEmpty': 'Select a room from the sidebar or create a new one to start designing.',
    'classrooms.properties': 'Properties',
    'classrooms.deleteObject': 'Delete Object',

    // Designer tools
    'tool.select': 'Select & Move',
    'tool.desk': 'Place Desk',
    'tool.teacherDesk': 'Teacher Desk',
    'tool.wall': 'Wall / Divider',
    'tool.door': 'Door',
    'tool.label': 'Text Label',
    'tool.toggleGrid': 'Toggle Grid',
    'tool.zoomIn': 'Zoom In',
    'tool.zoomOut': 'Zoom Out',
    'tool.fitToView': 'Fit to View',

    // Properties labels
    'prop.type': 'Type',
    'prop.x': 'X',
    'prop.y': 'Y',
    'prop.width': 'Width',
    'prop.height': 'Height',
    'prop.label': 'Label',
    'prop.hostname': 'Assigned Hostname',
    'prop.ip': 'Assigned IP',
    'prop.fontSize': 'Font Size',
    'prop.color': 'Color',
    'prop.wallTip': 'Adjust Width and Height to create horizontal or vertical walls.',
    'prop.labelPlaceholder': 'e.g. Seat 1',
    'prop.hostnamePlaceholder': 'e.g. LAB1-PC04',
    'prop.ipPlaceholder': 'e.g. 10.0.1.40',
    'prop.editLabel': 'Edit label:',
    'prop.enterLabel': 'Enter label text:',

    // Canvas labels
    'canvas.teacher': 'Teacher',
    'canvas.desk': 'Desk',
    'canvas.label': 'Label',

    // ── Teacher: Top Nav ────────────────────────────────────────
    'teacher.title': 'TAD.RV Teacher',
    'teacher.noRoomSelected': 'No Room Selected',
    'teacher.selectClassroom': 'Select Classroom',
    'teacher.noClassrooms': 'No classrooms configured.',
    'teacher.createInConsole': 'Create rooms in the Console app.',
    'teacher.noRoom': 'No Room',
    'teacher.deskCount': '{count} desks',

    // ── Teacher: Stats Bar ──────────────────────────────────────
    'stats.online': 'Online',
    'stats.locked': 'Locked',
    'stats.streaming': 'Streaming',
    'stats.frozen': 'Frozen',
    'stats.offline': 'Offline',
    'stats.showOffline': 'Show Offline',
    'stats.freezeTimer': 'Freeze Timer',
    'stats.previousRoom': 'Previous Room',
    'stats.nextRoom': 'Next Room',

    // ── Teacher: Student Tiles ──────────────────────────────────
    'tile.remoteView': 'Remote View',
    'tile.lock': 'Lock',
    'tile.unlock': 'Unlock',
    'tile.freezeTimer': 'Freeze Timer',

    // Badges
    'badge.locked': 'Locked',
    'badge.frozen': 'Frozen',
    'badge.rv': 'RV',
    'badge.online': 'Online',
    'badge.offline': 'Offline',

    // ── Teacher: Empty State ────────────────────────────────────
    'empty.noStudents': 'No Students Connected',
    'empty.waiting': 'Waiting for student endpoints to come online...',

    // ── Teacher: Remote View Modal ──────────────────────────────
    'rv.title': 'Remote View — {host}',
    'rv.student': 'Remote View — Student',
    'rv.privacyMode': 'Toggle Privacy Mode',
    'rv.lockStudent': 'Lock This Student',

    // ── Teacher: Freeze Modal ───────────────────────────────────
    'freeze.title': 'Custom Action — Freeze Timer',
    'freeze.desc': 'Lock keyboard & mouse and display a fullscreen overlay with countdown. Endpoints automatically unlock when the timer expires.',
    'freeze.duration': 'Duration',
    'freeze.seconds': 'seconds',
    'freeze.message': 'Message (shown on student screen)',
    'freeze.defaultMessage': 'Your screen has been frozen by the teacher.',
    'freeze.messagePlaceholder': 'Custom message...',
    'freeze.target': 'Target',
    'freeze.allStudents': 'All Students',
    'freeze.selectedOnly': 'Selected Only',
    'freeze.cancelFreeze': 'Cancel Active Freeze',
    'freeze.startFreeze': 'Start Freeze',
    'freeze.noStudents': 'No students online',
    'freeze.1min': '1 min',
    'freeze.2min': '2 min',
    'freeze.5min': '5 min',
    'freeze.10min': '10 min',
    'freeze.15min': '15 min',
    'freeze.30min': '30 min',

    // ── Teacher: Room Dropdown ──────────────────────────────────
    'room.desks': '{count} desk',
    'room.desksPlural': '{count} desks',

    // ── Language ────────────────────────────────────────────────
    'lang.label': 'Language',
};
