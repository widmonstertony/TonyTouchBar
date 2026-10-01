using System.Diagnostics;

namespace T2TouchBar;

internal static class Program
{
    [STAThread]
    private static async Task Main()
    {
        using var singleInstance = new Mutex(true, "Local\\TonyTouchBar", out var first);
        if (!first) return;

        var logDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TonyTouchBar", "logs");
        Directory.CreateDirectory(logDir);
        var log = Path.Combine(logDir, "host.log");
        void WriteLog(string message)
        {
            var line = $"{DateTimeOffset.Now:O} {message}{Environment.NewLine}";
            try { File.AppendAllText(log, line); } catch { }
            Debug.Write(line);
        }

        try
        {
            using var host = new AppHost(AppConfig.Load(), WriteLog);
            await host.RunAsync();
        }
        catch (Exception ex)
        {
            WriteLog("FATAL " + ex);
        }
    }
}
