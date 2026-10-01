using System.Diagnostics;
using System.IO.Compression;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Win32;

namespace TonyTouchBar.Setup;

internal sealed record InstallResult(bool NeedsRestart);

internal sealed class InstallerEngine(Action<string, int> report)
{
    private const string Distribution = "Ubuntu-24.04";
    private const string HardwareId = "05ac:8302";
    private readonly string installRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TonyTouchBar");

    public static void VerifyPayload()
    {
        using var temporary = new TemporaryDirectory();
        ExtractPayload(temporary.Path);
        foreach (var relative in new[] { "app/TonyTouchBar.exe", "app/settings.json", "bridge/t2-touchbar-bridge", "integrations/codex/plugin.json" })
            if (!File.Exists(Path.Combine(temporary.Path, relative.Replace('/', Path.DirectorySeparatorChar))))
                throw new InvalidDataException("Missing installer payload: " + relative);
    }

    public InstallResult Install(bool installCodex)
    {
        if (!Environment.Is64BitOperatingSystem || !OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000))
            throw new InvalidOperationException("TonyTouchBar requires Windows 11 x64.");

        using var temporary = new TemporaryDirectory();
        Report("Unpacking TonyTouchBar…", 5);
        ExtractPayload(temporary.Path);

        Report("Checking Windows Subsystem for Linux…", 12);
        if (!DistroInstalled())
        {
            var exit = Elevated("wsl.exe", $"--install -d {Distribution} --no-launch --web-download");
            if (exit != 0 && !DistroInstalled())
                throw new InvalidOperationException("Windows could not enable WSL. Setup was cancelled or Windows Update needs attention.");
            if (!DistroInstalled())
            {
                RegisterResume();
                return new InstallResult(true);
            }
        }

        Report("Checking USB support…", 24);
        var usbipd = FindUsbipd();
        if (usbipd is null)
        {
            var winget = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft", "WindowsApps", "winget.exe");
            if (!File.Exists(winget)) winget = "winget.exe";
            var exit = Elevated(winget, "install --exact --id dorssel.usbipd-win --silent --accept-package-agreements --accept-source-agreements");
            if (exit != 0) throw new InvalidOperationException("usbipd-win installation was cancelled or failed.");
            usbipd = FindUsbipd() ?? throw new InvalidOperationException("usbipd-win installed but was not found. Sign out and run Setup again.");
        }

        Report("Sharing the Touch Bar with WSL…", 36);
        var list = Run(usbipd, "list", false).Output;
        if (!list.Contains(HardwareId, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Apple Touch Bar USB device 05ac:8302 was not found. This preview currently supports Intel/T2 MacBook Pro models.");
        if (Elevated(usbipd, $"bind --force --hardware-id {HardwareId}") != 0)
            throw new InvalidOperationException("Administrator approval for the Touch Bar device was cancelled or failed.");

        Report("Preparing the Linux bridge…", 48);
        var dependency = Run("wsl.exe", $"-d {Distribution} -u root --exec sh -lc \"ldconfig -p | grep -q libusb-1.0.so.0 || (apt-get update && DEBIAN_FRONTEND=noninteractive apt-get install -y libusb-1.0-0)\"", false);
        if (dependency.ExitCode != 0) throw new InvalidOperationException("Could not install the WSL USB runtime. Check your internet connection and try again.");

        var bridge = Path.Combine(temporary.Path, "bridge", "t2-touchbar-bridge");
        var wslPath = Run("wsl.exe", $"-d {Distribution} --exec wslpath -a \"{bridge}\"", false).Output.Trim();
        if (string.IsNullOrWhiteSpace(wslPath)) throw new InvalidOperationException("Could not translate the bridge path into WSL.");
        var bridgeInstall = Run("wsl.exe", $"-d {Distribution} -u root --exec install -D -m 0755 \"{wslPath}\" /opt/t2touchbar/t2-touchbar-bridge", false);
        if (bridgeInstall.ExitCode != 0) throw new InvalidOperationException("Could not install the Touch Bar bridge in WSL.");

        Report("Installing TonyTouchBar…", 66);
        Directory.CreateDirectory(installRoot);
        StopApp();
        File.Copy(Path.Combine(temporary.Path, "app", "TonyTouchBar.exe"), Path.Combine(installRoot, "TonyTouchBar.exe"), true);
        var installedSetup = Path.Combine(installRoot, "TonyTouchBar-Setup.exe");
        if (!string.Equals(Environment.ProcessPath, installedSetup, StringComparison.OrdinalIgnoreCase))
            File.Copy(Environment.ProcessPath!, installedSetup, true);
        var settingsTarget = Path.Combine(installRoot, "settings.json");
        if (!File.Exists(settingsTarget)) File.Copy(Path.Combine(temporary.Path, "app", "settings.json"), settingsTarget);
        SetDistribution(settingsTarget);

        if (installCodex)
        {
            Report("Adding Codex Live integration…", 80);
            InstallCodexIntegration(Path.Combine(temporary.Path, "integrations", "codex"));
        }

        Report("Enabling automatic startup…", 90);
        using (var key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run"))
            key.SetValue("TonyTouchBar", $"\"{Path.Combine(installRoot, "TonyTouchBar.exe")}\"");
        RegisterUninstall(installedSetup);
        using (var runOnce = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\RunOnce", true))
            runOnce?.DeleteValue("TonyTouchBarSetup", false);

        Process.Start(new ProcessStartInfo(Path.Combine(installRoot, "TonyTouchBar.exe")) { UseShellExecute = true });
        Report("Done", 100);
        return new InstallResult(false);
    }

    private static void ExtractPayload(string target)
    {
        var assembly = Assembly.GetExecutingAssembly();
        using var stream = assembly.GetManifestResourceStream("TonyTouchBar.Setup.payload.zip")
            ?? throw new InvalidOperationException("This Setup build does not contain its installation payload.");
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
        archive.ExtractToDirectory(target, true);
    }

    private static bool DistroInstalled()
    {
        var result = Run("wsl.exe", "--list --quiet", false);
        return result.ExitCode == 0 && result.Output.Replace("\0", "").Split('\n').Any(line => line.Trim().Equals(Distribution, StringComparison.OrdinalIgnoreCase));
    }

    private static string? FindUsbipd()
    {
        var programFiles = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "usbipd-win", "usbipd.exe");
        if (File.Exists(programFiles)) return programFiles;
        var path = Environment.GetEnvironmentVariable("PATH")?.Split(Path.PathSeparator)
            .Select(folder => Path.Combine(folder.Trim(), "usbipd.exe")).FirstOrDefault(File.Exists);
        return path;
    }

    private void RegisterResume()
    {
        using var key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\RunOnce");
        key.SetValue("TonyTouchBarSetup", $"\"{Environment.ProcessPath}\"");
    }

    private static int Elevated(string file, string arguments)
    {
        using var process = Process.Start(new ProcessStartInfo(file, arguments) { UseShellExecute = true, Verb = "runas" });
        process?.WaitForExit();
        return process?.ExitCode ?? -1;
    }

    private static (int ExitCode, string Output) Run(string file, string arguments, bool shell)
    {
        using var process = new Process { StartInfo = new ProcessStartInfo(file, arguments) {
            UseShellExecute = shell, CreateNoWindow = !shell, RedirectStandardOutput = !shell, RedirectStandardError = !shell
        }};
        process.Start();
        var stdout = shell ? Task.FromResult("") : process.StandardOutput.ReadToEndAsync();
        var stderr = shell ? Task.FromResult("") : process.StandardError.ReadToEndAsync();
        process.WaitForExit();
        Task.WaitAll(stdout, stderr);
        return (process.ExitCode, stdout.Result + stderr.Result);
    }

    private static void StopApp()
    {
        foreach (var process in Process.GetProcessesByName("TonyTouchBar"))
            try { process.Kill(true); process.WaitForExit(3000); } catch { }
    }

    private void RegisterUninstall(string installedSetup)
    {
        using var key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Uninstall\TonyTouchBar");
        key.SetValue("DisplayName", "TonyTouchBar");
        key.SetValue("DisplayVersion", Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.2.0");
        key.SetValue("Publisher", "Tony");
        key.SetValue("InstallLocation", installRoot);
        key.SetValue("DisplayIcon", Path.Combine(installRoot, "TonyTouchBar.exe"));
        key.SetValue("UninstallString", $"\"{installedSetup}\" --uninstall");
        key.SetValue("URLInfoAbout", "https://github.com/widmonstertony/TonyTouchBar");
        key.SetValue("NoModify", 1, RegistryValueKind.DWord);
        key.SetValue("NoRepair", 0, RegistryValueKind.DWord);
    }

    private static void SetDistribution(string path)
    {
        var root = JsonNode.Parse(File.ReadAllText(path))?.AsObject() ?? new JsonObject();
        root["wslDistribution"] = Distribution;
        File.WriteAllText(path, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
    }

    private static void InstallCodexIntegration(string source)
    {
        var agentsRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".agents", "plugins");
        var pluginTarget = Path.Combine(agentsRoot, "tonytouchbar-codex-live");
        Directory.CreateDirectory(agentsRoot);
        if (Directory.Exists(pluginTarget)) Directory.Delete(pluginTarget, true);
        CopyDirectory(source, pluginTarget);

        var marketplacePath = Path.Combine(agentsRoot, "marketplace.json");
        JsonObject root;
        try { root = File.Exists(marketplacePath) ? JsonNode.Parse(File.ReadAllText(marketplacePath))?.AsObject() ?? new JsonObject() : new JsonObject(); }
        catch { root = new JsonObject(); }
        root["name"] ??= "personal";
        var plugins = root["plugins"] as JsonArray;
        if (plugins is null) { plugins = new JsonArray(); root["plugins"] = plugins; }
        for (var i = plugins.Count - 1; i >= 0; i--)
            if (plugins[i]?["name"]?.GetValue<string>() == "tonytouchbar-codex-live") plugins.RemoveAt(i);
        plugins.Add(new JsonObject {
            ["name"] = "tonytouchbar-codex-live",
            ["source"] = new JsonObject { ["source"] = "local", ["path"] = "./tonytouchbar-codex-live" },
            ["policy"] = new JsonObject { ["installation"] = "INSTALLED_BY_DEFAULT", ["authentication"] = "ON_INSTALL" },
            ["category"] = "Productivity",
            ["interface"] = new JsonObject { ["displayName"] = "TonyTouchBar Codex Live" }
        });
        File.WriteAllText(marketplacePath, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
    }

    private static void CopyDirectory(string source, string target)
    {
        Directory.CreateDirectory(target);
        foreach (var directory in Directory.GetDirectories(source, "*", SearchOption.AllDirectories))
            Directory.CreateDirectory(directory.Replace(source, target));
        foreach (var file in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
            File.Copy(file, file.Replace(source, target), true);
    }

    private void Report(string message, int progress) => report(message, progress);

    private sealed class TemporaryDirectory : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "TonyTouchBar-Setup-" + Guid.NewGuid().ToString("N"));
        public TemporaryDirectory() => Directory.CreateDirectory(Path);
        public void Dispose() { try { Directory.Delete(Path, true); } catch { } }
    }
}
