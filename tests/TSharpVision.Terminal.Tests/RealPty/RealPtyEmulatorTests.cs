using System.Runtime.Versioning;
using TSharpVision.Constants;
using TSharpVision.Terminal.Windows;
using Xunit;

namespace TSharpVision.Terminal.Tests.RealPty;

/// <summary>
/// U-1b on real pseudo-terminals: a shell and the full-screen programs that are installed, driven through the emulator
/// and the key encoder. Each probe is bounded and skips when its program is not there; nothing is installed for it.
/// </summary>
[Collection("RealPtyEmulator")]
public sealed class RealPtyEmulatorTests : IDisposable
{
    private readonly string _directory = Directory.CreateTempSubdirectory("u1b-").FullName;

    public void Dispose()
    {
        try { Directory.Delete(_directory, recursive: true); } catch { }
    }

    private static string? FindPosix(string program)
    {
        foreach (string folder in new[] { "/usr/bin", "/bin", "/usr/local/bin", "/opt/homebrew/bin" })
        {
            string path = Path.Combine(folder, program);
            if (File.Exists(path)) return path;
        }

        return null;
    }

    private static void SkipUnlessPosix()
        => Skip.IfNot(OperatingSystem.IsLinux() || OperatingSystem.IsMacOS(), "POSIX PTY probe.");

    private string LinesFile(int count)
    {
        string path = Path.Combine(_directory, "lines.txt");
        File.WriteAllLines(path, Enumerable.Range(1, count).Select(i => $"line {i}"));
        return path;
    }

    // ── POSIX shell ──────────────────────────────────────────────────────────

    [SkippableFact]
    public async Task PosixShellUnicodeResizeInterruptAndExit()
    {
        SkipUnlessPosix();
        if (!OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS()) return;
        using var h = PtyEmulatorHarness.Posix("/bin/bash", "--noprofile --norc -i", 80, 24, _directory);
        await h.StartAsync();
        await h.WaitForTextAsync("$");

        // Unicode typed and printed back, through UTF-8 both ways.
        await h.TypeAsync("printf '<%s>\\n' 'čü€Ω中😀'\r");
        await h.WaitForTextAsync("<čü€Ω中😀>");

        // The size the program sees follows the emulator.
        await h.TypeAsync("stty size\r");
        await h.WaitForTextAsync("24 80");
        await h.ResizeAsync(100, 30);
        await h.TypeAsync("clear; stty size\r");
        await h.WaitForTextAsync("30 100");

        // Ctrl+C interrupts the foreground program; the shell survives.
        await h.TypeAsync("sleep 30; echo NOT_INTERRUPTED\r");
        await Task.Delay(300);
        await h.KeyAsync(Keys.kbCtrlC, Keys.kbLeftCtrl);
        await h.TypeAsync("echo status=$?\r");
        await h.WaitForTextAsync("status=130");
        Assert.DoesNotContain("NOT_INTERRUPTED", h.Screen.Split('\n'));   // the echo never ran (the command line shows it)

        await h.TypeAsync("exit 0\r");
        await h.WaitForExitAsync();
        Assert.Equal(0, ((IExitCodeTerminalSession)h.Session).ExitCode);
    }

    // ── full-screen programs (POSIX) ─────────────────────────────────────────

    [SkippableFact]
    public async Task LessUsesTheAlternateScreenPagesAndRestoresTheShell()
    {
        SkipUnlessPosix();
        string? less = FindPosix("less");
        Skip.If(less is null, "less is not installed.");
        if (!OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS()) return;

        using var h = PtyEmulatorHarness.Posix(less!, LinesFile(300), 80, 24, _directory);
        await h.StartAsync();
        await h.WaitForAsync(e => e.IsAlternateScreenActive, "the alternate screen");
        await h.WaitForTextAsync("line 1");

        await h.TypeAsync("G");
        await h.WaitForTextAsync("line 300");
        await h.KeyAsync(Keys.kbPgUp);
        await h.WaitForAsync(e => !Enumerable.Range(0, e.Rows).Any(r => e.GetRowText(r).Contains("line 300")), "a page back");

        await h.TypeAsync("q");
        await h.WaitForExitAsync();
        Assert.False(h.Read(e => e.IsAlternateScreenActive));
    }

    [SkippableFact]
    public async Task VimEditsAndWritesAFile()
    {
        SkipUnlessPosix();
        string? vim = FindPosix("vim") ?? FindPosix("vim.tiny");
        Skip.If(vim is null, "vim is not installed.");
        if (!OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS()) return;

        string file = Path.Combine(_directory, "vim.txt");
        using var h = PtyEmulatorHarness.Posix(vim!, $"-u NONE -N -n -i NONE {file}", 80, 24, _directory);
        await h.StartAsync();
        await h.WaitForAsync(e => e.IsAlternateScreenActive, "the alternate screen");
        await h.WaitForTextAsync("~");

        await h.TypeAsync("ihello from vim");
        await h.KeyAsync(Keys.kbEsc);
        await h.TypeAsync("o");
        await h.TypeAsync("čü€");
        await h.KeyAsync(Keys.kbEsc);
        await h.KeyAsync(Keys.kbUp);
        await h.KeyAsync(Keys.kbEnd);
        await h.TypeAsync("a!");
        await h.KeyAsync(Keys.kbEsc);
        await h.TypeAsync(":wq\r");
        await h.WaitForExitAsync();

        Assert.Equal("hello from vim!\nčü€\n", File.ReadAllText(file));
        Assert.False(h.Read(e => e.IsAlternateScreenActive));
    }

    [SkippableFact]
    public async Task NanoEditsAndSavesWithControlKeys()
    {
        SkipUnlessPosix();
        string? nano = FindPosix("nano");
        Skip.If(nano is null, "nano is not installed.");
        if (!OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS()) return;

        string file = Path.Combine(_directory, "nano.txt");
        using var h = PtyEmulatorHarness.Posix(nano!, $"-I {file}", 80, 24, _directory);
        await h.StartAsync();
        await h.WaitForTextAsync("GNU nano");

        await h.TypeAsync("hi nano ✓");
        await h.KeyAsync(Keys.kbCtrlO, Keys.kbLeftCtrl);
        await h.WaitForTextAsync("File Name to Write");
        await h.KeyAsync(Keys.kbEnter);
        await h.WaitForTextAsync("Wrote");
        await h.KeyAsync(Keys.kbCtrlX, Keys.kbLeftCtrl);
        await h.WaitForExitAsync();

        Assert.Equal("hi nano ✓\n", File.ReadAllText(file));
    }

    [SkippableFact]
    public async Task TopDrawsAndQuits()
    {
        SkipUnlessPosix();
        string? top = FindPosix("top");
        Skip.If(top is null, "top is not installed.");
        if (!OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS()) return;

        using var h = PtyEmulatorHarness.Posix(top!, "-d 1", 100, 30, _directory);
        await h.StartAsync();
        await h.WaitForTextAsync("load average");
        await h.WaitForTextAsync("PID");
        await h.TypeAsync("q");
        await h.WaitForExitAsync();
    }

    [SkippableFact]
    public async Task HtopDrawsItsMetersAndQuitsWithF10()
    {
        SkipUnlessPosix();
        string? htop = FindPosix("htop");
        Skip.If(htop is null, "htop is not installed.");
        if (!OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS()) return;

        using var h = PtyEmulatorHarness.Posix(htop!, "", 100, 30, _directory);
        await h.StartAsync();
        await h.WaitForAsync(e => e.IsAlternateScreenActive, "the alternate screen");
        await h.WaitForTextAsync("Mem");
        await h.WaitForTextAsync("Quit");
        await h.KeyAsync(Keys.kbF10);
        await h.WaitForExitAsync();
        Assert.False(h.Read(e => e.IsAlternateScreenActive));
    }

    [SkippableFact]
    public async Task MidnightCommanderDrawsItsPanelsAndQuits()
    {
        SkipUnlessPosix();
        string? mc = FindPosix("mc");
        Skip.If(mc is null, "mc is not installed.");
        if (!OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS()) return;

        LinesFile(3);
        using var h = PtyEmulatorHarness.Posix(mc!, $"--nosubshell --nocolor {_directory} {_directory}", 100, 30, _directory);
        await h.StartAsync();
        await h.WaitForAsync(e => e.IsAlternateScreenActive, "the alternate screen");
        await h.WaitForTextAsync("lines.txt");
        await h.WaitForTextAsync("Quit");

        // F10 quits; mc asks first only when its "confirm exit" option is on (off by default in current versions).
        await h.KeyAsync(Keys.kbF10);
        await h.WaitForAsync(e => h.Exited.IsCompleted
                                  || Enumerable.Range(0, e.Rows).Any(r => e.GetRowText(r).Contains("really want to quit")),
            "mc quitting or asking to");
        if (!h.Exited.IsCompleted) await h.KeyAsync(Keys.kbEnter);
        await h.WaitForExitAsync();
        Assert.False(h.Read(e => e.IsAlternateScreenActive));
    }

    // ── Windows ──────────────────────────────────────────────────────────────

    [SkippableFact]
    public async Task ConPtyShellUnicodeResizeInterruptAndExit()
    {
        Skip.IfNot(OperatingSystem.IsWindowsVersionAtLeast(10, 0, 17763), "ConPTY probe.");
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 17763)) return;
        await ConPtyShell();
    }

    [SupportedOSPlatform("windows10.0.17763")]
    private async Task ConPtyShell()
    {
        string cmd = Environment.GetEnvironmentVariable("COMSPEC") ?? "cmd.exe";
        using var h = PtyEmulatorHarness.ConPty(cmd, "/d /q /k prompt $G$S", 80, 24, _directory);
        await h.StartAsync();
        await h.WaitForTextAsync(">");

        await h.TypeAsync("echo [čü€Ω]\r");
        await h.WaitForTextAsync("[čü€Ω]");

        await h.ResizeAsync(100, 30);
        await h.TypeAsync("cls & mode con\r");
        await h.WaitForTextAsync("100");

        await h.TypeAsync("ping -t 127.0.0.1\r");
        await h.WaitForTextAsync("Reply from");
        await ((IInterruptibleTerminalSession)h.Session).InterruptAsync();
        await h.WaitForAsync(e => e.GetRowText(e.CursorRow).TrimEnd().EndsWith(">", StringComparison.Ordinal), "the prompt back");

        await h.TypeAsync("exit 7\r");
        await h.WaitForExitAsync();
        Assert.Equal(7, ((IExitCodeTerminalSession)h.Session).ExitCode);
    }

    [SkippableFact]
    public async Task ConPtyRunsGitForWindowsLessOnTheAlternateScreen()
    {
        Skip.IfNot(OperatingSystem.IsWindowsVersionAtLeast(10, 0, 17763), "ConPTY probe.");
        string less = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Git", "usr", "bin", "less.exe");
        Skip.IfNot(File.Exists(less), "Git for Windows less is not installed.");
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 17763)) return;
        await ConPtyLess(less);
    }

    [SupportedOSPlatform("windows10.0.17763")]
    private async Task ConPtyLess(string less)
    {
        using var h = PtyEmulatorHarness.ConPty(less, "lines.txt", 80, 24, _directory);
        LinesFile(300);
        await h.StartAsync();
        await h.WaitForTextAsync("line 1");
        await h.TypeAsync("G");
        await h.WaitForTextAsync("line 300");
        await h.TypeAsync("q");
        await h.WaitForExitAsync();
    }

    [SkippableFact]
    public async Task ConPtyRunsTheWindowsEditor()
    {
        Skip.IfNot(OperatingSystem.IsWindowsVersionAtLeast(10, 0, 17763), "ConPTY probe.");
        string edit = Path.Combine(Environment.SystemDirectory, "edit.exe");
        Skip.IfNot(File.Exists(edit), "Microsoft Edit is not installed.");
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 17763)) return;
        await ConPtyEdit(edit);
    }

    [SupportedOSPlatform("windows10.0.17763")]
    private async Task ConPtyEdit(string edit)
    {
        string file = Path.Combine(_directory, "edit.txt");
        using var h = PtyEmulatorHarness.ConPty(edit, "edit.txt", 100, 30, _directory);
        await h.StartAsync();
        await h.WaitForTextAsync("edit.txt");

        await h.TypeAsync("hello edit");
        await h.KeyAsync(Keys.kbCtrlS, Keys.kbLeftCtrl);
        await WaitForFile(file, "hello edit");
        await h.KeyAsync(Keys.kbCtrlQ, Keys.kbLeftCtrl);
        await h.WaitForExitAsync();
    }

    private static async Task WaitForFile(string path, string content)
    {
        DateTime deadline = DateTime.UtcNow + PtyEmulatorHarness.Timeout;
        while (!(File.Exists(path) && SafeRead(path).Contains(content, StringComparison.Ordinal)))
        {
            Assert.True(DateTime.UtcNow < deadline, $"{path} never held '{content}'.");
            await Task.Delay(50);
        }

        static string SafeRead(string path)
        {
            try { return File.ReadAllText(path); } catch (IOException) { return string.Empty; }
        }
    }
}

[CollectionDefinition("RealPtyEmulator", DisableParallelization = true)]
public sealed class RealPtyEmulatorCollection
{
}
