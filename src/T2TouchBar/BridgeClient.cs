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
        var ready = new byte[12];
        await ReadExactlyAsync(output, ready, cancellationToken);
        if (!ready.AsSpan(0, 4).SequenceEqual("TBR1"u8)) throw new IOException("Invalid bridge handshake.");
        Width = BinaryPrimitives.ReadInt32LittleEndian(ready.AsSpan(4, 4));
        Height = BinaryPrimitives.ReadInt32LittleEndian(ready.AsSpan(8, 4));
        if (Width <= 0 || Height <= 0 || Width > 4096 || Height > 256) throw new IOException("Unsafe display dimensions.");
        readerCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _ = Task.Run(() => ReadEventsAsync(process.StandardError, readerCancellation.Token), readerCancellation.Token);
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
        var start = new ProcessStartInfo(usbipd) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
        start.ArgumentList.Add("attach");
        start.ArgumentList.Add("--wsl");
        start.ArgumentList.Add("--hardware-id");
        start.ArgumentList.Add("05ac:8302");
        using var attach = Process.Start(start) ?? throw new InvalidOperationException("usbipd-win is not installed.");
        await attach.WaitForExitAsync(cancellationToken);
        var diagnostic = (await attach.StandardOutput.ReadToEndAsync(cancellationToken) + await attach.StandardError.ReadToEndAsync(cancellationToken)).Trim();
        if (attach.ExitCode != 0 && !diagnostic.Contains("already attached", StringComparison.OrdinalIgnoreCase))
            log("usbipd attach: " + diagnostic);
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
            if (line.StartsWith("TOUCH ", StringComparison.Ordinal))
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
