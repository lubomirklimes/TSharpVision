<#
TV-1.1-FIX-3: Windows Console native validation of keyboard window resizing (Size/Move, Shift+arrows).

Starts the TVDemo sample on Win32ConsoleDriver (TSHARPVISION_DRIVER=Win32ConsoleDriver overrides the sample's
cfg, which selects SDL) in its own classic conhost window at 80x25. Keys are sent in one of three ways (-InputMode):

  SendInput     synthetic keyboard input through the OS input stack to the foreground window (the conhost window is
                brought to the foreground and checked before every key; the run stops if it loses the foreground).
                Conhost translates it exactly as typed keys, including the modifier state. Not a physical keyboard.
  ConsoleInput  WriteConsoleInputW of the KEY_EVENT_RECORDs conhost produces for those keys (scripts/write-console-keys.ps1),
                straight into the console input buffer that Win32ConsoleDriver reads; bypasses conhost's key translation.
  Post          WM_KEYDOWN/WM_KEYUP posted to the console window (the TSW-8E mechanism). Conhost reads modifiers from
                the keyboard state, which posted messages do not change, so Shift and Ctrl chords arrive unmodified.

Size/Move is started with Ctrl+F5 (status line) except in Post mode, which uses Window > Resize / move.

The welcome window ("TSharpVision Demo", a plain resizable TWindow at desktop (2,2)-(60,18)) is measured after each
step from the console text (scripts/read-console-text.ps1, a separate process attached to that console): the frame
corners around its title give origin and size. Each step is also captured with PrintWindow.

Usage:
  pwsh scripts/capture-tv11-fix3-native.ps1 -Demo samples/TSharpVision.Samples.TVDemo/bin/Debug/net10.0/TSharpVision.Samples.TVDemo.exe -Output artifacts/tv-1.1-fix-3
#>
param(
    [Parameter(Mandatory)] [string] $Demo,
    [Parameter(Mandatory)] [string] $Output,
    [ValidateSet('SendInput', 'ConsoleInput', 'Post')] [string] $InputMode = 'SendInput'
)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
Add-Type -TypeDefinition @'
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
public static class Fix3Native
{
    public delegate bool EnumProc(IntPtr h, IntPtr l);
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] static extern bool EnumWindows(EnumProc f, IntPtr l);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetClassNameW(IntPtr h, StringBuilder s, int n);
    [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll")] static extern bool PostMessageW(IntPtr h, uint m, IntPtr w, IntPtr l);
    [DllImport("user32.dll")] static extern uint MapVirtualKeyW(uint code, uint type);
    [DllImport("user32.dll")] public static extern bool GetClientRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr h, IntPtr hdc, uint flags);
    [DllImport("user32.dll")] public static extern IntPtr SetProcessDpiAwarenessContext(IntPtr value);
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
    [StructLayout(LayoutKind.Explicit, Size = 40)]
    struct INPUT { [FieldOffset(0)] public uint type; [FieldOffset(8)] public ushort wVk; [FieldOffset(10)] public ushort wScan;
                   [FieldOffset(12)] public uint dwFlags; [FieldOffset(16)] public uint time; [FieldOffset(24)] public IntPtr extra; }
    [DllImport("user32.dll", SetLastError = true)] static extern uint SendInput(uint n, INPUT[] inputs, int size);
    static INPUT In(ushort vk, bool up, bool extended) =>
        new INPUT { type = 1, wVk = vk, wScan = (ushort)MapVirtualKeyW(vk, 0), dwFlags = (extended ? 1u : 0u) | (up ? 2u : 0u) };
    public static void Input(IntPtr h, ushort vk, bool extended, ushort modifier)
    {
        if (GetForegroundWindow() != h) throw new InvalidOperationException("The validation console lost the foreground; stopping SendInput.");
        var list = new List<INPUT>();
        if (modifier != 0) list.Add(In(modifier, false, false));
        list.Add(In(vk, false, extended)); list.Add(In(vk, true, extended));
        if (modifier != 0) list.Add(In(modifier, true, false));
        if (SendInput((uint)list.Count, list.ToArray(), Marshal.SizeOf<INPUT>()) != list.Count)
            throw new InvalidOperationException("SendInput failed: " + Marshal.GetLastWin32Error());
    }
    public static List<IntPtr> ConsoleWindows()
    {
        var found = new List<IntPtr>();
        EnumWindows((h, l) => { var s = new StringBuilder(64); GetClassNameW(h, s, 64);
            if (s.ToString() == "ConsoleWindowClass" && IsWindowVisible(h)) found.Add(h); return true; }, IntPtr.Zero);
        return found;
    }
    static void Post(IntPtr h, ushort vk, bool up, bool extended)
    {
        uint l = 1u | (MapVirtualKeyW(vk, 0) << 16) | (extended ? 1u << 24 : 0u) | (up ? (1u << 30) | (1u << 31) : 0u);
        PostMessageW(h, up ? 0x101u : 0x100u, (IntPtr)vk, (IntPtr)(long)l);
    }
    public static void Key(IntPtr h, ushort vk, bool extended, ushort modifier)
    {
        if (modifier != 0) Post(h, modifier, false, false);
        Post(h, vk, false, extended);
        Post(h, vk, true, extended);
        if (modifier != 0) Post(h, modifier, true, false);
    }
}
'@
[void][Fix3Native]::SetProcessDpiAwarenessContext([IntPtr]-4)
$Demo = (Resolve-Path $Demo).Path
New-Item -ItemType Directory -Force $Output | Out-Null
$Output = (Resolve-Path $Output).Path
$reader = Join-Path $PSScriptRoot 'read-console-text.ps1'
$VK = @{ F5 = 0x74; F10 = 0x79; Esc = 0x1B; Enter = 0x0D; Left = 0x25; Up = 0x26; Right = 0x27; Down = 0x28 }
$Extended = 'Left', 'Up', 'Right', 'Down'
$Modifier = @{ Shift = 0x10; Ctrl = 0x11 }

function Send-Keys([string[]] $keys, [int] $settle = 600) {
    if ($InputMode -eq 'ConsoleInput') {
        & pwsh -NoProfile -File (Join-Path $PSScriptRoot 'write-console-keys.ps1') -ProcessId $script:demoPid -Keys ($keys -join ',') -DelayMs 150
        if ($LASTEXITCODE) { throw 'write-console-keys failed' }
    }
    else {
        foreach ($k in $keys) {
            $parts = $k -split '\+'; $name = $parts[-1]; $mod = if ($parts.Count -gt 1) { $Modifier[$parts[0]] } else { 0 }
            if ($InputMode -eq 'SendInput') { [Fix3Native]::Input($script:window, [uint16]$VK[$name], $Extended -contains $name, [uint16]$mod) }
            else { [Fix3Native]::Key($script:window, [uint16]$VK[$name], $Extended -contains $name, [uint16]$mod) }
            Start-Sleep -Milliseconds 150
        }
    }
    Start-Sleep -Milliseconds $settle
}

function Measure-Window {
    $rows = @(& pwsh -NoProfile -File $reader -ProcessId $script:demoPid)
    $title = '[■]'   # the close icon; the title is clipped at small widths and the welcome window is the only window
    for ($y = 0; $y -lt $rows.Count; $y++) {
        $x = $rows[$y].IndexOf($title); if ($x -lt 0) { continue }
        $l = $x; while ($l -ge 0 -and '╔┌╒╓'.IndexOf($rows[$y][$l]) -lt 0) { $l-- }
        $r = $x; while ($r -lt $rows[$y].Length -and '╗┐╕╖'.IndexOf($rows[$y][$r]) -lt 0) { $r++ }
        $b = $y + 1; while ($b -lt $rows.Count -and '╚└╘╙'.IndexOf($rows[$b][$l]) -lt 0) { $b++ }
        if ($l -lt 0 -or $r -ge $rows[$y].Length -or $b -ge $rows.Count) { break }
        # Screen row 0 is the menu bar, so desktop y = screen y - 1.
        return [ordered]@{ x = $l; y = $y - 1; width = $r - $l + 1; height = $b - $y + 1; frame = "$($rows[$y][$l])$($rows[$y][$r])$($rows[$b][$l])"; text = $rows }
    }
    return [ordered]@{ x = $null; y = $null; width = $null; height = $null; frame = $null; text = $rows }
}

$steps = [System.Collections.Generic.List[object]]::new()
function Step([string] $name, [string] $keys, $expect) {
    $m = Measure-Window
    $name = "$InputMode-$name"; $file = "$name.native.png"
    $r = New-Object Fix3Native+RECT; [void][Fix3Native]::GetClientRect($script:window, [ref]$r)
    $bmp = [System.Drawing.Bitmap]::new($r.Right - $r.Left, $r.Bottom - $r.Top)
    $g = [System.Drawing.Graphics]::FromImage($bmp); $hdc = $g.GetHdc()
    $ok = [Fix3Native]::PrintWindow($script:window, $hdc, 3); $g.ReleaseHdc($hdc); $g.Dispose()
    $bmp.Save((Join-Path $Output $file), [System.Drawing.Imaging.ImageFormat]::Png); $bmp.Dispose()
    $m.text | Set-Content -Encoding utf8 (Join-Path $Output "$name.console.txt")
    $actual = "($($m.x),$($m.y)) $($m.width)x$($m.height)"
    $pass = $null -eq $expect -or $actual -eq $expect
    $steps.Add([ordered]@{ step = $name; keys = $keys; geometry = $actual; frameCorners = $m.frame; expected = $expect; pass = $pass;
        screenshot = $file; printWindow = $ok; consoleText = "$name.console.txt" })
    Write-Host ("{0,-34} {1,-44} {2,-16} {3}" -f $name, $keys, $actual, $(if ($null -eq $expect) { '' } elseif ($pass) { 'PASS' } else { "FAIL (expected $expect)" }))
}

$env:TSHARPVISION_DRIVER = 'Win32ConsoleDriver'
$before = [Fix3Native]::ConsoleWindows()
$conhost = Start-Process -FilePath "$env:SystemRoot\System32\conhost.exe" -ArgumentList "cmd.exe /d /c mode con cols=80 lines=25 && `"$Demo`"" -PassThru
try {
    $script:window = [IntPtr]::Zero
    for ($i = 0; $i -lt 100 -and $script:window -eq [IntPtr]::Zero; $i++) {
        Start-Sleep -Milliseconds 100
        $script:window = [Fix3Native]::ConsoleWindows() | Where-Object { $before -notcontains $_ } | Select-Object -First 1
        if ($null -eq $script:window) { $script:window = [IntPtr]::Zero }
    }
    if ($script:window -eq [IntPtr]::Zero) { throw 'The owned conhost window did not appear.' }
    $name = [IO.Path]::GetFileNameWithoutExtension($Demo)
    for ($i = 0; $i -lt 50 -and -not $script:demoPid; $i++) {
        Start-Sleep -Milliseconds 200
        $script:demoPid = (Get-Process -Name $name -ErrorAction SilentlyContinue | Where-Object StartTime -gt $conhost.StartTime | Select-Object -First 1).Id
    }
    if (-not $script:demoPid) { throw 'TVDemo did not start.' }
    Start-Sleep -Seconds 3
    if ($InputMode -eq 'SendInput') {
        [void][Fix3Native]::SetForegroundWindow($script:window); Start-Sleep -Milliseconds 500
        if ([Fix3Native]::GetForegroundWindow() -ne $script:window) { throw 'The validation console could not be brought to the foreground.' }
    }

    # Ctrl+F5 (status line). Post mode: Window > Resize / move from a fresh menu bar (Esc first, F10, Right x5,
    # Down opens Window, Down x3 past Next, Previous, Zoom, Enter); only the first Post session is reliable.
    $sizeMove = if ($InputMode -eq 'Post') { @('Esc', 'F10', 'Right', 'Right', 'Right', 'Right', 'Right', 'Down', 'Down', 'Down', 'Down', 'Enter') }
                else { @('Ctrl+F5') }

    Step '01-initial' 'start' '(2,2) 58x16'
    Send-Keys $sizeMove
    Step '02-size-move-started' ($sizeMove -join ', ') '(2,2) 58x16'
    Send-Keys @('Right', 'Down')
    Step '03-arrows-move' 'Right, Down' '(3,3) 58x16'
    Send-Keys @('Shift+Right', 'Shift+Right', 'Shift+Right', 'Shift+Down', 'Shift+Down')
    Step '04-shift-right-down-grow' 'Shift+Right x3, Shift+Down x2' '(3,3) 61x18'
    Send-Keys @('Shift+Left', 'Shift+Up')
    Step '05-shift-left-up-shrink' 'Shift+Left, Shift+Up' '(3,3) 60x17'
    Send-Keys @('Esc') 900
    Step '06-esc-restores' 'Esc' '(2,2) 58x16'

    Send-Keys $sizeMove
    Send-Keys @('Shift+Right', 'Shift+Right', 'Shift+Down')
    Step '07-second-session-resized' 'Size/Move again, Shift+Right x2, Shift+Down' '(2,2) 60x17'
    Send-Keys @('Enter') 900
    Step '08-enter-accepts' 'Enter' '(2,2) 60x17'

    Send-Keys $sizeMove
    Send-Keys (@('Shift+Left') * 50 + @('Shift+Up') * 15) 300
    Step '09-shrink-to-minimum' 'Shift+Left x50, Shift+Up x15' '(2,2) 16x6'
    Send-Keys (@('Shift+Right') * 80 + @('Shift+Down') * 30) 300
    Step '10-grow-to-desktop-edge' 'Shift+Right x80, Shift+Down x30' '(2,2) 78x21'
    Send-Keys @('Esc') 900
    Step '11-esc-restores-accepted' 'Esc' '(2,2) 60x17'

    Send-Keys $sizeMove
    Send-Keys @('Shift+Right', 'Right', 'Shift+Down', 'Enter') 900
    Step '12-move-and-resize-enter' 'Size/Move, Shift+Right, Right, Shift+Down, Enter' '(3,2) 61x18'
}
finally {
    if ($script:demoPid) { Stop-Process -Id $script:demoPid -ErrorAction SilentlyContinue }
    Stop-Process -Id $conhost.Id -ErrorAction SilentlyContinue
}
[ordered]@{ task = 'TV-1.1-FIX-3'; host = 'classic conhost (conhost.exe) 80x25'; driver = 'Win32ConsoleDriver (TSHARPVISION_DRIVER)';
    application = $Demo; input = $InputMode; physicalKeyboard = $false;
    measurement = 'console text read by a separate process attached to the console; frame corners around the close icon of the window';
    steps = $steps } | ConvertTo-Json -Depth 5 | Set-Content -Encoding utf8 (Join-Path $Output "native-validation-$InputMode.json")
