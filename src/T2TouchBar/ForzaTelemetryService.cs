using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;

namespace T2TouchBar;

internal sealed record ForzaTelemetryState(
    bool Available,
    float EngineMaxRpm,
    float CurrentEngineRpm,
    float SpeedKmh,
    byte Gear,
    float BestLapSeconds,
    float LastLapSeconds,
    float CurrentLapSeconds,
    ushort LapNumber,
    byte RacePosition,
    DateTimeOffset UpdatedAt)
{
    public static ForzaTelemetryState Empty { get; } = new(false, 0, 0, 0, 11, 0, 0, 0, 0, 0, DateTimeOffset.MinValue);
}

internal sealed class ForzaTelemetryService : IDisposable
{
    private readonly int port;
    private readonly Action<string> log;
    private readonly CancellationTokenSource cancellation = new();
    private readonly Task listener;
    private ForzaTelemetryState latest = ForzaTelemetryState.Empty;
    private int lastPacketSize;

    public ForzaTelemetryService(int port, Action<string> log)
    {
        this.port = port;
        this.log = log;
        listener = Task.Run(() => ListenAsync(cancellation.Token));
    }

    public ForzaTelemetryState State
    {
        get
        {
            var value = Volatile.Read(ref latest);
            return DateTimeOffset.Now - value.UpdatedAt <= TimeSpan.FromSeconds(2)
                ? value with { Available = true }
                : value with { Available = false };
        }
    }

    private async Task ListenAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            try
            {
                using var udp = new UdpClient(new IPEndPoint(IPAddress.Any, port));
                log($"Forza telemetry listening on UDP {port}.");
                while (!token.IsCancellationRequested)
                {
                    var packet = await udp.ReceiveAsync(token);
                    if (TryParse(packet.Buffer, out var state))
                    {
                        Volatile.Write(ref latest, state);
                        if (Interlocked.Exchange(ref lastPacketSize, packet.Buffer.Length) != packet.Buffer.Length)
                            log($"Forza telemetry detected ({packet.Buffer.Length}-byte packet).");
                    }
                }
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { }
            catch (Exception ex)
            {
                log("Forza telemetry listener: " + ex.Message);
                try { await Task.Delay(TimeSpan.FromSeconds(3), token); }
                catch (OperationCanceledException) { }
            }
        }
    }

    internal static bool TryParse(ReadOnlySpan<byte> packet, out ForzaTelemetryState state)
    {
        state = ForzaTelemetryState.Empty;
        if (packet.Length < 311) return false;

        var maxRpm = ReadFloat(packet, 8);
        var currentRpm = ReadFloat(packet, 16);
        if (!IsFinite(maxRpm) || !IsFinite(currentRpm) || maxRpm is < 500 or > 30000 || currentRpm is < -500 or > 40000)
            return false;

        // Motorsport/FM7 use the documented 311/331-byte Car Dash layout.
        // Horizon inserts a 12-byte block between Sled and Dash. FH4/5/6 are
        // normally 323/324 bytes, while newer FH6 builds may append fields.
        var dashShift = packet.Length is 323 or 324 || packet.Length >= 332 ? 12 : 0;
        var speed = ReadFloat(packet, 244 + dashShift);
        var bestLap = ReadFloat(packet, 284 + dashShift);
        var lastLap = ReadFloat(packet, 288 + dashShift);
        var currentLap = ReadFloat(packet, 292 + dashShift);
        var lapOffset = 300 + dashShift;
        var positionOffset = 302 + dashShift;
        var gearOffset = 307 + dashShift;
        if (gearOffset >= packet.Length || positionOffset >= packet.Length || lapOffset + 1 >= packet.Length ||
            !IsFinite(speed) || speed is < -2 or > 250 ||
            !ValidTime(bestLap) || !ValidTime(lastLap) || !ValidTime(currentLap))
            return false;

        state = new ForzaTelemetryState(
            true,
            maxRpm,
            Math.Max(0, currentRpm),
            Math.Max(0, speed * 3.6f),
            packet[gearOffset],
            Math.Max(0, bestLap),
            Math.Max(0, lastLap),
            Math.Max(0, currentLap),
            BinaryPrimitives.ReadUInt16LittleEndian(packet.Slice(lapOffset, 2)),
            packet[positionOffset],
            DateTimeOffset.Now);
        return true;
    }

    // Byte-array entry point keeps the parser easy to validate with synthetic
    // packets without opening a UDP socket or launching a game.
    internal static ForzaTelemetryState? Parse(byte[] packet) => TryParse(packet, out var state) ? state : null;

    private static float ReadFloat(ReadOnlySpan<byte> packet, int offset) =>
        offset + 4 <= packet.Length ? BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(packet.Slice(offset, 4))) : float.NaN;
    private static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    private static bool ValidTime(float value) => IsFinite(value) && value is >= 0 and < 100000;

    public void Dispose()
    {
        cancellation.Cancel();
        try { listener.Wait(TimeSpan.FromSeconds(2)); } catch { }
        cancellation.Dispose();
    }
}
