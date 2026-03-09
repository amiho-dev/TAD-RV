import sys
content = open("tools/Setup/Program.cs", "r").read()

old_code = """    // Check basic hardware requirement (e.g., at least 2 logical processors)
    if (Environment.ProcessorCount < 2) isTooOld = true;

    if (isTooOld)
    {
        if (!silent)
        {
            System.Windows.Forms.Application.EnableVisualStyles();"""

new_code = """    // Check basic hardware requirement (e.g., at least 2 logical processors)
    if (Environment.ProcessorCount < 2) isTooOld = true;

    if (isTooOld)
    {
        if (!silent)
        {
            System.Windows.Forms.Application.EnableVisualStyles();
            System.Windows.Forms.MessageBox.Show(":( Oh no! Your PC is not supported by TAD-RV anymore!\\n\\nWe are working on a LTS release for older devices.", "Unsupported Hardware", 
                System.Windows.Forms.MessageBoxButtons.OK, System.Windows.Forms.MessageBoxIcon.Error);
        }
        else
        {
            Err(":( Oh no! Your PC is not supported by TAD-RV anymore!");
        }
        return 1;
    }

    bool hasPendingUpdates = false;
    try
    {
        Type? updateSessionType = Type.GetTypeFromProgID("Microsoft.Update.Session");
        if (updateSessionType != null)
        {
            dynamic session = Activator.CreateInstance(updateSessionType)!;
            dynamic searcher = session.CreateUpdateSearcher();
            searcher.Online = false; // Fast local check
            dynamic result = searcher.Search("IsInstalled=0 and IsHidden=0");
            if (result.Updates.Count > 0)
                hasPendingUpdates = true;
        }
    }
    catch { /* Ignore COM errors */ }

    if (hasPendingUpdates)
    {
        if (!silent)
        {
            System.Windows.Forms.Application.EnableVisualStyles();
            System.Windows.Forms.MessageBox.Show("Your Windows Update list is not empty.\\nPlease install pending updates and reboot before installing TAD-RV.", "Pending Updates", 
                System.Windows.Forms.MessageBoxButtons.OK, System.Windows.Forms.MessageBoxIcon.Warning);
        }
        else Err("Pending Windows Updates detected.");
        return 1;
    }

    // Keep syntax checking ok for replaced block:
    if (false)
    {
        if (!silent)
        {
            System.Windows.Forms.Application.EnableVisualStyles();"""

content = content.replace(old_code, new_code)
open("tools/Setup/Program.cs", "w").write(content)
