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

    // ── Teacher: Toolbar Actions ────────────────────────────────
    'toolbar.lockAll': 'Lock All',
    'toolbar.unlockAll': 'Unlock All',
    'toolbar.freezeAll': 'Freeze All',
    'toolbar.unfreezeAll': 'Unfreeze All',
    'toolbar.message': 'Message',
    'toolbar.filters': 'Filters',
    'toolbar.searchPlaceholder': 'Search students...',
    'toolbar.viewThumb': 'Thumbnail View',
    'toolbar.viewList': 'List View',

    // ── Teacher: Context Menu ───────────────────────────────────
    'ctx.remoteView': 'Live View',
    'ctx.details': 'Details',
    'ctx.lock': 'Lock',
    'ctx.unlock': 'Unlock',
    'ctx.sendMessage': 'Send Message',
    'ctx.webLock': 'Web-Lock',
    'ctx.programLock': 'Program-Lock',
    'ctx.logoff': 'Log Off User',
    'ctx.reboot': 'Reboot Device',
    'ctx.shutdown': 'Shutdown Device',
    'ctx.record': 'Record Screen',
    'ctx.stopRecord': 'Stop Recording',

    // ── Teacher: Tile Indicators ────────────────────────────────
    'ind.locked': 'Locked',
    'ind.disconnected': 'Disconnected',
    'ind.blanked': 'Blanked',
    'ind.webLock': 'Web-Lock',
    'ind.programLock': 'Program-Lock',
    'ind.streaming': 'Streaming',
    'ind.handRaised': 'Hand Raised',
    'ind.recording': 'Recording',

    // ── Teacher: Blocklist / Content Filters ────────────────────
    'blocklist.title': 'Content Filters',
    'blocklist.blockedPrograms': 'Blocked Programs',
    'blocklist.blockedWebsites': 'Blocked Websites',
    'blocklist.progHint': 'Process names without .exe (e.g. "fortnite", "steam", "minecraft")',
    'blocklist.siteHint': 'Domain names matched in browser titles (e.g. "youtube.com", "tiktok.com")',
    'blocklist.addProgPlaceholder': 'Add program name...',
    'blocklist.addSitePlaceholder': 'Add website domain...',
    'blocklist.add': 'Add',
    'blocklist.applyAll': 'Apply to All Students',
    'blocklist.clearAll': 'Clear All',
    'blocklist.noPrograms': 'No programs blocked',
    'blocklist.noWebsites': 'No websites blocked',

    // ── Teacher: Confirm Dialog ─────────────────────────────────
    'confirm.title': 'Confirm Action',
    'confirm.confirm': 'Confirm',
    'confirm.duration': 'Duration',
    'confirm.untilReversed': 'Until manually reversed',
    'confirm.5min': '5 minutes',
    'confirm.10min': '10 minutes',
    'confirm.15min': '15 minutes',
    'confirm.30min': '30 minutes',
    'confirm.1hour': '1 hour',

    // ── Teacher: Announcement ───────────────────────────────────
    'announce.locked': 'All screens locked — Eyes on the teacher!',
    'announce.blanked': 'All screens blanked — Attention mode active',

    // ── Teacher: Toast Messages ─────────────────────────────────
    'toast.lockSent': 'Lock command sent to {name}',
    'toast.unlockSent': 'Unlock command sent to {name}',
    'toast.blankSent': 'Blank screen sent to {name}',
    'toast.unblankSent': 'Screen restored for {name}',
    'toast.lockAllSent': 'Lock All command sent',
    'toast.unlockAllSent': 'Unlock All sent',
    'toast.autoUnlock': 'Auto-unlock: duration expired',
    'toast.logoffSent': 'Log off command sent to {name}',
    'toast.rebootSent': 'Reboot command sent to {name}',
    'toast.shutdownSent': 'Shutdown command sent to {name}',
    'toast.messageSentTo': 'Message sent to {name}',
    'toast.recordStarted': 'Recording started for {name}',
    'toast.recordSaved': 'Recording saved for {name}',
    'toast.recordFailed': 'Failed to start recording',
    'toast.recordNoCanvas': 'No video stream available to record',

    // ── Teacher: Message Dialog ─────────────────────────────────
    'message.title': 'Broadcast Message',
    'message.placeholder': 'Type a message to send to all students...',
    'message.sendAll': 'Send to All',
    'message.titleTo': 'Message — {name}',
    'message.sendTo': 'Send to {name}',

    // ── Teacher: Device Panel ───────────────────────────────────
    'device.title': 'Device Details',
    'device.identity': 'Identity',
    'device.host': 'Host',
    'device.user': 'User',
    'device.ip': 'IP',
    'device.version': 'Version',
    'device.storage': 'Storage',
    'device.disk': 'Disk',
    'device.performance': 'Performance',
    'device.cpu': 'CPU',
    'device.ram': 'RAM',
    'device.openWindows': 'Open Windows',
    'device.noData': 'No data yet',

    // ── Teacher: Confirm Dialog (extra) ─────────────────────────
    'confirm.lockAllTitle': '🔒 Lock All Screens',
    'confirm.lockAllDesc': 'Lock all student screens? Students will not be able to use their computers.',
    'confirm.lockAllBtn': '🔒 Lock All',
    'confirm.logoffDesc': 'Are you sure you want to log off {name}? Unsaved work will be lost.',
    'confirm.rebootDesc': 'Are you sure you want to reboot {name}? Unsaved work will be lost.',
    'confirm.shutdownDesc': 'Are you sure you want to shut down {name}? Unsaved work will be lost.',

    // ── Teacher: Remote View ────────────────────────────────────
    'rv.title': 'Remote View — {name}',
    'rv.subStream': 'Sub-stream 480p',
    'rv.demoStream': 'Demo stream (synthetic)',

    // ── Teacher: About ──────────────────────────────────────────
    'about.subtitle': 'Teacher Controller',
    'about.desc': 'Next-generation classroom management.',
    'about.whatsNew': "What's New",

    // ── Language ────────────────────────────────────────────────
    'lang.label': 'Language',
};
