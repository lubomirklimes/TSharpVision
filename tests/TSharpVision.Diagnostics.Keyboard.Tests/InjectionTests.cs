using TSharpVision.Diagnostics.Keyboard.Controller;
using TSharpVision.Diagnostics.Keyboard.Profiles;
using TSharpVision.Diagnostics.Keyboard.Protocol;
using TSharpVision.Diagnostics.Keyboard.Sources;
using TSharpVision.Drivers;

namespace TSharpVision.Diagnostics.Keyboard.Tests;

/// <summary>
/// Phase 2 without a keyboard: source selection, the Level B / Level C distinction, layout
/// verification, the foreground guard, the denylist and the native events that are injected.
/// Nothing here calls SendInput, opens a window or writes to a console.
/// </summary>
public sealed class InjectionTests
{
    private static readonly LayoutProfile Us = UsProfile.Instance, Cz = CzechQwertyProfile.Instance;

    private static SourceRequest Request(string source, string driver, bool windows = true, bool elevated = false,
        string host = "auto", int kitty = 0) =>
        new(source, driver) { Windows = windows, Elevated = elevated, Host = host, KittyFlags = kitty, PosixPty = !windows };

    private static Step Step(LayoutProfile profile, string suite, string id) =>
        Suites.Build(profile, suite, latin1Identity: false, lockKeys: true).Single(s => s.Id == id);

    // ── Source selection ────────────────────────────────────────────────────

    [Theory]
    [InlineData("human", "console", typeof(PromptedHumanSource), TargetConsole.NewDefault)]
    [InlineData("human", "sdl", typeof(PromptedHumanSource), TargetConsole.Inherit)]
    [InlineData("native", "console", typeof(RemoteInjectorSource), TargetConsole.Conhost)]
    [InlineData("native", "sdl", typeof(RemoteInjectorSource), TargetConsole.Inherit)]
    [InlineData("native", "sdl-gpu", typeof(RemoteInjectorSource), TargetConsole.Inherit)]
    [InlineData("os", "console", typeof(WindowsSendInputSource), TargetConsole.Conhost)]
    [InlineData("os", "sdl", typeof(WindowsSendInputSource), TargetConsole.Inherit)]
    [InlineData("os", "sdl-gpu", typeof(WindowsSendInputSource), TargetConsole.Inherit)]
    public void WindowsSourcesAreSelectedWithTheHostTheyCanTrust(string source, string driver, Type type, TargetConsole console)
    {
        SourceSelection selection = SourceFactory.Create(Request(source, driver));

        using IInputSource input = Assert.IsAssignableFrom<IInputSource>(selection.Source);
        Assert.IsType(type, input);
        Assert.Equal(console, selection.Console);
        Assert.Equal(source, input.Name);
    }

    [Fact]
    public void HostOptionOverridesTheDefaultOnlyForAWindowsConsole()
    {
        Assert.Equal(TargetConsole.Conhost, SourceFactory.Create(Request("human", "console", host: "conhost")).Console);
        Assert.Equal(TargetConsole.NewDefault, SourceFactory.Create(Request("native", "console", host: "default")).Console);
        Assert.Equal(TargetConsole.NewDefault, SourceFactory.Create(Request("os", "console", host: "default")).Console);

        Assert.True(SourceFactory.Create(Request("native", "sdl", host: "conhost")).Usage);
        Assert.True(SourceFactory.Create(Request("human", "console", windows: false, host: "conhost")).Usage);
        Assert.True(SourceFactory.Create(Request("human", "console", host: "wt")).Usage);
    }

    // Unsupported is an environment fact (exit 2 without the usage text), never a silent fallback.
    [Theory]
    [InlineData("os", "console", false, false, "needs Windows")]
    [InlineData("os", "sdl", false, false, "needs Windows")]
    [InlineData("os", "sdl-gpu", true, true, "elevated")]
    [InlineData("native", "console", false, false, "needs Windows")]
    [InlineData("pty", "terminal", true, false, "needs Linux or macOS")]
    [InlineData("human", "terminal", true, false, "needs Linux or macOS")]
    public void UnsupportedEnvironmentsAreRefusedNotFaked(string source, string driver, bool windows, bool elevated, string reason)
    {
        SourceSelection selection = SourceFactory.Create(Request(source, driver, windows, elevated));

        Assert.Null(selection.Source);
        Assert.False(selection.Usage);
        Assert.Contains(reason, selection.Error);
    }

    [Theory]
    [InlineData("native", "terminal", false)]
    [InlineData("pty", "console", false)]
    [InlineData("sendinput", "console", true)]
    public void MeaninglessCombinationsAreUsageErrors(string source, string driver, bool windows)
    {
        SourceSelection selection = SourceFactory.Create(Request(source, driver, windows));

        Assert.Null(selection.Source);
        Assert.True(selection.Usage);
    }

    [Fact]
    public void NativeSdlInjectionIsNotTiedToWindowsAndKittyFlagsBelongToThePty()
    {
        using IInputSource? sdl = SourceFactory.Create(Request("native", "sdl", windows: false)).Source;
        Assert.IsType<RemoteInjectorSource>(sdl);
        Assert.True(SourceFactory.Create(Request("native", "sdl", kitty: 31)).Usage);
        Assert.Equal(["human", "native", "os", "pty"], SourceFactory.Names);
    }

    // ── Level B is never Level C ────────────────────────────────────────────

    [Fact]
    public void LevelBAndLevelCResultsCarryDifferentLabels()
    {
        using var human = new PromptedHumanSource(TargetConsole.Inherit, false, 1000);
        using var os = new WindowsSendInputSource(TargetConsole.Inherit);
        using var native = new RemoteInjectorSource(TargetConsole.Inherit);
        using var pty = new PtyByteSource(0);

        Assert.Equal("REAL LAYOUT E2E", SourceLabels.RealLayout);
        Assert.Equal("NATIVE INJECTED — LAYOUT BYPASSED", SourceLabels.NativeInjected);
        Assert.Equal(SourceLabels.RealLayout, human.Label);
        Assert.Equal(SourceLabels.RealLayout, os.Label);
        Assert.Equal(SourceLabels.NativeInjected, native.Label);
        Assert.Equal(SourceLabels.TransportBytes, pty.Label);

        // Only a source whose input goes through the layout verifies it; only those claim Level C.
        Assert.All(new IInputSource[] { human, os }, s => { Assert.True(s.VerifiesLayout); Assert.StartsWith("C ", s.Level); });
        Assert.All(new IInputSource[] { native, pty }, s => { Assert.False(s.VerifiesLayout); Assert.StartsWith("B ", s.Level); });
        Assert.False(os.Interactive);
        Assert.True(pty.TransportSteps);
    }

    [Fact]
    public void ReportHeaderStatesLevelSourceHostOsLayoutAndCapabilities()
    {
        var info = new RunInfo("Czech QWERTY", "SDLDriver", "os", "C (physical key position)", SourceLabels.RealLayout,
            "SDL window", "windows", "00010405 Czech (QWERTY) (HKL 0xF0050405)", KeyboardCapabilities.KeyReleaseEvents);
        var output = new StringWriter();

        Report.WriteHeader(output, info, [new OracleDisagreement(PhysicalKey.Quote, KeyLevel.AltGr, "\"¤\"", "nothing")]);

        string[] lines = output.ToString().Split('\n', StringSplitOptions.TrimEntries);
        Assert.Equal("*** REAL LAYOUT E2E ***", lines[0]);
        Assert.Contains("Level: C (physical key position)", lines);
        Assert.Contains("Source: os", lines);
        Assert.Contains("Host: SDL window", lines);
        Assert.Contains("OS: windows", lines);
        Assert.Contains("Active layout: 00010405 Czech (QWERTY) (HKL 0xF0050405)", lines);
        Assert.Contains("Capabilities: KeyReleaseEvents", lines);
        Assert.Contains("PROFILE/ORACLE DISAGREEMENT: AltGr Quote: profile \"¤\", OS layout nothing", lines);

        var native = new StringWriter();
        Report.WriteHeader(native, info with { Label = SourceLabels.NativeInjected }, []);
        Assert.StartsWith("*** NATIVE INJECTED — LAYOUT BYPASSED ***", native.ToString());
        Assert.DoesNotContain("REAL LAYOUT", native.ToString());
    }

    [Fact]
    public void JsonReportCarriesTheSameMetadata()
    {
        string path = Path.Combine(Path.GetTempPath(), $"kbdiag-{Guid.NewGuid():N}.jsonl");
        try
        {
            var info = new RunInfo("US English", "Win32ConsoleDriver", "native", "B (native events)",
                SourceLabels.NativeInjected, "conhost", "windows", "unknown", KeyboardCapabilities.None);
            Report.WriteJsonLines(path, info, [], [], [], 0);

            string run = File.ReadLines(path).First();
            Assert.Contains("\"label\":\"NATIVE INJECTED", run);
            Assert.Contains("\"level\":\"B (native events)\"", run);
            Assert.Contains("\"host\":\"conhost\"", run);
            Assert.Contains("\"os\":\"windows\"", run);
            Assert.Contains("\"activeLayout\":\"unknown\"", run);
        }
        finally
        {
            File.Delete(path);
        }
    }

    // ── Hosts ───────────────────────────────────────────────────────────────

    [Fact]
    public void HelloCarriesTheTargetWindowAndItsHost()
    {
        var hello = new Hello("T", 1, "Win32ConsoleDriver", 7, "windows", Wire.Version) { Hwnd = 0x1B06A4, Host = "conhost" };

        var parsed = Assert.IsType<Hello>(Wire.Parse(Wire.Serialize(hello)));

        Assert.Equal(0x1B06A4, parsed.Hwnd);
        Assert.Equal("conhost", parsed.Host);
        Assert.Equal(2, Wire.Version);
    }

    [Theory]
    [InlineData(0x1234L, "conhost", null)]
    [InlineData(0x1234L, "SDL window", null)]
    [InlineData(0x1234L, "Windows Terminal", "cannot prove which tab")]
    [InlineData(0x1234L, "unknown console host (Foo)", "cannot prove which tab")]
    [InlineData(0L, "conhost", "no window")]
    public void OsInjectionAcceptsOnlyAHostWhoseForegroundProvesWhereKeysGo(long hwnd, string host, string? refusal)
    {
        var hello = new Hello("T", 1, "Win32ConsoleDriver", 7, "windows", Wire.Version) { Hwnd = hwnd, Host = host };

        string? actual = WindowsSendInputSource.HostRefusal(hello);

        if (refusal is null) Assert.Null(actual);
        else Assert.Contains(refusal, actual);
    }

    [Fact]
    public void NativeInjectionKnowsWhichQueueEachDriverReads()
    {
        Assert.Contains("WriteConsoleInputW", RemoteInjectorSource.QueueOf("Win32ConsoleDriver"));
        Assert.Contains("SDL_PushEvent", RemoteInjectorSource.QueueOf("SDLDriver"));
        Assert.Contains("SDL_PushEvent", RemoteInjectorSource.QueueOf("SDLGpuDriver"));
        Assert.Null(RemoteInjectorSource.QueueOf("AnsiTerminalDriver"));
    }

    // ── Layout verification ─────────────────────────────────────────────────

    [Fact]
    public void WrongLayoutIsInvalidWhereTheQueryIsReliableAndLeftToTheFingerprintWhereNot()
    {
        var czech = new ActiveLayout(0xF0050405, "00010405", "Czech (QWERTY)");
        var qwertz = new ActiveLayout(0x04050405, "00000405", "Czech");
        var unknown = new ActiveLayout(0, "", "");

        Assert.Null(LayoutCheck.Mismatch(Cz, czech, reliable: true));
        Assert.Contains("active layout is not US English", LayoutCheck.Mismatch(Us, czech, reliable: true));
        // The language id alone would call QWERTZ a match; the KLID does not.
        Assert.Contains("active layout is not Czech QWERTY", LayoutCheck.Mismatch(Cz, qwertz, reliable: true));
        Assert.Null(LayoutCheck.Mismatch(Us, czech, reliable: false));
        Assert.Null(LayoutCheck.Mismatch(Us, unknown, reliable: true));
    }

    [Theory]
    [InlineData("ě", null)]
    [InlineData("2", "active layout is not Czech QWERTY")]
    [InlineData("", "active layout is not Czech QWERTY")]
    [InlineData("ěě", "active layout is not Czech QWERTY")]
    public void FingerprintVerdictComparesTextOnly(string observed, string? verdict) =>
        Assert.Equal(verdict, LayoutCheck.FingerprintMismatch(Cz, "ě", observed));

    [Fact]
    public void OracleAgreementIsSilentAndDisagreementIsReportedNotCorrected()
    {
        OracleText? Agrees(PhysicalKey key, KeyLevel level)
        {
            KeyLevels levels = Cz.Keys[key];
            string? text = level switch { KeyLevel.Plain => levels.Plain, KeyLevel.Shift => levels.Shift, _ => levels.AltGr };
            return new OracleText(text ?? "", Cz.IsDead(key, level));
        }

        Assert.Empty(LayoutCheck.Disagreements(Cz, Agrees));

        IReadOnlyList<OracleDisagreement> differences = LayoutCheck.Disagreements(Cz, (key, level) =>
            (key, level) switch
            {
                (PhysicalKey.Digit2, KeyLevel.Plain) => new OracleText("2", false),        // different text
                (PhysicalKey.Equal, KeyLevel.Plain) => new OracleText("´", false),         // not dead after all
                (PhysicalKey.KeyA, KeyLevel.AltGr) => new OracleText("æ", false),          // a level the profile lacks
                (PhysicalKey.Digit3, _) => null,                                            // oracle has no answer
                _ => Agrees(key, level),
            });

        Assert.Equal(
            [
                "Plain Digit2: profile \"ě\", OS layout \"2\"",
                "Plain Equal: profile dead \"´\", OS layout \"´\"",
                "AltGr KeyA: profile nothing, OS layout \"æ\"",
            ],
            differences.Select(d => d.ToString()));
        // The profile is untouched: a disagreement is a finding, not a patch.
        Assert.Equal("ě", Cz.Text(PhysicalKey.Digit2, KeyLevel.Plain));
    }

    // ── The foreground guard ────────────────────────────────────────────────

    private sealed class FakeDesktop(long foreground)
    {
        public long Foreground { get; set; } = foreground;
        public List<IReadOnlyList<KeyStroke>> Calls { get; } = new();
        public IEnumerable<KeyStroke> Sent => Calls.SelectMany(c => c);
        public PhysicalInjector Injector(long target, int maxEvents = InputSafety.MaxEvents) =>
            new(() => Foreground, Calls.Add, target, maxEvents);
    }

    [Fact]
    public void KeysAreSentOneByOneWhileTheTargetIsInTheForeground()
    {
        var desktop = new FakeDesktop(42);
        PhysicalInjector injector = desktop.Injector(42);
        Step step = Step(Cz, "modifiers", "Shift+Digit2");

        injector.Play(step.Strokes);

        Assert.Equal(step.Strokes, desktop.Sent);
        Assert.All(desktop.Calls, call => Assert.Single(call));
        Assert.Empty(injector.Pressed);
        Assert.Equal(4, injector.Sent);
    }

    [Fact]
    public void FocusLossStopsInjectionReleasesEverythingHeldAndAbortsTheRun()
    {
        var desktop = new FakeDesktop(42);
        PhysicalInjector injector = desktop.Injector(42);
        IReadOnlyList<KeyStroke> strokes = Step(Cz, "modifiers", "AltGr+Digit2").Strokes;
        int played = 0;

        // Another window comes forward after AltGr and Digit2 are down.
        var aborted = Assert.Throws<InputAbortedException>(() =>
            injector.Play(strokes, pause: () => { if (++played == 2) desktop.Foreground = 7; }));

        Assert.Contains("lost the foreground", aborted.Message);
        Assert.Equal(
            [
                new KeyStroke(PhysicalKey.AltRight, true), new KeyStroke(PhysicalKey.Digit2, true, "@"),
                // released by the guard, most recent first; the rest of the step is never sent
                new KeyStroke(PhysicalKey.Digit2, false), new KeyStroke(PhysicalKey.AltRight, false),
            ],
            desktop.Sent);
        Assert.Empty(injector.Pressed);

        // And nothing at all is sent to a window that was never the target.
        var other = new FakeDesktop(7);
        Assert.Throws<InputAbortedException>(() => other.Injector(42).Play(strokes));
        Assert.Empty(other.Calls);
    }

    [Fact]
    public void BatchIsOneCallAfterOneForegroundCheck()
    {
        var desktop = new FakeDesktop(42);
        Step rollover = Step(Us, "release", "KeyA+KeyB rollover");

        desktop.Injector(42).Play(rollover.Strokes, batch: true);

        Assert.True(rollover.Batch);
        Assert.Equal(rollover.Strokes, Assert.Single(desktop.Calls));
    }

    [Fact]
    public void InjectionHasAHardUpperBound()
    {
        var desktop = new FakeDesktop(42);
        PhysicalInjector injector = desktop.Injector(42, maxEvents: 5);
        IReadOnlyList<KeyStroke> tap = Step(Us, "main", "KeyA").Strokes;

        injector.Play(tap);
        injector.Play(tap);
        var aborted = Assert.Throws<InputAbortedException>(() => injector.Play(tap));

        Assert.Contains("limit of 5", aborted.Message);
        // Five injected transitions, then only the release of the key that was still down.
        Assert.Equal(5, injector.Sent);
        Assert.Equal(6, desktop.Sent.Count());
        Assert.Empty(injector.Pressed);
        Assert.Equal(new KeyStroke(PhysicalKey.KeyA, false), desktop.Sent.Last());
    }

    // ── Denylist and locks ──────────────────────────────────────────────────

    public static IEnumerable<object[]> SystemChords() =>
    [
        [new[] { PhysicalKey.AltLeft, PhysicalKey.F4 }, "Alt+F4"],
        [new[] { PhysicalKey.AltRight, PhysicalKey.F4 }, "Alt+F4"],
        [new[] { PhysicalKey.AltLeft, PhysicalKey.Tab }, "Alt+Tab"],
        [new[] { PhysicalKey.AltLeft, PhysicalKey.Escape }, "Alt+Esc"],
        [new[] { PhysicalKey.ControlLeft, PhysicalKey.Escape }, "Ctrl+Esc"],
        [new[] { PhysicalKey.AltLeft, PhysicalKey.Space }, "Alt+Space"],
        [new[] { PhysicalKey.AltLeft, PhysicalKey.Enter }, "Alt+Enter"],
        [new[] { PhysicalKey.ControlLeft, PhysicalKey.AltLeft, PhysicalKey.Delete }, "Ctrl+Alt+Del"],
    ];

    [Theory, MemberData(nameof(SystemChords))]
    public void SystemChordsAreRefusedBeforeAnythingIsSent(PhysicalKey[] chord, string name)
    {
        KeyStroke[] strokes = [.. chord.Select(k => new KeyStroke(k, true)), .. chord.Reverse().Select(k => new KeyStroke(k, false))];
        var desktop = new FakeDesktop(42);

        Assert.Contains(name, InputSafety.Denied(strokes));
        var aborted = Assert.Throws<InputAbortedException>(() => desktop.Injector(42).Play(strokes));
        Assert.Contains("refused", aborted.Message);
        Assert.Empty(desktop.Calls);
    }

    [Fact]
    public void ASequenceThatWouldLeaveAKeyHeldIsRefused() =>
        Assert.Contains("left held", InputSafety.Denied([new KeyStroke(PhysicalKey.ShiftLeft, true), new KeyStroke(PhysicalKey.KeyA, true), new KeyStroke(PhysicalKey.KeyA, false)]));

    // Injection is expressible only as profile steps, and none of them is a denied chord.
    [Fact]
    public void NoSuiteStepIsADeniedChordAndEveryStepReleasesWhatItPresses()
    {
        foreach (LayoutProfile profile in LayoutProfile.All)
            foreach (Step step in Suites.Build(profile, Suites.Full, latin1Identity: false, lockKeys: true)
                         .Where(s => s.SdlScript == null))
            {
                Assert.NotEmpty(step.Strokes);
                Assert.Null(InputSafety.Denied(WindowsSendInputSource.Expand(step)));
            }
    }

    [Fact]
    public void LockKeysAreOptInAndLockStatesAreNativeOnly()
    {
        Assert.DoesNotContain(Suites.Build(Cz, Suites.Full, false), s => s.Strokes.Any(k => k.Key == PhysicalKey.NumLock));
        Step numLock = Step(Cz, "keypad", "NumLock");
        Assert.Equal(4, numLock.Strokes.Count(s => s.Key == PhysicalKey.NumLock));

        using var os = new WindowsSendInputSource(TargetConsole.Inherit);
        using var human = new PromptedHumanSource(TargetConsole.Inherit, false, 1000);
        Step caps = Step(Cz, "modifiers", "CapsLock+KeyA"), order = Suites.Build(Cz, "release", false).First(s => s.SdlScript != null);
        Assert.All(new[] { caps, order }, s =>
        {
            Assert.True(s.NativeOnly);
            Assert.NotNull(os.CannotDeliver(s));
            Assert.NotNull(human.CannotDeliver(s));
        });
        Assert.Null(os.CannotDeliver(numLock));
        // F11 belongs to the console host; an SDL window gets it.
        using var console = new WindowsSendInputSource(TargetConsole.Conhost);
        Assert.Contains("F11", console.CannotDeliver(Step(Cz, "function", "F11")));
        Assert.Null(os.CannotDeliver(Step(Cz, "function", "F11")));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void LockIsToggledBackOnlyWhenItChanged(bool initiallyOn)
    {
        bool on = initiallyOn;
        int toggles = 0;
        var restorer = new LockRestorer(() => on, () => { on = !on; toggles++; });

        Assert.False(restorer.Restore());         // untouched: nothing to do
        on = !on;                                 // a step pressed the lock key once
        Assert.True(restorer.Restore());
        Assert.Equal(initiallyOn, on);
        Assert.False(restorer.Restore());
        Assert.Equal(1, toggles);
    }

    [Fact]
    public void HeldKeyIsExpandedIntoRepeatedKeyDownsBeforeItsRelease()
    {
        Step held = Step(Cz, "modifiers", "AltRight+KeyX held");

        Assert.Equal(
            [
                (PhysicalKey.AltRight, true), (PhysicalKey.KeyX, true), (PhysicalKey.KeyX, true), (PhysicalKey.KeyX, true),
                (PhysicalKey.KeyX, false), (PhysicalKey.AltRight, false),
            ],
            WindowsSendInputSource.Expand(held).Select(s => (s.Key, s.Down)));
    }

    // ── Native events (Level B) ─────────────────────────────────────────────

    [Fact]
    public void EveryKeyPositionHasItsNativeIdentities()
    {
        foreach (PhysicalKey key in PhysicalKeys.All)
        {
            Assert.NotEqual(0, NativeKeys.VirtualKey(key));
            Assert.NotEqual(0, NativeKeys.SdlScancode(key));
        }
        Assert.Equal(PhysicalKeys.All.Count, PhysicalKeys.All.Select(NativeKeys.SdlScancode).Distinct().Count());
        // Num Lock is the plain make code: with the E0 prefix Windows sees no key at all (observed).
        Assert.Equal(((byte)0x45, false), PhysicalKeys.ScanCode(PhysicalKey.NumLock));
    }

    [Fact]
    public void CzechDigit2AsAConsoleRecordCarriesTheLayoutsCharacterAndThePhysicalScan()
    {
        IReadOnlyList<NativeEvent> records = NativeEvents.ForConsole(Step(Cz, "main", "Digit2"), Cz);

        Assert.Equal(2, records.Count);
        Assert.All(records, r =>
        {
            Assert.Equal(NativeEvent.KeyKind, r.Kind);
            Assert.Equal((uint)'2', r.Key);
            Assert.Equal(0x03, r.Scan);
            Assert.Equal('ě', (char)r.Char);
        });
        Assert.Equal([true, false], records.Select(r => r.Down));
    }

    // Shapes observed under conhost with SendInput and reproduced for injection.
    [Fact]
    public void ConsoleRecordsFollowWhatTheConsoleHostWasObservedToSend()
    {
        IReadOnlyList<NativeEvent> altGr = NativeEvents.ForConsole(Step(Cz, "modifiers", "AltGr+Digit2"), Cz);
        Assert.Equal(new uint[] { 0x11, 0x12, '2', '2', 0x11, 0x12 }, altGr.Select(r => r.Key));
        Assert.Equal(new uint[] { 0x008, 0x109, 0x009, 0x009, 0x001, 0x100 }, altGr.Select(r => r.State));
        Assert.Equal(new ushort[] { 0, 0, '@', '@', 0, 0 }, altGr.Select(r => r.Char));

        IReadOnlyList<NativeEvent> dead = NativeEvents.ForConsole(Step(Cz, "unicode", "Equal"), Cz);
        Assert.Equal(new uint[] { 0xBB, 0xBB, 0xBB, 0x20, 0x20 }, dead.Select(r => r.Key));
        Assert.Equal(new ushort[] { 0x0D, 0x00, 0x0D, 0x39, 0x39 }, dead.Select(r => r.Scan));
        Assert.Equal(new ushort[] { 0, '´', '´', '´', ' ' }, dead.Select(r => r.Char));
        Assert.Equal([true, false, false, true, false], dead.Select(r => r.Down));

        IReadOnlyList<NativeEvent> composed = NativeEvents.ForConsole(Step(Cz, "unicode", "Equal,KeyE"), Cz);
        Assert.Equal(new ushort[] { 0, '´', '´', 'é', 'e' }, composed.Select(r => r.Char));

        IReadOnlyList<NativeEvent> keypad = NativeEvents.ForConsole(Step(Us, "keypad", "NumpadEnter"), Us);
        Assert.All(keypad, r => Assert.Equal(NativeEvents.NumLockOn | NativeEvents.EnhancedKey, r.State));
        Assert.All(NativeEvents.ForConsole(Step(Us, "navigation", "Enter"), Us), r => Assert.Equal(0u, r.State));
    }

    [Fact]
    public void CzechDigit2AsSdlEventsIsKeyDownTextCommitKeyUp()
    {
        IReadOnlyList<NativeEvent> events = NativeEvents.ForSdl(Step(Cz, "main", "Digit2"), Cz);

        Assert.Equal([NativeEvent.KeyKind, NativeEvent.TextKind, NativeEvent.KeyKind], events.Select(e => e.Kind));
        Assert.Equal((uint)'2', events[0].Key);          // SDL reports the digit for the Czech number row
        Assert.True(events[0].Down);
        Assert.Equal("ě", events[1].Text);
        Assert.Equal((uint)'2', events[2].Key);
        Assert.False(events[2].Down);
        Assert.Equal(31, events[0].Scan);
    }

    [Fact]
    public void SdlEventsCarryModifiersRepeatsAndNamedKeycodes()
    {
        // Observed with SendInput: AltGr reaches SDL as Right Alt alone.
        IReadOnlyList<NativeEvent> altGr = NativeEvents.ForSdl(Step(Cz, "modifiers", "AltGr+KeyE"), Cz);
        Assert.Equal(new uint[] { 0x400000E6, 'e', 0, 'e', 0x400000E6 }, altGr.Select(e => e.Key));
        Assert.Equal(new uint[] { 0x200, 0x200, 0, 0x200, 0 }, altGr.Select(e => e.State));
        Assert.Equal("€", altGr[2].Text);

        IReadOnlyList<NativeEvent> held = NativeEvents.ForSdl(Step(Us, "release", "KeyA held"), Us);
        Assert.Equal([false, false, true, false, true, false, false], held.Select(e => e.Repeat));
        Assert.Equal(3, held.Count(e => e.Kind == NativeEvent.TextKind));

        Assert.Equal(new uint[] { 0x4000003E, 0x4000003E }, NativeEvents.ForSdl(Step(Us, "function", "F5"), Us).Select(e => e.Key));
        IReadOnlyList<NativeEvent> keypad = NativeEvents.ForSdl(Step(Us, "keypad", "Numpad1"), Us);
        Assert.Equal(0x40000059u, keypad[0].Key);
        Assert.Equal(NativeEvents.SdlNum, keypad[0].State);
        Assert.Equal("1", keypad[1].Text);

        Step order = Suites.Build(Cz, "release", false).First(s => s.SdlScript != null);
        Assert.Same(order.SdlScript, NativeEvents.ForSdl(order, Cz));
    }

    [Fact]
    public void InjectMessageRoundTripsItsNativeEvents()
    {
        var inject = new Inject(9, NativeEvents.ForSdl(Step(Cz, "main", "Digit2"), Cz));

        string line = Wire.Serialize(inject);
        var parsed = Assert.IsType<Inject>(Wire.Parse(line));

        Assert.Equal(9, parsed.Step);
        Assert.Equal(inject.Events, parsed.Events);
        Assert.Contains("\\u011B", line);
    }
}
