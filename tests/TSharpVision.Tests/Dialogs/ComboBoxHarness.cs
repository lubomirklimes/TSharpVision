// Shared helpers for the TComboBox tests: a headless program that runs scripted steps whenever its queue runs dry,
// and builders for the events the drivers deliver.
using System.Text;
using TSharpVision;
using TSharpVision.Constants;
using Xunit;

namespace TSharpVision.Tests.Dialogs;

/// <summary>
/// A program whose modal loops run queued test steps when no input is waiting. Each step usually asserts on the state
/// the previous input left and enqueues the next input. When the steps are exhausted and the queue stays empty the
/// program feeds cmCancel to whatever loop is running and records the timeout, so a broken script cannot hang.
/// </summary>
internal sealed class ComboApp : TProgram
{
    private readonly Queue<Action> _steps = new();
    private int _idle;

    public ComboApp()
    {
        Pending = default;
    }

    public bool TimedOut { get; private set; }

    public int StepsLeft => _steps.Count;

    public int ResizeCommands { get; private set; }

    public override void HandleEvent(ref TEvent @event)
    {
        if (@event.What == Events.evCommand && @event.message.command == Views.cmScreenResized)
            ResizeCommands++;
        base.HandleEvent(ref @event);
    }

    public void Step(Action step) => _steps.Enqueue(step);

    protected override int GetIdleWaitTimeout() => 0;

    public override void GetEvent(ref TEvent @event)
    {
        base.GetEvent(ref @event);
        if (@event.What != Events.evNothing)
        {
            _idle = 0;
            return;
        }
        if (_steps.Count > 0)
        {
            _idle = 0;
            _steps.Dequeue()();
            return;
        }
        if (++_idle > 20)
        {
            TimedOut = true;
            @event = default;
            @event.What = Events.evCommand;
            @event.message.command = Views.cmCancel;
        }
    }

    /// <summary>Characters of a screen row.</summary>
    public string Row(int y, int x, int count)
    {
        var sb = new StringBuilder(count);
        for (int i = 0; i < count; i++)
        {
            char c = Cell(x + i, y).Character;
            sb.Append(c == '\0' ? ' ' : c);
        }
        return sb.ToString();
    }

    public TScreenChar Cell(int x, int y) => buffer!.Data[y * size.x + x];

    public TScreenChar[] Snapshot() => buffer!.Data.ToArray();
}

internal static class ComboInput
{
    public static TEvent Key(ushort keyCode, uint modifiers = 0)
    {
        var ev = new TEvent { What = Events.evKeyDown };
        ev.keyDown.keyCode = keyCode;
        ev.keyDown.charScan = new CharScanType(keyCode);
        ev.keyDown.controlKeyState = modifiers;
        return ev;
    }

    public static TEvent Typed(string text)
    {
        TEvent ev = Key(text.Length == 1 && text[0] < 0x80 ? text[0] : (ushort)0);
        ev.keyDown.text = text;
        return ev;
    }

    public static TEvent AltDown => Key(Keys.kbDown, Keys.kbLeftAlt);

    public static TEvent Mouse(ushort what, TPoint where)
    {
        var ev = new TEvent { What = what };
        ev.mouse.where = where;
        ev.mouse.buttons = what == Events.evMouseUp ? (byte)0 : (byte)Events.mbLeftButton;
        return ev;
    }

    public static void Send(params TEvent[] events)
    {
        foreach (TEvent ev in events) TEventQueue.Enqueue(ev);
    }

    public static void Type(string text)
    {
        System.Globalization.TextElementEnumerator e = System.Globalization.StringInfo.GetTextElementEnumerator(text);
        while (e.MoveNext())
            TEventQueue.Enqueue(Typed((string)e.Current));
    }

    public static void Click(TPoint where)
    {
        TEventQueue.Enqueue(Mouse(Events.evMouseDown, where));
        TEventQueue.Enqueue(Mouse(Events.evMouseUp, where));
    }

    /// <summary>Empties the shared input queue and leaves the mouse released, so one test cannot feed the next.</summary>
    public static void ResetQueue()
    {
        TEventQueue.Resume();
        TEventQueue.ClearPosted();
        TEvent ev = default;
        do TEventQueue.GetNextEvent(ref ev); while (ev.What != Events.evNothing);
        TEventQueue.Enqueue(Mouse(Events.evMouseUp, new TPoint(-50, -50)));
        TEventQueue.GetNextEvent(ref ev);
    }

    /// <summary>The screen position of a cell of <paramref name="view"/>.</summary>
    public static TPoint At(TView view, int x, int y) => view.MakeGlobal(new TPoint(x, y));
}

/// <summary>Counts the change notifications of a combo box.</summary>
internal sealed class ComboEvents
{
    public int Selection;
    public int Text;
    public readonly List<string> Log = new();

    public ComboEvents(TComboBox combo)
    {
        combo.SelectionChanged += (_, _) => { Selection++; Log.Add($"sel:{combo.SelectedIndex}"); };
        combo.TextChanged += (_, _) => { Text++; Log.Add($"text:{combo.Text}"); };
    }
}

/// <summary>The sample dialog of the tests: a Case and an Encoding combo box, an input line and OK/Cancel.</summary>
internal sealed class ComboDialog
{
    public static readonly string[] CaseItems = { "Keep", "Lower", "Upper" };
    public static readonly string[] EncodingItems = { "UTF-8", "UTF-16 LE", "Windows-1250", "ISO-8859-2" };

    public readonly TDialog Dialog;
    public readonly TComboBox Case;
    public readonly TComboBox Encoding;
    public readonly TInputLine Line;
    public readonly TButton Ok;

    public ComboDialog(TRect? bounds = null, IEnumerable<string>? caseItems = null)
    {
        Dialog = new TDialog(bounds ?? new TRect(10, 3, 60, 16), "Combo");
        Case = new TComboBox(new TRect(12, 2, 30, 3), caseItems ?? CaseItems);
        Encoding = new TComboBox(new TRect(12, 4, 34, 5), EncodingItems, editable: true, maxLength: 40);
        Line = new TInputLine(new TRect(12, 6, 30, 7), 20);
        Ok = new TButton(new TRect(10, 9, 20, 11), "O~K~", Views.cmOK, ButtonConstants.bfDefault);
        Dialog.Insert(new TLabel(new TRect(2, 2, 11, 3), "~C~ase:", Case));
        Dialog.Insert(Case);
        Dialog.Insert(new TLabel(new TRect(2, 4, 11, 5), "~E~ncoding:", Encoding));
        Dialog.Insert(Encoding);
        Dialog.Insert(Line);
        Dialog.Insert(Ok);
        Dialog.Insert(new TButton(new TRect(22, 9, 34, 11), "Cancel", Views.cmCancel, ButtonConstants.bfNormal));
        Case.Select();
    }

    public TComboBoxDropDown Popup(TComboBox combo) => Assert.IsType<TComboBoxDropDown>(combo.DropDown);

    public static int ChildCount(TGroup group)
    {
        int n = 0;
        group.ForEachView(_ => n++);
        return n;
    }
}
