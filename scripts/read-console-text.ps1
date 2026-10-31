<#
TV-1.1-FIX-3 helper: prints the visible text of another process's console, one line per row.
Run it as its own process (its console is detached and replaced by the target's): its stdout must be a pipe.

Usage: pwsh -NoProfile -File scripts/read-console-text.ps1 -ProcessId <pid>
#>
param([Parameter(Mandatory)] [int] $ProcessId)
$ErrorActionPreference = 'Stop'
Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
using System.Text;
public static class ConsoleText
{
    [StructLayout(LayoutKind.Sequential)] struct COORD { public short X, Y; }
    [StructLayout(LayoutKind.Sequential)] struct SMALL_RECT { public short Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] struct INFO { public COORD Size, Cursor; public ushort Attr; public SMALL_RECT Window; public COORD Max; }
    [DllImport("kernel32.dll")] static extern bool FreeConsole();
    [DllImport("kernel32.dll", SetLastError = true)] static extern bool AttachConsole(int pid);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern IntPtr CreateFileW(string name, uint access, uint share, IntPtr sa, uint disposition, uint flags, IntPtr template);
    [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr h);
    [DllImport("kernel32.dll", SetLastError = true)] static extern bool GetConsoleScreenBufferInfo(IntPtr h, out INFO info);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern bool ReadConsoleOutputCharacterW(IntPtr h, StringBuilder text, int length, COORD at, out int read);
    public static string[] Read(int pid)
    {
        FreeConsole();
        if (!AttachConsole(pid)) throw new InvalidOperationException("AttachConsole failed: " + Marshal.GetLastWin32Error());
        IntPtr h = CreateFileW("CONOUT$", 0xC0000000, 3, IntPtr.Zero, 3, 0, IntPtr.Zero);
        try
        {
            if (!GetConsoleScreenBufferInfo(h, out INFO info)) throw new InvalidOperationException("GetConsoleScreenBufferInfo failed");
            int width = info.Window.Right - info.Window.Left + 1, height = info.Window.Bottom - info.Window.Top + 1;
            var rows = new string[height];
            for (int y = 0; y < height; y++)
            {
                var sb = new StringBuilder(width);
                ReadConsoleOutputCharacterW(h, sb, width, new COORD { X = info.Window.Left, Y = (short)(info.Window.Top + y) }, out int read);
                rows[y] = sb.ToString().Substring(0, Math.Min(read, sb.Length));
            }
            return rows;
        }
        finally { CloseHandle(h); FreeConsole(); }
    }
}
'@
$rows = [ConsoleText]::Read($ProcessId)
[Console]::OutputEncoding = [Text.Encoding]::UTF8
foreach ($row in $rows) { [Console]::Out.WriteLine($row) }
