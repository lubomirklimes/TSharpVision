using TSharpVision.Constants;
using TSharpVision.Samples.TVDemo;
using Xunit;
using MousePart = TSharpVision.Samples.TVDemo.MouseStateView.MousePart;
using PaletteRole = TSharpVision.Samples.TVDemo.MouseStateView.PaletteRole;

namespace TSharpVision.Tests.Samples;

[Collection("NonParallel")]
public sealed class MouseDialogTests
{
    private static TEvent Mouse(ushort what, byte buttons = 0, int x = 0, int y = 0, uint flags = 0)
    {
        var ev = new TEvent { What = what };
        ev.mouse.where = new TPoint(x, y);
        ev.mouse.buttons = buttons;
        ev.mouse.eventFlags = flags;
        return ev;
    }

    private static MouseStateView NewView() =>
        new(new TRect(0, 0, MouseStateView.ViewW, MouseStateView.ViewH), new MouseState());

    [Fact]
    public void DiagramRowsAreUniformAndRegionsStayInsideTheOutline()
    {
        Assert.Equal(MouseStateView.ViewH, MouseStateView.Diagram.Length);
        Assert.All(MouseStateView.Diagram, row => Assert.Equal(MouseStateView.DiagramW, row.Length));
        foreach (MousePart part in Enum.GetValues<MousePart>())
            Assert.Contains(MouseStateView.Regions, r => r.Part == part);
        foreach (var span in MouseStateView.Regions)
        {
            string cells = MouseStateView.Diagram[span.Y].Substring(span.X, span.Width);
            // A region never recolours the outline or the dividers between regions.
            Assert.DoesNotContain('|', cells);
            Assert.DoesNotContain('/', cells);
            Assert.DoesNotContain('\\', cells);
        }
        Assert.Equal('O', MouseStateView.Diagram[MouseStateView.WheelY][MouseStateView.WheelX]);
    }

    [Theory]
    [InlineData(Events.mbLeftButton, (int)MousePart.Left)]
    [InlineData(Events.mbMiddleButton, (int)MousePart.Middle)]
    [InlineData(Events.mbRightButton, (int)MousePart.Right)]
    public void PressedButtonHighlightsOnlyItsRegion(ushort button, int pressedPart)
    {
        var pressed = (MousePart)pressedPart;
        var view = NewView();
        try
        {
            var down = Mouse(Events.evMouseDown, (byte)button, 80, 23);
            view.HandleEvent(ref down);
            Assert.Equal(Events.evMouseDown, down.What); // Not consumed.
            foreach (MousePart part in Enum.GetValues<MousePart>())
                Assert.Equal(part == pressed ? PaletteRole.Pressed : PaletteRole.Region, view.PartRole(part));
            Assert.Equal("Position: (80, 23)", view.TextLine(1));

            var up = Mouse(Events.evMouseUp, 0, 81, 23);
            view.HandleEvent(ref up);
            Assert.Equal(PaletteRole.Region, view.PartRole(pressed));
            Assert.Equal("Buttons: - - -", view.TextLine(2));
            Assert.Equal("Position: (81, 23)", view.TextLine(1));
        }
        finally { view.ShutDown(); }
    }

    [Fact]
    public void ChordHighlightsEveryHeldButton()
    {
        var view = NewView();
        try
        {
            var down = Mouse(Events.evMouseDown, (byte)(Events.mbLeftButton | Events.mbRightButton));
            view.HandleEvent(ref down);
            Assert.Equal(PaletteRole.Pressed, view.PartRole(MousePart.Left));
            Assert.Equal(PaletteRole.Region, view.PartRole(MousePart.Middle));
            Assert.Equal(PaletteRole.Pressed, view.PartRole(MousePart.Right));
            Assert.Equal("Buttons: L - R", view.TextLine(2));
        }
        finally { view.ShutDown(); }
    }

    [Fact]
    public void WheelShowsDirectionUntilTheNextMouseEvent()
    {
        var view = NewView();
        try
        {
            Assert.Equal("Wheel: 0", view.TextLine(3));
            Assert.Equal('O', view.WheelGlyph);

            var wheel = Mouse(Events.evMouseWheel, flags: Events.meWheelUp);
            view.HandleEvent(ref wheel);
            Assert.Equal("Wheel: Up  net +1", view.TextLine(3));
            Assert.Equal('^', view.WheelGlyph);
            Assert.Equal(PaletteRole.Pressed, view.PartRole(MousePart.Wheel));
            Assert.Equal(PaletteRole.Region, view.PartRole(MousePart.Middle));

            wheel = Mouse(Events.evMouseWheel, flags: Events.meWheelDown);
            view.HandleEvent(ref wheel);
            view.HandleEvent(ref wheel);
            Assert.Equal("Wheel: Down  net -1", view.TextLine(3));
            Assert.Equal('v', view.WheelGlyph);
            Assert.Equal(-1, view.State.WheelDelta);

            var move = Mouse(Events.evMouseMove, x: 5, y: 6);
            view.HandleEvent(ref move);
            Assert.Equal("Wheel: 0  net -1", view.TextLine(3));
            Assert.Equal('O', view.WheelGlyph);
            Assert.Equal(PaletteRole.Region, view.PartRole(MousePart.Wheel));
        }
        finally { view.ShutDown(); }
    }

    [Fact]
    public void CloseButtonSitsBottomRightClearOfTheDiagramAndText()
    {
        var dialog = new MouseDialog();
        try
        {
            TButton? close = null;
            dialog.ForEachView(v => { if (v is TButton b) close = b; });
            Assert.NotNull(close);
            Assert.Equal(MouseDialog.DlgW - 3, close.origin.x + close.size.x);
            Assert.Equal(MouseDialog.DlgH - 2, close.origin.y + close.size.y);
            // Right of the diagram and below the last text line.
            Assert.True(close.origin.x >= dialog.View.origin.x + MouseStateView.TextX);
            Assert.True(close.origin.y > dialog.View.origin.y + 3);
            Assert.True(dialog.View.origin.x + dialog.View.size.x <= dialog.size.x - 2);
            Assert.True(dialog.View.origin.y + dialog.View.size.y <= dialog.size.y - 1);
        }
        finally { dialog.ShutDown(); }
    }
}
