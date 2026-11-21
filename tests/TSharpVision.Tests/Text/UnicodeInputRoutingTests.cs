using TSharpVision;
using TSharpVision.Constants;
using TSharpVision.Tests.Infrastructure;
using Xunit;

namespace TSharpVision.Tests.Text;

/// <summary>
/// Characters outside Latin-1 arrive with <c>keyCode == kbNoKey</c> (U7 visual matrix, defects D1 and D2). Views
/// without a key or hotkey must leave such keystrokes to the focused input instead of matching kbNoKey.
/// </summary>
[Collection("NonParallel")]
public sealed class UnicodeInputRoutingTests : IDisposable
{
    private readonly DriverScope _driver = new();
    private readonly TCommandSet _savedCommands = new(TView.curCommandSet);

    public void Dispose()
    {
        TView.curCommandSet = _savedCommands;
        _driver.Dispose();
    }

    private static TEvent Typed(string text)
    {
        var ev = new TEvent { What = Events.evKeyDown };
        ev.keyDown.keyCode = Keys.kbNoKey;
        ev.keyDown.text = text;
        return ev;
    }

    [Theory]
    [InlineData("\U0001F44D\U0001F3FD")]
    [InlineData("漢")]
    [InlineData("š")]
    public void StatusItemWithoutAKeyDoesNotSwallowTypedText(string text)
    {
        const ushort command = 1200;
        TView.EnableCommand(command);
        var status = new TStatusLine(new TRect(0, 24, 80, 25),
            new TStatusDef(0, 0xFFFF) + new TStatusItem("Hint only", Keys.kbNoKey, command));

        TEvent ev = Typed(text);
        status.HandleEvent(ref ev);

        Assert.Equal(Events.evKeyDown, ev.What);
        Assert.Equal(text, ev.keyDown.text);
    }

    [Fact]
    public void StatusItemWithAKeyStillConvertsIt()
    {
        const ushort command = 1201;
        TView.EnableCommand(command);
        var status = new TStatusLine(new TRect(0, 24, 80, 25),
            new TStatusDef(0, 0xFFFF) + new TStatusItem("~F5~ Go", Keys.kbF5, command));
        var ev = new TEvent { What = Events.evKeyDown };
        ev.keyDown.keyCode = Keys.kbF5;
        status.HandleEvent(ref ev);
        Assert.Equal((Events.evCommand, command), (ev.What, ev.message.command));
    }

    private static TGroup Host()
    {
        var group = new TGroup(new TRect(0, 0, 40, 10));
        group.state |= Views.sfVisible;
        return group;
    }

    [Theory]
    [InlineData("No hotkey")]
    [InlineData("~漢~字")]
    public void ClusterItemWithoutALatinHotkeyIgnoresTypedText(string item)
    {
        TGroup host = Host();
        var boxes = new TCheckBoxes(new TRect(1, 1, 30, 2), new TSItem(item, null));
        host.Insert(boxes);

        TEvent ev = Typed("\U0001F44D\U0001F3FD");
        boxes.HandleEvent(ref ev);

        Assert.Equal(Events.evKeyDown, ev.What);
        Assert.Equal(0u, boxes.value);
    }

    [Fact]
    public void LabelWithoutAHotkeyIgnoresTypedText()
    {
        TGroup host = Host();
        var target = new TCheckBoxes(new TRect(1, 2, 30, 3), new TSItem("~A~lpha", null));
        var label = new TLabel(new TRect(1, 1, 20, 2), "Plain label", target);
        host.Insert(target);
        host.Insert(label);

        TEvent ev = Typed("漢");
        label.HandleEvent(ref ev);

        Assert.Equal(Events.evKeyDown, ev.What);
    }
}
