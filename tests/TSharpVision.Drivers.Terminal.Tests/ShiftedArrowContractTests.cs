using System.Text;
using TSharpVision.Constants;
using TSharpVision.Drivers;
using Xunit;
namespace TSharpVision.Drivers.Terminal.Tests;

// TV-1.1-FIX-3: xterm reports Shift+arrow as CSI 1;2 <final>. The decoder keeps the plain arrow code and
// reports Shift in controlKeyState; TView.DragView resizes on that combination. A plain CSI <final> has no Shift.
public sealed class ShiftedArrowContractTests
{
    [Theory]
    [InlineData("\x1b[1;2D", Keys.kbLeft)]
    [InlineData("\x1b[1;2C", Keys.kbRight)]
    [InlineData("\x1b[1;2A", Keys.kbUp)]
    [InlineData("\x1b[1;2B", Keys.kbDown)]
    public void ShiftArrowIsArrowWithShift(string sequence, ushort code)
    {
        AnsiKeyDecoder.TryDecode(Encoding.ASCII.GetBytes(sequence), out var ev, out _);
        Assert.Equal(code, ev.keyDown.keyCode);
        Assert.NotEqual(0u, ev.keyDown.controlKeyState & Keys.kbShift);
    }

    [Theory]
    [InlineData("\x1b[D", Keys.kbLeft)]
    [InlineData("\x1b[B", Keys.kbDown)]
    public void PlainArrowHasNoShift(string sequence, ushort code)
    {
        AnsiKeyDecoder.TryDecode(Encoding.ASCII.GetBytes(sequence), out var ev, out _);
        Assert.Equal(code, ev.keyDown.keyCode);
        Assert.Equal(0u, ev.keyDown.controlKeyState & Keys.kbShift);
    }
}
