using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;

namespace TSharpVision.Diagnostics.Keyboard.Sources;

/// <summary>The Win32 calls of Phase 2. Every caller guards with <see cref="OperatingSystem.IsWindows()"/>.</summary>
[SupportedOSPlatform("windows")]
internal static class WindowsNative
{
    public const uint KeyEventExtendedKey = 0x0001, KeyEventKeyUp = 0x0002, KeyEventScanCode = 0x0008;
    public const int VkNumLock = 0x90, VkShift = 0x10, VkControl = 0x11, VkMenu = 0x12;
    public const uint MapVscToVkEx = 3;
    public const uint GaRootOwner = 3;
    private const uint InputKeyboard = 1;

    [StructLayout(LayoutKind.Sequential)]
    private struct KeyboardInput
    {
        public ushort Vk;
        public ushort Scan;
        public uint Flags;
        public uint Time;
        public IntPtr ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MouseInput
    {
        public int Dx, Dy;
        public uint MouseData, Flags, Time;
        public IntPtr ExtraInfo;
    }

    // The union is as large as its largest member; declaring that member keeps the size right.
    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)] public MouseInput Mouse;
        [FieldOffset(0)] public KeyboardInput Keyboard;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Input
    {
        public uint Type;
        public InputUnion Data;
    }

    [StructLayout(LayoutKind.Explicit, CharSet = CharSet.Unicode)]
    public struct InputRecord
    {
        [FieldOffset(0)] public ushort EventType;
        [FieldOffset(4)] public int KeyDown;
        [FieldOffset(8)] public ushort RepeatCount;
        [FieldOffset(10)] public ushort VirtualKeyCode;
        [FieldOffset(12)] public ushort VirtualScanCode;
        [FieldOffset(14)] public char UnicodeChar;
        [FieldOffset(16)] public uint ControlKeyState;
    }

    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr window);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr window);
    [DllImport("user32.dll")] public static extern IntPtr GetAncestor(IntPtr window, uint flags);
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);
    [DllImport("user32.dll")] public static extern IntPtr GetKeyboardLayout(uint thread);
    [DllImport("user32.dll")] public static extern short GetKeyState(int virtualKey);
    [DllImport("user32.dll")] public static extern uint MapVirtualKeyExW(uint code, uint mapType, IntPtr layout);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassNameW(IntPtr window, StringBuilder name, int count);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern int ToUnicodeEx(uint virtualKey, uint scanCode, byte[] keyState,
        [Out] StringBuilder buffer, int size, uint flags, IntPtr layout);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint count, Input[] inputs, int size);

    [DllImport("kernel32.dll")] public static extern IntPtr GetConsoleWindow();

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern IntPtr CreateFileW(string name, uint access, uint share, IntPtr security,
        uint disposition, uint flags, IntPtr template);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool WriteConsoleInputW(IntPtr input, InputRecord[] records, uint count, out uint written);

    [DllImport("kernel32.dll")] public static extern bool CloseHandle(IntPtr handle);

    public static string ClassName(IntPtr window)
    {
        var name = new StringBuilder(256);
        return GetClassNameW(window, name, name.Capacity) > 0 ? name.ToString() : string.Empty;
    }

    /// <summary>
    /// Injects key transitions by scan code only, so the layout of the thread that receives them
    /// decides the virtual key and the character. Never KEYEVENTF_UNICODE: that would bypass the layout.
    /// </summary>
    public static bool SendScanCodes(IReadOnlyList<(byte Scan, bool Extended, bool Down)> keys)
    {
        var inputs = new Input[keys.Count];
        for (int i = 0; i < keys.Count; i++)
        {
            uint flags = KeyEventScanCode
                | (keys[i].Extended ? KeyEventExtendedKey : 0) | (keys[i].Down ? 0 : KeyEventKeyUp);
            inputs[i].Type = InputKeyboard;
            inputs[i].Data.Keyboard = new KeyboardInput { Scan = keys[i].Scan, Flags = flags };
        }
        return SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<Input>()) == inputs.Length;
    }
}
