using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace T2TouchBar;

internal sealed record ForegroundApp(string Executable, string Title, string? Path)
{
    public static readonly ForegroundApp Empty = new("", "", null);
}

internal static class Native
{
    [StructLayout(LayoutKind.Sequential)]
    private struct Input { public uint Type; public InputUnion Data; }

    [StructLayout(LayoutKind.Explicit, Size = 32)]
    private struct InputUnion { [FieldOffset(0)] public KeyboardInput Keyboard; }

    [StructLayout(LayoutKind.Sequential)]
    private struct KeyboardInput
    {
        public ushort VirtualKey;
        public ushort ScanCode;
        public uint Flags;
        public uint Time;
        public nint ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SystemPowerStatus
    {
        public byte AcLineStatus;
        public byte BatteryFlag;
        public byte BatteryLifePercent;
        public byte SystemStatusFlag;
        public uint BatteryLifeTime;
        public uint BatteryFullLifeTime;
    }

    [DllImport("user32.dll")] private static extern nint GetForegroundWindow();
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(nint handle, StringBuilder text, int length);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(nint handle, out uint processId);
    [DllImport("user32.dll", SetLastError = true)] private static extern uint SendInput(uint count, Input[] inputs, int size);
    [DllImport("kernel32.dll")] private static extern bool GetSystemPowerStatus(out SystemPowerStatus status);

    public static ForegroundApp GetForegroundApp()
    {
        try
        {
            var window = GetForegroundWindow();
            if (window == 0) return ForegroundApp.Empty;
            GetWindowThreadProcessId(window, out var pid);
            using var process = Process.GetProcessById((int)pid);
            var title = new StringBuilder(512);
            GetWindowText(window, title, title.Capacity);
            string? path = null;
            try { path = process.MainModule?.FileName; } catch { }
            return new ForegroundApp(process.ProcessName + ".exe", title.ToString(), path);
        }
        catch { return ForegroundApp.Empty; }
    }

    public static (int Percent, bool Charging) GetBattery()
    {
        if (!GetSystemPowerStatus(out var state) || state.BatteryLifePercent == 255) return (-1, false);
        return (state.BatteryLifePercent, state.AcLineStatus == 1);
    }

    public static void TapKey(string key)
    {
        if (!TryVirtualKey(key, out var code)) return;
        var inputs = new[]
        {
            new Input { Type = 1, Data = new InputUnion { Keyboard = new KeyboardInput { VirtualKey = code } } },
            new Input { Type = 1, Data = new InputUnion { Keyboard = new KeyboardInput { VirtualKey = code, Flags = 2 } } }
        };
        SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<Input>());
    }

    private static bool TryVirtualKey(string value, out ushort key)
    {
        value = value.Trim().ToUpperInvariant();
        if (value.Length == 1 && value[0] is >= 'A' and <= 'Z' or >= '0' and <= '9')
        {
            key = value[0];
            return true;
        }
        if (value.StartsWith('F') && int.TryParse(value[1..], out var number) && number is >= 1 and <= 24)
        {
            key = (ushort)(0x70 + number - 1);
            return true;
        }
        key = value switch
        {
            "ESC" or "ESCAPE" => 0x1B,
            "TAB" => 0x09,
            "SPACE" => 0x20,
            "ENTER" => 0x0D,
            "LEFT" => 0x25,
            "UP" => 0x26,
            "RIGHT" => 0x27,
            "DOWN" => 0x28,
            "VOLUME_MUTE" => 0xAD,
            "VOLUME_DOWN" => 0xAE,
            "VOLUME_UP" => 0xAF,
            "MEDIA_NEXT" => 0xB0,
            "MEDIA_PREVIOUS" => 0xB1,
            "MEDIA_PLAY_PAUSE" => 0xB3,
            _ => 0
        };
        return key != 0;
    }
}
