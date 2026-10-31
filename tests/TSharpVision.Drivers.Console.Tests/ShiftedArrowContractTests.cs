using TSharpVision.Constants;
using Xunit;
namespace TSharpVision.Drivers.Console.Tests;

// TV-1.1-FIX-3: Shift+arrow keeps the plain arrow code and reports Shift in controlKeyState;
// TView.DragView resizes on that combination.
public sealed class ShiftedArrowContractTests
{
    [Theory]
    [InlineData((ushort)0x25, (ushort)0x4B, Keys.kbLeft)]
    [InlineData((ushort)0x27, (ushort)0x4D, Keys.kbRight)]
    [InlineData((ushort)0x26, (ushort)0x48, Keys.kbUp)]
    [InlineData((ushort)0x28, (ushort)0x50, Keys.kbDown)]
    public void ShiftArrowIsArrowWithShift(ushort vk, ushort scan, ushort code)
    {
        var driver = new Win32ConsoleDriver();
        Assert.True(driver.ProcessKeyRecord(true, vk, scan, '\0',
            Win32KeyTranslator.SHIFT_PRESSED | Win32KeyTranslator.ENHANCED_KEY));
        Assert.True(driver.ReadKeyEvent(out var ev));
        Assert.Equal(Events.evKeyDown, ev.What);
        Assert.Equal(code, ev.keyDown.keyCode);
        Assert.NotEqual(0u, ev.keyDown.controlKeyState & Keys.kbShift);
    }

    [Theory]
    [InlineData((ushort)0x25, (ushort)0x4B, Keys.kbLeft)]
    [InlineData((ushort)0x28, (ushort)0x50, Keys.kbDown)]
    public void PlainArrowHasNoShift(ushort vk, ushort scan, ushort code)
    {
        var driver = new Win32ConsoleDriver();
        Assert.True(driver.ProcessKeyRecord(true, vk, scan, '\0', Win32KeyTranslator.ENHANCED_KEY));
        Assert.True(driver.ReadKeyEvent(out var ev));
        Assert.Equal(code, ev.keyDown.keyCode);
        Assert.Equal(0u, ev.keyDown.controlKeyState & Keys.kbShift);
    }
}
