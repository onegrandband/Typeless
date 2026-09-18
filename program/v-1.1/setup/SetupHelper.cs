using System.Security.Principal;
using System.Reflection;
using System.Diagnostics;

namespace AutoTyper.Setup;

public static class SetupHelper
{
    public static string GetSettingsPath()
    {
        var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Typeless");
        try { Directory.CreateDirectory(dir); } catch { }
        return Path.Combine(dir, "settings.ini");
    }

    public static Dictionary<string, string> LoadSettings()
    {
        var path = GetSettingsPath();
        var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (!File.Exists(path)) return d;
        try
        {
            foreach (var line in File.ReadAllLines(path))
            {
                if (string.IsNullOrWhiteSpace(line) || !line.Contains('=')) continue;
                var idx = line.IndexOf('=');
                var k = line.Substring(0, idx).Trim();
                var v = line.Substring(idx + 1).Trim();
                d[k] = v;
            }
        }
        catch { }
        return d;
    }

    public static void SaveSettings(Dictionary<string, string> settings)
    {
        var path = GetSettingsPath();
        try
        {
            var lines = settings.Select(kv => kv.Key + "=" + kv.Value).ToList();
            File.WriteAllLines(path, lines);
        }
        catch { }
    }

    public static bool IsAdministrator()
    {
        try
        {
            var identity = WindowsIdentity.GetCurrent();
            var principal = new WindowsPrincipal(identity);
            return principal.IsInRole(WindowsBuiltInRole.Administrator);
        }
        catch { return false; }
    }

    public static string? WriteTempSetupFile(Dictionary<string, string> settings)
    {
        try
        {
            var tempFile = Path.Combine(Path.GetTempPath(), "TypelessSetup_" + Guid.NewGuid().ToString() + ".ini");
            var lines = settings.Select(kv => kv.Key + "=" + kv.Value).ToList();
            File.WriteAllLines(tempFile, lines);
            return tempFile;
        }
        catch { return null; }
    }

    public static void ApplySetupFromFile(string tempFilePath)
    {
        if (!File.Exists(tempFilePath)) return;
        try
        {
            var settings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var line in File.ReadAllLines(tempFilePath))
            {
                if (string.IsNullOrWhiteSpace(line) || !line.Contains('=')) continue;
                var idx = line.IndexOf('=');
                var k = line.Substring(0, idx).Trim();
                var v = line.Substring(idx + 1).Trim();
                settings[k] = v;
            }

            var installScope = settings.TryGetValue("installScope", out var scope) ? scope : "justThisUser";
            var createDesktopShortcut = settings.TryGetValue("createDesktopShortcut", out var create) && create == "true";
            var addToStartMenu = settings.TryGetValue("addToStartMenu", out var add) && add == "true";

            if (installScope == "allUsers")
            {
                if (createDesktopShortcut)
                {
                    try { CreateShortcutAtPath("Typeless", "", Environment.SpecialFolder.CommonDesktopDirectory); } catch { }
                    try { CreateShortcutAtPath("Uninstall Typeless", "--uninstall", Environment.SpecialFolder.CommonDesktopDirectory); } catch { }
                }
                if (addToStartMenu)
                {
                    try { CreateStartMenuShortcutAtPath("Typeless", "", Environment.SpecialFolder.CommonPrograms); } catch { }
                    try { CreateStartMenuShortcutAtPath("Uninstall Typeless", "--uninstall", Environment.SpecialFolder.CommonPrograms); } catch { }
                }
                SaveSettingsToMachineWide(settings);
            }
            else if (installScope == "custom")
            {
                var installUsers = settings.TryGetValue("installUsers", out var users) ? users.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries) : Array.Empty<string>();
                CreateShortcutsForUsers(installUsers, createDesktopShortcut, addToStartMenu);
                SaveSettingsToMachineWide(settings);
            }
        }
        finally
        {
            try { File.Delete(tempFilePath); } catch { }
        }
    }

    public static void CreateShortcutsForUsers(IEnumerable<string> usernames, bool createDesktop, bool createStartMenu)
    {
        foreach (var username in usernames)
        {
            var userProfile = Path.Combine(@"C:\Users", username.Trim());
            if (!Directory.Exists(userProfile)) continue;

            if (createDesktop)
            {
                var desktopPath = Path.Combine(userProfile, "Desktop");
                try { CreateShortcutAtPath("Typeless", "", Environment.SpecialFolder.Desktop, desktopPath); } catch { }
                try { CreateShortcutAtPath("Uninstall Typeless", "--uninstall", Environment.SpecialFolder.Desktop, desktopPath); } catch { }
            }

            if (createStartMenu)
            {
                var startMenuPath = Path.Combine(userProfile, @"AppData\Roaming\Microsoft\Windows\Start Menu\Programs");
                try { CreateStartMenuShortcutAtPath("Typeless", "", Environment.SpecialFolder.StartMenu, startMenuPath); } catch { }
                try { CreateStartMenuShortcutAtPath("Uninstall Typeless", "--uninstall", Environment.SpecialFolder.StartMenu, startMenuPath); } catch { }
            }
        }
    }

    private static void CreateShortcutAtPath(string name, string args, Environment.SpecialFolder specialFolder, string? overridePath = null)
    {
        var targetPath = overridePath ?? Environment.GetFolderPath(specialFolder);
        var shortcutPath = Path.Combine(targetPath, name + ".lnk");
        CreateShortcut(Application.ExecutablePath, args, shortcutPath);
    }

    private static void CreateStartMenuShortcutAtPath(string name, string args, Environment.SpecialFolder specialFolder, string? overridePath = null)
    {
        var targetPath = overridePath ?? Environment.GetFolderPath(specialFolder);
        var shortcutPath = Path.Combine(targetPath, name + ".lnk");
        CreateShortcut(Application.ExecutablePath, args, shortcutPath);
    }

    public static void CreateShortcut(string exePath, string args, string shortcutPath)
    {
        if (string.IsNullOrEmpty(shortcutPath)) return;
        try
        {
            Type? t = Type.GetTypeFromProgID("WScript.Shell");
            if (t != null)
            {
                object shell = Activator.CreateInstance(t)!;
                object shortcut = t.InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, shell, new object[] { shortcutPath })!;
                var scType = shortcut.GetType();
                scType.InvokeMember("TargetPath", BindingFlags.SetProperty, null, shortcut, new object[] { exePath });
                if (!string.IsNullOrEmpty(args))
                    scType.InvokeMember("Arguments", BindingFlags.SetProperty, null, shortcut, new object[] { args });
                scType.InvokeMember("Save", BindingFlags.InvokeMethod, null, shortcut, null);
            }
        }
        catch { }
    }

    public static void SaveSettingsToMachineWide(Dictionary<string, string> settings)
    {
        try
        {
            var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Typeless");
            Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, "settings.ini");
            var lines = settings.Select(kv => kv.Key + "=" + kv.Value).ToList();
            File.WriteAllLines(path, lines);
        }
        catch { }
    }

    public static Dictionary<string, string> LoadMachineWideSettings()
    {
        var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Typeless", "settings.ini");
        var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (!File.Exists(path)) return d;
        try
        {
            foreach (var line in File.ReadAllLines(path))
            {
                if (string.IsNullOrWhiteSpace(line) || !line.Contains('=')) continue;
                var idx = line.IndexOf('=');
                var k = line.Substring(0, idx).Trim();
                var v = line.Substring(idx + 1).Trim();
                d[k] = v;
            }
        }
        catch { }
        return d;
    }

    public static List<string> GetLocalUserFolders()
    {
        var users = new List<string>();
        var usersDir = @"C:\Users";
        if (!Directory.Exists(usersDir)) return users;
        try
        {
            var excludedFolders = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Public", "Default", "Default User", "All Users", "desktop.ini", "Shared" };
            foreach (var dir in Directory.GetDirectories(usersDir))
            {
                var folderName = new DirectoryInfo(dir).Name;
                if (!excludedFolders.Contains(folderName))
                    users.Add(folderName);
            }
        }
        catch { }
        return users;
    }

    public static bool RequestElevation(string tempFilePath)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = Application.ExecutablePath,
                Arguments = $"--apply-setup \"{tempFilePath}\"",
                Verb = "runas",
                UseShellExecute = true
            };
            var proc = Process.Start(psi);
            proc?.WaitForExit();
            return true;
        }
        catch { return false; }
    }

    public static bool RunSetupDialog(Dictionary<string, string> settings)
    {
        var f = new Form { Text = "Typeless - Setup", Size = new Size(500, 380), FormBorderStyle = FormBorderStyle.FixedDialog, StartPosition = FormStartPosition.CenterScreen };
        var langLabel = new Label { Text = "Language:", Location = new Point(12, 12), AutoSize = true };
        var langBox = new ComboBox { Location = new Point(120, 8), Width = 200, DropDownStyle = ComboBoxStyle.DropDownList };
        langBox.Items.AddRange(new[] { "English", "Spanish", "French", "German" });
        langBox.SelectedIndex = 0;

        var pathLabel = new Label { Text = "Install location:", Location = new Point(12, 48), AutoSize = true };
        var pathBox = new TextBox { Location = new Point(120, 44), Width = 240 };
        pathBox.Text = AppDomain.CurrentDomain.BaseDirectory;
        var browse = new Button { Text = "Browse...", Location = new Point(370, 42), Width = 80 };
        browse.Click += (s, e) => { using (var d = new FolderBrowserDialog()) { if (d.ShowDialog() == DialogResult.OK) pathBox.Text = d.SelectedPath; } };

        var createShortcut = new CheckBox { Text = "Create desktop shortcut", Location = new Point(12, 84), AutoSize = true };
        var addToStart = new CheckBox { Text = "Add to Start Menu", Location = new Point(12, 108), AutoSize = true, Checked = true };

        var scopeLabel = new Label { Text = "Install scope:", Location = new Point(12, 140), AutoSize = true };
        var scopeBox = new ComboBox { Location = new Point(120, 136), Width = 200, DropDownStyle = ComboBoxStyle.DropDownList };
        var currentUserName = Environment.UserName;
        scopeBox.Items.AddRange(new[] { "Just this user (" + currentUserName + ")", "All users of this device", "Custom (select specific users)" });
        scopeBox.SelectedIndex = 0;

        var selectUsersButton = new Button { Text = "Select Users...", Location = new Point(330, 134), Width = 120, Visible = false };
        var selectedUsers = new List<string>();

        selectUsersButton.Click += (s, e) =>
        {
            var userSelectionForm = new Form { Text = "Select Users", Size = new Size(300, 350), FormBorderStyle = FormBorderStyle.FixedDialog, StartPosition = FormStartPosition.CenterParent };
            var label = new Label { Text = "Select users to install shortcuts for:", Location = new Point(12, 12), AutoSize = true };
            var userList = new CheckedListBox { Location = new Point(12, 40), Size = new Size(260, 260), CheckOnClick = true };
            var okBtn = new Button { Text = "OK", Location = new Point(120, 310), DialogResult = DialogResult.OK };
            var cancelBtn = new Button { Text = "Cancel", Location = new Point(200, 310), DialogResult = DialogResult.Cancel };

            var localUsers = GetLocalUserFolders();
            foreach (var user in localUsers)
            {
                var idx = userList.Items.Add(user);
                if (selectedUsers.Contains(user)) userList.SetItemChecked(idx, true);
            }

            userSelectionForm.Controls.AddRange(new Control[] { label, userList, okBtn, cancelBtn });
            userSelectionForm.AcceptButton = okBtn;
            userSelectionForm.CancelButton = cancelBtn;

            if (userSelectionForm.ShowDialog() == DialogResult.OK)
            {
                selectedUsers.Clear();
                foreach (var item in userList.CheckedItems) selectedUsers.Add((string)item);
            }
        };

        scopeBox.SelectedIndexChanged += (s, e) =>
        {
            selectUsersButton.Visible = (scopeBox.SelectedIndex == 2);
        };

        var finish = new Button { Text = "Finish", Location = new Point(300, 330), DialogResult = DialogResult.OK };
        var cancel = new Button { Text = "Cancel", Location = new Point(380, 330), DialogResult = DialogResult.Cancel };
        f.Controls.AddRange(new Control[] { langLabel, langBox, pathLabel, pathBox, browse, createShortcut, addToStart, scopeLabel, scopeBox, selectUsersButton, finish, cancel });
        f.AcceptButton = finish; f.CancelButton = cancel;

        var res = f.ShowDialog();
        if (res != DialogResult.OK) return false;

        var installScope = scopeBox.SelectedIndex == 1 ? "allUsers" : (scopeBox.SelectedIndex == 2 ? "custom" : "justThisUser");
        var requiresElevation = (installScope == "allUsers") || (installScope == "custom" && selectedUsers.Count > 0);

        if (requiresElevation && !IsAdministrator())
        {
            var tempFile = WriteTempSetupFile(new Dictionary<string, string>
            {
                { "setupComplete", "true" },
                { "language", langBox.SelectedItem?.ToString() ?? "English" },
                { "installPath", pathBox.Text },
                { "createDesktopShortcut", createShortcut.Checked ? "true" : "false" },
                { "addToStartMenu", addToStart.Checked ? "true" : "false" },
                { "installScope", installScope },
                { "installUsers", string.Join(",", selectedUsers) }
            });

            if (tempFile != null)
            {
                var elevResult = MessageBox.Show("This operation requires administrator privileges. Do you want to proceed with elevation?", "Elevation Required", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
                if (elevResult == DialogResult.Yes)
                {
                    if (RequestElevation(tempFile))
                    {
                        Application.Exit();
                        return false;
                    }
                    else
                    {
                        MessageBox.Show("Failed to elevate. Installation will continue for the current user only.", "Elevation Failed", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        installScope = "justThisUser";
                    }
                }
                else
                {
                    try { File.Delete(tempFile); } catch { }
                    installScope = "justThisUser";
                }
            }
        }

        settings["setupComplete"] = "true";
        settings["language"] = langBox.SelectedItem?.ToString() ?? "English";
        settings["installPath"] = pathBox.Text;
        settings["createDesktopShortcut"] = createShortcut.Checked ? "true" : "false";
        settings["addToStartMenu"] = addToStart.Checked ? "true" : "false";
        settings["installScope"] = installScope;
        if (installScope == "custom")
            settings["installUsers"] = string.Join(",", selectedUsers);

        return true;
    }

    public static void CreateShortcutAtDesktop(string name, string args, string? targetPath = null)
    {
        try
        {
            string exePath = Application.ExecutablePath;
            string desktop = targetPath ?? Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            string shortcutPath = Path.Combine(desktop, name + ".lnk");
            CreateShortcutInternal(exePath, args, shortcutPath);
        }
        catch { }
    }

    public static void CreateStartMenuShortcut(string name, string args, string? targetPath = null)
    {
        try
        {
            string exePath = Application.ExecutablePath;
            string startPrograms = targetPath ?? Environment.GetFolderPath(Environment.SpecialFolder.Programs);
            string shortcutPath = Path.Combine(startPrograms, name + ".lnk");
            CreateShortcutInternal(exePath, args, shortcutPath);
        }
        catch { }
    }

    private static void CreateShortcutInternal(string exePath, string args, string shortcutPath)
    {
        Type? t = Type.GetTypeFromProgID("WScript.Shell");
        if (t == null) return;
        object shell = Activator.CreateInstance(t)!;
        object shortcut = t.InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, shell, new object[] { shortcutPath })!;
        var scType = shortcut.GetType();
        scType.InvokeMember("TargetPath", BindingFlags.SetProperty, null, shortcut, new object[] { exePath });
        scType.InvokeMember("Arguments", BindingFlags.SetProperty, null, shortcut, new object[] { args });
        scType.InvokeMember("WorkingDirectory", BindingFlags.SetProperty, null, shortcut, new object[] { Path.GetDirectoryName(exePath) });
        scType.InvokeMember("WindowStyle", BindingFlags.SetProperty, null, shortcut, new object[] { 1 });
        scType.InvokeMember("Description", BindingFlags.SetProperty, null, shortcut, new object[] { "Typeless - Auto typing tool" });
        scType.InvokeMember("IconLocation", BindingFlags.SetProperty, null, shortcut, new object[] { exePath + ",0" });
        scType.InvokeMember("Save", BindingFlags.InvokeMethod, null, shortcut, null);
    }

    public static void RunUninstall()
    {
        try
        {
            DialogResult r = MessageBox.Show("Are you sure you want to uninstall Typeless?", "Uninstall Typeless", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            if (r != DialogResult.Yes) return;

            MessageBox.Show("Goodbye! Thanks for using Typeless.", "Uninstall", MessageBoxButtons.OK, MessageBoxIcon.Information);

            var machineSettings = LoadMachineWideSettings();
            var requiresElevation = machineSettings.Count > 0 && !IsAdministrator();

            if (requiresElevation)
            {
                var elevResult = MessageBox.Show("Machine-wide shortcuts were detected. Admin rights are needed to remove them. Do you want to continue?", "Elevation Required", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
                if (elevResult == DialogResult.Yes)
                {
                    var psi = new ProcessStartInfo
                    {
                        FileName = Application.ExecutablePath,
                        Arguments = "--uninstall",
                        Verb = "runas",
                        UseShellExecute = true
                    };
                    var proc = Process.Start(psi);
                    proc?.WaitForExit();
                    return;
                }
            }

            try
            {
                var settings = GetSettingsPath();
                if (File.Exists(settings)) File.Delete(settings);
            }
            catch { }

            try
            {
                var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
                DeleteIfExists(Path.Combine(desktop, "Typeless.lnk"));
                DeleteIfExists(Path.Combine(desktop, "Uninstall Typeless.lnk"));
            }
            catch { }

            try
            {
                var startPrograms = Environment.GetFolderPath(Environment.SpecialFolder.Programs);
                DeleteIfExists(Path.Combine(startPrograms, "Typeless.lnk"));
                DeleteIfExists(Path.Combine(startPrograms, "Uninstall Typeless.lnk"));
            }
            catch { }

            if (IsAdministrator() && machineSettings.Count > 0)
            {
                try
                {
                    var commonDesktop = Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory);
                    DeleteIfExists(Path.Combine(commonDesktop, "Typeless.lnk"));
                    DeleteIfExists(Path.Combine(commonDesktop, "Uninstall Typeless.lnk"));
                }
                catch { }

                try
                {
                    var commonPrograms = Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms);
                    DeleteIfExists(Path.Combine(commonPrograms, "Typeless.lnk"));
                    DeleteIfExists(Path.Combine(commonPrograms, "Uninstall Typeless.lnk"));
                }
                catch { }

                try
                {
                    var machineSettingsPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Typeless", "settings.ini");
                    if (File.Exists(machineSettingsPath)) File.Delete(machineSettingsPath);
                }
                catch { }

                if (machineSettings.TryGetValue("installScope", out var scope) && scope == "custom" && machineSettings.TryGetValue("installUsers", out var users))
                {
                    var userList = users.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries);
                    foreach (var username in userList)
                    {
                        var userProfile = Path.Combine(@"C:\Users", username.Trim());
                        try
                        {
                            DeleteIfExists(Path.Combine(userProfile, "Desktop", "Typeless.lnk"));
                            DeleteIfExists(Path.Combine(userProfile, "Desktop", "Uninstall Typeless.lnk"));
                        }
                        catch { }

                        try
                        {
                            DeleteIfExists(Path.Combine(userProfile, @"AppData\Roaming\Microsoft\Windows\Start Menu\Programs", "Typeless.lnk"));
                            DeleteIfExists(Path.Combine(userProfile, @"AppData\Roaming\Microsoft\Windows\Start Menu\Programs", "Uninstall Typeless.lnk"));
                        }
                        catch { }
                    }
                }
            }

            MessageBox.Show("Uninstall completed. You can now delete the executable if desired.", "Uninstalled", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex) { MessageBox.Show("Uninstall failed: " + ex.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error); }
    }

    private static void DeleteIfExists(string path)
    {
        if (File.Exists(path)) File.Delete(path);
    }
}
