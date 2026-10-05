using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using TSharpVision.Diagnostics.Keyboard.Protocol;
using TSharpVision.Diagnostics.Keyboard.Sources;

namespace TSharpVision.Diagnostics.Keyboard.Target;

/// <summary>Puts native events into the queue the running driver reads.</summary>
internal interface ITargetInjector : IDisposable
{
    void Inject(IReadOnlyList<NativeEvent> events);
    /// <summary>Releases whatever the last injection had to keep alive until the driver consumed it.</summary>
    void Release();
}

/// <summary>
/// Level B for the Console driver: KEY_EVENT_RECORDs written to the target's own console input buffer
/// with WriteConsoleInputW. The driver then reads them through PeekConsoleInput/ReadConsoleInput like
/// any real key. The records already hold the character, so no layout is involved.
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed class Win32ConsoleBufferInjector : ITargetInjector
{
    private const uint GenericRead = 0x80000000, GenericWrite = 0x40000000, ShareReadWrite = 0x3, OpenExisting = 3;
    private const ushort KeyEvent = 0x0001;
    private static readonly IntPtr InvalidHandle = new(-1);

    private readonly IntPtr _input;

    public Win32ConsoleBufferInjector()
    {
        // CONIN$ is the console's input buffer whatever stdin was redirected to.
        _input = WindowsNative.CreateFileW("CONIN$", GenericRead | GenericWrite, ShareReadWrite, IntPtr.Zero,
            OpenExisting, 0, IntPtr.Zero);
        if (_input == InvalidHandle) throw new Win32Exception(Marshal.GetLastWin32Error(), "CONIN$ could not be opened.");
    }

    public void Inject(IReadOnlyList<NativeEvent> events)
    {
        WindowsNative.InputRecord[] records = events.Where(e => e.Kind == NativeEvent.KeyKind).Select(e =>
            new WindowsNative.InputRecord
            {
                EventType = KeyEvent,
                KeyDown = e.Down ? 1 : 0,
                RepeatCount = 1,
                VirtualKeyCode = (ushort)e.Key,
                VirtualScanCode = e.Scan,
                UnicodeChar = (char)e.Char,
                ControlKeyState = e.State,
            }).ToArray();
        if (records.Length == 0) return;
        if (!WindowsNative.WriteConsoleInputW(_input, records, (uint)records.Length, out uint written)
            || written != records.Length)
            throw new Win32Exception(Marshal.GetLastWin32Error(), "WriteConsoleInputW failed.");
    }

    public void Release() { }

    public void Dispose() => WindowsNative.CloseHandle(_input);
}
