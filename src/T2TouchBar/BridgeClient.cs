using System.Buffers.Binary;
using System.Diagnostics;

namespace T2TouchBar;

internal enum TouchKind { Down, Move, Up }
internal sealed record TouchEvent(int X, int Y, TouchKind Kind);

internal sealed class BridgeClient : IAsyncDisposable
{
    private readonly AppConfig config;
    private readonly Action<string> log;
    private readonly SemaphoreSlim writeGate = new(1, 1);
    private Process? process;
    private Stream? input;
    private Stream? output;
    private CancellationTokenSource? readerCancellation;
    private TaskCompletionSource<(int Width, int Height)>? readySignal;
    public int Width { get; private set; } = 2008;
    public int Height { get; private set; } = 60;
    public event Action<TouchEvent>? Touched;

    public BridgeClient(AppConfig config, Action<string> log) { this.config = config; this.log = log; }

    public async Task ConnectAsync(CancellationToken cancellationToken)
    {
        await AttachUsbAsync(cancellationToken);
        var start = new ProcessStartInfo("wsl.exe")
        {
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        start.ArgumentList.Add("-d");
        start.ArgumentList.Add(config.WslDistribution);
        start.ArgumentList.Add("--exec");
        start.ArgumentList.Add(config.BridgePath);
        process = Process.Start(start) ?? throw new InvalidOperationException("Could not start WSL bridge.");
        input = process.StandardInput.BaseStream;
        output = process.StandardOutput.BaseStream;
        readySignal = new(TaskCreationOptions.RunContinuationsAsynchronously);
        readerCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _ = Task.Run(() => ReadEventsAsync(process.StandardError, readerCancellation.Token), readerCancellation.Token);
        var ready = await readySignal.Task.WaitAsync(TimeSpan.FromSeconds(10), cancellationToken);
        Width = ready.Width;
        Height = ready.Height;
        if (Width <= 0 || Height <= 0 || Width > 4096 || Height > 256) throw new IOException("Unsafe display dimensions.");
        log($"Bridge connected ({Width}x{Height}).");
    }

    public async Task SendFrameAsync(byte[] rgba, CancellationToken cancellationToken)
    {
        if (input is null || output is null) throw new IOException("Bridge disconnected.");
        if (rgba.Length != checked(Width * Height * 4)) throw new ArgumentException("Unexpected frame size.");
        await writeGate.WaitAsync(cancellationToken);
        try
        {
            var header = new byte[8];
            "FRM1"u8.CopyTo(header);
            BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(4), rgba.Length);
            await input.WriteAsync(header, cancellationToken);
            await input.WriteAsync(rgba, cancellationToken);
            await input.FlushAsync(cancellationToken);
            var ack = new byte[12];
            await ReadExactlyAsync(output, ack, cancellationToken);
            if (!ack.AsSpan(0, 4).SequenceEqual("ACK1"u8)) throw new IOException("Invalid bridge acknowledgement.");
        }
        finally { writeGate.Release(); }
    }

    private async Task AttachUsbAsync(CancellationToken cancellationToken)
    {
        var usbipd = ResolveUsbipd();
        if (await IsUsbAttachedAsync(usbipd, cancellationToken) && !await BridgeStillExistsAsync(cancellationToken))
        {
            log("Touch Bar USB is already attached; reusing it.");
            return;
        }

        await ResetStaleBridgeAsync(usbipd, cancellationToken);

        _ = Process.Start(CreateProcess("wsl.exe", ["-d", config.WslDistribution, "--exec", "sleep", "30"]));
        if (!await WaitForWslReadyAsync(cancellationToken))
            throw new IOException("WSL did not become ready for USB attachment.");

        var attach = await RunAsync(usbipd, ["attach", "--wsl", "--hardware-id", "05ac:8302"], TimeSpan.FromSeconds(20), cancellationToken);
        if (attach.TimedOut)
        {
            await TerminateBridgeDistroAsync("USB attach timeout", cancellationToken);
            throw new TimeoutException("usbipd attach timed out.");
        }
        if (attach.ExitCode != 0 && !attach.Output.Contains("already attached", StringComparison.OrdinalIgnoreCase))
            throw new IOException("usbipd attach failed: " + attach.Output.Trim());
        await Task.Delay(1500, cancellationToken);
        if (!await IsUsbAttachedAsync(usbipd, cancellationToken))
            throw new IOException("Touch Bar USB device is not attached to WSL yet.");
    }

    private async Task ResetStaleBridgeAsync(string usbipd, CancellationToken cancellationToken)
    {
        await RunAsync(usbipd, ["detach", "--hardware-id", "05ac:8302"], TimeSpan.FromSeconds(6), cancellationToken);
        var bridgePattern = $"^{config.BridgePath}$";
        var cleanup = await RunAsync("wsl.exe", ["-d", config.WslDistribution, "-u", "root", "--exec", "pkill", "-9", "-f", bridgePattern], TimeSpan.FromSeconds(6), cancellationToken);
        if (cleanup.TimedOut || await BridgeStillExistsAsync(cancellationToken))
            await TerminateBridgeDistroAsync(cleanup.TimedOut ? "bridge cleanup timeout" : "stale bridge remains", cancellationToken);
        await Task.Delay(750, cancellationToken);
    }

    private async Task<bool> BridgeStillExistsAsync(CancellationToken cancellationToken)
    {
        try
        {
            var bridgePattern = $"^{config.BridgePath}$";
            var probe = await RunAsync("wsl.exe", ["-d", config.WslDistribution, "-u", "root", "--exec", "pgrep", "-f", bridgePattern], TimeSpan.FromSeconds(5), cancellationToken);
            return probe.TimedOut || probe.ExitCode == 0;
        }
        catch { return true; }
    }

    private async Task TerminateBridgeDistroAsync(string reason, CancellationToken cancellationToken)
    {
        log($"Restarting {config.WslDistribution} to recover Touch Bar ({reason}).");
        await RunAsync("wsl.exe", ["--terminate", config.WslDistribution], TimeSpan.FromSeconds(12), cancellationToken);
    }

    private async Task<bool> WaitForWslReadyAsync(CancellationToken cancellationToken)
    {
        var deadline = DateTimeOffset.Now.AddSeconds(18);
        while (DateTimeOffset.Now < deadline)
        {
            var probe = await RunAsync("wsl.exe", ["-d", config.WslDistribution, "--exec", "sh", "-lc", "echo READY"], TimeSpan.FromSeconds(3), cancellationToken);
            if (!probe.TimedOut && probe.ExitCode == 0 && probe.Output.Contains("READY", StringComparison.Ordinal)) return true;
            await Task.Delay(500, cancellationToken);
        }
        return false;
    }

    private static async Task<bool> IsUsbAttachedAsync(string usbipd, CancellationToken cancellationToken)
    {
        var result = await RunAsync(usbipd, ["list"], TimeSpan.FromSeconds(5), cancellationToken);
        return !result.TimedOut && result.Output.Split('\n').Any(line =>
            line.Contains("05ac:8302", StringComparison.OrdinalIgnoreCase) &&
            line.Contains("Attached", StringComparison.OrdinalIgnoreCase));
    }

    private static ProcessStartInfo CreateProcess(string file, IEnumerable<string> arguments)
    {
        var start = new ProcessStartInfo(file)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        return start;
    }

    private static async Task<(int ExitCode, string Output, bool TimedOut)> RunAsync(
        string file, IEnumerable<string> arguments, TimeSpan timeout, CancellationToken cancellationToken)
    {
        using var process = Process.Start(CreateProcess(file, arguments)) ?? throw new InvalidOperationException($"Could not start {file}.");
        var stdout = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderr = process.StandardError.ReadToEndAsync(cancellationToken);
        using var timeoutCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCancellation.CancelAfter(timeout);
        try
        {
            await process.WaitForExitAsync(timeoutCancellation.Token);
            return (process.ExitCode, (await stdout) + (await stderr), false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            try { process.Kill(true); } catch { }
            return (-1, "Timed out.", true);
        }
    }

    private static string ResolveUsbipd()
    {
        var standard = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "usbipd-win", "usbipd.exe");
        if (File.Exists(standard)) return standard;
        return "usbipd.exe";
    }

    private async Task ReadEventsAsync(StreamReader reader, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync(cancellationToken);
            if (line is null) break;
            if (line.StartsWith("READY ", StringComparison.Ordinal))
            {
                var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 3 && int.TryParse(parts[1], out var width) && int.TryParse(parts[2], out var height))
                    readySignal?.TrySetResult((width, height));
            }
            else if (line.StartsWith("TOUCH ", StringComparison.Ordinal))
            {
                var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 4 && int.TryParse(parts[1], out var x) && int.TryParse(parts[2], out var y) && Enum.TryParse<TouchKind>(parts[3], true, out var kind))
                    Touched?.Invoke(new TouchEvent(x, y, kind));
            }
            else log("bridge: " + line);
        }
    }

    private static async Task ReadExactlyAsync(Stream stream, byte[] buffer, CancellationToken cancellationToken)
    {
        var offset = 0;
        while (offset < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(offset), cancellationToken);
            if (read == 0) throw new EndOfStreamException("Bridge closed unexpectedly.");
            offset += read;
        }
    }

    public async ValueTask DisposeAsync()
    {
        readerCancellation?.Cancel();
        try { if (input is not null) { await input.WriteAsync("QUIT"u8.ToArray()); await input.FlushAsync(); } } catch { }
        try { if (process is { HasExited: false }) process.Kill(true); } catch { }
        process?.Dispose();
        readerCancellation?.Dispose();
        writeGate.Dispose();
    }
}
