<#
TV-1.1-FIX-3 helper: writes key presses into another process's console input buffer with WriteConsoleInputW, as
the KEY_EVENT_RECORDs conhost produces for those keys (modifier key-down, key-down, key-up, modifier key-up, with
SHIFT_PRESSED / LEFT_CTRL_PRESSED and ENHANCED_KEY for the arrows). Run it as its own process: its console is
detached and replaced by the target's.

Usage: pwsh -NoProfile -File scripts/write-console-keys.ps1 -ProcessId <pid> -Keys Shift+Right,Down,Enter
#>
param([Parameter(Mandatory)] [int] $ProcessId, [Parameter(Mandatory)] [string[]] $Keys, [int] $DelayMs = 120)
$ErrorActionPreference = 'Stop'
Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
using System.Threading;
public static class ConsoleKeys
{
    [StructLayout(LayoutKind.Explicit, CharSet = CharSet.Unicode)]
    struct INPUT_RECORD
    {
        [FieldOffset(0)] public ushort EventType;
        [FieldOffset(4)] public int bKeyDown;
        [FieldOffset(8)] public ushort wRepeatCount;
        [FieldOffset(10)] public ushort wVirtualKeyCode;
        [FieldOffset(12)] public ushort wVirtualScanCode;
        [FieldOffset(14)] public char UnicodeChar;
        [FieldOffset(16)] public uint dwControlKeyState;
    }
    [DllImport("kernel32.dll")] static extern bool FreeConsole();
    [DllImport("kernel32.dll", SetLastError = true)] static extern bool AttachConsole(int pid);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern IntPtr CreateFileW(string name, uint access, uint share, IntPtr sa, uint disposition, uint flags, IntPtr template);
    [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr h);
    [DllImport("kernel32.dll", SetLastError = true)] static extern bool WriteConsoleInputW(IntPtr h, INPUT_RECORD[] records, int count, out int written);

    static INPUT_RECORD Rec(bool down, ushort vk, ushort scan, uint state) =>
        new INPUT_RECORD { EventType = 1, bKeyDown = down ? 1 : 0, wRepeatCount = 1, wVirtualKeyCode = vk, wVirtualScanCode = scan, dwControlKeyState = state };

    // (vk, scan, extended, modifier vk, modifier scan, modifier state)
    public static void Write(int pid, (ushort Vk, ushort Scan, bool Ext, ushort ModVk, ushort ModScan, uint ModState)[] keys, int delayMs)
    {
        FreeConsole();
        if (!AttachConsole(pid)) throw new InvalidOperationException("AttachConsole failed: " + Marshal.GetLastWin32Error());
        IntPtr h = CreateFileW("CONIN$", 0xC0000000, 3, IntPtr.Zero, 3, 0, IntPtr.Zero);
        try
        {
            foreach (var k in keys)
            {
                uint state = k.ModState, keyState = state | (k.Ext ? 0x100u : 0u);
                var list = new System.Collections.Generic.List<INPUT_RECORD>();
                if (k.ModVk != 0) list.Add(Rec(true, k.ModVk, k.ModScan, state));
                list.Add(Rec(true, k.Vk, k.Scan, keyState));
                list.Add(Rec(false, k.Vk, k.Scan, keyState));
                if (k.ModVk != 0) list.Add(Rec(false, k.ModVk, k.ModScan, 0));
                if (!WriteConsoleInputW(h, list.ToArray(), list.Count, out _)) throw new InvalidOperationException("WriteConsoleInputW failed: " + Marshal.GetLastWin32Error());
                Thread.Sleep(delayMs);
            }
        }
        finally { CloseHandle(h); FreeConsole(); }
    }
}
'@
$VK = @{ F5 = (0x74, 0x3F, $false); F10 = (0x79, 0x44, $false); Esc = (0x1B, 0x01, $false); Enter = (0x0D, 0x1C, $false)
         Left = (0x25, 0x4B, $true); Up = (0x26, 0x48, $true); Right = (0x27, 0x4D, $true); Down = (0x28, 0x50, $true) }
$Mod = @{ Shift = (0x10, 0x2A, 0x10); Ctrl = (0x11, 0x1D, 0x08) }   # SHIFT_PRESSED, LEFT_CTRL_PRESSED
$list = foreach ($k in ($Keys -split ',')) {
    $parts = $k -split '\+'; $key = $VK[$parts[-1]]; $m = if ($parts.Count -gt 1) { $Mod[$parts[0]] } else { (0, 0, 0) }
    [ValueTuple[uint16, uint16, bool, uint16, uint16, uint32]]::new($key[0], $key[1], $key[2], $m[0], $m[1], $m[2])
}
[ConsoleKeys]::Write($ProcessId, [ValueTuple[uint16, uint16, bool, uint16, uint16, uint32][]]@($list), $DelayMs)
