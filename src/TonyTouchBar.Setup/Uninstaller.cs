using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Win32;

namespace TonyTouchBar.Setup;

internal static class Uninstaller
{
    public static int Run()
    {
        ApplicationConfiguration.Initialize();
        var answer = MessageBox.Show(
            "Remove TonyTouchBar from Windows?\n\nYour settings are preserved so a future reinstall can restore them.",
            "Uninstall TonyTouchBar", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
        if (answer != DialogResult.Yes) return 0;

        try
        {
            foreach (var process in Process.GetProcessesByName("TonyTouchBar"))
                try { process.Kill(true); process.WaitForExit(3000); } catch { }

            using (var run = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", true))
                run?.DeleteValue("TonyTouchBar", false);
            Registry.CurrentUser.DeleteSubKeyTree(@"Software\Microsoft\Windows\CurrentVersion\Uninstall\TonyTouchBar", false);
            RemoveCodexIntegration();

            var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TonyTouchBar");
            foreach (var file in new[] { "TonyTouchBar.exe", "TonyTouchBar.pdb" })
                try { File.Delete(Path.Combine(root, file)); } catch { }

            var self = Environment.ProcessPath;
            if (!string.IsNullOrWhiteSpace(self))
            {
                var escaped = self.Replace("\"", "\"\"");
                Process.Start(new ProcessStartInfo("cmd.exe", $"/d /c timeout /t 2 /nobreak >nul & del /f /q \"{escaped}\"") {
                    UseShellExecute = false, CreateNoWindow = true
                });
            }
            MessageBox.Show("TonyTouchBar was removed. Settings and the one-time USB share were preserved.", "TonyTouchBar", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return 0;
        }
        catch (Exception ex)
        {
            MessageBox.Show("Uninstall could not finish:\n\n" + ex.Message, "TonyTouchBar", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return 1;
        }
    }

    private static void RemoveCodexIntegration()
    {
        var pluginsRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".agents", "plugins");
        var plugin = Path.Combine(pluginsRoot, "tonytouchbar-codex-live");
        try { if (Directory.Exists(plugin)) Directory.Delete(plugin, true); } catch { }
        var marketplace = Path.Combine(pluginsRoot, "marketplace.json");
        try
        {
            if (!File.Exists(marketplace)) return;
            var root = JsonNode.Parse(File.ReadAllText(marketplace))?.AsObject();
            var plugins = root?["plugins"] as JsonArray;
            if (root is null || plugins is null) return;
            for (var i = plugins.Count - 1; i >= 0; i--)
                if (plugins[i]?["name"]?.GetValue<string>() == "tonytouchbar-codex-live") plugins.RemoveAt(i);
            File.WriteAllText(marketplace, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { }
    }
}
