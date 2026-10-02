namespace T2TouchBar;

internal sealed class AppHost : IDisposable
{
    private readonly AppConfig config;
    private readonly Action<string> log;
    private readonly MediaService media = new();
    private readonly CodexStateService codex = new();
    private readonly ForzaTelemetryService forza;
    private readonly TouchBarRenderer renderer = new();
    private readonly CancellationTokenSource cancellation = new();

    public AppHost(AppConfig config, Action<string> log)
    {
        this.config = config;
        this.log = log;
        forza = new ForzaTelemetryService(config.ForzaTelemetryPort, log);
        AppDomain.CurrentDomain.ProcessExit += (_, _) => cancellation.Cancel();
        Console.CancelKeyPress += (_, args) => { args.Cancel = true; cancellation.Cancel(); };
    }

    public async Task RunAsync()
    {
        while (!cancellation.IsCancellationRequested)
        {
            try
            {
                await using var bridge = new BridgeClient(config, log);
                bridge.Touched += renderer.HandleTouch;
                await bridge.ConnectAsync(cancellation.Token);
                renderer.SetDimensions(bridge.Width, bridge.Height);
                await RenderLoopAsync(bridge, cancellation.Token);
            }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
            catch (Exception ex)
            {
                log("Connection failed: " + ex.Message);
                try { await Task.Delay(TimeSpan.FromSeconds(3), cancellation.Token); } catch (OperationCanceledException) { }
            }
        }
    }

    private async Task RenderLoopAsync(BridgeClient bridge, CancellationToken token)
    {
        var nextMediaRefresh = DateTimeOffset.MinValue;
        while (!token.IsCancellationRequested)
        {
            var app = Native.GetForegroundApp();
            var profile = config.ResolveProfile(app);
            if (DateTimeOffset.Now >= nextMediaRefresh)
            {
                await media.RefreshAsync();
                nextMediaRefresh = DateTimeOffset.Now.AddMilliseconds(500);
            }
            var frame = renderer.Render(app, profile, media.State, media, codex.Read(), forza.State, config.ForzaTelemetryPort);
            await bridge.SendFrameAsync(frame, token);
            var refresh = profile?.UsesForzaDashboard is true ? 100 : Math.Clamp(config.RefreshMilliseconds, 100, 2000);
            await Task.Delay(refresh, token);
        }
    }

    public void Dispose()
    {
        cancellation.Cancel();
        cancellation.Dispose();
        media.Dispose();
        forza.Dispose();
        renderer.Dispose();
    }
}
