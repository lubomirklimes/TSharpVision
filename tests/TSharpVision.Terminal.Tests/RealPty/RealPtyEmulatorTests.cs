using System.Diagnostics;
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
        await h.TypeAsync("sleep 30\r");
        await Task.Delay(300);
        await h.KeyAsync(Keys.kbCtrlC, Keys.kbLeftCtrl);
        await h.TypeAsync("echo status=$?\r");
        await h.WaitForTextAsync("status=130");

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
        Skip.IfNot(IsGnuNano(nano!), "GNU nano is not installed.");
        if (!OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS()) return;

        string file = Path.Combine(_directory, "nano.txt");
        using var h = PtyEmulatorHarness.Posix(nano!, $"-I {file}", 80, 24, _directory);
        await h.StartAsync();
        await h.WaitForTextAsync("GNU nano");

        await h.TypeAsync("hi nano ✓");
        await h.KeyAsync(Keys.kbCtrlO, Keys.kbLeftCtrl);
        await h.WaitForAsync(e => Enumerable.Range(0, e.Rows).Any(r =>
            e.GetRowText(r).Contains("File Name to Write", StringComparison.Ordinal)
            || e.GetRowText(r).Contains("Write to File:", StringComparison.Ordinal)), "nano's write prompt");
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

        if (OperatingSystem.IsMacOS())
            await AssertTop(top!, "-s 1", "Load Avg:");
        else
            await AssertTop(top!, "-d 1", "load average");
    }

    [SupportedOSPlatform("linux")]
    [SupportedOSPlatform("macos")]
    private async Task AssertTop(string top, string arguments, string loadAverage)
    {
        using var h = PtyEmulatorHarness.Posix(top, arguments, 100, 30, _directory);
        await h.StartAsync();
        await h.WaitForTextAsync(loadAverage);
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
        // mc reads (and on exit writes) the developer's own configuration: a skin chosen there - a
        // 256-colour one, say - opens a dialog instead of the panels. An empty configuration of its
        // own and the default skin make the probe the same on every machine.
        string home = Directory.CreateTempSubdirectory("u1b-mc-").FullName;
        try
        {
            await MidnightCommander(mc!, new Dictionary<string, string?>
            {
                ["XDG_CONFIG_HOME"] = Path.Combine(home, "config"),
                ["XDG_DATA_HOME"] = Path.Combine(home, "data"),
                ["XDG_CACHE_HOME"] = Path.Combine(home, "cache"),
                ["MC_SKIN"] = "default",
            });
        }
        finally
        {
            try { Directory.Delete(home, recursive: true); } catch { }
        }
    }

    [SupportedOSPlatform("linux")]
    [SupportedOSPlatform("macos")]
    private async Task MidnightCommander(string mc, IReadOnlyDictionary<string, string?> environment)
    {
        using var h = PtyEmulatorHarness.Posix(mc, $"--nosubshell --nocolor {_directory} {_directory}", 100, 30, _directory, environment);
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
        // ConPTY inherits the host environment. Give this child the terminal type our
        // emulator implements, even when the test runner has TERM=dumb (or no TERM).
        // Set it inside cmd so parallel tests and the parent environment are untouched.
        string cmd = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "cmd.exe");
        string arguments = $"/d /s /c \"set \"TERM={TTerminal.TermName}\"&&set \"COLORTERM=\"&&\"{less}\" lines.txt\"";
        using var h = PtyEmulatorHarness.ConPty(cmd, arguments, 80, 24, _directory);
        LinesFile(300);
        await h.StartAsync();
        await h.WaitForTextAsync("line 1");
        await h.TypeAsync("G");
        await h.WaitForTextAsync("line 300");
        await h.TypeAsync("q");
        await h.WaitForExitAsync();
    }

    private static bool IsGnuNano(string path)
    {
        using Process? process = Process.Start(new ProcessStartInfo(path, "--version")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        });
        if (process is null) return false;

        Task<string> output = process.StandardOutput.ReadToEndAsync();
        Task<string> error = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(5000))
        {
            process.Kill(entireProcessTree: true);
            return false;
        }

        return process.ExitCode == 0
               && (output.GetAwaiter().GetResult() + error.GetAwaiter().GetResult())
                   .Contains("GNU nano", StringComparison.Ordinal);
    }
}

[CollectionDefinition("RealPtyEmulator", DisableParallelization = true)]
public sealed class RealPtyEmulatorCollection
{
}
