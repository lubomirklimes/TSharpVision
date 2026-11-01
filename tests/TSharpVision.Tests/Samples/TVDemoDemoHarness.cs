using TSharpVision.Constants;
using TSharpVision.Samples.TVDemo;
using TSharpVision.Tests.Dialogs;
using TSharpVision.Tests.Infrastructure;
using Xunit;

namespace TSharpVision.Tests.Samples;

// Runs the production TVDemo dispatch and its real nested modal loops. Steps run
// only after all preceding queued/pending input has been delivered.
internal sealed class ScriptedDemoApp : TVDemoApp
{
    private readonly Queue<Action> _steps = new();
    private int _empty;
    internal ScriptedDemoApp() { Pending = default; }
    internal void Step(Action step) => _steps.Enqueue(step);
    internal T Modal<T>() where T : TView => Assert.IsAssignableFrom<T>(TView.ModalTopView);
    protected override int GetIdleWaitTimeout() => 0;
    public override void GetEvent(ref TEvent ev)
    {
        base.GetEvent(ref ev);
        if (ev.What != Events.evNothing) { _empty = 0; return; }
        if (_steps.Count > 0) { _empty = 0; _steps.Dequeue()(); return; }
        if (++_empty > 100) throw new InvalidOperationException("Script exhausted with a modal view still open: " + TView.ModalTopView?.GetType().Name);
    }

    internal void RunCommand(ushort command)
    {
        var ev = CommandEvent(command);
        HandleEvent(ref ev);
        Assert.Equal(Events.evNothing, ev.What);
        Assert.Empty(_steps);
        Assert.Null(TView.ModalTopView);
    }

    internal void DismissResult(string expected)
    {
        Step(() =>
        {
            Assert.Contains(expected, Text(Modal<TDialog>()));
            ComboInput.Send(ComboInput.Key(Keys.kbEsc));
        });
    }

    internal static TEvent CommandEvent(ushort command)
    {
        var ev = new TEvent { What = Events.evCommand };
        ev.message.command = command;
        return ev;
    }

    internal static void SendCommand(ushort command) => TEventQueue.Enqueue(CommandEvent(command));

    internal static IEnumerable<TView> Descendants(TGroup group)
    {
        var result = new List<TView>();
        group.ForEachView(v =>
        {
            result.Add(v);
            if (v is TGroup child) result.AddRange(Descendants(child));
        });
        return result;
    }

    internal static string Text(TGroup group) => string.Join("\n", Descendants(group).OfType<TStaticText>().Select(v =>
    {
        v.GetText(out string text);
        return text;
    }));

    internal static TButton Button(TGroup group, ushort code) =>
        Assert.Single(Descendants(group).OfType<TButton>(), b => b.Command == code);
}

internal sealed class DemoTestScope : IDisposable
{
    private readonly DriverScope _driver;
    private readonly StreamableRegistryScope _registry = new();
    private readonly TCommandSet _commands = new();
    private readonly TEditor.TEditorDialog _editorDialog = TEditor.editorDialog;
    private readonly string _directory = Directory.GetCurrentDirectory();
    private readonly Dictionary<ushort, string[]> _history = CaptureHistory();

    private static Dictionary<ushort, string[]> CaptureHistory()
    {
        var histories = new Dictionary<ushort, string[]>();
        for (int id = 0; id <= ushort.MaxValue; id++)
        {
            int count = THistoryList.Count((ushort)id);
            if (count != 0) histories[(ushort)id] =
                Enumerable.Range(0, count).Select(i => THistoryList.Str((ushort)id, i)).ToArray();
        }
        return histories;
    }

    internal ScriptedDemoApp App { get; }

    // wrapDriver (TV12e) installs a test driver around the NullDriver before the app is built; DriverScope restores.
    internal DemoTestScope(int width = 80, int height = 25, Func<TSharpVision.Drivers.IDriver, TSharpVision.Drivers.IDriver>? wrapDriver = null)
    {
        _driver = new DriverScope(checked((ushort)width), checked((ushort)height));
        if (wrapDriver != null) TDisplay.driver = wrapDriver(_driver.Driver);
        TView.GetCommands(_commands);
        ComboInput.ResetQueue();
        App = new ScriptedDemoApp();
    }

    public void Dispose()
    {
        try { App.ShutDown(); }
        finally
        {
            Directory.SetCurrentDirectory(_directory);
            TEditor.editorDialog = _editorDialog;
            TView.SetCommands(_commands);
            THistoryList.Clear();
            foreach (var (id, entries) in _history)
                foreach (string entry in entries.Reverse()) THistoryList.Add(id, entry);
            ComboInput.ResetQueue();
            _registry.Dispose();
            _driver.Dispose();
        }
    }
}
