import sys
content = open("src/Admin/Web/dashboard.js", "r").read()

old_code = """    student.status = data;
    student.lastSeen = Date.now();"""

new_code = """    const prevNet = student.status && student.status.IsNetworkConnected !== undefined ? student.status.IsNetworkConnected : true;
    if (prevNet === true && data.IsNetworkConnected === false) {
        showToast("⚠️ Network disconnected on " + (data.Username || ip), "warning");
    }

    student.status = data;
    student.lastSeen = Date.now();"""

content = content.replace(old_code, new_code)
open("src/Admin/Web/dashboard.js", "w").write(content)
