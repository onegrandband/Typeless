using AutoTyper.UI;
using AutoTyper.Setup;
using System.Runtime.InteropServices;

namespace AutoTyper;

static class Program
{
    [STAThread]
    static void Main()
    {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        var args = Environment.GetCommandLineArgs();

        for (int i = 1; i < args.Length; i++)
        {
            if (string.Equals(args[i], "--apply-setup", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
            {
                SetupHelper.ApplySetupFromFile(args[i + 1]);
                return;
            }
        }

        for (int i = 1; i < args.Length; i++)
        {
            if (string.Equals(args[i], "--uninstall", StringComparison.OrdinalIgnoreCase))
            {
                SetupHelper.RunUninstall();
                return;
            }
        }

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
