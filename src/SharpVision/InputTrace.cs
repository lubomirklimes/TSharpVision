// InputTrace — opt-in input-routing diagnostics for SharpVision.
//
// Enable by setting the environment variable:
//   SHARPVISION_TRACE_INPUT=1
//
// Output is written to  sharpvision-input-trace.log  in the CWD.
// All public methods are no-ops when tracing is disabled, so the flag can
// stay in shipped builds without runtime overhead.
using System;
using System.IO;
using System.Text;
using SharpVision.Constants;

namespace SharpVision;

/// <summary>
/// Opt-in input-routing trace.  Enabled at process start when the environment
/// variable <c>SHARPVISION_TRACE_INPUT=1</c>.
/// Output goes to <c>sharpvision-input-trace.log</c> in the working directory.
/// </summary>
public static class InputTrace
{
    // ---- public API -------------------------------------------------------

    /// <summary>True when SHARPVISION_TRACE_INPUT=1 was set at process start.</summary>
    public static readonly bool Enabled = 
        string.Equals(Environment.GetEnvironmentVariable("SHARPVISION_TRACE_INPUT"),
                      "1", StringComparison.Ordinal);

    /// <summary>Write a free-form log line.</summary>
    public static void Log(string stage, string detail)
    {
        if (!Enabled) return;
        WriteRaw($"[{DateTime.Now:HH:mm:ss.fff}] {stage}: {detail}");
    }

    /// <summary>Write a log line that includes a formatted TEvent.</summary>
    public static void LogEvent(string stage, in TEvent ev)
    {
        if (!Enabled) return;
        WriteRaw($"[{DateTime.Now:HH:mm:ss.fff}] {stage}: {FormatEvent(ev)}");
    }

    // ---- formatting helpers -----------------------------------------------

    /// <summary>Human-readable TEvent summary (short one-liner).</summary>
    public static string FormatEvent(in TEvent ev)
    {
        string whatName = ev.What switch
        {
            Events.evNothing   => "evNothing",
            Events.evMouseDown => "evMouseDown",
            Events.evMouseUp   => "evMouseUp",
            Events.evMouseMove => "evMouseMove",
            Events.evMouseAuto => "evMouseAuto",
            Events.evKeyDown   => "evKeyDown",
            Events.evCommand   => "evCommand",
            Events.evBroadcast => "evBroadcast",
            _                  => $"0x{ev.What:X4}",
        };

        var sb = new StringBuilder();
        sb.Append($"what=0x{ev.What:X4}({whatName})");

        if ((ev.What & Events.evKeyboard) != 0)
        {
            sb.Append($" kc=0x{ev.keyDown.keyCode:X4} ch=0x{ev.keyDown.charScan.charCode:X2}");
            sb.Append($" sh=0x{ev.keyDown.shiftState:X4}");
        }
        else if ((ev.What & Events.evMessage) != 0)
        {
            string cmdName = ev.message.command switch
            {
                Views.cmMenu   => "cmMenu",
                Views.cmHelp   => "cmHelp",
                Views.cmQuit   => "cmQuit",
                Views.cmClose  => "cmClose",
                Views.cmNext   => "cmNext",
                Views.cmPrev   => "cmPrev",
                Views.cmZoom   => "cmZoom",
                Views.cmResize => "cmResize",
                _              => ev.message.command.ToString(),
            };
            sb.Append($" cmd={ev.message.command}({cmdName})");
        }
        else if ((ev.What & Events.evMouse) != 0)
        {
            sb.Append($" btn=0x{ev.mouse.buttons:X2}");
            sb.Append($" at=({ev.mouse.where.x},{ev.mouse.where.y})");
            if (ev.mouse.doubleClick) sb.Append(" dbl");
        }

        return sb.ToString();
    }

    // ---- internal ---------------------------------------------------------

    private static readonly StreamWriter _writer;
    private static readonly object _lock = new object();

    static InputTrace()
    {
        if (!Enabled) return;
        try
        {
            _writer = new StreamWriter(
                path: "sharpvision-input-trace.log",
                append: false,
                encoding: Encoding.UTF8)
            { AutoFlush = true };
            _writer.WriteLine(
                $"[START] SharpVision input trace — {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            _writer.WriteLine(
                "[START] Press F10, Alt+F, F1, Alt+X, click menu bar, click status line.");
            _writer.WriteLine(
                "[START] Each line = one trace stage.  Stages 1-2 = driver, 4 = TProgram, 8 = StatusLine, 7 = MenuView/MenuBar, 9 = Execute, 10 = Demo01.");
        }
        catch
        {
            // If the log file can't be opened, disable silently.
        }
    }

    private static void WriteRaw(string line)
    {
        lock (_lock)
        {
            try { _writer?.WriteLine(line); }
            catch { /* ignore I/O errors in trace */ }
        }
    }
}
