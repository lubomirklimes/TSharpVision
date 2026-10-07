using TSharpVision.Constants;
using TSharpVision.Drivers;
using TSharpVision.Drivers.SDL.Gpu;

namespace TSharpVision.Drivers.SDL.Tests;

/// <summary>
/// KEYPAD-CLOSURE through SDL. SDL reports a keypad key press with its keycode and <c>SDL_KMOD_NUM</c>, and — when the
/// key types — commits the text as a separate <c>SDL_EVENT_TEXT_INPUT</c>. The drivers publish both as they come
/// (pinned by <see cref="KeypadConformanceTests"/>); these tests are about what the two mean together once
/// <see cref="KeypadKeys.Normalize"/> has seen them: the identity event is navigation or nothing, the commit is the
/// text, and nothing is typed twice.
/// </summary>
public sealed class KeypadSemanticsTests
{
    private const ushort Num = SdlKeyTranslator.SDL_KMOD_NUM;
    private const ushort Shift = SdlKeyTranslator.SDL_KMOD_LSHIFT;
    private const ushort Ctrl = SdlKeyTranslator.SDL_KMOD_LCTRL;
    private const ushort Alt = SdlKeyTranslator.SDL_KMOD_LALT;

    private const uint Kp0 = 0x40000062, Kp1 = 0x40000059, Kp2 = 0x4000005A, Kp3 = 0x4000005B, Kp4 = 0x4000005C,
        Kp5 = 0x4000005D, Kp6 = 0x4000005E, Kp7 = 0x4000005F, Kp8 = 0x40000060, Kp9 = 0x40000061,
        KpDecimal = 0x40000063, KpDivide = 0x40000054, KpMultiply = 0x40000055, KpMinus = 0x40000056, KpPlus = 0x40000057;

    /// <summary>The driver's events for a script of native calls, each given its semantic identity.</summary>
    private static List<TEvent> Dispatched(bool gpu, Action<Func<uint, ushort, bool>, Action<string, ushort>, Func<uint, ushort, bool>> script)
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
        while (driver.ReadKeyEvent(out TEvent ev))
        {
            KeypadKeys.Normalize(ref ev);
            events.Add(ev);
        }

        return events;
    }

    private static string Typed(IEnumerable<TEvent> events) =>
        string.Concat(events.Where(e => e.What == Events.evKeyDown).Select(e => KeyText.PrintableText(e.keyDown)));

    public static IEnumerable<object[]> NavigationCases()
    {
        (uint Keypad, uint Main, ushort Identity)[] keys =
        {
            (Kp0, SdlKeyTranslator.SDLK_INSERT, Keys.kbKeypad0), (Kp1, SdlKeyTranslator.SDLK_END, Keys.kbKeypad1),
            (Kp2, SdlKeyTranslator.SDLK_DOWN, Keys.kbKeypad2), (Kp3, SdlKeyTranslator.SDLK_PAGEDOWN, Keys.kbKeypad3),
            (Kp4, SdlKeyTranslator.SDLK_LEFT, Keys.kbKeypad4), (Kp6, SdlKeyTranslator.SDLK_RIGHT, Keys.kbKeypad6),
            (Kp7, SdlKeyTranslator.SDLK_HOME, Keys.kbKeypad7), (Kp8, SdlKeyTranslator.SDLK_UP, Keys.kbKeypad8),
            (Kp9, SdlKeyTranslator.SDLK_PAGEUP, Keys.kbKeypad9), (KpDecimal, SdlKeyTranslator.SDLK_DELETE, Keys.kbKeypadDecimal),
        };
        foreach (var key in keys)
            foreach (ushort modifiers in new ushort[] { 0, Shift, Ctrl, (ushort)(Ctrl | Shift), Alt })
                foreach (bool gpu in new[] { false, true })
                    yield return new object[] { gpu, key.Keypad, key.Main, key.Identity, modifiers };
    }

    /// <summary>NumLock off: press and release are the main cursor key's, modifier variant and state included, and no text follows.</summary>
    [Theory, MemberData(nameof(NavigationCases))]
    public void WithNumLockOffAKeypadKeyIsWhatTheMainCursorKeyIs(bool gpu, uint keypad, uint main, ushort identity, ushort modifiers)
    {
        List<TEvent> expected = Dispatched(gpu, (down, _, up) => { down(main, modifiers); up(main, modifiers); });
        List<TEvent> actual = Dispatched(gpu, (down, _, up) => { down(keypad, modifiers); up(keypad, modifiers); });

        Assert.Equal(new[] { Events.evKeyDown, Events.evKeyUp }, actual.Select(e => e.What));
        Assert.Equal(expected.Select(e => e.keyDown.keyCode), actual.Select(e => e.keyDown.keyCode));
        Assert.Equal(expected.Select(e => e.keyDown.controlKeyState), actual.Select(e => e.keyDown.controlKeyState));
        Assert.All(expected, e => Assert.Equal(0, e.keyDown.keypadKey));
        Assert.All(actual, e => Assert.Equal(identity, e.keyDown.keypadKey));
        Assert.All(actual, e => Assert.Equal(string.Empty, e.keyDown.text));
        Assert.Equal(expected.Select(e => e.keyDown.charScan.ToUShort()), actual.Select(e => e.keyDown.charScan.ToUShort()));
    }

    public static IEnumerable<object[]> TextCases()
    {
        (uint Keypad, ushort Identity, string Text)[] keys =
        {
            (Kp0, Keys.kbKeypad0, "0"), (Kp1, Keys.kbKeypad1, "1"), (Kp4, Keys.kbKeypad4, "4"), (Kp5, Keys.kbKeypad5, "5"),
            (Kp9, Keys.kbKeypad9, "9"), (KpDecimal, Keys.kbKeypadDecimal, "."), (KpDecimal, Keys.kbKeypadDecimal, ","),
            (KpDivide, Keys.kbKeypadDivide, "/"), (KpMultiply, Keys.kbKeypadMultiply, "*"),
            (KpMinus, Keys.kbGrayMinus, "-"), (KpPlus, Keys.kbGrayPlus, "+"),
        };
        foreach (var key in keys)
            foreach (bool gpu in new[] { false, true })
                yield return new object[] { gpu, key.Keypad, key.Identity, key.Text };
    }

    /// <summary>
    /// NumLock on: key down, the text commit, key up. One character is typed — by the commit. The identity event is
    /// not a cursor key and types nothing, also for + and −, whose legacy scan pair holds the character.
    /// </summary>
    [Theory, MemberData(nameof(TextCases))]
    public void WithNumLockOnTheIdentityAndItsTextCommitTypeTheCharacterOnce(bool gpu, uint keypad, ushort identity, string text)
    {
        List<TEvent> events = Dispatched(gpu, (down, commit, up) =>
        {
            down(keypad, Num);
            commit(text, Num);
            up(keypad, Num);
        });

        Assert.Equal(new[] { Events.evKeyDown, Events.evKeyDown, Events.evKeyUp }, events.Select(e => e.What));
        Assert.Equal(text, Typed(events));

        // The identity: its own code, the physical key, no text.
        Assert.Equal(identity, events[0].keyDown.keyCode);
        Assert.Equal(identity, events[0].keyDown.keypadKey);
        Assert.Equal(string.Empty, KeyText.PrintableText(events[0].keyDown));

        // The commit: text as SDL commits any text, with no keypad identity of its own.
        Assert.Equal(text, events[1].keyDown.text);
        Assert.Equal(text[0], events[1].keyDown.keyCode);
        Assert.Equal(0, events[1].keyDown.keypadKey);

        Assert.Equal(identity, events[2].keyDown.keypadKey);
    }

    /// <summary>The operators type whatever the lock state: the commit comes either way and is the only text.</summary>
    [Theory]
    [InlineData(false, KpDivide, "/")]
    [InlineData(true, KpMultiply, "*")]
    [InlineData(false, KpMinus, "-")]
    [InlineData(true, KpPlus, "+")]
    public void OperatorsTypeOnceWithNumLockOffToo(bool gpu, uint keypad, string text)
    {
        List<TEvent> events = Dispatched(gpu, (down, commit, up) => { down(keypad, 0); commit(text, 0); up(keypad, 0); });

        Assert.Equal(text, Typed(events));
        Assert.DoesNotContain(events, e => e.keyDown.keyCode is Keys.kbIns or Keys.kbDel or Keys.kbHome or Keys.kbEnd);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void KeypadFiveWithNumLockOffIsNoKey(bool gpu)
    {
        List<TEvent> events = Dispatched(gpu, (down, _, up) => { down(Kp5, 0); up(Kp5, 0); });

        Assert.All(events, e => Assert.Equal(Keys.kbKeypad5, e.keyDown.keyCode));
        Assert.All(events, e => Assert.Equal(Keys.kbKeypad5, e.keyDown.keypadKey));
        Assert.Equal(string.Empty, Typed(events));
    }

    /// <summary>
    /// With NumLock on and no commit (Ctrl held; or Shift on Windows, which lifts Shift around the key and sends no
    /// text) nothing can be told from the press, so the key does nothing. It is never guessed to be navigation: a
    /// commit that did follow would then type beside a cursor movement.
    /// </summary>
    [Theory]
    [InlineData(false, Num)]
    [InlineData(true, (ushort)(Num | Ctrl))]
    public void WithNumLockOnAPressWithoutACommitIsNotNavigation(bool gpu, ushort modifiers)
    {
        List<TEvent> events = Dispatched(gpu, (down, _, up) => { down(Kp4, modifiers); up(Kp4, modifiers); });

        Assert.All(events, e => Assert.Equal(Keys.kbKeypad4, e.keyDown.keyCode));
        Assert.Equal(string.Empty, Typed(events));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void KeypadEnterIsStillEnterAndCtrlEnter(bool gpu)
    {
        List<TEvent> events = Dispatched(gpu, (down, _, up) =>
        {
            down(SdlKeyTranslator.SDLK_KP_ENTER, Num);
            up(SdlKeyTranslator.SDLK_KP_ENTER, Num);
            down(SdlKeyTranslator.SDLK_KP_ENTER, Ctrl);
        });

        Assert.Equal(new[] { Keys.kbEnter, Keys.kbEnter, Keys.kbCtrlEnter }, events.Select(e => e.keyDown.keyCode));
        Assert.All(events, e => Assert.Equal(Keys.kbKeypadEnter, e.keyDown.keypadKey));
    }
}
