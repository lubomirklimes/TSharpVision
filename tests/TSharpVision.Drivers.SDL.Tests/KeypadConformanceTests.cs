using TSharpVision.Constants;
using TSharpVision.Drivers;
using TSharpVision.Drivers.SDL.Gpu;
namespace TSharpVision.Drivers.SDL.Tests;

public sealed class KeypadConformanceTests
{
    public static IEnumerable<object[]> Cases()
    {
        (uint Native, ushort Code)[] keys =
        [
            (0x40000062, Keys.kbKeypad0),
            (0x40000059, Keys.kbKeypad1),
            (0x4000005A, Keys.kbKeypad2),
            (0x4000005B, Keys.kbKeypad3),
            (0x4000005C, Keys.kbKeypad4),
            (0x4000005D, Keys.kbKeypad5),
            (0x4000005E, Keys.kbKeypad6),
            (0x4000005F, Keys.kbKeypad7),
            (0x40000060, Keys.kbKeypad8),
            (0x40000061, Keys.kbKeypad9),
            (0x40000063, Keys.kbKeypadDecimal),
            (0x40000054, Keys.kbKeypadDivide),
            (0x40000055, Keys.kbKeypadMultiply),
            (0x40000058, Keys.kbKeypadEnter),
            (0x40000053, Keys.kbNumLock),
            (0x40000056, Keys.kbGrayMinus),
            (0x40000057, Keys.kbGrayPlus),
        ];
        foreach (var key in keys)
            foreach (bool gpu in new[] { false, true })
                foreach (ushort mod in new ushort[] { 0, 0x1000 })
                    yield return new object[] { gpu, key.Native, key.Code, mod };
    }
    [Theory, MemberData(nameof(Cases))]
    public void NativePressRepeatReleaseAndText(bool gpu, uint native, ushort code, ushort mod)
    {
        IDriver driver;
        if (gpu)
        {
            var d = new SDLGpuDriver();
            Assert.True(d.ProcessOrdinaryKeyDown(native, mod));
            Assert.True(d.ProcessOrdinaryKeyDown(native, mod));
            d.ProcessTextInput(",", mod);
            Assert.True(d.ProcessOrdinaryKeyUp(native, mod));
            driver = d;
        }
        else
        {
            var d = new SDLDriver();
            Assert.True(d.ProcessOrdinaryKeyDown(native, mod));
            Assert.True(d.ProcessOrdinaryKeyDown(native, mod));
            d.ProcessTextInput(",", mod);
            Assert.True(d.ProcessOrdinaryKeyUp(native, mod));
            driver = d;
        }
        var events = new List<TEvent>();
        while (driver.ReadKeyEvent(out var ev)) events.Add(ev);
        Assert.Equal(new[] { Events.evKeyDown, Events.evKeyDown, Events.evKeyDown, Events.evKeyUp }, events.Select(e => e.What));
        Assert.All(new[] { events[0], events[1], events[3] }, e => {
            Assert.Equal(code, e.keyDown.keyCode);
            Assert.Equal(new CharScanType(code).ToUShort(), e.keyDown.charScan.ToUShort());
            Assert.Empty(e.keyDown.text);
        });
        Assert.Equal(",", events[2].keyDown.text);
        Assert.Equal((ushort)',', events[2].keyDown.keyCode);
        Assert.All(events, e => Assert.Equal(mod == 0 ? 0u : Keys.kbNumState, e.keyDown.controlKeyState));
        Assert.True(SdlKeyTranslator.TryTranslate(native, mod, ',', out var translated));
        Assert.Equal(code, translated.keyDown.keyCode);
        Assert.Equal(",", translated.keyDown.text);
        Assert.NotEqual((ushort)'1', code);
    }

    private static List<TEvent> Run(bool gpu, Action<Func<uint, ushort, bool>, Action<string, ushort>, Func<uint, ushort, bool>> script)
    {
        IDriver driver;
        if (gpu)
        {
            var d = new SDLGpuDriver();
            script(d.ProcessOrdinaryKeyDown, d.ProcessTextInput, d.ProcessOrdinaryKeyUp);
            driver = d;
        }
        else
        {
            var d = new SDLDriver();
            script(d.ProcessOrdinaryKeyDown, d.ProcessTextInput, d.ProcessOrdinaryKeyUp);
            driver = d;
        }
        var events = new List<TEvent>();
        while (driver.ReadKeyEvent(out var ev)) events.Add(ev);
        return events;
    }

    [Fact]
    public void BothDriversAdvertiseDistinctNumericKeypad()
    {
        Assert.True(new SDLDriver().KeyboardCapabilities.HasFlag(KeyboardCapabilities.DistinctNumericKeypad));
        Assert.True(new SDLGpuDriver().KeyboardCapabilities.HasFlag(KeyboardCapabilities.DistinctNumericKeypad));
    }

    public static IEnumerable<object[]> StandardKeys()
    {
        (uint Native, ushort Mod, ushort Code)[] keys =
        [
            (SdlKeyTranslator.SDLK_ESCAPE, 0, Keys.kbEsc), (SdlKeyTranslator.SDLK_RETURN, 0, Keys.kbEnter),
            (SdlKeyTranslator.SDLK_TAB, 0, Keys.kbTab), (SdlKeyTranslator.SDLK_TAB, 1, Keys.kbShiftTab),
            (SdlKeyTranslator.SDLK_BACKSPACE, 0, Keys.kbBack),
            (SdlKeyTranslator.SDLK_F1, 0, Keys.kbF1), (SdlKeyTranslator.SDLK_F2, 0, Keys.kbF2),
            (SdlKeyTranslator.SDLK_F3, 0, Keys.kbF3), (SdlKeyTranslator.SDLK_F4, 0, Keys.kbF4),
            (SdlKeyTranslator.SDLK_F5, 0, Keys.kbF5), (SdlKeyTranslator.SDLK_F6, 0, Keys.kbF6),
            (SdlKeyTranslator.SDLK_F7, 0, Keys.kbF7), (SdlKeyTranslator.SDLK_F8, 0, Keys.kbF8),
            (SdlKeyTranslator.SDLK_F9, 0, Keys.kbF9), (SdlKeyTranslator.SDLK_F10, 0, Keys.kbF10),
            (SdlKeyTranslator.SDLK_F11, 0, Keys.kbF11), (SdlKeyTranslator.SDLK_F12, 0, Keys.kbF12),
            (SdlKeyTranslator.SDLK_F10, 1, Keys.kbShiftF10), (SdlKeyTranslator.SDLK_F11, 0x40, Keys.kbCtrlF11),
            (SdlKeyTranslator.SDLK_F12, 0x100, Keys.kbAltF12),
            (SdlKeyTranslator.SDLK_INSERT, 0, Keys.kbIns), (SdlKeyTranslator.SDLK_HOME, 0, Keys.kbHome),
            (SdlKeyTranslator.SDLK_PAGEUP, 0, Keys.kbPgUp), (SdlKeyTranslator.SDLK_DELETE, 0, Keys.kbDel),
            (SdlKeyTranslator.SDLK_END, 0, Keys.kbEnd), (SdlKeyTranslator.SDLK_PAGEDOWN, 0, Keys.kbPgDn),
            (SdlKeyTranslator.SDLK_UP, 0, Keys.kbUp), (SdlKeyTranslator.SDLK_LEFT, 0, Keys.kbLeft),
            (SdlKeyTranslator.SDLK_DOWN, 0, Keys.kbDown), (SdlKeyTranslator.SDLK_RIGHT, 0, Keys.kbRight),
            (SdlKeyTranslator.SDLK_LEFT, 0x40, Keys.kbCtrlLeft),
            ('a', 0x40, Keys.kbCtrlA), ('x', 0x100, Keys.kbAltX), ('1', 0x100, Keys.kbAlt1),
        ];
        foreach (var key in keys)
            foreach (bool gpu in new[] { false, true })
                yield return new object[] { gpu, key.Native, key.Mod, key.Code };
    }

    [Theory, MemberData(nameof(StandardKeys))]
    public void StandardKeysPressRepeatAndReleaseKeepOneIdentity(bool gpu, uint native, ushort mod, ushort code)
    {
        var events = Run(gpu, (down, _, up) =>
        {
            Assert.True(down(native, mod));
            Assert.True(down(native, mod));
            Assert.True(up(native, mod));
        });
        Assert.Equal(new[] { Events.evKeyDown, Events.evKeyDown, Events.evKeyUp }, events.Select(e => e.What));
        Assert.All(events, e =>
        {
            Assert.Equal(code, e.keyDown.keyCode);
            Assert.Equal(SdlKeyTranslator.ToShiftState(mod), e.keyDown.controlKeyState);
            Assert.Empty(e.keyDown.text);
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MainKeysAndKeypadKeysAreDistinct(bool gpu)
    {
        var events = Run(gpu, (down, text, up) =>
        {
            Assert.False(down('1', 0x1000));          // main 1: identity arrives with its text
            text("1", 0x1000);
            Assert.True(up('1', 0x1000));
            Assert.True(down(0x40000059, 0x1000));    // KP1
            Assert.True(up(0x40000059, 0x1000));
            Assert.True(down(SdlKeyTranslator.SDLK_RETURN, 0));
            Assert.True(up(SdlKeyTranslator.SDLK_RETURN, 0));
            Assert.True(down(SdlKeyTranslator.SDLK_KP_ENTER, 0));
            Assert.True(up(SdlKeyTranslator.SDLK_KP_ENTER, 0));
        });
        Assert.Equal(
            new ushort[] { '1', '1', Keys.kbKeypad1, Keys.kbKeypad1, Keys.kbEnter, Keys.kbEnter, Keys.kbKeypadEnter, Keys.kbKeypadEnter },
            events.Select(e => e.keyDown.keyCode));
        Assert.NotEqual(events[0].keyDown.keyCode, events[2].keyDown.keyCode);
        Assert.NotEqual(events[4].keyDown.keyCode, events[6].keyDown.keyCode);
    }

    [Theory]
    [InlineData((ushort)0x1000, Keys.kbNumState)]
    [InlineData((ushort)0x2000, Keys.kbCapsState)]
    [InlineData((ushort)0x8000, Keys.kbScrollState)]
    [InlineData((ushort)0xB003, Keys.kbNumState | Keys.kbCapsState | Keys.kbScrollState | Keys.kbShift)]
    public void LockStateIsReportedByBothDrivers(ushort mod, uint expected)
    {
        Assert.Equal(expected, SdlKeyTranslator.ToShiftState(mod));
        foreach (bool gpu in new[] { false, true })
        {
            var events = Run(gpu, (down, _, up) => { down(SdlKeyTranslator.SDLK_HOME, mod); up(SdlKeyTranslator.SDLK_HOME, mod); });
            Assert.All(events, e => Assert.Equal(expected, e.keyDown.controlKeyState));
        }
    }

    // A release reports the identity its press reported, which for printable keys is
    // the identity of the committed text rather than of the layout-dependent keycode.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ReleaseMatchesTheIdentityOfTheCommittedText(bool gpu)
    {
        const uint ecaron = 0x11B; // Czech layout: the key right of '1'
        var events = Run(gpu, (down, text, up) =>
        {
            Assert.False(down(ecaron, 1));
            text("2", 1);                     // Shift+ě commits "2"
            Assert.True(up(ecaron, 1));
            Assert.False(down(ecaron, 0));
            text("ě", 0);                     // Unicode: text only, so the release has no identity either
            Assert.True(up(ecaron, 0));
        });
        Assert.Equal(new[] { Events.evKeyDown, Events.evKeyUp, Events.evKeyDown, Events.evKeyUp }, events.Select(e => e.What));
        Assert.Equal(new ushort[] { '2', '2', 0, 0 }, events.Select(e => e.keyDown.keyCode));
        Assert.Equal(new[] { "2", "", "ě", "" }, events.Select(e => e.keyDown.text));
    }

    [Theory]
    [InlineData("ě")]
    [InlineData("š")]
    [InlineData("č")]
    [InlineData("ř")]
    [InlineData("😀")]
    public void UnicodeTextNeverFabricatesAKeyIdentity(string text)
    {
        foreach (bool gpu in new[] { false, true })
        {
            var ev = Assert.Single(Run(gpu, (_, commit, _) => commit(text, 0x1000)));
            Assert.Equal(Events.evKeyDown, ev.What);
            Assert.Equal(0, ev.keyDown.keyCode);
            Assert.Equal(0, ev.keyDown.charScan.ToUShort());
            Assert.Equal(text, ev.keyDown.text);
            Assert.Equal(Keys.kbNumState, ev.keyDown.controlKeyState);
        }
    }
}
