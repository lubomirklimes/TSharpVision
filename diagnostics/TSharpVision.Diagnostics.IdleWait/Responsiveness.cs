using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using TSharpVision;
using TSharpVision.Constants;
using NativeSdl = SDL3.SDL;

static class Responsiveness
{
    internal static void Run(string driver, string output)
    {
        var app = new Probe();
        var results = new Dictionary<string, double>();
        var worker = Task.Run(async () =>
        {
            await Task.Delay(500);
            async Task Measure(string name, Func<TEvent, bool> matches, Action inject)
            {
                await Task.Delay(200); // Allow the UI to return to idle between stimuli.
                var delivered = new TaskCompletionSource<double>(TaskCreationOptions.RunContinuationsAsynchronously);
                long started = Stopwatch.GetTimestamp();
                app.Accept = ev => { if (matches(ev)) delivered.TrySetResult(Stopwatch.GetElapsedTime(started).TotalMilliseconds); };
                inject();
                results[name] = await delivered.Task.WaitAsync(TimeSpan.FromSeconds(2));
            }
            try
            {
                await Measure("ordinaryQueue", ev => ev.What == Events.evCommand && ev.message.command == 900,
                    () => TEventQueue.Enqueue(Command(900)));
                await Measure("targetedPost", ev => ev.What == Events.evCommand && ev.message.command == 901,
                    () => app.Post(901));
                await Measure("nativeKey", ev => ev.What == Events.evKeyDown, () => InjectKey(driver));
                await Measure("nativeMouse", ev => ev.What == Events.evMouseDown, () => InjectMouse(driver));
                await Measure("nativeResize", ev => ev.What == Events.evCommand && ev.message.command == Views.cmScreenResized,
                    () => InjectResize(driver));
                await Measure("quit", ev => ev.What == Events.evCommand && ev.message.command == Views.cmQuit,
                    () => TEventQueue.Enqueue(Command(Views.cmQuit)));
                File.WriteAllText(output, JsonSerializer.Serialize(new { driver, latencyMilliseconds = results,
                    allWithin400Milliseconds = results.Values.All(value => value < 400) }, new JsonSerializerOptions { WriteIndented = true }));
            }
            catch (Exception ex)
            {
                File.WriteAllText(output, JsonSerializer.Serialize(new { driver, latencyMilliseconds = results, error = ex.ToString() }, new JsonSerializerOptions { WriteIndented = true }));
                TEventQueue.Enqueue(Command(Views.cmQuit));
                throw;
            }
        });
        try { app.Run(); worker.GetAwaiter().GetResult(); }
        finally { app.ShutDown(); }
    }
    private static TEvent Command(ushort code) { var ev = new TEvent { What = Events.evCommand }; ev.message.command = code; return ev; }
    private sealed class Probe : TProgram
    {
        internal Action<TEvent>? Accept;
        public override void HandleEvent(ref TEvent ev)
        {
            Accept?.Invoke(ev);
            if (ev.What == Events.evCommand && ev.message.command == Views.cmQuit) base.HandleEvent(ref ev);
            else ClearEvent(ref ev);
        }
    }
    private static void InjectKey(string driver)
    {
        if (driver == "console") Write(new Record { Type = 1, Down = 1, Repeat = 1, VirtualKey = 0x41, Character = 'a' });
        else
        {
            var ev = new NativeSdl.Event();
            ev.Key.Type = NativeSdl.EventType.KeyDown;
            ev.Key.Key = NativeSdl.Keycode.Return;
            ev.Key.Scancode = NativeSdl.Scancode.Return;
            ev.Key.Down = true;
            Push(ref ev);
        }
    }
    private static void InjectMouse(string driver)
    {
        if (driver == "console") Write(new Record { Type = 2, X = 2, Y = 2, Buttons = 1 });
        else
        {
            var ev = new NativeSdl.Event();
            ev.Button.Type = NativeSdl.EventType.MouseButtonDown;
            ev.Button.Button = 1;
            ev.Button.Clicks = 1;
            ev.Button.X = 20; ev.Button.Y = 20;
            Push(ref ev);
        }
    }
    private static void InjectResize(string driver)
    {
        if (driver == "console")
        {
            IntPtr output = GetStdHandle(-11);
            if (!GetConsoleScreenBufferInfo(output, out var info)) throw new IOException("GetConsoleScreenBufferInfo failed");
            info.Window.Right--;
            if (!SetConsoleWindowInfo(output, true, ref info.Window)) throw new IOException("SetConsoleWindowInfo failed");
            Write(new Record { Type = 4, X = (short)(info.Window.Right + 1), Y = (short)(info.Window.Bottom + 1) });
        }
        else
        {
            IntPtr[]? windows = NativeSdl.GetWindows(out int count);
            if (windows == null || count == 0) throw new IOException("SDL window missing");
            NativeSdl.GetWindowSize(windows[0], out int width, out int height);
            if (!NativeSdl.SetWindowSize(windows[0], width + 20, height)) throw new IOException(NativeSdl.GetError());
        }
    }
    private static void Push(ref NativeSdl.Event ev) { if (!NativeSdl.PushEvent(ref ev)) throw new IOException(NativeSdl.GetError()); }
    private static void Write(Record record)
    {
        if (!WriteConsoleInputW(GetStdHandle(-10), new[] { record }, 1, out uint count) || count != 1)
            throw new IOException($"WriteConsoleInputW failed: {Marshal.GetLastWin32Error()}");
    }
    [StructLayout(LayoutKind.Explicit, Size = 20)]
    private struct Record
    {
        [FieldOffset(0)] internal ushort Type;
        [FieldOffset(4)] internal int Down;
        [FieldOffset(8)] internal ushort Repeat;
        [FieldOffset(10)] internal ushort VirtualKey;
        [FieldOffset(14)] internal char Character;
        [FieldOffset(4)] internal short X;
        [FieldOffset(6)] internal short Y;
        [FieldOffset(8)] internal uint Buttons;
    }
    [StructLayout(LayoutKind.Sequential)] private struct Coord { internal short X, Y; }
    [StructLayout(LayoutKind.Sequential)] private struct Rect { internal short Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct Info { internal Coord Size, Cursor; internal ushort Attributes; internal Rect Window; internal Coord Maximum; }
    [DllImport("kernel32.dll")] private static extern IntPtr GetStdHandle(int id);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool WriteConsoleInputW(IntPtr input, Record[] records, uint count, out uint written);
    [DllImport("kernel32.dll")] private static extern bool GetConsoleScreenBufferInfo(IntPtr output, out Info info);
    [DllImport("kernel32.dll")] private static extern bool SetConsoleWindowInfo(IntPtr output, bool absolute, ref Rect window);
}
