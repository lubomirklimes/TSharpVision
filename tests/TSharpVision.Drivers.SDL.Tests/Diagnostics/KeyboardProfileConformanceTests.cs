using TSharpVision.Constants;
using TSharpVision.Diagnostics.Keyboard.Controller;
using TSharpVision.Diagnostics.Keyboard.Profiles;
using TSharpVision.Diagnostics.Keyboard.Protocol;
using TSharpVision.Diagnostics.Keyboard.Sources;
using TSharpVision.Drivers;
using TSharpVision.Drivers.SDL.Gpu;

namespace TSharpVision.Drivers.SDL.Tests.Diagnostics;

/// <summary>
/// Level A against the shared keyboard profiles: one physical action is the native group SDL delivers for
/// it (KEY_DOWN, TEXT_INPUT, KEY_UP), and the resulting events are judged by the diagnostics matcher.
/// Only the native construction lives here; what is expected comes from the profile.
/// </summary>
public sealed class KeyboardProfileConformanceTests
{
    private const KeyboardCapabilities Capabilities = KeyboardCapabilities.KeyReleaseEvents
        | KeyboardCapabilities.StandaloneModifierTransitions | KeyboardCapabilities.DistinctNumericKeypad;
    private const ushort LShift = SdlKeyTranslator.SDL_KMOD_LSHIFT, LCtrl = SdlKeyTranslator.SDL_KMOD_LCTRL,
        LAlt = SdlKeyTranslator.SDL_KMOD_LALT, RAlt = SdlKeyTranslator.SDL_KMOD_RALT, Num = SdlKeyTranslator.SDL_KMOD_NUM;

    private static readonly LayoutProfile Cz = CzechQwertyProfile.Instance;

    private sealed class Rig(bool gpu)
    {
        private readonly SDLDriver? _renderer = gpu ? null : new SDLDriver();
        private readonly SDLGpuDriver? _gpu = gpu ? new SDLGpuDriver() : null;

        private IDriver Driver => (IDriver?)_gpu ?? _renderer!;
        public KeyboardCapabilities Capabilities => Driver.KeyboardCapabilities;
        public bool Down(uint key, ushort mod) =>
            _gpu?.ProcessOrdinaryKeyDown(key, mod) ?? _renderer!.ProcessOrdinaryKeyDown(key, mod);
        public bool Up(uint key, ushort mod) =>
            _gpu?.ProcessOrdinaryKeyUp(key, mod) ?? _renderer!.ProcessOrdinaryKeyUp(key, mod);
        public bool Modifier(uint key, bool down) =>
            _gpu?.ProcessModifierKey(key, down) ?? _renderer!.ProcessModifierKey(key, down);
        public bool FocusLost() => _gpu?.ProcessModifierFocusLost() ?? _renderer!.ProcessModifierFocusLost();
        public void Text(string text, ushort mod)
        {
            if (_gpu != null) _gpu.ProcessTextInput(text, mod);
            else _renderer!.ProcessTextInput(text, mod);
        }

        public List<TEvent> Drain()
        {
            var events = new List<TEvent>();
            while (Driver.ReadKeyEvent(out TEvent ev)) events.Add(ev);
            return events;
        }

        public IReadOnlyList<RecordedEvent> Events() => RecordedEvent.FromAll(Drain());
    }

    // SDL's default "french_numbers" keycode option reports the number row as SDLK_1..SDLK_0 whenever
    // the layout puts the digits on Shift, which is exactly the Czech case; any other key reports its
    // unshifted character. (SDL 3 src/events/SDL_keyboard.c; not something a Level-A test can observe.)
    private static uint Keycode(LayoutProfile profile, PhysicalKey key)
    {
        if (key is >= PhysicalKey.Digit1 and <= PhysicalKey.Digit0)
            return key == PhysicalKey.Digit0 ? '0' : (uint)('1' + (key - PhysicalKey.Digit1));
        return profile.Keys[key].Plain![0];
    }

    private static readonly Dictionary<PhysicalKey, uint> NamedKeycodes = new()
    {
        [PhysicalKey.Escape] = SdlKeyTranslator.SDLK_ESCAPE, [PhysicalKey.Tab] = SdlKeyTranslator.SDLK_TAB,
        [PhysicalKey.Backspace] = SdlKeyTranslator.SDLK_BACKSPACE, [PhysicalKey.Enter] = SdlKeyTranslator.SDLK_RETURN,
        [PhysicalKey.F1] = SdlKeyTranslator.SDLK_F1, [PhysicalKey.F2] = SdlKeyTranslator.SDLK_F2,
        [PhysicalKey.F3] = SdlKeyTranslator.SDLK_F3, [PhysicalKey.F4] = SdlKeyTranslator.SDLK_F4,
        [PhysicalKey.F5] = SdlKeyTranslator.SDLK_F5, [PhysicalKey.F6] = SdlKeyTranslator.SDLK_F6,
        [PhysicalKey.F7] = SdlKeyTranslator.SDLK_F7, [PhysicalKey.F8] = SdlKeyTranslator.SDLK_F8,
        [PhysicalKey.F9] = SdlKeyTranslator.SDLK_F9, [PhysicalKey.F10] = SdlKeyTranslator.SDLK_F10,
        [PhysicalKey.F11] = SdlKeyTranslator.SDLK_F11, [PhysicalKey.F12] = SdlKeyTranslator.SDLK_F12,
        [PhysicalKey.Insert] = SdlKeyTranslator.SDLK_INSERT, [PhysicalKey.Delete] = SdlKeyTranslator.SDLK_DELETE,
        [PhysicalKey.Home] = SdlKeyTranslator.SDLK_HOME, [PhysicalKey.End] = SdlKeyTranslator.SDLK_END,
        [PhysicalKey.PageUp] = SdlKeyTranslator.SDLK_PAGEUP, [PhysicalKey.PageDown] = SdlKeyTranslator.SDLK_PAGEDOWN,
        [PhysicalKey.ArrowUp] = SdlKeyTranslator.SDLK_UP, [PhysicalKey.ArrowDown] = SdlKeyTranslator.SDLK_DOWN,
        [PhysicalKey.ArrowLeft] = SdlKeyTranslator.SDLK_LEFT, [PhysicalKey.ArrowRight] = SdlKeyTranslator.SDLK_RIGHT,
        [PhysicalKey.Numpad1] = 0x40000059, [PhysicalKey.Numpad2] = 0x4000005A, [PhysicalKey.Numpad3] = 0x4000005B,
        [PhysicalKey.Numpad4] = 0x4000005C, [PhysicalKey.Numpad5] = 0x4000005D, [PhysicalKey.Numpad6] = 0x4000005E,
        [PhysicalKey.Numpad7] = 0x4000005F, [PhysicalKey.Numpad8] = 0x40000060, [PhysicalKey.Numpad9] = 0x40000061,
        [PhysicalKey.Numpad0] = 0x40000062, [PhysicalKey.NumpadDecimal] = 0x40000063,
        [PhysicalKey.NumpadDivide] = 0x40000054, [PhysicalKey.NumpadMultiply] = 0x40000055,
        [PhysicalKey.NumpadSubtract] = 0x40000056, [PhysicalKey.NumpadAdd] = 0x40000057,
        [PhysicalKey.NumpadEnter] = SdlKeyTranslator.SDLK_KP_ENTER,
    };

    private static void AssertPasses(Step step, Rig rig)
    {
        IReadOnlyList<RecordedEvent> events = rig.Events();
        StepOutcome outcome = SequenceMatcher.Evaluate(step.Expect, events, rig.Capabilities);
        Assert.True(outcome.Verdict == Verdict.Pass,
            $"{step.Id}: {outcome.Verdict} — {string.Join("; ", outcome.Reasons)} — " +
            string.Join(", ", events.Select(KeyNames.Describe)));
    }

    public static IEnumerable<object[]> Backends() => [[false], [true]];

    public static IEnumerable<object[]> ProfilesAndBackends() =>
        from profile in LayoutProfile.All
        from gpu in new[] { false, true }
        select new object[] { profile.Id, gpu };

    // ── Predicted case 1: the orphan release ────────────────────────────────

    // Czech Digit2 arrives as KEY_DOWN '2', TEXT_INPUT "ě", KEY_UP '2'. The published press is
    // text-only, so the release must not carry the layout keycode as an identity no press had.
    [Theory, MemberData(nameof(Backends))]
    public void CzechDigit2ReleaseDoesNotInventAnIdentity(bool gpu)
    {
        var rig = new Rig(gpu);

        Assert.False(rig.Down('2', 0));
        rig.Text("ě", 0);
        Assert.True(rig.Up('2', 0));

        // The key was pressed and is released: a real release, with the press's absent identity.
        List<TEvent> events = rig.Drain();
        Assert.Equal([Events.evKeyDown, Events.evKeyUp], events.Select(e => e.What));
        Assert.All(events, e => Assert.Equal(0, e.keyDown.keyCode));
        Assert.All(events, e => Assert.Equal(0, e.keyDown.charScan.ToUShort()));
        Assert.Equal(["ě", ""], events.Select(e => e.keyDown.text));
    }

    [Theory, MemberData(nameof(Backends))]
    public void HeldCzechDigit2RepeatsItsTextAndReleasesOnceWithoutIdentity(bool gpu)
    {
        var rig = new Rig(gpu);
        for (int i = 0; i < 3; i++)
        {
            Assert.False(rig.Down('2', 0));
            rig.Text("ě", 0);
        }
        Assert.True(rig.Up('2', 0));
        Assert.False(rig.Up('2', 0));

        List<TEvent> events = rig.Drain();
        Assert.Equal(
            [Events.evKeyDown, Events.evKeyDown, Events.evKeyDown, Events.evKeyUp], events.Select(e => e.What));
        Assert.All(events, e => Assert.Equal(0, e.keyDown.keyCode));
    }

    // The same keycode keeps its ASCII identity as soon as the committed text is ASCII.
    [Theory, MemberData(nameof(Backends))]
    public void SameKeycodeReleasesTheIdentityItsOwnPressReported(bool gpu)
    {
        var rig = new Rig(gpu);

        Assert.False(rig.Down('2', 0));
        rig.Text("ě", 0);
        Assert.True(rig.Up('2', 0));
        Assert.False(rig.Down('2', LShift));
        rig.Text("2", LShift);
        Assert.True(rig.Up('2', LShift));
        Assert.False(rig.Down('2', 0));
        rig.Text("2", 0);                     // US layout: the same key is plain ASCII
        Assert.True(rig.Up('2', 0));

        List<TEvent> events = rig.Drain();
        Assert.Equal(
            [Events.evKeyDown, Events.evKeyUp, Events.evKeyDown, Events.evKeyUp, Events.evKeyDown, Events.evKeyUp],
            events.Select(e => e.What));
        Assert.Equal(new ushort[] { 0, 0, '2', '2', '2', '2' }, events.Select(e => e.keyDown.keyCode));
    }

    // Releases are matched by the native keycode of the key that goes up, never by its text, so
    // several text-only keys held together each release once, in whatever order they go up.
    [Theory, MemberData(nameof(Backends))]
    public void SeveralHeldTextKeysReleaseIndependentlyByNativeKey(bool gpu)
    {
        var rig = new Rig(gpu);

        Assert.False(rig.Down('2', 0));
        rig.Text("ě", 0);
        Assert.False(rig.Down('3', 0));
        rig.Text("š", 0);
        Assert.False(rig.Down('a', 0));
        rig.Text("a", 0);
        Assert.True(rig.Up('2', 0));
        Assert.True(rig.Up('a', 0));
        Assert.True(rig.Up('3', 0));
        Assert.False(rig.Up('3', 0));

        List<TEvent> events = rig.Drain();
        Assert.Equal(new ushort[] { 0, 0, 'a', 0, 'a', 0 }, events.Select(e => e.keyDown.keyCode));
        Assert.Equal(["ě", "š", "a", "", "", ""], events.Select(e => e.keyDown.text));
        Assert.Equal(3, events.Count(e => e.What == Events.evKeyUp));
    }

    // Focus loss releases what was published as held, including text-only keys, and nothing else.
    [Theory, MemberData(nameof(Backends))]
    public void FocusLossReleasesHeldTextKeysWithoutIdentityAndSkipsUnpublishedOnes(bool gpu)
    {
        var rig = new Rig(gpu);

        Assert.False(rig.Down('2', 0));
        rig.Text("ě", 0);
        Assert.False(rig.Down('a', 0));
        rig.Text("a", 0);
        Assert.False(rig.Down('=', 0));       // dead key: nothing published
        Assert.True(rig.FocusLost());
        // A release arriving after the reset must not fall back to the layout keycode as an identity.
        Assert.False(rig.Up('2', 0));
        Assert.False(rig.Up('a', 0));
        Assert.False(rig.Up('=', 0));

        List<TEvent> events = rig.Drain();
        Assert.Equal(
            [Events.evKeyDown, Events.evKeyDown, Events.evKeyUp, Events.evKeyUp], events.Select(e => e.What));
        Assert.Equal(new ushort[] { 0, 'a', 0, 'a' }, events.Select(e => e.keyDown.keyCode));
    }

    // A printable key whose text never arrives (a dead key, a swallowed commit) published no press,
    // so its release has nothing to match either.
    [Theory, MemberData(nameof(Backends))]
    public void PrintableKeyWithoutATextCommitPublishesNeitherPressNorRelease(bool gpu)
    {
        var rig = new Rig(gpu);

        Assert.False(rig.Down('=', 0));
        Assert.False(rig.Up('=', 0));

        Assert.Empty(rig.Drain());
    }

    // Observed with SendInput on the Czech layout: the dead acute publishes nothing, and the key
    // that resolves it commits text that is not its own. Space after it commits the accent, E the
    // composed letter; both are text-only presses whose release has no identity either.
    [Theory]
    [InlineData(false, (uint)' ', "´")]
    [InlineData(true, (uint)' ', "´")]
    [InlineData(false, (uint)'e', "é")]
    [InlineData(true, (uint)'e', "é")]
    public void DeadKeyThenResolvingKeyIsOneTextOnlyPressAndRelease(bool gpu, uint resolving, string text)
    {
        var rig = new Rig(gpu);

        Assert.False(rig.Down(0xB4, 0));
        Assert.False(rig.Up(0xB4, 0));
        Assert.False(rig.Down(resolving, 0));
        rig.Text(text, 0);
        Assert.True(rig.Up(resolving, 0));

        List<TEvent> events = rig.Drain();
        Assert.Equal([Events.evKeyDown, Events.evKeyUp], events.Select(e => e.What));
        Assert.All(events, e => Assert.Equal(0, e.keyDown.keyCode));
        Assert.Equal([text, ""], events.Select(e => e.keyDown.text));
    }

    // Level B without a window: the exact events the native source pushes with SDL_PushEvent for
    // every step of both profiles, including the synthetic event orders, through the driver seams.
    [Theory, MemberData(nameof(ProfilesAndBackends))]
    public void EveryStepAsInjectedSdlEventsPasses(string profileId, bool gpu)
    {
        LayoutProfile profile = LayoutProfile.Find(profileId)!;
        foreach (Step step in Suites.Build(profile, Suites.Full, latin1Identity: false))
        {
            var rig = new Rig(gpu);
            ushort lastMod = 0;
            foreach (NativeEvent native in NativeEvents.ForSdl(step, profile))
            {
                switch (native.Kind)
                {
                    case NativeEvent.KeyKind:
                        lastMod = (ushort)native.State;
                        if (SdlModifierTranslator.IsModifierKey(native.Key)) rig.Modifier(native.Key, native.Down);
                        else if (native.Down) rig.Down(native.Key, lastMod);
                        else rig.Up(native.Key, lastMod);
                        break;
                    case NativeEvent.TextKind:
                        rig.Text(native.Text, lastMod);
                        break;
                    case NativeEvent.FocusLostKind:
                        lastMod = 0;
                        rig.FocusLost();
                        break;
                }
            }

            AssertPasses(step, rig);
        }
    }

    // What the tracker does with an order no SDL backend produces: both key-downs before either
    // commit. The first commit is attributed to the most recent key-down, so the two presses
    // share one release. Text is intact and nothing is invented; the real order (each commit right
    // after its own key-down, confirmed at Level C) gives each key its own release.
    [Theory, MemberData(nameof(Backends))]
    public void KeyDownsBeforeEitherCommitLoseOneReleaseButInventNothing(bool gpu)
    {
        var rig = new Rig(gpu);
        Assert.False(rig.Down('a', 0));
        Assert.False(rig.Down('b', 0));
        rig.Text("a", 0);
        rig.Text("b", 0);
        Assert.False(rig.Up('a', 0));
        Assert.True(rig.Up('b', 0));

        List<TEvent> events = rig.Drain();
        Assert.Equal([Events.evKeyDown, Events.evKeyDown, Events.evKeyUp], events.Select(e => e.What));
        Assert.Equal(new ushort[] { 'a', 'b', 'a' }, events.Select(e => e.keyDown.keyCode));

        var real = new Rig(gpu);
        Assert.False(real.Down('a', 0));
        real.Text("a", 0);
        Assert.False(real.Down('b', 0));
        real.Text("b", 0);
        Assert.True(real.Up('a', 0));
        Assert.True(real.Up('b', 0));
        Assert.Equal(new ushort[] { 'a', 'b', 'a', 'b' }, real.Drain().Select(e => e.keyDown.keyCode));
    }

    // ── Shared profile, plain and shifted levels ────────────────────────────

    [Theory, MemberData(nameof(ProfilesAndBackends))]
    public void EveryPlainTextKeyOfTheProfilePasses(string profileId, bool gpu)
    {
        LayoutProfile profile = LayoutProfile.Find(profileId)!;
        foreach (Step step in Suites.Build(profile, Suites.Full, latin1Identity: false)
                     .Where(s => s.Key is { } key && s.Id == key.ToString() && profile.Text(key, KeyLevel.Plain) != null))
        {
            var rig = new Rig(gpu);
            uint keycode = Keycode(profile, step.Key!.Value);
            string text = profile.Text(step.Key.Value, KeyLevel.Plain)!;

            Assert.False(rig.Down(keycode, 0));
            rig.Text(text, 0);
            rig.Up(keycode, 0);

            AssertPasses(step, rig);
        }
    }

    [Theory, MemberData(nameof(ProfilesAndBackends))]
    public void EveryShiftedTextKeyOfTheProfilePasses(string profileId, bool gpu)
    {
        LayoutProfile profile = LayoutProfile.Find(profileId)!;
        foreach (PhysicalKey key in profile.Keys.Keys.Where(k => profile.Text(k, KeyLevel.Shift) != null && k != PhysicalKey.Space))
        {
            var rig = new Rig(gpu);
            uint keycode = Keycode(profile, key);

            Assert.True(rig.Modifier(SdlKeyTranslator.SDLK_LSHIFT, true));
            Assert.False(rig.Down(keycode, LShift));
            rig.Text(profile.Text(key, KeyLevel.Shift)!, LShift);
            rig.Up(keycode, LShift);
            Assert.True(rig.Modifier(SdlKeyTranslator.SDLK_LSHIFT, false));

            AssertPasses(Suites.TextKey(profile, key, KeyLevel.Shift, "test", false), rig);
        }
    }

    [Theory, MemberData(nameof(Backends))]
    public void NamedAndKeypadKeysPass(bool gpu)
    {
        foreach (Step step in Suites.Build(Cz, Suites.Full, latin1Identity: false)
                     .Where(s => s.Key is { } key && NamedKeycodes.ContainsKey(key) && s.Level == KeyLevel.Plain
                         && s.Suite is "function" or "navigation" or "keypad"))
        {
            var rig = new Rig(gpu);
            uint keycode = NamedKeycodes[step.Key!.Value];
            bool keypad = step.Suite == "keypad";

            Assert.True(rig.Down(keycode, keypad ? Num : (ushort)0));
            // With Num Lock on, the layout commits the keypad key's character as separate text.
            if (step.Key is >= PhysicalKey.Numpad0 and <= PhysicalKey.Numpad9)
                rig.Text(((char)('0' + (step.Key.Value - PhysicalKey.Numpad0))).ToString(), Num);
            Assert.True(rig.Up(keycode, keypad ? Num : (ushort)0));

            AssertPasses(step, rig);
        }
    }

    // ── AltGr ───────────────────────────────────────────────────────────────

    public static IEnumerable<object[]> AltGrCases() =>
        from gpu in new[] { false, true }
        // Right Alt alone, and with the synthetic Left Ctrl Windows adds for AltGr.
        from mod in new[] { RAlt, (ushort)(LCtrl | RAlt) }
        from key in new[] { PhysicalKey.Digit2, PhysicalKey.KeyE }
        select new object[] { gpu, mod, key };

    // Whether SDL reports the synthetic Left Ctrl in the key event's modifiers is a property of its
    // platform backend, so both deliveries must give text only: no Ctrl or Alt shortcut beside it.
    [Theory, MemberData(nameof(AltGrCases))]
    public void AltGrTextIsNeverAlsoAShortcut(bool gpu, ushort mod, PhysicalKey key)
    {
        var rig = new Rig(gpu);
        uint keycode = Keycode(Cz, key);
        bool ctrl = (mod & LCtrl) != 0;

        if (ctrl) Assert.True(rig.Modifier(SdlKeyTranslator.SDLK_LCTRL, true));
        Assert.True(rig.Modifier(SdlKeyTranslator.SDLK_RALT, true));
        Assert.False(rig.Down(keycode, mod));
        rig.Text(Cz.Text(key, KeyLevel.AltGr)!, mod);
        rig.Up(keycode, mod);
        Assert.True(rig.Modifier(SdlKeyTranslator.SDLK_RALT, false));
        if (ctrl) Assert.True(rig.Modifier(SdlKeyTranslator.SDLK_LCTRL, false));

        AssertPasses(Suites.TextKey(Cz, key, KeyLevel.AltGr, "test", false), rig);
    }

    // A held AltGr key repeats its text; the chord never becomes a shortcut along the way.
    [Theory, MemberData(nameof(AltGrCases))]
    public void HeldAltGrKeyRepeatsTextOnly(bool gpu, ushort mod, PhysicalKey key)
    {
        var rig = new Rig(gpu);
        uint keycode = Keycode(Cz, key);
        string text = Cz.Text(key, KeyLevel.AltGr)!;

        for (int i = 0; i < 3; i++)
        {
            Assert.False(rig.Down(keycode, mod));
            rig.Text(text, mod);
        }
        Assert.True(rig.Up(keycode, mod));

        List<TEvent> events = rig.Drain();
        Assert.Equal([Events.evKeyDown, Events.evKeyDown, Events.evKeyDown, Events.evKeyUp], events.Select(e => e.What));
        Assert.Equal([text, text, text, ""], events.Select(e => e.keyDown.text));
        ushort identity = text[0] <= 0x7F ? text[0] : (ushort)0;
        Assert.All(events, e => Assert.Equal(identity, e.keyDown.keyCode));
    }

    // ── Right Alt as an ordinary Alt ────────────────────────────────────────

    public static IEnumerable<object[]> RightAltChords() =>
        from gpu in new[] { false, true }
        // Right Alt alone, and Left Ctrl + Right Alt, which without text is a Ctrl+Alt chord.
        from mod in new[] { RAlt, (ushort)(LCtrl | RAlt) }
        from chord in new[] { (Key: (uint)'2', Code: Keys.kbAlt2), (Key: (uint)'e', Code: Keys.kbAltE) }
        select new object[] { gpu, mod, chord.Key, chord.Code };

    // Right Alt cannot be told from AltGr when the key goes down, so the chord waits for the text
    // commit. On a layout without an AltGr character there none comes, and the release publishes
    // the Alt shortcut the chord was: press and release with one identity, modifiers back to none.
    [Theory, MemberData(nameof(RightAltChords))]
    public void RightAltChordWithoutTextIsAnAltShortcut(bool gpu, ushort mod, uint keycode, ushort expected)
    {
        var rig = new Rig(gpu);
        bool ctrl = (mod & LCtrl) != 0;

        if (ctrl) Assert.True(rig.Modifier(SdlKeyTranslator.SDLK_LCTRL, true));
        Assert.True(rig.Modifier(SdlKeyTranslator.SDLK_RALT, true));
        Assert.False(rig.Down(keycode, mod));
        Assert.True(rig.Up(keycode, mod));
        Assert.True(rig.Modifier(SdlKeyTranslator.SDLK_RALT, false));
        if (ctrl) Assert.True(rig.Modifier(SdlKeyTranslator.SDLK_LCTRL, false));

        List<TEvent> keys = rig.Drain();
        List<TEvent> chord = keys.Where(e => e.What != Events.evModifierChanged).ToList();
        Assert.Equal([Events.evKeyDown, Events.evKeyUp], chord.Select(e => e.What));
        Assert.All(chord, e => Assert.Equal(expected, e.keyDown.keyCode));
        Assert.All(chord, e => Assert.Empty(e.keyDown.text));
        Assert.All(chord, e => Assert.Equal(SdlKeyTranslator.ToShiftState(mod), e.keyDown.controlKeyState));
        Assert.Equal(Events.evModifierChanged, keys[^1].What);
        Assert.Equal(0u, keys[^1].Modifiers);
    }

    [Theory, MemberData(nameof(RightAltChords))]
    public void HeldRightAltChordRepeatsAsTheShortcutAndReleasesOnce(bool gpu, ushort mod, uint keycode, ushort expected)
    {
        var rig = new Rig(gpu);

        Assert.False(rig.Down(keycode, mod));     // could still be AltGr
        Assert.True(rig.Down(keycode, mod));      // a repeat without a commit: it is not
        Assert.True(rig.Down(keycode, mod));
        Assert.True(rig.Up(keycode, mod));
        Assert.False(rig.Up(keycode, mod));

        List<TEvent> events = rig.Drain();
        Assert.Equal([Events.evKeyDown, Events.evKeyDown, Events.evKeyUp], events.Select(e => e.What));
        Assert.All(events, e => Assert.Equal(expected, e.keyDown.keyCode));
    }

    // Alt released before the letter: the shortcut still carries the Alt it was pressed with.
    [Theory, MemberData(nameof(Backends))]
    public void RightAltChordKeepsItsPressStateWhenAltGoesUpFirst(bool gpu)
    {
        var rig = new Rig(gpu);

        Assert.False(rig.Down('x', RAlt));
        Assert.True(rig.Up('x', 0));

        List<TEvent> events = rig.Drain();
        Assert.Equal([Events.evKeyDown, Events.evKeyUp], events.Select(e => e.What));
        Assert.All(events, e => Assert.Equal(Keys.kbAltX, e.keyDown.keyCode));
        Assert.Equal(Keys.kbAltShift, events[0].keyDown.controlKeyState & Keys.kbAltShift);
        Assert.Equal(0u, events[1].keyDown.controlKeyState);
    }

    // The same chord on two layouts: text decides, and exactly one of the two results is published.
    [Theory, MemberData(nameof(Backends))]
    public void SameRightAltChordIsTextWhereTheLayoutHasItAndAShortcutWhereItHasNot(bool gpu)
    {
        var czech = new Rig(gpu);
        Assert.False(czech.Down('e', RAlt));
        czech.Text("€", RAlt);
        Assert.True(czech.Up('e', RAlt));
        List<TEvent> typed = czech.Drain();
        Assert.Equal(["€", ""], typed.Select(e => e.keyDown.text));
        Assert.All(typed, e => Assert.Equal(0, e.keyDown.keyCode));

        var us = new Rig(gpu);
        Assert.False(us.Down('e', RAlt));
        Assert.True(us.Up('e', RAlt));
        Assert.All(us.Drain(), e => Assert.Equal(Keys.kbAltE, e.keyDown.keyCode));
    }

    // Right Alt with a key that has no Alt shortcut identity publishes nothing: the pure translator's
    // text fallback is not something the layout committed.
    [Theory, MemberData(nameof(Backends))]
    public void RightAltWithPunctuationAndNoCommitPublishesNothing(bool gpu)
    {
        var rig = new Rig(gpu);

        Assert.False(rig.Down(';', RAlt));
        Assert.False(rig.Up(';', RAlt));

        Assert.Empty(rig.Drain());
    }

    [Theory, MemberData(nameof(Backends))]
    public void FocusLossDropsAWaitingRightAltChord(bool gpu)
    {
        var rig = new Rig(gpu);

        Assert.False(rig.Down('e', RAlt));
        rig.FocusLost();
        Assert.False(rig.Up('e', RAlt));

        Assert.DoesNotContain(rig.Drain(), e => e.What != Events.evModifierChanged);
    }

    // Combinations that are never AltGr are published at once with their legacy identity: Left
    // Alt, Ctrl alone, both Alts, and Right Ctrl with Right Alt.
    [Theory]
    [InlineData(false, (uint)'2', LAlt, Keys.kbAlt2)]
    [InlineData(true, (uint)'x', LAlt, Keys.kbAltX)]
    [InlineData(false, (uint)'a', LCtrl, Keys.kbCtrlA)]
    [InlineData(true, (uint)'e', LCtrl, Keys.kbCtrlE)]
    [InlineData(false, (uint)'x', (ushort)(LAlt | RAlt), Keys.kbAltX)]
    [InlineData(true, (uint)'x', (ushort)(SdlKeyTranslator.SDL_KMOD_RCTRL | RAlt), Keys.kbAltX)]
    public void RealShortcutsKeepTheirIdentityOnPressAndRelease(bool gpu, uint keycode, ushort mod, ushort expected)
    {
        var rig = new Rig(gpu);

        Assert.True(rig.Down(keycode, mod));
        Assert.True(rig.Up(keycode, mod));

        List<TEvent> events = rig.Drain();
        Assert.Equal([Events.evKeyDown, Events.evKeyUp], events.Select(e => e.What));
        Assert.All(events, e => Assert.Equal(expected, e.keyDown.keyCode));
        Assert.All(events, e => Assert.Empty(e.keyDown.text));
    }
}
