using System.Runtime.InteropServices;
using TSharpVision.Constants;
using TSharpVision.Drivers;
using TSharpVision.Drivers.SDL.Gpu;
using NativeSdl = global::SDL3.SDL;

namespace TSharpVision.Drivers.SDL.Tests;

/// <summary>
/// TV-1.1-FIX-1. A keypad digit or decimal press is held until the text commit SDL sends after it in the same drain of
/// the event queue. A press with a commit is published once, carrying the identity and the text; a press without one
/// is published with its identity when another key event arrives or the drain ends. Found natively on macOS, where
/// SDL reports <c>SDL_KMOD_NUM</c> clear and still commits keypad digits: keypad 4 moved the cursor left and then
/// typed '4'.
/// </summary>
public sealed class SdlKeypadTextPairingTests
{
    private const ushort Num = SdlKeyTranslator.SDL_KMOD_NUM;
    private const ushort Ctrl = SdlKeyTranslator.SDL_KMOD_LCTRL;
    private const uint Kp0 = 0x40000062, Kp1 = 0x40000059, Kp2 = 0x4000005A, Kp3 = 0x4000005B, Kp4 = 0x4000005C,
        KpDecimal = 0x40000063, KpPlus = 0x40000057;

    /// <summary>The seams of one driver, as the SDL event switch calls them.</summary>
    private sealed class Rig(bool gpu)
    {
        private readonly SDLDriver? _renderer = gpu ? null : new SDLDriver();
        private readonly SDLGpuDriver? _gpu = gpu ? new SDLGpuDriver() : null;

        private IDriver Driver => (IDriver?)_gpu ?? _renderer!;
        public bool Down(uint key, ushort mod) =>
            _gpu?.ProcessOrdinaryKeyDown(key, mod) ?? _renderer!.ProcessOrdinaryKeyDown(key, mod);
        public bool Up(uint key, ushort mod) =>
            _gpu?.ProcessOrdinaryKeyUp(key, mod) ?? _renderer!.ProcessOrdinaryKeyUp(key, mod);
        public bool FocusLost() => _gpu?.ProcessModifierFocusLost() ?? _renderer!.ProcessModifierFocusLost();
        public void Text(string text, ushort mod)
        {
            if (_gpu != null) _gpu.ProcessTextInput(text, mod);
            else _renderer!.ProcessTextInput(text, mod);
        }

        public void Drain(params NativeSdl.Event[] events)
        {
            if (_gpu != null) _gpu.DrainForTesting(events);
            else _renderer!.DrainForTesting(events);
        }

        /// <summary>What views see: the published events, normalized as <see cref="TProgram.GetEvent"/> does.</summary>
        public List<TEvent> Read()
        {
            var events = new List<TEvent>();
            while (Driver.ReadKeyEvent(out TEvent ev))
            {
                KeypadKeys.Normalize(ref ev);
                events.Add(ev);
            }
            return events;
        }
    }

    private static string Typed(IEnumerable<TEvent> events) =>
        string.Concat(events.Where(e => e.What == Events.evKeyDown).Select(e => KeyText.PrintableText(e.keyDown)));

    private static ushort[] Codes(IEnumerable<TEvent> events, ushort what) =>
        events.Where(e => e.What == what).Select(e => e.keyDown.keyCode).ToArray();

    private static NativeSdl.Event KeyEvent(bool down, uint keycode, ushort mod, bool repeat = false)
    {
        var e = new NativeSdl.Event();
        e.Type = (uint)(down ? NativeSdl.EventType.KeyDown : NativeSdl.EventType.KeyUp);
        e.Key.Key = (NativeSdl.Keycode)keycode;
        e.Key.Mod = (NativeSdl.Keymod)mod;
        e.Key.Down = down;
        e.Key.Repeat = repeat;
        return e;
    }

    // ── the original defect ─────────────────────────────────────────────────

    /// <summary>
    /// The events the 1.1 preview drivers published for macOS keypad 4 (an identity press, then the commit), normalized:
    /// a cursor movement and then the digit. Kept as the negative control for the fix below.
    /// </summary>
    [Fact]
    public void ThePreviewPublicationOfKeypadFourWithTheLockBitClearWasLeftAndThenFour()
    {
        TEvent identity = default;
        identity.What = Events.evKeyDown;
        identity.keyDown.keyCode = Keys.kbKeypad4;
        identity.keyDown.charScan = new CharScanType(Keys.kbKeypad4);
        TEvent commit = SdlTextInput.CreateEvent("4", 0);

        KeypadKeys.Normalize(ref identity);
        KeypadKeys.Normalize(ref commit);

        Assert.Equal(Keys.kbLeft, identity.keyDown.keyCode);
        Assert.Equal((ushort)'4', commit.keyDown.keyCode);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void KeypadFourWithTheLockBitClearAndACommitTypesFourWithoutMovingLeft(bool gpu)
    {
        var rig = new Rig(gpu);
        Assert.False(rig.Down(Kp4, 0));
        rig.Text("4", 0);
        Assert.True(rig.Up(Kp4, 0));

        List<TEvent> events = rig.Read();
        Assert.Equal(new[] { Events.evKeyDown, Events.evKeyUp }, events.Select(e => e.What));
        Assert.Equal("4", Typed(events));
        Assert.Equal(new ushort[] { '4' }, Codes(events, Events.evKeyDown));
        Assert.DoesNotContain(events, e => e.What == Events.evKeyDown && e.keyDown.keyCode == Keys.kbLeft);
        // The release reports the press's raw identity and pairs with it by keypadKey; like every keypad release it
        // carries no text, so its normalized key code follows the reported lock state. No view handles releases.
        Assert.All(events, e => Assert.Equal(Keys.kbKeypad4, e.keyDown.keypadKey));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void KeypadOneWithTheLockBitClearAndACommitTypesOneWithoutEnd(bool gpu)
    {
        var rig = new Rig(gpu);
        rig.Down(Kp1, 0);
        rig.Text("1", 0);
        rig.Up(Kp1, 0);

        List<TEvent> events = rig.Read();
        Assert.Equal("1", Typed(events));
        Assert.DoesNotContain(events, e => e.What == Events.evKeyDown && e.keyDown.keyCode == Keys.kbEnd);
    }

    // ── through the native event switch and the drain ───────────────────────

    /// <summary>The macOS sequence as SDL event structures, through the same switch and drain end as PumpMessages.</summary>
    [Theory]
    [InlineData(false, (ushort)0)]
    [InlineData(true, (ushort)0)]
    [InlineData(false, Num)]
    [InlineData(true, Num)]
    public void NativeKeyDownTextInputKeyUpInOneDrainIsOnePressAndItsRelease(bool gpu, ushort mod)
    {
        var rig = new Rig(gpu);
        IntPtr text = Marshal.StringToCoTaskMemUTF8("4");
        try
        {
            var commit = new NativeSdl.Event();
            commit.Type = (uint)NativeSdl.EventType.TextInput;
            commit.Text.Text = text;
            rig.Drain(KeyEvent(true, Kp4, mod), commit, KeyEvent(false, Kp4, mod));
        }
        finally { Marshal.FreeCoTaskMem(text); }

        List<TEvent> events = rig.Read();
        Assert.Equal(new[] { Events.evKeyDown, Events.evKeyUp }, events.Select(e => e.What));
        Assert.Equal("4", Typed(events));
        Assert.Equal(Keys.kbKeypad4, events[0].keyDown.keypadKey);
    }

    /// <summary>
    /// The key is still down when the drain ends without a commit (NumLock off on Windows or Linux): the press is
    /// published then, as the navigation key, and does not wait for the release.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void APressWithoutACommitIsPublishedWhenItsDrainEnds(bool gpu)
    {
        var rig = new Rig(gpu);
        rig.Drain(KeyEvent(true, Kp4, 0));

        List<TEvent> events = rig.Read();
        Assert.Equal(new[] { Keys.kbLeft }, Codes(events, Events.evKeyDown));
        Assert.Empty(rig.Read());

        // The release later pairs with it.
        rig.Drain(KeyEvent(false, Kp4, 0));
        Assert.Equal(new[] { Keys.kbLeft }, Codes(rig.Read(), Events.evKeyUp));
    }

    /// <summary>A driver read never leaves a press waiting, even where no drain completed (a host that only reads).</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AReadPublishesAWaitingPress(bool gpu)
    {
        var rig = new Rig(gpu);
        Assert.False(rig.Down(Kp0, Ctrl));

        List<TEvent> events = rig.Read();
        Assert.Equal(new[] { Keys.kbCtrlIns }, Codes(events, Events.evKeyDown));
        Assert.Empty(Typed(events));
    }

    // ── lifecycle ───────────────────────────────────────────────────────────

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AutoRepeatWithCommitsTypesOncePerRepeatAndReleasesOnce(bool gpu)
    {
        var rig = new Rig(gpu);
        for (int i = 0; i < 3; i++)
        {
            Assert.False(rig.Down(Kp4, 0));
            rig.Text("4", 0);
        }
        Assert.True(rig.Up(Kp4, 0));

        List<TEvent> events = rig.Read();
        Assert.Equal("444", Typed(events));
        Assert.Equal(3, events.Count(e => e.What == Events.evKeyDown));
        Assert.Single(events, e => e.What == Events.evKeyUp);
        Assert.DoesNotContain(events, e => e.What == Events.evKeyDown && e.keyDown.keyCode == Keys.kbLeft);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AutoRepeatWithoutCommitsIsRepeatedNavigation(bool gpu)
    {
        var rig = new Rig(gpu);
        rig.Down(Kp4, 0);
        rig.Down(Kp4, 0);
        rig.Down(Kp4, 0);
        rig.Up(Kp4, 0);

        List<TEvent> events = rig.Read();
        Assert.Equal(new[] { Keys.kbLeft, Keys.kbLeft, Keys.kbLeft }, Codes(events, Events.evKeyDown));
        Assert.Equal(new[] { Keys.kbLeft }, Codes(events, Events.evKeyUp));
        Assert.Empty(Typed(events));
    }

    /// <summary>Rapid typing, each key's commit right after its own press and the releases overlapping.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RapidSuccessiveKeysTypeEachDigitOnceInOrder(bool gpu)
    {
        var rig = new Rig(gpu);
        rig.Down(Kp1, 0); rig.Text("1", 0);
        rig.Down(Kp2, 0); rig.Text("2", 0);
        rig.Up(Kp1, 0);
        rig.Down(Kp3, 0); rig.Text("3", 0);
        rig.Down(KpDecimal, 0); rig.Text(",", 0);
        rig.Up(Kp2, 0); rig.Up(Kp3, 0); rig.Up(KpDecimal, 0);

        List<TEvent> events = rig.Read();
        Assert.Equal("123,", Typed(events));
        Assert.Equal(new ushort[] { '1', '2', '3', ',' }, Codes(events, Events.evKeyDown));
        Assert.Equal(new[] { Keys.kbKeypad1, Keys.kbKeypad2, Keys.kbKeypad3, Keys.kbKeypadDecimal },
            events.Where(e => e.What == Events.evKeyUp).Select(e => e.keyDown.keypadKey));
    }

    /// <summary>
    /// Another key ends the wait: the waiting press is published first, as navigation, and the next key's commit is
    /// that key's text, not attributed to the keypad key.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AnInterveningKeyPublishesTheWaitingPressFirst(bool gpu)
    {
        var rig = new Rig(gpu);
        rig.Down(Kp4, 0);
        Assert.True(rig.Down(SdlKeyTranslator.SDLK_RIGHT, 0));
        Assert.False(rig.Down('5', 0));
        rig.Text("5", 0);

        List<TEvent> events = rig.Read();
        Assert.Equal(new ushort[] { Keys.kbLeft, Keys.kbRight, '5' }, Codes(events, Events.evKeyDown));
        Assert.Equal(Keys.kbKeypad4, events[0].keyDown.keypadKey);
        Assert.Equal(0, events[2].keyDown.keypadKey);
        Assert.Equal("5", Typed(events));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FocusLossPublishesTheWaitingPressAndReleasesIt(bool gpu)
    {
        var rig = new Rig(gpu);
        rig.Down(Kp2, Num);
        Assert.True(rig.FocusLost());

        List<TEvent> events = rig.Read();
        Assert.Equal(new[] { Events.evKeyDown, Events.evKeyUp }, events.Where(e => e.What != Events.evModifierChanged).Select(e => e.What));
        Assert.All(events.Where(e => e.What != Events.evModifierChanged), e => Assert.Equal(Keys.kbKeypad2, e.keyDown.keypadKey));
        Assert.Empty(Typed(events));
    }

    // ── what is unchanged ───────────────────────────────────────────────────

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MainDigitsArrowsOperatorsAndKeypadEnterAreUnchanged(bool gpu)
    {
        var rig = new Rig(gpu);
        Assert.False(rig.Down('4', 0)); rig.Text("4", 0); Assert.True(rig.Up('4', 0));
        Assert.True(rig.Down(SdlKeyTranslator.SDLK_LEFT, 0)); Assert.True(rig.Up(SdlKeyTranslator.SDLK_LEFT, 0));
        Assert.True(rig.Down(KpPlus, 0)); rig.Text("+", 0); Assert.True(rig.Up(KpPlus, 0));
        Assert.True(rig.Down(SdlKeyTranslator.SDLK_KP_ENTER, 0)); Assert.True(rig.Up(SdlKeyTranslator.SDLK_KP_ENTER, 0));
        Assert.True(rig.Down(SdlKeyTranslator.SDLK_TAB, SdlKeyTranslator.SDL_KMOD_LSHIFT));

        List<TEvent> events = rig.Read();
        Assert.Equal(new ushort[] { '4', Keys.kbLeft, Keys.kbGrayPlus, '+', Keys.kbEnter, Keys.kbShiftTab },
            Codes(events, Events.evKeyDown));
        Assert.Equal("4+", Typed(events));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void KeypadZeroAndDecimalWithoutACommitKeepTheirNavigation(bool gpu)
    {
        var rig = new Rig(gpu);
        rig.Down(Kp0, 0); rig.Up(Kp0, 0);
        rig.Down(KpDecimal, 0); rig.Up(KpDecimal, 0);
        rig.Down(Kp3, Num); rig.Up(Kp3, Num);

        // NumLock on without a commit stays the identity no view handles, as before.
        Assert.Equal(new[] { Keys.kbIns, Keys.kbDel, Keys.kbKeypad3 }, Codes(rig.Read(), Events.evKeyDown));
    }
}
