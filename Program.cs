using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Diagnostics;
using System.Security.Principal;

namespace AutoTyper
{
    static class Program
    {
        [STAThread]
        static void Main()
        {
            var args = Environment.GetCommandLineArgs();
            // If launched with apply-setup argument (from elevation), apply the setup
            for (int i = 1; i < args.Length; i++)
            {
                if (string.Equals(args[i], "--apply-setup", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
                {
                    Application.EnableVisualStyles();
                    Application.SetCompatibleTextRenderingDefault(false);
                    SetupHelper.ApplySetupFromFile(args[i + 1]);
                    return;
                }
            }
            // If launched with uninstall argument, run uninstall flow and exit
            for (int i = 1; i < args.Length; i++)
            {
                if (string.Equals(args[i], "--uninstall", StringComparison.OrdinalIgnoreCase))
                {
                    Application.EnableVisualStyles();
                    Application.SetCompatibleTextRenderingDefault(false);
                    SetupHelper.RunUninstall();
                    return;
                }
            }

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            // Ensure setup completed
            var settings = SetupHelper.LoadSettings();
            if (!settings.ContainsKey("setupComplete") || settings["setupComplete"] != "true")
            {
                bool ok = SetupHelper.RunSetupDialog(settings);
                if (!ok)
                {
                    MessageBox.Show("Setup was not completed. The application will now exit.", "Setup required", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
                SetupHelper.SaveSettings(settings);

                if (settings.TryGetValue("createDesktopShortcut", out var create) && create == "true")
                {
                    try { SetupHelper.CreateShortcutAtDesktop("Typeless", "", null); } catch { }
                    try { SetupHelper.CreateShortcutAtDesktop("Uninstall Typeless", "--uninstall", null); } catch { }
                }

                if (settings.TryGetValue("addToStartMenu", out var add) && add == "true")
                {
                    try { SetupHelper.CreateStartMenuShortcut("Typeless", "", null); } catch { }
                    try { SetupHelper.CreateStartMenuShortcut("Uninstall Typeless", "--uninstall", null); } catch { }
                }
            }

            Application.Run(new MainForm());
        }
    }

    public static class SetupHelper
    {
        public static string GetSettingsPath()
        {
            var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Typeless");
            try { Directory.CreateDirectory(dir); } catch { }
            return Path.Combine(dir, "settings.ini");
        }

        public static Dictionary<string,string> LoadSettings()
        {
            var path = GetSettingsPath();
            var d = new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
            if (!File.Exists(path)) return d;
            try
            {
                foreach (var line in File.ReadAllLines(path))
                {
                    if (string.IsNullOrWhiteSpace(line) || !line.Contains("=")) continue;
                    var idx = line.IndexOf('=');
                    var k = line.Substring(0, idx).Trim();
                    var v = line.Substring(idx+1).Trim();
                    d[k] = v;
                }
            }
            catch { }
            return d;
        }

        public static void SaveSettings(Dictionary<string,string> settings)
        {
            var path = GetSettingsPath();
            try
            {
                var lines = new List<string>();
                foreach (var kv in settings) lines.Add(kv.Key + "=" + kv.Value);
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

        public static string WriteTempSetupFile(Dictionary<string,string> settings)
        {
            try
            {
                var tempFile = Path.Combine(Path.GetTempPath(), "TypelessSetup_" + Guid.NewGuid().ToString() + ".ini");
                var lines = new List<string>();
                foreach (var kv in settings) lines.Add(kv.Key + "=" + kv.Value);
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
                var settings = new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
                foreach (var line in File.ReadAllLines(tempFilePath))
                {
                    if (string.IsNullOrWhiteSpace(line) || !line.Contains("=")) continue;
                    var idx = line.IndexOf('=');
                    var k = line.Substring(0, idx).Trim();
                    var v = line.Substring(idx+1).Trim();
                    settings[k] = v;
                }

                var installScope = settings.TryGetValue("installScope", out var scope) ? scope : "justThisUser";
                var createDesktopShortcut = settings.TryGetValue("createDesktopShortcut", out var create) && create == "true";
                var addToStartMenu = settings.TryGetValue("addToStartMenu", out var add) && add == "true";

                if (installScope == "allUsers")
                {
                    // Create shortcuts in common locations
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
                    // Save settings to ProgramData
                    SaveSettingsToMachineWide(settings);
                }
                else if (installScope == "custom")
                {
                    var installUsers = settings.TryGetValue("installUsers", out var users) ? users.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries) : new string[0];
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

        private static void CreateShortcutAtPath(string name, string args, Environment.SpecialFolder specialFolder, string overridePath = null)
        {
            var targetPath = overridePath ?? Environment.GetFolderPath(specialFolder);
            var shortcutPath = Path.Combine(targetPath, name + ".lnk");
            CreateShortcut(Application.ExecutablePath, args, shortcutPath);
        }

        private static void CreateStartMenuShortcutAtPath(string name, string args, Environment.SpecialFolder specialFolder, string overridePath = null)
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
                Type t = Type.GetTypeFromProgID("WScript.Shell");
                if (t != null)
                {
                    object shell = Activator.CreateInstance(t);
                    object shortcut = t.InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, shell, new object[] { shortcutPath });
                    var scType = shortcut.GetType();
                    scType.InvokeMember("TargetPath", BindingFlags.SetProperty, null, shortcut, new object[] { exePath });
                    if (!string.IsNullOrEmpty(args))
                        scType.InvokeMember("Arguments", BindingFlags.SetProperty, null, shortcut, new object[] { args });
                    scType.InvokeMember("Save", BindingFlags.InvokeMethod, null, shortcut, null);
                }
            }
            catch { }
        }

        public static void SaveSettingsToMachineWide(Dictionary<string,string> settings)
        {
            try
            {
                var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Typeless");
                Directory.CreateDirectory(dir);
                var path = Path.Combine(dir, "settings.ini");
                var lines = new List<string>();
                foreach (var kv in settings) lines.Add(kv.Key + "=" + kv.Value);
                File.WriteAllLines(path, lines);
            }
            catch { }
        }

        public static Dictionary<string,string> LoadMachineWideSettings()
        {
            var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Typeless", "settings.ini");
            var d = new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
            if (!File.Exists(path)) return d;
            try
            {
                foreach (var line in File.ReadAllLines(path))
                {
                    if (string.IsNullOrWhiteSpace(line) || !line.Contains("=")) continue;
                    var idx = line.IndexOf('=');
                    var k = line.Substring(0, idx).Trim();
                    var v = line.Substring(idx+1).Trim();
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
                    Arguments = $@"--apply-setup ""{tempFilePath}""",
                    Verb = "runas",
                    UseShellExecute = true
                };
                var proc = Process.Start(psi);
                proc?.WaitForExit();
                return true;
            }
            catch { return false; }
        }

        public static bool RunSetupDialog(Dictionary<string,string> settings)
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
            browse.Click += (s,e) => { using (var d = new FolderBrowserDialog()) { if (d.ShowDialog() == DialogResult.OK) pathBox.Text = d.SelectedPath; } };

            var createShortcut = new CheckBox { Text = "Create desktop shortcut", Location = new Point(12, 84), AutoSize = true };
            var addToStart = new CheckBox { Text = "Add to Start Menu (Show in Recently added)", Location = new Point(12, 108), AutoSize = true };

            var scopeLabel = new Label { Text = "Install scope:", Location = new Point(12, 140), AutoSize = true };
            var scopeBox = new ComboBox { Location = new Point(120, 136), Width = 200, DropDownStyle = ComboBoxStyle.DropDownList };
            var currentUserName = Environment.UserName;
            scopeBox.Items.AddRange(new[] { "Just this user (" + currentUserName + ")", "All users of this device", "Custom (select specific users)" });
            scopeBox.SelectedIndex = 0;

            var selectUsersButton = new Button { Text = "Select Users...", Location = new Point(330, 134), Width = 120, Visible = false };
            var selectedUsers = new List<string>();

            selectUsersButton.Click += (s, e) => {
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

            scopeBox.SelectedIndexChanged += (s, e) => {
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
                var tempFile = WriteTempSetupFile(new Dictionary<string,string>
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

        public static void CreateShortcutAtDesktop(string name, string args, string targetPath = null)
        {
            try
            {
                string exePath = Application.ExecutablePath;
                string desktop = targetPath ?? Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
                string shortcutPath = Path.Combine(desktop, name + ".lnk");

                Type t = Type.GetTypeFromProgID("WScript.Shell");
                if (t != null)
                {
                    object shell = Activator.CreateInstance(t);
                    object shortcut = t.InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, shell, new object[] { shortcutPath });
                    var scType = shortcut.GetType();
                    scType.InvokeMember("TargetPath", BindingFlags.SetProperty, null, shortcut, new object[] { exePath });
                    scType.InvokeMember("Arguments", BindingFlags.SetProperty, null, shortcut, new object[] { args });
                    scType.InvokeMember("WorkingDirectory", BindingFlags.SetProperty, null, shortcut, new object[] { Path.GetDirectoryName(exePath) });
                    scType.InvokeMember("WindowStyle", BindingFlags.SetProperty, null, shortcut, new object[] { 1 });
                    scType.InvokeMember("Description", BindingFlags.SetProperty, null, shortcut, new object[] { "Typeless - Auto typing tool" });
                    scType.InvokeMember("IconLocation", BindingFlags.SetProperty, null, shortcut, new object[] { exePath + ",0" });
                    scType.InvokeMember("Save", BindingFlags.InvokeMethod, null, shortcut, new object[] { });
                }
            }
            catch { }
        }

        public static void CreateStartMenuShortcut(string name, string args, string targetPath = null)
        {
            try
            {
                string exePath = Application.ExecutablePath;
                string startPrograms = targetPath ?? Environment.GetFolderPath(Environment.SpecialFolder.Programs);
                string shortcutPath = Path.Combine(startPrograms, name + ".lnk");

                Type t = Type.GetTypeFromProgID("WScript.Shell");
                if (t != null)
                {
                    object shell = Activator.CreateInstance(t);
                    object shortcut = t.InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, shell, new object[] { shortcutPath });
                    var scType = shortcut.GetType();
                    scType.InvokeMember("TargetPath", BindingFlags.SetProperty, null, shortcut, new object[] { exePath });
                    scType.InvokeMember("Arguments", BindingFlags.SetProperty, null, shortcut, new object[] { args });
                    scType.InvokeMember("WorkingDirectory", BindingFlags.SetProperty, null, shortcut, new object[] { Path.GetDirectoryName(exePath) });
                    scType.InvokeMember("WindowStyle", BindingFlags.SetProperty, null, shortcut, new object[] { 1 });
                    scType.InvokeMember("Description", BindingFlags.SetProperty, null, shortcut, new object[] { "Typeless - Auto typing tool" });
                    scType.InvokeMember("IconLocation", BindingFlags.SetProperty, null, shortcut, new object[] { exePath + ",0" });
                    scType.InvokeMember("Save", BindingFlags.InvokeMethod, null, shortcut, new object[] { });
                }
            }
            catch { }
        }

        public static void RunUninstall()
        {
            try
            {
                DialogResult r = MessageBox.Show("Are you sure you want to uninstall Typeless?", "Uninstall Typeless", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
                if (r != DialogResult.Yes) return;

                MessageBox.Show("Goodbye! Thanks for using Typeless.\nWould you like to leave feedback?", "Uninstall", MessageBoxButtons.OK, MessageBoxIcon.Information);
                var ask = MessageBox.Show("Open feedback page now?", "Feedback", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
                if (ask == DialogResult.Yes)
                {
                    try { Process.Start(new ProcessStartInfo("https://example.com/feedback") { UseShellExecute = true }); } catch { }
                }

                // Check if we have machine-wide settings that require elevation
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

                // Remove per-user settings and shortcuts
                try
                {
                    var settings = GetSettingsPath();
                    if (File.Exists(settings)) File.Delete(settings);
                }
                catch { }

                try
                {
                    var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
                    var s1 = Path.Combine(desktop, "Typeless.lnk");
                    var s2 = Path.Combine(desktop, "Uninstall Typeless.lnk");
                    if (File.Exists(s1)) File.Delete(s1);
                    if (File.Exists(s2)) File.Delete(s2);
                }
                catch { }

                try
                {
                    var startPrograms = Environment.GetFolderPath(Environment.SpecialFolder.Programs);
                    var s1 = Path.Combine(startPrograms, "Typeless.lnk");
                    var s2 = Path.Combine(startPrograms, "Uninstall Typeless.lnk");
                    if (File.Exists(s1)) File.Delete(s1);
                    if (File.Exists(s2)) File.Delete(s2);
                }
                catch { }

                // If elevated and we have machine settings, remove common shortcuts
                if (IsAdministrator() && machineSettings.Count > 0)
                {
                    try
                    {
                        var commonDesktop = Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory);
                        var s1 = Path.Combine(commonDesktop, "Typeless.lnk");
                        var s2 = Path.Combine(commonDesktop, "Uninstall Typeless.lnk");
                        if (File.Exists(s1)) File.Delete(s1);
                        if (File.Exists(s2)) File.Delete(s2);
                    }
                    catch { }

                    try
                    {
                        var commonPrograms = Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms);
                        var s1 = Path.Combine(commonPrograms, "Typeless.lnk");
                        var s2 = Path.Combine(commonPrograms, "Uninstall Typeless.lnk");
                        if (File.Exists(s1)) File.Delete(s1);
                        if (File.Exists(s2)) File.Delete(s2);
                    }
                    catch { }

                    // Remove machine-wide settings
                    try
                    {
                        var machineSettingsPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Typeless", "settings.ini");
                        if (File.Exists(machineSettingsPath)) File.Delete(machineSettingsPath);
                    }
                    catch { }

                    // Also remove shortcuts from custom user folders if recorded
                    if (machineSettings.TryGetValue("installScope", out var scope) && scope == "custom")
                    {
                        if (machineSettings.TryGetValue("installUsers", out var users))
                        {
                            var userList = users.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries);
                            foreach (var username in userList)
                            {
                                var userProfile = Path.Combine(@"C:\Users", username.Trim());
                                try
                                {
                                    var userDesktop = Path.Combine(userProfile, "Desktop");
                                    var s1 = Path.Combine(userDesktop, "Typeless.lnk");
                                    var s2 = Path.Combine(userDesktop, "Uninstall Typeless.lnk");
                                    if (File.Exists(s1)) File.Delete(s1);
                                    if (File.Exists(s2)) File.Delete(s2);
                                }
                                catch { }

                                try
                                {
                                    var userStartMenu = Path.Combine(userProfile, @"AppData\Roaming\Microsoft\Windows\Start Menu\Programs");
                                    var s1 = Path.Combine(userStartMenu, "Typeless.lnk");
                                    var s2 = Path.Combine(userStartMenu, "Uninstall Typeless.lnk");
                                    if (File.Exists(s1)) File.Delete(s1);
                                    if (File.Exists(s2)) File.Delete(s2);
                                }
                                catch { }
                            }
                        }
                    }
                }

                MessageBox.Show("Uninstall completed. You can now delete the executable if desired.", "Uninstalled", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex) { MessageBox.Show("Uninstall failed: " + ex.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error); }
        }
    }

    public static class ScreenCapture
    {
        public static Bitmap CaptureScreen()
        {
            var screen = Screen.PrimaryScreen;
            var bitmap = new Bitmap(screen.Bounds.Width, screen.Bounds.Height);
            using (var g = Graphics.FromImage(bitmap))
            {
                g.CopyFromScreen(screen.Bounds.Location, Point.Empty, screen.Bounds.Size);
            }
            return bitmap;
        }

        public static Bitmap CaptureWindow(IntPtr hwnd)
        {
            try
            {
                Rect rect;
                GetWindowRect(hwnd, out rect);
                int width = rect.Right - rect.Left;
                int height = rect.Bottom - rect.Top;
                var bitmap = new Bitmap(width, height);
                using (var g = Graphics.FromImage(bitmap))
                {
                    g.CopyFromScreen(rect.Left, rect.Top, 0, 0, new Size(width, height));
                }
                return bitmap;
            }
            catch { return null; }
        }

        public static Bitmap CaptureRectangle(Rectangle rect)
        {
            var bitmap = new Bitmap(rect.Width, rect.Height);
            using (var g = Graphics.FromImage(bitmap))
            {
                g.CopyFromScreen(rect.Location, Point.Empty, rect.Size);
            }
            return bitmap;
        }

        [DllImport("user32.dll")]
        private static extern bool GetWindowRect(IntPtr hWnd, out Rect rect);

        [StructLayout(LayoutKind.Sequential)]
        private struct Rect
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }
    }

    public static class OCRHelper
    {
        // Simple OCR using IronOCR or fallback to dummy implementation
        // For .NET Framework 4.7.2, we'll use a simpler approach
        public static async Task<string> RecognizeTextAsync(Bitmap bitmap)
        {
            try
            {
                // Save bitmap to temp file for potential external OCR processing
                string tempImagePath = Path.Combine(Path.GetTempPath(), "ocr_temp_" + Guid.NewGuid().ToString() + ".png");
                bitmap.Save(tempImagePath, System.Drawing.Imaging.ImageFormat.Png);

                // Attempt to use Windows built-in OCR via PowerShell or other method
                // For now, return a message indicating OCR is not available
                // Users would need to install IronOCR or Tesseract.NET NuGet package for full functionality

                string ocrResult = await TryOCRViaExternalTool(tempImagePath);

                try { File.Delete(tempImagePath); } catch { }

                return ocrResult ?? "OCR not available. Please install IronOCR NuGet package for OCR support.";
            }
            catch (Exception ex)
            {
                Debug.WriteLine("OCR Error: " + ex.Message);
                return "OCR Error: " + ex.Message;
            }
        }

        private static async Task<string> TryOCRViaExternalTool(string imagePath)
        {
            try
            {
                // Try using Windows built-in OCR through a script or external tool
                // This is a fallback - proper OCR requires NuGet package
                await Task.Delay(100); // Simulate async work
                return null; // Fall back to message
            }
            catch { return null; }
        }
    }

    public class MainForm : Form
    {
        // P/Invoke for global hotkeys and foreground tracking
        [DllImport("user32.dll")] private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);
        [DllImport("user32.dll")] private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
        private const int WM_HOTKEY = 0x0312;

        // WinEvent hook for tracking last external foreground window
        private delegate void WinEventDelegate(IntPtr hWinEventHook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint dwEventThread, uint dwmsEventTime);
        [DllImport("user32.dll")] private static extern IntPtr SetWinEventHook(uint eventMin, uint eventMax, IntPtr hmodWinEventProc, WinEventDelegate lpfnWinEventProc, uint idProcess, uint idThread, uint dwFlags);
        [DllImport("user32.dll")] private static extern bool UnhookWinEvent(IntPtr hWinEventHook);
        [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hWnd);
        [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);
        [DllImport("user32.dll")] private static extern bool IsWindow(IntPtr hWnd);
        [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        private const uint EVENT_SYSTEM_FOREGROUND = 0x0003;
        private const uint WINEVENT_OUTOFCONTEXT = 0x0000;
        private const int SW_SHOW = 5;

        private IntPtr winEventHook = IntPtr.Zero;
        private WinEventDelegate winEventDelegate;
        private IntPtr lastExternalForeground = IntPtr.Zero;

        // Controls
        private TextBox inputBox;
        private NumericUpDown wpsControl;
        private Button startButton, stopButton;
        private ComboBox modeBox;
        private Label statusLabel;
        private TextBox startHotkeyBox, stopHotkeyBox;
        private Button registerHotkeysButton;
        private CheckBox loopCheck;

        // Screen Reader (OCR) controls
        private Button captureReadButton;
        private ComboBox captureAreaBox;
        private CheckBox autoLoopOCRCheck;
        private Label ocrStatusLabel;

        // Custom title
        private Panel titleBar;
        private Label titleLabel;
        private Button minButton, maxButton, closeButton;
        private bool dragging = false;
        private Point dragStart;
        // title button state
        private Dictionary<Button, bool> titleHover = new Dictionary<Button, bool>();
        private Dictionary<Button, bool> titlePressed = new Dictionary<Button, bool>();

        // Fun features
        private CheckBox partyModeCheck;
        private System.Windows.Forms.Timer partyTimer;
        private Random rng = new Random();
        private NotifyIcon trayIcon;
        private CheckBox emojiCheck;
        private ComboBox emojiBox;
        private Button shuffleButton, previewButton;
        private ComboBox rateUnitBox; // WPS or WPM

        // Typing
        private CancellationTokenSource cts;
        private Task typingTask;

        // Hotkey ids
        private const int HOTKEY_START_ID = 0x1000;
        private const int HOTKEY_STOP_ID = 0x1001;

        public MainForm()
        {
            InitializeComponents();
            this.FormBorderStyle = FormBorderStyle.None;
            this.StartPosition = FormStartPosition.CenterScreen;
            this.MinimumSize = new Size(600, 400);
            this.Text = "AutoTyper";

            // start monitoring foreground changes to remember last external window
            try
            {
                winEventDelegate = new WinEventDelegate(WinEventProc);
                winEventHook = SetWinEventHook(EVENT_SYSTEM_FOREGROUND, EVENT_SYSTEM_FOREGROUND, IntPtr.Zero, winEventDelegate, 0, 0, WINEVENT_OUTOFCONTEXT);
            }
            catch { winEventHook = IntPtr.Zero; }
        }

        private void InitializeComponents()
        {
            // Title bar
            titleBar = new Panel { Dock = DockStyle.Top, Height = 30, BackColor = Color.FromArgb(215, 219, 233) };
            titleBar.MouseDown += TitleBar_MouseDown;
            titleBar.MouseMove += TitleBar_MouseMove;
            titleBar.MouseUp += TitleBar_MouseUp;
            this.Controls.Add(titleBar);

            titleLabel = new Label { Text = "AutoTyper", AutoSize = true, Location = new Point(8, 6), Font = new Font("Segoe UI", 9F) };
            titleBar.Controls.Add(titleLabel);

            minButton = new Button { Size = new Size(30, 24), FlatStyle = FlatStyle.Flat, BackColor = Color.Transparent, TabStop = false };
            maxButton = new Button { Size = new Size(30, 24), FlatStyle = FlatStyle.Flat, BackColor = Color.Transparent, TabStop = false };
            closeButton = new Button { Size = new Size(30, 24), FlatStyle = FlatStyle.Flat, BackColor = Color.Transparent, TabStop = false };

            foreach (var b in new[] { minButton, maxButton, closeButton })
            {
                b.FlatAppearance.BorderSize = 0;
                b.Paint += TitleButton_Paint;
                // hover/press handling to improve visuals
                b.MouseEnter += (s, e) => { titleHover[b] = true; b.Invalidate(); };
                b.MouseLeave += (s, e) => { titleHover[b] = false; titlePressed[b] = false; b.Invalidate(); };
                b.MouseDown += (s, e) => { if (e.Button == MouseButtons.Left) { titlePressed[b] = true; b.Invalidate(); } };
                b.MouseUp += (s, e) => { titlePressed[b] = false; b.Invalidate(); };
                titleBar.Controls.Add(b);
                titleHover[b] = false; titlePressed[b] = false;
            }
            minButton.Tag = "min"; maxButton.Tag = "max"; closeButton.Tag = "close";
            minButton.Click += TitleButtons_Click; maxButton.Click += TitleButtons_Click; closeButton.Click += TitleButtons_Click;

            // Main controls
            inputBox = new TextBox { Multiline = true, ScrollBars = ScrollBars.Vertical, Location = new Point(12, 44), Size = new Size(560, 180) };
            this.Controls.Add(inputBox);

            var wpsLabel = new Label { Text = "Rate:", Location = new Point(12, 236), AutoSize = true };
            this.Controls.Add(wpsLabel);

            wpsControl = new NumericUpDown { Location = new Point(140, 232), Minimum = 0.01M, Maximum = 10000M, DecimalPlaces = 2, Increment = 0.5M, Value = 50.00M, Width = 80 };
            this.Controls.Add(wpsControl);

            rateUnitBox = new ComboBox { Location = new Point(230, 232), Width = 120, DropDownStyle = ComboBoxStyle.DropDownList };
            rateUnitBox.Items.AddRange(new[] { "Words per second", "Words per minute" });
            rateUnitBox.SelectedIndex = 1; // default to WPM to match common expectation
            this.Controls.Add(rateUnitBox);

            var modeLabel = new Label { Text = "Mode:", Location = new Point(12, 266), AutoSize = true };
            this.Controls.Add(modeLabel);
            modeBox = new ComboBox { Location = new Point(140, 262), Width = 120, DropDownStyle = ComboBoxStyle.DropDownList };
            modeBox.Items.AddRange(new[] { "Type (keystrokes)", "Paste (clipboard + Ctrl+V)" });
            modeBox.Items.AddRange(new[] { "Type (keystrokes)", "Paste (clipboard + Ctrl+V)", "Screen Reader (OCR)" });
            modeBox.SelectedIndex = 0;
            modeBox.SelectedIndexChanged += (s, e) => { UpdateUIForMode(); };
            this.Controls.Add(modeBox);

            startButton = new Button { Text = "Start", Location = new Point(12, 300), Width = 80 };
            stopButton = new Button { Text = "Stop", Location = new Point(100, 300), Width = 80, Enabled = false };
            startButton.Click += StartButton_Click; stopButton.Click += StopButton_Click;
            this.Controls.Add(startButton); this.Controls.Add(stopButton);

            statusLabel = new Label { Text = "Status: Idle", Location = new Point(12, 340), AutoSize = true };
            this.Controls.Add(statusLabel);

            var hkLabel1 = new Label { Text = "Start hotkey (e.g. Ctrl+Alt+S):", Location = new Point(280, 232), AutoSize = true };
            this.Controls.Add(hkLabel1);
            startHotkeyBox = new TextBox { Location = new Point(280, 252), Width = 180, Text = "Ctrl+Alt+S" };
            this.Controls.Add(startHotkeyBox);

            var hkLabel2 = new Label { Text = "Stop hotkey (e.g. Ctrl+Alt+X):", Location = new Point(280, 286), AutoSize = true };
            this.Controls.Add(hkLabel2);
            stopHotkeyBox = new TextBox { Location = new Point(280, 306), Width = 180, Text = "Ctrl+Alt+X" };
            this.Controls.Add(stopHotkeyBox);

            registerHotkeysButton = new Button { Text = "Register Hotkeys", Location = new Point(480, 252), Width = 120 };
            registerHotkeysButton.Click += RegisterHotkeysButton_Click;
            this.Controls.Add(registerHotkeysButton);

            loopCheck = new CheckBox { Text = "Loop", Location = new Point(200, 300) };
            this.Controls.Add(loopCheck);

            // Desktop shortcut button
            var createShortcutButton = new Button { Text = "Create Desktop Shortcut", Location = new Point(480, 220), Width = 160 };
            createShortcutButton.Click += CreateShortcutButton_Click;
            this.Controls.Add(createShortcutButton);

            // Fun features UI: party mode, emoji, shuffle/preview
            partyModeCheck = new CheckBox { Text = "Party Mode", Location = new Point(12, 372) };
            partyModeCheck.CheckedChanged += (s, e) => {
                if (partyModeCheck.Checked) { partyTimer.Start(); } else { partyTimer.Stop(); titleBar.BackColor = Color.FromArgb(215, 219, 233); }
            };
            this.Controls.Add(partyModeCheck);

            partyTimer = new System.Windows.Forms.Timer { Interval = 300 };
            partyTimer.Tick += (s, e) => { titleBar.BackColor = Color.FromArgb(200 + rng.Next(56), rng.Next(256), rng.Next(256), rng.Next(256)); };

            emojiCheck = new CheckBox { Text = "Emoji prefix", Location = new Point(120, 372) };
            this.Controls.Add(emojiCheck);
            emojiBox = new ComboBox { Location = new Point(220, 368), Width = 120, DropDownStyle = ComboBoxStyle.DropDownList };
            emojiBox.Items.AddRange(new[] { "😊", "🔥", "✨", "🎉", "🚀" });
            emojiBox.SelectedIndex = 0;
            this.Controls.Add(emojiBox);

            shuffleButton = new Button { Text = "Shuffle", Location = new Point(480, 300), Width = 80 };
            shuffleButton.Click += (s, e) => {
                var parts = inputBox.Text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                var list = new List<string>(parts);
                for (int i = list.Count - 1; i > 0; i--)
                {
                    int j = rng.Next(i + 1);
                    var tmp = list[i]; list[i] = list[j]; list[j] = tmp;
                }
                inputBox.Text = string.Join(Environment.NewLine, list);
            };
            this.Controls.Add(shuffleButton);

            previewButton = new Button { Text = "Preview", Location = new Point(560, 300), Width = 80 };
            previewButton.Click += (s, e) => {
                var txt = inputBox.Text.Trim();
                if (string.IsNullOrEmpty(txt)) MessageBox.Show("No text to preview.", "Preview", MessageBoxButtons.OK, MessageBoxIcon.Information);
                else MessageBox.Show(txt.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)[0], "Preview", MessageBoxButtons.OK, MessageBoxIcon.Information);
            };
            this.Controls.Add(previewButton);

            // OCR/Screen Reader controls
            var captureAreaLabel = new Label { Text = "Capture:", Location = new Point(12, 410), AutoSize = true };
            this.Controls.Add(captureAreaLabel);
            captureAreaBox = new ComboBox { Location = new Point(80, 406), Width = 140, DropDownStyle = ComboBoxStyle.DropDownList };
            captureAreaBox.Items.AddRange(new[] { "Full Screen", "Active Window", "Selection" });
            captureAreaBox.SelectedIndex = 0;
            this.Controls.Add(captureAreaBox);

            captureReadButton = new Button { Text = "Capture & Read", Location = new Point(230, 404), Width = 120 };
            captureReadButton.Click += CaptureReadButton_Click;
            this.Controls.Add(captureReadButton);

            autoLoopOCRCheck = new CheckBox { Text = "Auto-read loop", Location = new Point(360, 410) };
            this.Controls.Add(autoLoopOCRCheck);

            ocrStatusLabel = new Label { Text = "OCR: Ready", Location = new Point(12, 440), AutoSize = true };
            this.Controls.Add(ocrStatusLabel);

            // Notify icon for tray behavior
            trayIcon = new NotifyIcon { Icon = SystemIcons.Application, Visible = false, Text = "AutoTyper" };
            trayIcon.DoubleClick += (s, e) => { this.Show(); this.WindowState = FormWindowState.Normal; trayIcon.Visible = false; };

            this.Resize += MainForm_Resize;
            // ensure the app doesn't steal the last external focus on startup
            try
            {
                var fg = GetForegroundWindow();
                if (fg != IntPtr.Zero)
                {
                    uint pid; GetWindowThreadProcessId(fg, out pid);
                    if (pid != (uint)Process.GetCurrentProcess().Id) lastExternalForeground = fg;
                }
            }
            catch { }
            this.ClientSize = new Size(720, 420);
            LayoutTitleButtons();
        }

        private void CreateShortcutButton_Click(object sender, EventArgs e)
        {
            try
            {
                string exePath = Application.ExecutablePath;
                string desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
                string shortcutPath = Path.Combine(desktop, "Typeless.lnk");

                Type t = Type.GetTypeFromProgID("WScript.Shell");
                if (t != null)
                {
                    object shell = Activator.CreateInstance(t);
                    object shortcut = t.InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, shell, new object[] { shortcutPath });
                    var scType = shortcut.GetType();
                    scType.InvokeMember("TargetPath", BindingFlags.SetProperty, null, shortcut, new object[] { exePath });
                    scType.InvokeMember("WorkingDirectory", BindingFlags.SetProperty, null, shortcut, new object[] { Path.GetDirectoryName(exePath) });
                    scType.InvokeMember("WindowStyle", BindingFlags.SetProperty, null, shortcut, new object[] { 1 });
                    scType.InvokeMember("Description", BindingFlags.SetProperty, null, shortcut, new object[] { "Typeless - Auto typing tool" });
                    scType.InvokeMember("IconLocation", BindingFlags.SetProperty, null, shortcut, new object[] { exePath + ",0" });
                    scType.InvokeMember("Save", BindingFlags.InvokeMethod, null, shortcut, new object[] { });
                    MessageBox.Show("Desktop shortcut created.", "Shortcut", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                else
                {
                    MessageBox.Show("Unable to create shortcut on this system.", "Shortcut", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("Failed to create shortcut: " + ex.Message, "Shortcut", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void UpdateUIForMode()
        {
            bool isOCRMode = (modeBox.SelectedIndex == 2);
            captureAreaBox.Visible = isOCRMode;
            captureReadButton.Visible = isOCRMode;
            autoLoopOCRCheck.Visible = isOCRMode;
            ocrStatusLabel.Visible = isOCRMode;
            inputBox.Visible = !isOCRMode;
        }

        private async void CaptureReadButton_Click(object sender, EventArgs e)
        {
            try
            {
                ocrStatusLabel.Text = "OCR: Capturing...";
                this.Refresh();

                Bitmap screenCapture = null;
                int captureMode = captureAreaBox.SelectedIndex;

                if (captureMode == 0) // Full screen
                {
                    screenCapture = ScreenCapture.CaptureScreen();
                }
                else if (captureMode == 1) // Active window
                {
                    var fg = GetForegroundWindow();
                    if (fg == IntPtr.Zero)
                    {
                        ocrStatusLabel.Text = "OCR: No active window";
                        return;
                    }
                    screenCapture = ScreenCapture.CaptureWindow(fg);
                    if (screenCapture == null)
                    {
                        ocrStatusLabel.Text = "OCR: Failed to capture window";
                        return;
                    }
                }
                else if (captureMode == 2) // Selection (simplified: just use screen for now)
                {
                    // For full selection UI, would need to implement a selection overlay
                    // For now, just capture full screen
                    screenCapture = ScreenCapture.CaptureScreen();
                }

                if (screenCapture == null)
                {
                    ocrStatusLabel.Text = "OCR: Capture failed";
                    return;
                }

                ocrStatusLabel.Text = "OCR: Recognizing text...";
                this.Refresh();

                string recognizedText = await OCRHelper.RecognizeTextAsync(screenCapture);
                screenCapture.Dispose();

                if (string.IsNullOrWhiteSpace(recognizedText))
                {
                    ocrStatusLabel.Text = "OCR: No text found";
                    return;
                }

                inputBox.Text = recognizedText;
                ocrStatusLabel.Text = $"OCR: Found {recognizedText.Split(new[] { ' ', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries).Length} words";

                // Auto-start typing if auto-loop is enabled
                if (autoLoopOCRCheck.Checked)
                {
                    StartTyping();
                }
            }
            catch (Exception ex)
            {
                ocrStatusLabel.Text = "OCR: Error - " + ex.Message.Substring(0, Math.Min(20, ex.Message.Length));
                MessageBox.Show("OCR failed: " + ex.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void WinEventProc(IntPtr hWinEventHook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint dwEventThread, uint dwmsEventTime)
        {
            try
            {
                if (hwnd == IntPtr.Zero) return;
                if (!IsWindow(hwnd)) return;
                uint pid; GetWindowThreadProcessId(hwnd, out pid);
                if (pid != (uint)Process.GetCurrentProcess().Id)
                {
                    lastExternalForeground = hwnd;
                }
            }
            catch { }
        }

        private void LayoutTitleButtons()
        {
            int right = this.ClientSize.Width - 3;
            closeButton.Location = new Point(right - closeButton.Width, 3);
            maxButton.Location = new Point(closeButton.Left - maxButton.Width - 1, 3);
            minButton.Location = new Point(maxButton.Left - minButton.Width - 1, 3);
        }

        private void MainForm_Resize(object sender, EventArgs e)
        {
            LayoutTitleButtons();
            if (this.WindowState == FormWindowState.Minimized)
            {
                if (trayIcon != null) { trayIcon.Visible = true; trayIcon.ShowBalloonTip(1000, "AutoTyper", "Minimized to tray", ToolTipIcon.Info); }
                this.Hide();
            }
            else
            {
                if (trayIcon != null) trayIcon.Visible = false;
                this.Show();
            }
        }

        private void TitleButtons_Click(object sender, EventArgs e)
        {
            var b = sender as Button;
            var tag = (string)b.Tag;
            if (tag == "min") this.WindowState = FormWindowState.Minimized;
            else if (tag == "max") this.WindowState = (this.WindowState == FormWindowState.Maximized) ? FormWindowState.Normal : FormWindowState.Maximized;
            else if (tag == "close") this.Close();
        }

        private void TitleButton_Paint(object sender, PaintEventArgs e)
        {
            var b = (Button)sender;
            e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            e.Graphics.Clear(Color.Transparent);

            bool hover = titleHover.ContainsKey(b) && titleHover[b];
            bool pressed = titlePressed.ContainsKey(b) && titlePressed[b];

            string tag = (string)b.Tag;
            Color accent = Color.FromArgb(80, 180, 90);
            if (tag == "close") accent = Color.FromArgb(220, 70, 70);
            else if (tag == "max") accent = Color.FromArgb(240, 200, 80);
            else if (tag == "min") accent = Color.FromArgb(80, 180, 90);

            if (pressed) accent = ControlPaint.Dark(accent);

            if (hover)
            {
                var hb = Color.FromArgb(160, accent.R, accent.G, accent.B);
                using (var br = new SolidBrush(hb)) e.Graphics.FillRectangle(br, new Rectangle(0, 0, b.Width, b.Height));
            }

            Rectangle r = new Rectangle(8, 8, b.Width - 16, b.Height - 16);
            using (var pen = new Pen(Color.White, 2))
            {
                if (tag == "min") e.Graphics.DrawLine(pen, r.Left, r.Bottom - 1, r.Right, r.Bottom - 1);
                else if (tag == "max") e.Graphics.DrawRectangle(pen, r);
                else { e.Graphics.DrawLine(pen, r.Left, r.Top, r.Right, r.Bottom); e.Graphics.DrawLine(pen, r.Right, r.Top, r.Left, r.Bottom); }
            }
        }

        private void TitleBar_MouseDown(object sender, MouseEventArgs e) { if (e.Button == MouseButtons.Left) { dragging = true; dragStart = e.Location; } }
        private void TitleBar_MouseMove(object sender, MouseEventArgs e) { if (dragging) this.Location = new Point(this.Location.X + e.X - dragStart.X, this.Location.Y + e.Y - dragStart.Y); }
        private void TitleBar_MouseUp(object sender, MouseEventArgs e) { dragging = false; }

        private void RegisterHotkeysButton_Click(object sender, EventArgs e)
        {
            UnregisterAllHotkeys();
            bool ok1 = TryParseHotkey(startHotkeyBox.Text.Trim(), out uint mods1, out uint vk1);
            bool ok2 = TryParseHotkey(stopHotkeyBox.Text.Trim(), out uint mods2, out uint vk2);
            if (ok1) RegisterHotKey(this.Handle, HOTKEY_START_ID, mods1, vk1);
            if (ok2) RegisterHotKey(this.Handle, HOTKEY_STOP_ID, mods2, vk2);
            MessageBox.Show("Hotkeys registered (if valid). Use Start/Stop buttons or hotkeys.", "Hotkeys", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private void UnregisterAllHotkeys()
        {
            UnregisterHotKey(this.Handle, HOTKEY_START_ID);
            UnregisterHotKey(this.Handle, HOTKEY_STOP_ID);
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_HOTKEY)
            {
                int id = m.WParam.ToInt32();
                if (id == HOTKEY_START_ID) Invoke(new Action(() => StartTyping()));
                if (id == HOTKEY_STOP_ID) Invoke(new Action(() => StopTyping()));
            }
            base.WndProc(ref m);
        }

        private bool TryParseHotkey(string text, out uint mods, out uint vk)
        {
            mods = 0; vk = 0;
            if (string.IsNullOrEmpty(text)) return false;
            var parts = text.Split('+');
            string keyPart = parts[parts.Length - 1].Trim();
            for (int i = 0; i < parts.Length - 1; i++)
            {
                var p = parts[i].Trim().ToLower();
                if (p == "ctrl" || p == "control") mods |= 0x0002; // MOD_CONTROL
                else if (p == "alt") mods |= 0x0001; // MOD_ALT
                else if (p == "shift") mods |= 0x0004; // MOD_SHIFT
                else if (p == "win" || p == "windows") mods |= 0x0008; // MOD_WIN
            }
            try { Keys k = (Keys)Enum.Parse(typeof(Keys), keyPart, true); vk = (uint)k; return true; }
            catch { return false; }
        }

        private void StartButton_Click(object sender, EventArgs e) => StartTyping();
        private void StopButton_Click(object sender, EventArgs e) => StopTyping();

        private void StartTyping()
        {
            if (typingTask != null && !typingTask.IsCompleted) return;
            string text = inputBox.Text;
            if (string.IsNullOrWhiteSpace(text)) { MessageBox.Show("Please enter words or phrases to auto-type.", "Input required", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }

            var words = new List<string>();
            var lines = text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var l in lines)
            {
                var parts = l.Split(new[] { '\t', ' ' }, StringSplitOptions.RemoveEmptyEntries);
                foreach (var p in parts) words.Add(p);
            }
            if (words.Count == 0) { MessageBox.Show("No words found after parsing input.", "Input error", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }

            double rateVal = (double)wpsControl.Value;
            // convert to words per second depending on unit
            double wps = rateVal;
            if (rateUnitBox != null && rateUnitBox.SelectedIndex == 1) // WPM selected
            {
                wps = rateVal / 60.0;
            }
            if (wps < 0.01) wps = 0.01;
            if (wps > 1000) wps = 1000;

            int mode = modeBox.SelectedIndex; // 0 type, 1 paste
            int mode = modeBox.SelectedIndex; // 0 type, 1 paste, 2 OCR (use paste mode)
            if (mode == 2) mode = 1; // OCR results are pasted

            // Attempt to restore focus to last external window so typing goes to the last textbox in other apps
            try
            {
                if (lastExternalForeground != IntPtr.Zero && IsWindow(lastExternalForeground))
                {
                    ShowWindow(lastExternalForeground, SW_SHOW);
                    SetForegroundWindow(lastExternalForeground);
                    Thread.Sleep(100);
                }
                else
                {
                    var fg = GetForegroundWindow();
                    if (fg != IntPtr.Zero)
                    {
                        uint pid; GetWindowThreadProcessId(fg, out pid);
                        if (pid != (uint)Process.GetCurrentProcess().Id)
                        {
                            lastExternalForeground = fg;
                        }
                    }
                }
            }
            catch { }

            cts = new CancellationTokenSource();
            typingTask = Task.Run(() => TypingLoop(words, wps, mode, cts.Token));
            startButton.Enabled = false; stopButton.Enabled = true; statusLabel.Text = "Status: Running";
        }

        private void StopTyping()
        {
            cts?.Cancel();
            startButton.Enabled = true; stopButton.Enabled = false; statusLabel.Text = "Status: Stopped";
        }

        private async Task TypingLoop(List<string> words, double wps, int mode, CancellationToken token)
        {
            double intervalMs = 1000.0 / wps;
            var sw = new Stopwatch();
            try
            {
                do
                {
                    foreach (var w in words)
                    {
                        if (token.IsCancellationRequested) return;
                        string toSend = w;
                        if (emojiCheck != null && emojiCheck.Checked && emojiBox != null && emojiBox.SelectedItem != null)
                            toSend = emojiBox.SelectedItem.ToString() + " " + toSend;

                        if (mode == 0)
                        {
                            // Type each character spaced so the whole word (plus space) fits the target words-per-second interval
                            int units = Math.Max(1, toSend.Length + 1); // include trailing space as a unit
                            double perUnitMs = intervalMs / units;
                            foreach (var ch in toSend)
                            {
                                if (token.IsCancellationRequested) return;
                                sw.Restart();
                                SendCharByInput(ch);
                                sw.Stop();
                                double remaining = perUnitMs - sw.Elapsed.TotalMilliseconds;
                                if (remaining > 1)
                                {
                                    try { await Task.Delay(TimeSpan.FromMilliseconds(remaining), token); }
                                    catch (TaskCanceledException) { return; }
                                }
                                else { await Task.Yield(); }
                            }

                            // send trailing space as last unit
                            if (token.IsCancellationRequested) return;
                            sw.Restart();
                            SendKey((ushort)Keys.Space);
                            sw.Stop();
                            double remSpace = perUnitMs - sw.Elapsed.TotalMilliseconds;
                            if (remSpace > 1)
                            {
                                try { await Task.Delay(TimeSpan.FromMilliseconds(remSpace), token); }
                                catch (TaskCanceledException) { return; }
                            }
                            else { await Task.Yield(); }
                        }
                        else
                        {
                            sw.Restart();
                            await PasteStringAsync(toSend);
                            SendKey((ushort)Keys.Space);
                            sw.Stop();

                            double elapsed = sw.Elapsed.TotalMilliseconds;
                            double remaining = intervalMs - elapsed;
                            if (remaining > 1)
                            {
                                try { await Task.Delay(TimeSpan.FromMilliseconds(remaining), token); }
                                catch (TaskCanceledException) { return; }
                            }
                            else { await Task.Yield(); }
                        }
                    }
                } while (loopCheck.Checked && !token.IsCancellationRequested);
            }
            catch (TaskCanceledException) { }
            finally { Invoke(new Action(() => { startButton.Enabled = true; stopButton.Enabled = false; statusLabel.Text = "Status: Idle"; })); }
        }

        // SendInput helpers
        [StructLayout(LayoutKind.Sequential)] private struct INPUT { public uint type; public InputUnion U; public static int Size => Marshal.SizeOf(typeof(INPUT)); }
        [StructLayout(LayoutKind.Explicit)] private struct InputUnion { [FieldOffset(0)] public MOUSEINPUT mi; [FieldOffset(0)] public KEYBDINPUT ki; [FieldOffset(0)] public HARDWAREINPUT hi; }
        [StructLayout(LayoutKind.Sequential)] private struct MOUSEINPUT { public int dx; public int dy; public uint mouseData; public uint dwFlags; public uint time; public IntPtr dwExtraInfo; }
        [StructLayout(LayoutKind.Sequential)] private struct KEYBDINPUT { public ushort wVk; public ushort wScan; public uint dwFlags; public uint time; public IntPtr dwExtraInfo; }
        [StructLayout(LayoutKind.Sequential)] private struct HARDWAREINPUT { public uint uMsg; public ushort wParamL; public ushort wParamH; }
        [DllImport("user32.dll", SetLastError = true)] private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);
        private const uint INPUT_KEYBOARD = 1;
        private const uint KEYEVENTF_UNICODE = 0x0004;
        private const uint KEYEVENTF_KEYUP = 0x0002;

        private void SendStringByInput(string s)
        {
            var inputs = new List<INPUT>();
            foreach (var ch in s)
            {
                inputs.Add(new INPUT { type = INPUT_KEYBOARD, U = new InputUnion { ki = new KEYBDINPUT { wScan = ch, dwFlags = KEYEVENTF_UNICODE } } });
                inputs.Add(new INPUT { type = INPUT_KEYBOARD, U = new InputUnion { ki = new KEYBDINPUT { wScan = ch, dwFlags = KEYEVENTF_UNICODE | KEYEVENTF_KEYUP } } });
            }
            SendInput((uint)inputs.Count, inputs.ToArray(), INPUT.Size);
        }

        private void SendCharByInput(char ch)
        {
            var inputs = new INPUT[2];
            inputs[0] = new INPUT { type = INPUT_KEYBOARD, U = new InputUnion { ki = new KEYBDINPUT { wScan = ch, dwFlags = KEYEVENTF_UNICODE } } };
            inputs[1] = new INPUT { type = INPUT_KEYBOARD, U = new InputUnion { ki = new KEYBDINPUT { wScan = ch, dwFlags = KEYEVENTF_UNICODE | KEYEVENTF_KEYUP } } };
            SendInput(2, inputs, INPUT.Size);
        }

        private void SendKeyDown(ushort vk)
        {
            var input = new INPUT { type = INPUT_KEYBOARD, U = new InputUnion { ki = new KEYBDINPUT { wVk = vk, wScan = 0, dwFlags = 0, time = 0, dwExtraInfo = IntPtr.Zero } } };
            SendInput(1, new[] { input }, INPUT.Size);
        }
        private void SendKeyUp(ushort vk)
        {
            var input = new INPUT { type = INPUT_KEYBOARD, U = new InputUnion { ki = new KEYBDINPUT { wVk = vk, wScan = 0, dwFlags = KEYEVENTF_KEYUP, time = 0, dwExtraInfo = IntPtr.Zero } } };
            SendInput(1, new[] { input }, INPUT.Size);
        }
        private void SendKey(ushort vk) { SendKeyDown(vk); SendKeyUp(vk); }

        private Task PasteStringAsync(string s)
        {
            return Task.Run(() =>
            {
                IDataObject backup = null;
                try { backup = Clipboard.GetDataObject(); } catch { backup = null; }

                bool set = false;
                for (int i = 0; i < 5 && !set; i++)
                {
                    try { Clipboard.SetText(s); set = true; } catch { Thread.Sleep(10); }
                }

                // Send Ctrl+V: hold Ctrl, press V, release V, release Ctrl
                SendKeyDown((ushort)Keys.ControlKey);
                SendKey((ushort)Keys.V);
                SendKeyUp((ushort)Keys.ControlKey);

                Thread.Sleep(1);
                if (backup != null) { try { Clipboard.SetDataObject(backup); } catch { } }
            });
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            UnregisterAllHotkeys();
            cts?.Cancel();
            try { if (winEventHook != IntPtr.Zero) UnhookWinEvent(winEventHook); } catch { }
            base.OnFormClosing(e);
        }
    }
}
