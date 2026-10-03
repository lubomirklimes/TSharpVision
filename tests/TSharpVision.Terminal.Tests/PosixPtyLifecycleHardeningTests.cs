#pragma warning disable CA1416 // Each test skips unless it runs on Linux or macOS.

using System.Diagnostics;
using System.Text;
using TSharpVision.Terminal.Posix;
using Xunit;

namespace TSharpVision.Terminal.Tests;

/// <summary>
/// U-1a ownership, reaping, signalling and transport tests for <see cref="PosixPtyTerminalSession"/>.
/// They need a real Linux or macOS kernel and report <b>Skipped</b> anywhere else.
/// Commands use <c>/bin/sh</c> only, so no user profile or shell customisation is involved.
/// </summary>
public sealed class PosixPtyLifecycleHardeningTests
{
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(15);

    private static bool IsPosix => OperatingSystem.IsLinux() || OperatingSystem.IsMacOS();

    private static PosixPtyTerminalSession Create(string arguments, int columns = 80, int rows = 24) => new(
        new PosixPtyTerminalSessionOptions
        {
            FileName = "/bin/sh",
            Arguments = arguments,
            WorkingDirectory = Path.GetTempPath(),
            InitialSize = new TerminalSize(columns, rows),
        });

    private static async Task<(PosixPtyTerminalSession Session, StringBuilder Output, Task Exited)> Start(string arguments)
    {
        var session = Create(arguments);
        var output = new StringBuilder();
        var exited = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        session.OutputReceived += (_, e) => { lock (output) output.AppendOutput(e); };
        session.Exited += (_, _) => exited.TrySetResult();
        await session.StartAsync();
        return (session, output, exited.Task);
    }

    // ── descriptor ownership ──────────────────────────────────────────────────

    [SkippableFact]
    public async Task AfterExit_TheMasterIsReleased_AndLaterCallsNeverReachAReusedDescriptor()
    {
        Skip.IfNot(IsPosix, "POSIX PTY requires Linux or macOS.");

        (PosixPtyTerminalSession session, _, Task exited) = await Start("-c \"exit 0\"");
        using (session)
        {
            await exited.WaitAsync(Wait);
            Assert.False(session.HasMaster);

            // Open enough files that the old master's number is almost certainly handed out again.
            string directory = Directory.CreateTempSubdirectory("u1a-fd-").FullName;
            var files = new List<FileStream>();
            try
            {
                for (int i = 0; i < 32; i++)
                    files.Add(new FileStream(Path.Combine(directory, $"f{i}"), FileMode.CreateNew,
                        FileAccess.ReadWrite, FileShare.ReadWrite, bufferSize: 1));

                await session.SendTextAsync("MUST_NOT_LAND_ANYWHERE\n").WaitAsync(Wait);
                await session.InterruptAsync().WaitAsync(Wait);
                await session.ResizeAsync(new TerminalSize(111, 33)).WaitAsync(Wait);

                foreach (FileStream file in files) Assert.Equal(0, file.Length);
            }
            finally
            {
                foreach (FileStream file in files) file.Dispose();
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    [SkippableFact]
    public async Task TheMaster_IsCloseOnExec_SoNoLaterChildCanInheritIt()
    {
        Skip.IfNot(OperatingSystem.IsLinux(), "Uses /proc/self/fdinfo, which only Linux has.");

        (PosixPtyTerminalSession session, _, _) = await Start("-c \"sleep 20\"");
        using (session)
        {
            int fd = session.MasterDescriptorForDiagnostics;
            Assert.True(fd >= 0);

            // "flags:\t0102002" — octal open flags as the kernel holds them for this descriptor.
            string flagsLine = File.ReadLines($"/proc/self/fdinfo/{fd}").First(l => l.StartsWith("flags:"));
            int flags = Convert.ToInt32(flagsLine["flags:".Length..].Trim(), 8);
            const int O_CLOEXEC = 0x80000; // 02000000 octal on Linux
            Assert.True((flags & O_CLOEXEC) != 0, $"master fd {fd} is not close-on-exec ({flagsLine}).");
        }
    }

    [SkippableFact]
    public async Task TheMaster_IsNotInheritedByAnotherTerminalsShell()
    {
        Skip.IfNot(OperatingSystem.IsLinux(), "Uses /proc, which only Linux has.");

        (PosixPtyTerminalSession first, _, _) = await Start("-c \"sleep 20\"");
        using (first)
        {
            int firstMaster = first.MasterDescriptorForDiagnostics;
            (PosixPtyTerminalSession second, StringBuilder output, Task exited) =
                await Start("-c \"ls -l /proc/$$/fd; echo FD_LISTED\"");
            using (second)
            {
                await exited.WaitAsync(Wait);
                string text;
                lock (output) text = output.ToString();
                Assert.Contains("FD_LISTED", text);
                // The shell gets fds 0-2 and nothing else of this process (closefrom(3) at spawn):
                // not the first terminal's master, and not the foreign non-close-on-exec /dev/ptmx
                // the dotnet test host itself inherits from its own parent.
                Assert.True(firstMaster >= 0);
                Assert.False(text.Contains("/dev/ptmx", StringComparison.Ordinal),
                    $"The second shell holds a PTY master.\n--- its descriptors:\n{text}\n--- ours:\n{DescribeOwnPtmxDescriptors()}");
            }
        }
    }

    // ── process lifecycle ─────────────────────────────────────────────────────

    [SkippableFact]
    public async Task A_ChildThatExits_IsReaped_AndItsExitCodeReported()
    {
        Skip.IfNot(IsPosix, "POSIX PTY requires Linux or macOS.");

        (PosixPtyTerminalSession session, _, Task exited) = await Start("-c \"exit 7\"");
        using (session)
        {
            int pid = session.ProcessId;
            await exited.WaitAsync(Wait);

            Assert.Equal(7, session.ExitCode);
            Assert.True(session.IsChildReaped);
            if (OperatingSystem.IsLinux())
                Assert.False(Directory.Exists($"/proc/{pid}"), "A reaped child leaves no /proc entry (no zombie).");
        }
    }

    [SkippableFact]
    public async Task Dispose_KillsTheShellsProcessGroup()
    {
        Skip.IfNot(IsPosix, "POSIX PTY requires Linux or macOS.");

        (PosixPtyTerminalSession session, StringBuilder output, _) =
            await Start("-c \"sleep 60 & echo BG=$!; wait\"");
        int background = await ReadPid(output, "BG=");
        using Process sleeper = Process.GetProcessById(background);

        session.Dispose();

        Assert.True(sleeper.WaitForExit(Wait), "A job in the shell's process group must not survive the terminal.");
        await session.OutputReaderTask!.WaitAsync(Wait);
    }

    [SkippableFact]
    public async Task Stop_EndsAnInteractiveShellPromptly()
    {
        Skip.IfNot(IsPosix, "POSIX PTY requires Linux or macOS.");

        // An interactive sh ignores SIGTERM; it exits on SIGHUP, which is what closing a terminal sends.
        (PosixPtyTerminalSession session, _, Task exited) = await Start("-i");
        using (session)
        {
            await Task.Delay(200);
            var stopwatch = Stopwatch.StartNew();
            await session.StopAsync().WaitAsync(Wait);
            stopwatch.Stop();

            Assert.False(session.IsRunning);
            Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(2.5),
                $"Stop took {stopwatch.Elapsed}; SIGTERM alone would wait the full 3 s grace period.");
            await exited.WaitAsync(Wait);
        }
    }

    [SkippableFact]
    public async Task OpenCommunicateClose_ThreeTimes_LeavesNothingRunning()
    {
        Skip.IfNot(IsPosix, "POSIX PTY requires Linux or macOS.");

        for (int round = 0; round < 3; round++)
        {
            string marker = $"U1A_ROUND_{round}_OK";
            (PosixPtyTerminalSession session, StringBuilder output, _) = await Start("");
            int pid = session.ProcessId;

            await session.SendTextAsync($"echo {marker}\n");
            await WaitFor(() => { lock (output) return output.ToString().Contains(marker + "\r\n"); });

            session.Dispose();
            await session.OutputReaderTask!.WaitAsync(Wait);
            await WaitFor(() => session.IsChildReaped);
            if (OperatingSystem.IsLinux())
                await WaitFor(() => !Directory.Exists($"/proc/{pid}"));
        }
    }

    [SkippableFact]
    public async Task Dispose_RacingStopAndNaturalExit_NeverThrows()
    {
        Skip.IfNot(IsPosix, "POSIX PTY requires Linux or macOS.");

        for (int round = 0; round < 3; round++)
        {
            (PosixPtyTerminalSession session, _, _) = await Start("-c \"echo racing\"");
            Task stop = Task.Run(() => session.StopAsync());
            Task dispose = Task.Run(session.Dispose);
            Task disposeAgain = Task.Run(session.Dispose);
            await Task.WhenAll(stop, dispose, disposeAgain).WaitAsync(Wait);

            await session.OutputReaderTask!.WaitAsync(Wait);
            await WaitFor(() => session.IsChildReaped);
            Assert.False(session.HasMaster);
        }
    }

    [SkippableFact]
    public async Task A_SessionCanBeStartedOnlyOnce()
    {
        Skip.IfNot(IsPosix, "POSIX PTY requires Linux or macOS.");

        (PosixPtyTerminalSession session, _, Task exited) = await Start("-c \"exit 0\"");
        using (session)
        {
            await exited.WaitAsync(Wait);
            await Assert.ThrowsAsync<InvalidOperationException>(() => session.StartAsync());
        }
    }

    // ── resize ────────────────────────────────────────────────────────────────

    [SkippableFact]
    public async Task Resize_ReachesTheChildsTerminal()
    {
        Skip.IfNot(IsPosix, "POSIX PTY requires Linux or macOS.");

        (PosixPtyTerminalSession session, StringBuilder output, Task exited) =
            await Start("-c \"read line; stty size\"");
        using (session)
        {
            await session.ResizeAsync(new TerminalSize(101, 33));
            await session.SendTextAsync("\n");
            await exited.WaitAsync(Wait);

            lock (output) Assert.Contains("33 101", output.ToString());
        }
    }

    [SkippableFact]
    public async Task Resize_AfterExitAndAfterDispose_IsANoOp()
    {
        Skip.IfNot(IsPosix, "POSIX PTY requires Linux or macOS.");

        (PosixPtyTerminalSession session, _, Task exited) = await Start("-c \"exit 0\"");
        await exited.WaitAsync(Wait);
        await session.ResizeAsync(new TerminalSize(90, 30));
        session.Dispose();
        await session.ResizeAsync(new TerminalSize(91, 31));
        await session.ResizeAsync(new TerminalSize(0, 0));
    }

    // ── transport ─────────────────────────────────────────────────────────────

    [SkippableFact]
    public async Task Utf8_TravelsBothWays_Unchanged()
    {
        Skip.IfNot(IsPosix, "POSIX PTY requires Linux or macOS.");

        const string Text = "žluťoučký kůň € Ω";
        (PosixPtyTerminalSession session, StringBuilder output, Task exited) =
            await Start("-c \"read line; printf '[%s]\\\\n' \\\"$line\\\"\"");
        using (session)
        {
            await session.SendTextAsync(Text + "\n");
            await exited.WaitAsync(Wait);

            string text;
            lock (output) text = output.ToString();
            Assert.Contains($"[{Text}]", text);
            Assert.DoesNotContain('�', text);
        }
    }

    [SkippableFact]
    public async Task LargeInput_DoesNotBlockTheCaller_AndArrivesInOrder()
    {
        Skip.IfNot(IsPosix, "POSIX PTY requires Linux or macOS.");

        // wc counts what cat saw; the pump writes it while the caller carries on.
        (PosixPtyTerminalSession session, StringBuilder output, Task exited) =
            await Start("-c \"stty -echo -icanon; head -c 20000 | wc -c\"");
        using (session)
        {
            await Task.Delay(200); // let stty apply before the bytes arrive
            var stopwatch = Stopwatch.StartNew();
            Task sent = session.SendTextAsync(new string('x', 20000));
            stopwatch.Stop();
            Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(1), "The caller must not wait for the child.");

            await sent.WaitAsync(Wait);
            await exited.WaitAsync(Wait);
            lock (output) Assert.Contains("20000", output.ToString());
        }
    }

    // ── helpers ───────────────────────────────────────────────────────────────

    /// <summary>Every /dev/ptmx this test process holds, with its kernel flags, for failure messages.</summary>
    private static string DescribeOwnPtmxDescriptors()
    {
        var lines = new StringBuilder();
        foreach (string link in Directory.EnumerateFileSystemEntries("/proc/self/fd"))
        {
            string? target = null;
            try { target = new FileInfo(link).LinkTarget; } catch { }
            if (target is null || !target.Contains("ptmx", StringComparison.Ordinal)) continue;
            string fd = Path.GetFileName(link);
            string flags = "?";
            try { flags = File.ReadLines($"/proc/self/fdinfo/{fd}").First(l => l.StartsWith("flags:")); } catch { }
            lines.AppendLine($"fd {fd} -> {target} {flags}");
        }
        return lines.ToString();
    }

    private static async Task WaitFor(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow + Wait;
        while (!condition())
        {
            if (DateTime.UtcNow > deadline) throw new TimeoutException("Condition not reached.");
            await Task.Delay(20);
        }
    }

    private static async Task<int> ReadPid(StringBuilder output, string prefix)
    {
        int pid = 0;
        await WaitFor(() =>
        {
            string text;
            lock (output) text = output.ToString();
            int at = text.IndexOf(prefix, StringComparison.Ordinal);
            if (at < 0) return false;
            string digits = new(text[(at + prefix.Length)..].TakeWhile(char.IsDigit).ToArray());
            return digits.Length > 0 && int.TryParse(digits, out pid);
        });
        return pid;
    }
}
