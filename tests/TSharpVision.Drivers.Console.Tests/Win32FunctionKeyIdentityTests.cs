using TSharpVision.Constants;
using TSharpVision.Drivers.Console;
using Xunit;

namespace TSharpVision.Tests.Drivers;

/// <summary>A physical F11/F12 and a physical Shift+F4/Shift+F5 reach the application as different keys.</summary>
public sealed class Win32FunctionKeyIdentityTests
{
    private const ushort VkF4 = 0x73, VkF5 = 0x74, VkF11 = 0x7A, VkF12 = 0x7B;
    private const uint Shift = Win32KeyTranslator.SHIFT_PRESSED;
    private const uint Ctrl = Win32KeyTranslator.LEFT_CTRL_PRESSED;
    private const uint Alt = Win32KeyTranslator.LEFT_ALT_PRESSED;

    private static TEvent Translate(ushort vk, uint state)
    {
        Assert.True(Win32KeyTranslator.TryTranslate(true, vk, '\0', state, out TEvent ev));
        Assert.Equal(Events.evKeyDown, ev.What);
        return ev;
    }

    [Theory]
    [InlineData(VkF11, 0u, Keys.kbF11, 0u)]
    [InlineData(VkF12, 0u, Keys.kbF12, 0u)]
    [InlineData(VkF4, Shift, Keys.kbShiftF4, Keys.kbShift)]
    [InlineData(VkF5, Shift, Keys.kbShiftF5, Keys.kbShift)]
    [InlineData(VkF11, Shift, Keys.kbShiftF11, Keys.kbShift)]
    [InlineData(VkF12, Shift, Keys.kbShiftF12, Keys.kbShift)]
    [InlineData(VkF11, Ctrl, Keys.kbCtrlF11, Keys.kbCtrlShift)]
    [InlineData(VkF12, Ctrl, Keys.kbCtrlF12, Keys.kbCtrlShift)]
    [InlineData(VkF11, Alt, Keys.kbAltF11, Keys.kbAltShift)]
    [InlineData(VkF12, Alt, Keys.kbAltF12, Keys.kbAltShift)]
    public void PhysicalFunctionKeysKeepTheirIdentityAndModifierState(ushort vk, uint state, ushort expected, uint modifiers)
    {
        TEvent ev = Translate(vk, state);
        Assert.Equal(expected, ev.keyDown.keyCode);
        Assert.Equal(modifiers, ev.keyDown.controlKeyState);
    }

    [Fact]
    public void F11IsNotShiftF4AndF12IsNotShiftF5()
    {
        Assert.NotEqual(Translate(VkF4, Shift).keyDown.keyCode, Translate(VkF11, 0).keyDown.keyCode);
        Assert.NotEqual(Translate(VkF5, Shift).keyDown.keyCode, Translate(VkF12, 0).keyDown.keyCode);
    }
}
