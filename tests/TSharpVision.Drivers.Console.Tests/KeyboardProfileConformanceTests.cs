using TSharpVision.Constants;
using TSharpVision.Diagnostics.Keyboard.Controller;
using TSharpVision.Diagnostics.Keyboard.Profiles;
using TSharpVision.Diagnostics.Keyboard.Protocol;
using TSharpVision.Diagnostics.Keyboard.Sources;
using Xunit;

namespace TSharpVision.Drivers.Console.Tests;

/// <summary>
/// Level A against the shared keyboard profiles: one physical action is the KEY_EVENT_RECORD group the
/// console host delivers for it, and the resulting events are judged by the diagnostics matcher. Only
/// the native records are built here; scan codes and expectations come from the profile.
/// </summary>
public sealed class KeyboardProfileConformanceTests
{
    private const uint RightAlt = Win32KeyTranslator.RIGHT_ALT_PRESSED, LeftCtrl = Win32KeyTranslator.LEFT_CTRL_PRESSED,
        ShiftPressed = Win32KeyTranslator.SHIFT_PRESSED, NumLock = Win32KeyTranslator.NUMLOCK_ON,
        Enhanced = Win32KeyTranslator.ENHANCED_KEY;

    private static readonly LayoutProfile Cz = CzechQwertyProfile.Instance;

    // Virtual keys of the text positions. Both built-in layouts keep the US assignment
    // (verified for Czech QWERTY with MapVirtualKeyEx).
    private static ushort VirtualKey(PhysicalKey key) => key switch
    {
        >= PhysicalKey.Digit1 and <= PhysicalKey.Digit9 => (ushort)('1' + (key - PhysicalKey.Digit1)),
        PhysicalKey.Digit0 => '0',
        >= PhysicalKey.KeyA and <= PhysicalKey.KeyZ => (ushort)('A' + (key - PhysicalKey.KeyA)),
        PhysicalKey.Backquote => 0xC0, PhysicalKey.Minus => 0xBD, PhysicalKey.Equal => 0xBB,
        PhysicalKey.BracketLeft => 0xDB, PhysicalKey.BracketRight => 0xDD, PhysicalKey.Backslash => 0xDC,
        PhysicalKey.Semicolon => 0xBA, PhysicalKey.Quote => 0xDE, PhysicalKey.Comma => 0xBC,
        PhysicalKey.Period => 0xBE, PhysicalKey.Slash => 0xBF, PhysicalKey.IntlBackslash => 0xE2,
        PhysicalKey.Space => 0x20,
        PhysicalKey.Escape => 0x1B, PhysicalKey.Tab => 0x09, PhysicalKey.Backspace => 0x08, PhysicalKey.Enter => 0x0D,
        >= PhysicalKey.F1 and <= PhysicalKey.F12 => (ushort)(0x70 + (key - PhysicalKey.F1)),
        PhysicalKey.Insert => 0x2D, PhysicalKey.Delete => 0x2E, PhysicalKey.Home => 0x24, PhysicalKey.End => 0x23,
        PhysicalKey.PageUp => 0x21, PhysicalKey.PageDown => 0x22, PhysicalKey.ArrowUp => 0x26,
        PhysicalKey.ArrowDown => 0x28, PhysicalKey.ArrowLeft => 0x25, PhysicalKey.ArrowRight => 0x27,
        >= PhysicalKey.Numpad0 and <= PhysicalKey.Numpad9 => (ushort)(0x60 + (key - PhysicalKey.Numpad0)),
        PhysicalKey.NumpadDecimal => 0x6E, PhysicalKey.NumpadDivide => 0x6F, PhysicalKey.NumpadMultiply => 0x6A,
        PhysicalKey.NumpadSubtract => 0x6D, PhysicalKey.NumpadAdd => 0x6B, PhysicalKey.NumpadEnter => 0x0D,
        _ => throw new ArgumentOutOfRangeException(nameof(key), key, null),
    };

    private sealed class Rig
    {
        private readonly Win32ConsoleDriver _driver = new();

        public TSharpVision.Drivers.KeyboardCapabilities Capabilities => _driver.KeyboardCapabilities;

        /// <summary>One record for a profile key: its set-1 scan code, with the Enhanced bit where the key has the E0 prefix.</summary>
        public bool Record(bool down, PhysicalKey key, char character, uint state)
        {
            (byte scan, bool extended) = PhysicalKeys.ScanCode(key);
            return _driver.ProcessKeyRecord(down, VirtualKey(key), scan, character, state | (extended ? Enhanced : 0));
        }

        public bool Raw(bool down, ushort virtualKey, ushort scan, uint state) =>
            _driver.ProcessKeyRecord(down, virtualKey, scan, '\0', state);

        public bool RawCharacter(bool down, ushort virtualKey, ushort scan, char character, uint state) =>
            _driver.ProcessKeyRecord(down, virtualKey, scan, character, state);

        public List<TEvent> Drain()
        {
            var events = new List<TEvent>();
            while (_driver.ReadKeyEvent(out TEvent ev)) events.Add(ev);
            return events;
        }
    }

    private static void AssertPasses(Step step, Rig rig, PhysicalKey? rawScanOf = null)
    {
        List<TEvent> native = rig.Drain();
        IReadOnlyList<RecordedEvent> events = RecordedEvent.FromAll(native);
        StepOutcome outcome = SequenceMatcher.Evaluate(step.Expect, events, rig.Capabilities);
        Assert.True(outcome.Verdict == Verdict.Pass,
            $"{step.Id}: {outcome.Verdict} — {string.Join("; ", outcome.Reasons)} — " +
            string.Join(", ", events.Select(KeyNames.Describe)));
        // The Console driver is the one transport that supplies the raw scan: it is the profile's.
        if (rawScanOf is { } key)
            Assert.All(events.Where(e => e.What != RecordedEvent.ModifierChanged),
                e => Assert.Equal(PhysicalKeys.ScanCode(key).Scan, e.RawScan));
    }

    public static IEnumerable<object[]> Profiles() => LayoutProfile.All.Select(p => new object[] { p.Id });

    [Theory, MemberData(nameof(Profiles))]
    public void EveryPlainTextKeyOfTheProfilePasses(string profileId)
    {
        LayoutProfile profile = LayoutProfile.Find(profileId)!;
        foreach (Step step in Suites.Build(profile, Suites.Full, latin1Identity: true)
                     .Where(s => s.Key is { } key && s.Id == key.ToString() && profile.Text(key, KeyLevel.Plain) != null))
        {
            var rig = new Rig();
            PhysicalKey key = step.Key!.Value;
            char character = profile.Text(key, KeyLevel.Plain)![0];

            Assert.True(rig.Record(true, key, character, 0));
            Assert.True(rig.Record(false, key, character, 0));

            AssertPasses(step, rig, key);
        }
    }

    [Theory, MemberData(nameof(Profiles))]
    public void EveryShiftedTextKeyOfTheProfilePasses(string profileId)
    {
        LayoutProfile profile = LayoutProfile.Find(profileId)!;
        foreach (PhysicalKey key in profile.Keys.Keys.Where(k => profile.Text(k, KeyLevel.Shift) != null && k != PhysicalKey.Space))
        {
            var rig = new Rig();
            char character = profile.Text(key, KeyLevel.Shift)![0];

            Assert.True(rig.Raw(true, 0x10, 0x2A, ShiftPressed));
            Assert.True(rig.Record(true, key, character, ShiftPressed));
            Assert.True(rig.Record(false, key, character, ShiftPressed));
            Assert.True(rig.Raw(false, 0x10, 0x2A, 0));

            AssertPasses(Suites.TextKey(profile, key, KeyLevel.Shift, "test", latin1Identity: true), rig, key);
        }
    }

    // The policy pinned for this driver: text in U+0080..U+00FF keeps its value as the key code,
    // anything above has no identity. Both are a PASS; a fabricated legacy identity is neither.
    [Theory]
    [InlineData(PhysicalKey.Digit2, 'ě', 0x0000)]
    [InlineData(PhysicalKey.Digit3, 'š', 0x0000)]
    [InlineData(PhysicalKey.Digit4, 'č', 0x0000)]
    [InlineData(PhysicalKey.Digit5, 'ř', 0x0000)]
    [InlineData(PhysicalKey.Digit6, 'ž', 0x0000)]
    [InlineData(PhysicalKey.Digit7, 'ý', 0x00FD)]
    [InlineData(PhysicalKey.Digit8, 'á', 0x00E1)]
    [InlineData(PhysicalKey.Digit9, 'í', 0x00ED)]
    [InlineData(PhysicalKey.Digit0, 'é', 0x00E9)]
    public void CzechNumberRowHasTextAndOnlyTheLatin1Identity(PhysicalKey key, char text, ushort keyCode)
    {
        var rig = new Rig();
        Assert.Equal(text.ToString(), Cz.Text(key, KeyLevel.Plain));

        Assert.True(rig.Record(true, key, text, 0));
        Assert.True(rig.Record(false, key, text, 0));

        List<TEvent> events = rig.Drain();
        Assert.Equal([Events.evKeyDown, Events.evKeyUp], events.Select(e => e.What));
        Assert.All(events, e => Assert.Equal(keyCode, e.keyDown.keyCode));
        Assert.Equal(text.ToString(), events[0].keyDown.text);
        Assert.Empty(events[1].keyDown.text);
    }

    [Fact]
    public void NamedAndKeypadKeysPass()
    {
        foreach (Step step in Suites.Build(Cz, Suites.Full, latin1Identity: true)
                     .Where(s => s.Key != null && s.Suite is "function" or "navigation" or "keypad"))
        {
            var rig = new Rig();
            PhysicalKey key = step.Key!.Value;
            bool digit = key is >= PhysicalKey.Numpad0 and <= PhysicalKey.Numpad9;
            char character = digit ? (char)('0' + (key - PhysicalKey.Numpad0)) : key switch
            {
                PhysicalKey.Enter or PhysicalKey.NumpadEnter => '\r',
                PhysicalKey.Tab => '\t',
                PhysicalKey.Backspace => '\b',
                PhysicalKey.Escape => '\x1b',
                _ => '\0',
            };
            uint state = step.Suite == "keypad" ? NumLock : 0;

            Assert.True(rig.Record(true, key, character, state));
            Assert.True(rig.Record(false, key, character, state));

            AssertPasses(step, rig, key);
        }
    }

    // AltGr as the console host reports it: Left Ctrl and Right Alt transitions around a record that
    // already carries the layout's character. The character is text, never a Ctrl or Alt shortcut.
    [Theory]
    [InlineData(PhysicalKey.Digit2, '@')]
    [InlineData(PhysicalKey.KeyE, '€')]
    public void AltGrRecordSequenceGivesTextAndLeavesNoModifierBehind(PhysicalKey key, char character)
    {
        var rig = new Rig();
        Assert.Equal(character.ToString(), Cz.Text(key, KeyLevel.AltGr));

        Assert.True(rig.Raw(true, 0x11, 0x1D, LeftCtrl));
        Assert.True(rig.Raw(true, 0x12, 0x38, LeftCtrl | RightAlt | Enhanced));
        Assert.True(rig.Record(true, key, character, LeftCtrl | RightAlt));
        Assert.True(rig.Record(false, key, character, LeftCtrl | RightAlt));
        Assert.True(rig.Raw(false, 0x12, 0x38, LeftCtrl | Enhanced));
        Assert.True(rig.Raw(false, 0x11, 0x1D, 0));

        AssertPasses(Suites.TextKey(Cz, key, KeyLevel.AltGr, "test", latin1Identity: true), rig);
    }

    // The records conhost delivered for the Czech dead acute followed by Space (SendInput, traced):
    // the press has no character, and the key is released twice with the accent as the character,
    // first by a record without a scan code. Neither release may publish anything: the press
    // published nothing, a release never carries text, and "´" is not an identity any press had.
    [Theory]
    [InlineData(0xBB, 0x0D, '´')]     // Equal: dead acute
    [InlineData(0xDC, 0x2B, '¨')]     // Backslash: dead diaeresis
    public void DeadKeyReleasesPublishNothingAndSpaceCommitsTheAccent(ushort virtualKey, ushort scan, char accent)
    {
        const uint locks = 0x60;
        var rig = new Rig();

        Assert.False(rig.Raw(true, virtualKey, scan, locks));
        Assert.False(rig.RawCharacter(false, virtualKey, 0x00, accent, locks));
        Assert.False(rig.RawCharacter(false, virtualKey, scan, accent, locks));
        Assert.True(rig.RawCharacter(true, 0x20, 0x39, accent, locks));
        Assert.True(rig.RawCharacter(false, 0x20, 0x39, ' ', locks));

        List<TEvent> events = rig.Drain();
        Assert.Equal([Events.evKeyDown, Events.evKeyUp], events.Select(e => e.What));
        Assert.All(events, e => Assert.Equal(accent, e.keyDown.keyCode));
        Assert.Equal([accent.ToString(), ""], events.Select(e => e.keyDown.text));
    }

    // The same for a composition, as traced: dead acute, then E carrying "é" on its press and
    // plain "e" on its release. One press and one release, both with the composed identity.
    [Fact]
    public void DeadAcuteThenEIsOneComposedPressAndItsRelease()
    {
        const uint locks = 0x60;
        var rig = new Rig();

        Assert.False(rig.Raw(true, 0xBB, 0x0D, locks));
        Assert.False(rig.RawCharacter(false, 0xBB, 0x00, '´', locks));
        Assert.False(rig.RawCharacter(false, 0xBB, 0x0D, '´', locks));
        Assert.True(rig.RawCharacter(true, 0x45, 0x12, 'é', locks));
        Assert.True(rig.RawCharacter(false, 0x45, 0x12, 'e', locks));

        Step step = Suites.Build(Cz, "unicode", latin1Identity: true).Single(s => s.Id == "Equal,KeyE");
        AssertPasses(step, rig, PhysicalKey.KeyE);
    }

    // A release whose press this driver never saw may report a stable named identity, never a
    // character: not as text, and not as an identity.
    [Fact]
    public void UnpairedReleaseReportsANamedIdentityButNeverACharacter()
    {
        var rig = new Rig();

        Assert.True(rig.RawCharacter(false, 0x74, 0x3F, '\0', 0));      // F5
        Assert.False(rig.RawCharacter(false, 0x41, 0x1E, 'a', 0));      // a text key
        Assert.False(rig.RawCharacter(false, 0x32, 0x03, 'ě', 0));

        TEvent release = Assert.Single(rig.Drain());
        Assert.Equal(Events.evKeyUp, release.What);
        Assert.Equal(Keys.kbF5, release.keyDown.keyCode);
        Assert.Empty(release.keyDown.text);
    }

    // Level B without a console: the exact records the native source writes with WriteConsoleInputW
    // for every step of both profiles, through the driver's record seam.
    [Theory, MemberData(nameof(Profiles))]
    public void EveryStepAsInjectedConsoleRecordsPasses(string profileId)
    {
        LayoutProfile profile = LayoutProfile.Find(profileId)!;
        foreach (Step step in Suites.Build(profile, Suites.Full, latin1Identity: true).Where(s => s.SdlScript == null))
        {
            var rig = new Rig();
            foreach (NativeEvent record in NativeEvents.ForConsole(step, profile))
                rig.RawCharacter(record.Down, (ushort)record.Key, record.Scan, (char)record.Char, record.State);

            AssertPasses(step, rig);
        }
    }
}
