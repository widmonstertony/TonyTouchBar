using Microsoft.Win32.SafeHandles;
using System.ComponentModel;
using System.Runtime.InteropServices;

namespace T2TouchBar;

internal sealed class FnKeyService : IDisposable
{
    private const uint FnEventRegistration = 0xb40320e3;
    private readonly SafeFileHandle device;
    private readonly AutoResetEvent signal = new(false);
    private readonly CancellationTokenSource cancellation = new();
    private readonly Task listener;
    private DateTimeOffset lastTransition = DateTimeOffset.MinValue;
    private bool disposed;

    public FnKeyService(Action<string> log)
    {
        device = CreateFile(@"\\.\KeyManager", 0xc0000000, 3, 0, 3, 0, 0);
        if (device.IsInvalid)
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Apple KeyManager could not be opened.");

        if (!DeviceIoControl(device, FnEventRegistration, signal.SafeWaitHandle.DangerousGetHandle(),
                0, 0, 0, out _, 0))
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Apple Fn event could not be registered.");

        log("Apple Fn key listener ready.");
        listener = Task.Run(Listen);
    }

    public bool Pressed { get; private set; }

    private void Listen()
    {
        while (!cancellation.IsCancellationRequested)
        {
            if (!signal.WaitOne(250)) continue;
            if (cancellation.IsCancellationRequested) break;
            var now = DateTimeOffset.UtcNow;
            if (now - lastTransition < TimeSpan.FromMilliseconds(75)) continue;
            lastTransition = now;
            Pressed = !Pressed;
        }
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        cancellation.Cancel();
        signal.Set();
        try { listener.Wait(TimeSpan.FromSeconds(1)); } catch { }
        device.Dispose();
        signal.Dispose();
        cancellation.Dispose();
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFile(string name, uint access, uint share, nint security,
        uint creation, uint flags, nint template);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeviceIoControl(SafeFileHandle device, uint code, nint input, uint inputSize,
        nint output, uint outputSize, out uint returned, nint overlapped);
}
