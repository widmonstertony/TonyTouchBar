namespace TonyTouchBar.Setup;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Contains("--uninstall", StringComparer.OrdinalIgnoreCase))
            return Uninstaller.Run();
        if (args.Contains("--verify-payload", StringComparer.OrdinalIgnoreCase))
        {
            try { InstallerEngine.VerifyPayload(); return 0; }
            catch { return 2; }
        }
        ApplicationConfiguration.Initialize();
        Application.Run(new SetupForm());
        return 0;
    }
}
