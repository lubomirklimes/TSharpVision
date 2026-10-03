#pragma warning disable CA1416 // Each test skips unless ConPTY is available.

using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using TSharpVision.Terminal.Windows;
using Xunit;

namespace TSharpVision.Terminal.Tests;

/// <summary>
/// U-1a lifecycle, ownership and process-tree tests for <see cref="ConPtyTerminalSession"/>.
/// Unlike the older suite these report <b>Skipped</b> off Windows instead of passing vacuously.
/// </summary>
public sealed class ConPtyLifecycleHardeningTests
{
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(15);

    private static bool IsConPtySupported => OperatingSystem.IsWindowsVersionAtLeast(10, 0, 17763);

    private static string Cmd => Environment.GetEnvironmentVariable("COMSPEC") ?? "cmd.exe";

    private static ConPtyTerminalSession Create(string arguments) => new(new ConPtyTerminalSessionOptions
    {
        FileName = Cmd,
        Arguments = arguments,
        WorkingDirectory = Path.GetTempPath(),
        InitialSize = new TerminalSize(100, 30),
    });

    // ── pure: command line ────────────────────────────────────────────────────

    [Theory]
    [InlineData("cmd.exe", "", "cmd.exe")]
    [InlineData("cmd.exe", "/c echo x", "cmd.exe /c echo x")]
    [InlineData(@"C:\Program Files\Shell\sh.exe", "-l", "\"C:\\Program Files\\Shell\\sh.exe\" -l")]
    [InlineData("\"C:\\Program Files\\Shell\\sh.exe\"", null, "\"C:\\Program Files\\Shell\\sh.exe\"")]
    [InlineData(@"C:\Windows\system32\cmd.exe", null, @"C:\Windows\system32\cmd.exe")]
    public void BuildCommandLine_QuotesAnExecutablePathContainingWhitespace(
        string fileName, string? arguments, string expected)
        => Assert.Equal(expected, ConPtyTerminalSession.BuildCommandLine(fileName, arguments));

    // ── process tree ──────────────────────────────────────────────────────────

    [SkippableFact]
    public async Task Dispose_TerminatesTheWholeDescendantTree()
    {
        Skip.IfNot(IsConPtySupported, "ConPTY requires Windows 10 1809 or later.");

        // session → cmd → cmd → ping: two generations below the shell.
        using var session = Create("/d /c \"cmd /d /c ping -n 60 127.0.0.1 >nul\"");
        await session.StartAsync();
        Assert.True(session.HasJob, "The child must be in a kill-on-close Job Object.");

        List<Process> tree = await WaitForDescendants(session.ProcessId, minimum: 2);
        try
        {
            session.Dispose();
            await AssertAllExit(tree);
        }
        finally
        {
            foreach (Process process in tree) process.Dispose();
        }
    }

    [SkippableFact]
    public async Task Stop_TerminatesTheWholeDescendantTree()
    {
        Skip.IfNot(IsConPtySupported, "ConPTY requires Windows 10 1809 or later.");

        using var session = Create("/d /c \"cmd /d /c ping -n 60 127.0.0.1 >nul\"");
        await session.StartAsync();
        List<Process> tree = await WaitForDescendants(session.ProcessId, minimum: 2);
        try
        {
            await session.StopAsync().WaitAsync(Wait);
            Assert.False(session.IsRunning);
            await AssertAllExit(tree);
        }
        finally
        {
            foreach (Process process in tree) process.Dispose();
        }
    }

    [SkippableFact]
    public async Task Dispose_DoesNotTouchAProcessOutsideTheTree()
    {
        Skip.IfNot(IsConPtySupported, "ConPTY requires Windows 10 1809 or later.");

        // A process this test starts itself, beside the session rather than under it.
        using var bystander = Process.Start(new ProcessStartInfo(Cmd, "/d /c ping -n 30 127.0.0.1 >nul")
        {
            CreateNoWindow = true,
            UseShellExecute = false,
        })!;
        try
        {
            using (var session = Create("/d /c \"ping -n 60 127.0.0.1 >nul\""))
            {
                await session.StartAsync();
                await WaitForDescendants(session.ProcessId, minimum: 1);
            }

            await Task.Delay(300);
            Assert.False(bystander.HasExited);
        }
        finally
        {
            try { bystander.Kill(entireProcessTree: true); } catch { }
        }
    }

    // ── lifecycle ─────────────────────────────────────────────────────────────

    [SkippableFact]
    public async Task OpenCommunicateClose_ThreeTimes_LeavesNothingRunning()
    {
        Skip.IfNot(IsConPtySupported, "ConPTY requires Windows 10 1809 or later.");

        for (int round = 0; round < 3; round++)
        {
            string marker = $"U1A_ROUND_{round}_OK";
            var output = new StringBuilder();
            var session = Create("/d /q /k");
            session.OutputReceived += (_, e) => { lock (output) output.AppendOutput(e); };

            await session.StartAsync();
            using Process shell = Process.GetProcessById(session.ProcessId);
            await session.SendTextAsync($"echo {marker}\r");
            await WaitFor(() => { lock (output) return output.ToString().Contains(marker); });

            session.Dispose();

            Assert.True(shell.WaitForExit(Wait), "The shell must exit when its session is disposed.");
            Assert.NotNull(session.OutputReaderTask);
            await session.OutputReaderTask!.WaitAsync(Wait);
            Assert.False(session.IsRunning);
        }
    }

    [SkippableFact]
    public async Task A_SessionCanBeStartedOnlyOnce()
    {
        Skip.IfNot(IsConPtySupported, "ConPTY requires Windows 10 1809 or later.");

        using var session = Create("/d /c exit 0");
        var exited = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        session.Exited += (_, _) => exited.TrySetResult();
        await session.StartAsync();
        await exited.Task.WaitAsync(Wait);

        await Assert.ThrowsAsync<InvalidOperationException>(() => session.StartAsync());
    }

    [SkippableFact]
    public async Task A_FailedStart_LeavesTheSessionInert()
    {
        Skip.IfNot(IsConPtySupported, "ConPTY requires Windows 10 1809 or later.");

        var session = new ConPtyTerminalSession(new ConPtyTerminalSessionOptions
        {
            FileName = @"C:\definitely\not\here\missing-u1a.exe",
        });
        await Assert.ThrowsAnyAsync<Exception>(() => session.StartAsync());

        Assert.False(session.IsRunning);
        Assert.Null(session.OutputReaderTask);
        await session.SendTextAsync("ignored\r");
        await session.ResizeAsync(new TerminalSize(40, 10));
        await session.InterruptAsync();
        await session.StopAsync();
        session.Dispose();
        session.Dispose();
    }

    [SkippableFact]
    public async Task InputResizeAndInterrupt_AfterExitAndAfterDispose_AreQuietNoOps()
    {
        Skip.IfNot(IsConPtySupported, "ConPTY requires Windows 10 1809 or later.");

        var session = Create("/d /c exit 3");
        var exited = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        session.Exited += (_, _) => exited.TrySetResult();
        await session.StartAsync();
        await exited.Task.WaitAsync(Wait);
        Assert.Equal(3, session.ExitCode);

        await session.SendTextAsync("late\r").WaitAsync(Wait);
        await session.ResizeAsync(new TerminalSize(120, 40)).WaitAsync(Wait);
        await session.InterruptAsync().WaitAsync(Wait);

        session.Dispose();

        await session.SendTextAsync("later\r").WaitAsync(Wait);
        await session.ResizeAsync(new TerminalSize(60, 20)).WaitAsync(Wait);
        await session.InterruptAsync().WaitAsync(Wait);
        await session.StopAsync().WaitAsync(Wait);
    }

    [SkippableFact]
    public async Task Resize_DuringOutput_RepeatedAndDuplicate_IsSafe()
    {
        Skip.IfNot(IsConPtySupported, "ConPTY requires Windows 10 1809 or later.");

        using var session = Create("/d /c \"for /l %i in (1,1,300) do @echo line %i\"");
        var exited = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        session.Exited += (_, _) => exited.TrySetResult();
        await session.StartAsync();

        for (int i = 0; i < 40 && !exited.Task.IsCompleted; i++)
        {
            await session.ResizeAsync(new TerminalSize(60 + i % 5, 20 + i % 3));
            await session.ResizeAsync(new TerminalSize(60 + i % 5, 20 + i % 3)); // same size again
        }
        await session.ResizeAsync(new TerminalSize(0, -5)); // clamped, never an invalid COORD

        await exited.Task.WaitAsync(Wait);
    }

    [SkippableFact]
    public async Task Dispose_RacingStopAndNaturalExit_ReleasesOnceAndNeverThrows()
    {
        Skip.IfNot(IsConPtySupported, "ConPTY requires Windows 10 1809 or later.");

        for (int round = 0; round < 3; round++)
        {
            var session = Create("/d /c echo racing");
            await session.StartAsync();
            using Process shell = Process.GetProcessById(session.ProcessId);

            Task stop = Task.Run(() => session.StopAsync());
            Task dispose = Task.Run(session.Dispose);
            Task disposeAgain = Task.Run(session.Dispose);
            await Task.WhenAll(stop, dispose, disposeAgain).WaitAsync(Wait);

            Assert.True(shell.WaitForExit(Wait));
            await session.OutputReaderTask!.WaitAsync(Wait);
            Assert.False(session.IsRunning);
        }
    }

    [SkippableFact]
    public async Task Dispose_DoesNotRaiseExited_EvenWhenTheChildExitsAfterwards()
    {
        Skip.IfNot(IsConPtySupported, "ConPTY requires Windows 10 1809 or later.");

        int exitedCount = 0;
        var session = Create("/d /q /k");
        session.Exited += (_, _) => Interlocked.Increment(ref exitedCount);
        await session.StartAsync();
        using Process shell = Process.GetProcessById(session.ProcessId);

        session.Dispose();
        Assert.True(shell.WaitForExit(Wait));
        await Task.Delay(300); // the watcher observes the exit after Dispose

        Assert.Equal(0, Volatile.Read(ref exitedCount));
    }

    // ── transport ─────────────────────────────────────────────────────────────

    [SkippableFact]
    public async Task Utf8Input_ReachesTheChild_AndComesBackUnchanged()
    {
        Skip.IfNot(IsConPtySupported, "ConPTY requires Windows 10 1809 or later.");

        const string Text = "žluťoučký kůň € Ω";
        var output = new StringBuilder();
        using var session = Create("/d /q /k");
        session.OutputReceived += (_, e) => { lock (output) output.AppendOutput(e); };
        await session.StartAsync();

        await session.SendTextAsync($"echo [{Text}]\r");
        await WaitFor(() => { lock (output) return output.ToString().Contains($"[{Text}]"); });

        lock (output) Assert.DoesNotContain('\uFFFD', output.ToString());
    }

    [SkippableFact]
    public async Task ManyQueuedWrites_ArriveInOrder()
    {
        Skip.IfNot(IsConPtySupported, "ConPTY requires Windows 10 1809 or later.");

        var output = new StringBuilder();
        using var session = Create("/d /q /k");
        session.OutputReceived += (_, e) => { lock (output) output.AppendOutput(e); };
        await session.StartAsync();

        // One command typed a character at a time: any reordering would corrupt it.
        foreach (char c in "echo ORDER_ABCDEFGHIJKLMNOPQRSTUVWXYZ_0123456789\r")
            _ = session.SendTextAsync(c.ToString());

        await WaitFor(() => { lock (output) return output.ToString().Contains("ORDER_ABCDEFGHIJKLMNOPQRSTUVWXYZ_0123456789\r\n"); });
    }

    // ── helpers ───────────────────────────────────────────────────────────────

    private static async Task WaitFor(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow + Wait;
        while (!condition())
        {
            if (DateTime.UtcNow > deadline) throw new TimeoutException("Condition not reached.");
            await Task.Delay(20);
        }
    }

    /// <summary>Waits until the tree below <paramref name="rootPid"/> holds at least <paramref name="minimum"/> processes.</summary>
    private static async Task<List<Process>> WaitForDescendants(int rootPid, int minimum)
    {
        var deadline = DateTime.UtcNow + Wait;
        while (true)
        {
            List<int> pids = WindowsProcessTree.DescendantsOf(rootPid);
            if (pids.Count >= minimum)
            {
                var tree = new List<Process> { Process.GetProcessById(rootPid) };
                foreach (int pid in pids)
                {
                    try { tree.Add(Process.GetProcessById(pid)); }
                    catch (ArgumentException) { } // already gone
                }
                return tree;
            }
            if (DateTime.UtcNow > deadline)
                throw new TimeoutException($"Expected {minimum} descendants of {rootPid}, saw {pids.Count}.");
            await Task.Delay(50);
        }
    }

    private static async Task AssertAllExit(IEnumerable<Process> tree)
    {
        var deadline = DateTime.UtcNow + Wait;
        foreach (Process process in tree)
        {
            TimeSpan left = deadline - DateTime.UtcNow;
            bool exited = left > TimeSpan.Zero && await Task.Run(() => process.WaitForExit(left));
            Assert.True(exited, $"Process {process.Id} of the terminal's tree survived.");
        }
    }
}

/// <summary>Parent/child walk over a Toolhelp snapshot — .NET's Process has no parent id.</summary>
internal static class WindowsProcessTree
{
    internal static List<int> DescendantsOf(int rootPid)
    {
        var parentOf = new Dictionary<int, int>();
        IntPtr snapshot = CreateToolhelp32Snapshot(TH32CS_SNAPPROCESS, 0);
        if (snapshot == new IntPtr(-1)) return new List<int>();
        try
        {
            var entry = new PROCESSENTRY32 { dwSize = (uint)Marshal.SizeOf<PROCESSENTRY32>() };
            if (Process32First(snapshot, ref entry))
            {
                do parentOf[(int)entry.th32ProcessID] = (int)entry.th32ParentProcessID;
                while (Process32Next(snapshot, ref entry));
            }
        }
        finally
        {
            CloseHandle(snapshot);
        }

        var result = new List<int>();
        var frontier = new Queue<int>();
        frontier.Enqueue(rootPid);
        while (frontier.Count > 0)
        {
            int parent = frontier.Dequeue();
            foreach ((int pid, int ppid) in parentOf)
            {
                if (ppid == parent && pid != parent && !result.Contains(pid))
                {
                    result.Add(pid);
                    frontier.Enqueue(pid);
                }
            }
        }
        // conhost.exe belongs to the pseudo console, not to the shell's tree; it is not ours to judge.
        return result;
    }

    private const uint TH32CS_SNAPPROCESS = 0x00000002;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct PROCESSENTRY32
    {
        public uint dwSize;
        public uint cntUsage;
        public uint th32ProcessID;
        public IntPtr th32DefaultHeapID;
        public uint th32ModuleID;
        public uint cntThreads;
        public uint th32ParentProcessID;
        public int pcPriClassBase;
        public uint dwFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string szExeFile;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr CreateToolhelp32Snapshot(uint flags, uint processId);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, EntryPoint = "Process32FirstW")]
    private static extern bool Process32First(IntPtr snapshot, ref PROCESSENTRY32 entry);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, EntryPoint = "Process32NextW")]
    private static extern bool Process32Next(IntPtr snapshot, ref PROCESSENTRY32 entry);

    [DllImport("kernel32.dll")]
    private static extern bool CloseHandle(IntPtr handle);
}
