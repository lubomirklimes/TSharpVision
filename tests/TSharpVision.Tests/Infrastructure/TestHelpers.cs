// Shared probe/helper types for TSharpVision.Tests.
// These are duplicates of the file-local helpers in TSharpVision.Demo/Program.cs,
// copied here to keep the demo source unchanged.
using TSharpVision;
using TSharpVision.Constants;

namespace TSharpVision.Tests.Infrastructure;

// Minimal selectable view that counts HandleEvent hits.
public sealed class ProbeView : TView
{
    public int Hits;
    public ProbeView(TRect bounds) : base(bounds)
    {
        options |= Views.ofSelectable;
        eventMask = 0xFFFF;
    }
    public override void HandleEvent(ref TEvent ev) { Hits++; }
}

// TGroup that captures the last evBroadcast command delivered to the owner.
public sealed class BroadcastProbeGroup : TGroup
{
    public ushort LastCmd;
    public BroadcastProbeGroup(TRect bounds) : base(bounds)
    {
        options = (ushort)(options & ~Views.ofSelectable);
    }
    public override void HandleEvent(ref TEvent e)
    {
        if (e.What == Events.evBroadcast)
        {
            LastCmd = e.message.command;
        }
    }
    public override void GetEvent(ref TEvent e) => TScreen.GetEvent(ref e);
    public override void PutEvent(ref TEvent e)
    {
        if (e.What == Events.evBroadcast) LastCmd = e.message.command;
        TEventQueue.Enqueue(e);
    }
}

// TStatusLine subclass that tracks DrawView calls and exposes Items.
public sealed class ProbeStatusLine : TStatusLine
{
    public int PublicDrawCount;
    public ProbeStatusLine(TRect b, TStatusDef d) : base(b, d) { }
    public TStatusItem PublicItems => Items;
    public override void DrawView() { PublicDrawCount++; /* skip base — no buffer */ }
}

// TStatusLine subclass that returns context-specific hint strings.
public sealed class HintStatusLine : TStatusLine
{
    public HintStatusLine(TRect b, TStatusDef d) : base(b, d) { }
    public override string Hint(ushort ctx) => ctx switch
    {
        0 => "main menu",
        2 => "save",
        _ => "",
    };
}

// TGroup suitable as a root owner for subview tests (no auto-select).
public sealed class TestGroup : TGroup
{
    public TestGroup(TRect bounds) : base(bounds)
    {
        options = (ushort)(options & ~Views.ofSelectable);
    }
    public override void GetEvent(ref TEvent e) => TScreen.GetEvent(ref e);
    public override void PutEvent(ref TEvent e) => TEventQueue.Enqueue(e);
}

// Concrete TListViewer with no abstract overrides (GetText returns "").
public sealed class ProbeListViewer : TListViewer
{
    public ProbeListViewer(TRect bounds, ushort cols, TScrollBar h, TScrollBar v)
        : base(bounds, cols, h, v) { }
}

// TView that captures evBroadcast commands (for cluster/label tests).
public sealed class BroadcastProbe : TView
{
    public ushort LastBroadcast;
    public BroadcastProbe(TRect bounds) : base(bounds)
    {
        eventMask = 0xFFFF;
        options |= Views.ofSelectable;
    }
    public void Reset() { LastBroadcast = 0; }
    public override void HandleEvent(ref TEvent ev)
    {
        if (ev.What == Events.evBroadcast)
            LastBroadcast = ev.message.command;
    }
}

// Minimal TProgram subclass usable in headless tests.
public sealed class TestProgram : TProgram
{
    public TestProgram() : base() { }
}

// TDeskTop subclass that fires a callback on TileError().
public sealed class TileErrorDeskTop : TDeskTop
{
    private readonly Action _onError;
    public TileErrorDeskTop(TRect bounds, Action onError) : base(bounds)
        => _onError = onError;
    public override void TileError() => _onError();
}

// A view that writes a fixed character to its entire area via TVWrite.
public sealed class TVWriteTestView : TView
{
    private readonly char _ch;
    private readonly byte _attr;
    public TVWriteTestView(TRect bounds, char ch, byte attr) : base(bounds)
    {
        _ch = ch; _attr = attr;
        options |= Views.ofSelectable;
        state |= Views.sfVisible;
    }
    public override void DrawView() { Draw(); }
    public override void Draw()
    {
        var buf = new TDrawBuffer();
        buf.moveChar(0, _ch, _attr, size.x);
        for (int row = 0; row < size.y; row++)
            WriteBuf(0, row, size.x, 1, buf);
    }
}

// Helper to walk a view tree and check if any TButton/TLabel/TStaticText
// has text matching the given string.
public static class ViewTreeHelpers
{
    public static bool ViewContainsText(TGroup g, string text)
    {
        if (g?.last == null) return false;
        TView p = g.last.Next;
        do
        {
            if (p is TButton btn && btn.Title?.Contains(text) == true) return true;
            // TLabel/TStaticText.Text is protected — use GetText() API instead.
            if (p is TStaticText st) { st.GetText(out string t); if (t?.Contains(text) == true) return true; }
            if (p is TGroup sub && ViewContainsText(sub, text)) return true;
            p = p.Next;
        } while (p != g.last.Next);
        return false;
    }
}

// TView that ends its own modal with a given exit command.
public sealed class ModalView : TView
{
    private readonly ushort _exitCommand;
    public ModalView(TRect bounds, ushort exitCommand) : base(bounds)
    {
        options |= Views.ofSelectable;
        _exitCommand = exitCommand;
    }
    public override ushort Execute()
    {
        owner?.EndModal(_exitCommand);
        return _exitCommand;
    }
}
