using TSharpVision.Constants;
using TSharpVision.Diagnostics.Keyboard.Controller;
using TSharpVision.Diagnostics.Keyboard.Profiles;
using TSharpVision.Diagnostics.Keyboard.Protocol;
using TSharpVision.Drivers;

namespace TSharpVision.Diagnostics.Keyboard.Tests;

public sealed class SequenceMatcherTests
{
    private const KeyboardCapabilities All = KeyboardCapabilities.KeyReleaseEvents
        | KeyboardCapabilities.StandaloneModifierTransitions | KeyboardCapabilities.DistinctNumericKeypad;
    private const KeyboardCapabilities None = KeyboardCapabilities.None;
    private const uint Shift = Keys.kbLeftShift, Ctrl = Keys.kbLeftCtrl, Alt = Keys.kbRightAlt;

    private static readonly LayoutProfile Us = UsProfile.Instance, Cz = CzechQwertyProfile.Instance;

    private static RecordedEvent Down(ushort code, string text = "", uint state = 0) =>
        new(0, RecordedEvent.KeyDown, code, (byte)code, (byte)(code >> 8), 0, state, text, 0);
    private static RecordedEvent Up(ushort code, uint state = 0) =>
        new(0, RecordedEvent.KeyUp, code, (byte)code, (byte)(code >> 8), 0, state, "", 0);
    private static RecordedEvent Mod(uint state) => new(0, RecordedEvent.ModifierChanged, 0, 0, 0, 0, state, "", 0);

    private static Step Text(LayoutProfile profile, PhysicalKey key, KeyLevel level = KeyLevel.Plain, bool latin1 = false) =>
        Suites.TextKey(profile, key, level, "test", latin1);
    private static Step Find(string suite, string id, LayoutProfile? profile = null) =>
        Suites.Build(profile ?? Us, suite, latin1Identity: false).Single(s => s.Id == id);

    private static StepOutcome Evaluate(Step step, KeyboardCapabilities capabilities, params RecordedEvent[] events) =>
        SequenceMatcher.Evaluate(step.Expect, events, capabilities);

    private static void AssertFails(StepOutcome outcome, string reason)
    {
        Assert.Equal(Verdict.Fail, outcome.Verdict);
        Assert.Contains(outcome.Reasons, r => r.Contains(reason, StringComparison.Ordinal));
    }

    // ── PASS ────────────────────────────────────────────────────────────────

    [Fact]
    public void TextWithoutIdentityIsCorrect()
    {
        Step step = Text(Cz, PhysicalKey.Digit2);

        Assert.Equal(Verdict.Pass, Evaluate(step, All, Down(0, "ě"), Up(0)).Verdict);
        // A text-only press has no identity, so a transport that reports no release for it is right too.
        Assert.Equal(Verdict.Pass, Evaluate(step, All, Down(0, "ě")).Verdict);
    }

    [Fact]
    public void NamedKeyWithMatchingReleasePasses() =>
        Assert.Equal(Verdict.Pass, Evaluate(Find("function", "F5"), All, Down(Keys.kbF5), Up(Keys.kbF5)).Verdict);

    [Fact]
    public void AsciiTextKeepsItsIdentityOnPressAndRelease() =>
        Assert.Equal(Verdict.Pass, Evaluate(Text(Us, PhysicalKey.KeyA), All, Down('a', "a"), Up('a')).Verdict);

    [Fact]
    public void ConsoleLatin1IdentityIsAcceptedOnlyUnderTheConsolePolicy()
    {
        RecordedEvent[] events = [Down(0x00E1, "á"), Up(0x00E1)];

        Assert.Equal(Verdict.Pass, Evaluate(Text(Cz, PhysicalKey.Digit8, latin1: true), All, events).Verdict);
        Assert.Equal(Verdict.Pass, Evaluate(Text(Cz, PhysicalKey.Digit8, latin1: true), All, Down(0, "á")).Verdict);
        AssertFails(Evaluate(Text(Cz, PhysicalKey.Digit8), All, events), "false identity 0x00E1");
    }

    [Fact]
    public void ShiftChordPassesWithTransitionsStateTextAndIdentity()
    {
        Step step = Text(Cz, PhysicalKey.Digit2, KeyLevel.Shift);

        StepOutcome outcome = Evaluate(step, All, Mod(Shift), Down('2', "2", Shift), Up('2', Shift), Mod(0));

        Assert.Equal(Verdict.Pass, outcome.Verdict);
    }

    [Fact]
    public void AltGrTextPassesWithWhateverTransitionsTheHostReports()
    {
        Step at = Text(Cz, PhysicalKey.Digit2, KeyLevel.AltGr), euro = Text(Cz, PhysicalKey.KeyE, KeyLevel.AltGr);

        Assert.Equal(Verdict.Pass, Evaluate(at, All,
            Mod(Ctrl), Mod(Ctrl | Alt), Down('@', "@", Ctrl | Alt), Up('@', Ctrl | Alt), Mod(Ctrl), Mod(0)).Verdict);
        Assert.Equal(Verdict.Pass, Evaluate(euro, All, Mod(Alt), Down(0, "€", Alt), Mod(0)).Verdict);
    }

    [Fact]
    public void HeldKeyRepeatsAreNotTypedTwice()
    {
        Step step = Text(Cz, PhysicalKey.Digit2);

        Assert.Equal(Verdict.Pass, Evaluate(step, All, Down(0, "ě"), Down(0, "ě"), Down(0, "ě"), Up(0)).Verdict);
    }

    [Fact]
    public void TextIsJudgedAsAStepTotalNotPerEvent()
    {
        var expect = new Expectation([ExpectedItem.Press(IdentityRule.Any), ExpectedItem.Press(IdentityRule.Any)])
        {
            TextTotal = "ěš",
        };

        // One commit of two characters and two commits of one character are the same typed text.
        Assert.Equal(Verdict.Pass, SequenceMatcher.Evaluate(expect, [Down(0, "ě"), Down(0, "š")], All).Verdict);
        Assert.Equal(Verdict.Pass, SequenceMatcher.Evaluate(expect with { Items = [expect.Items[0]] },
            [Down(0, "ěš")], All).Verdict);
        Assert.Equal(Verdict.Fail, SequenceMatcher.Evaluate(expect, [Down(0, "ě"), Down(0, "x")], All).Verdict);
    }

    [Fact]
    public void DeadKeyResolvedBySpaceCommitsItsAccentWithoutBorrowingAnIdentity()
    {
        Step dead = Text(Cz, PhysicalKey.Equal), console = Text(Cz, PhysicalKey.Equal, latin1: true);

        Assert.Equal(Verdict.Pass, Evaluate(dead, All, Down(0, "´"), Up(0)).Verdict);
        Assert.Equal(Verdict.Pass, Evaluate(console, All, Down(0x00B4, "´"), Up(0x00B4)).Verdict);
        // The accent is not Space's text, and the dead key's own releases publish nothing.
        AssertFails(Evaluate(dead, All, Down(' ', "´"), Up(' ')), "false identity ' '");
        AssertFails(Evaluate(console, All, Up(0x00B4) with { Text = "´" }, Down(0x00B4, "´"), Up(0x00B4)),
            "KeyUp carries text");
        Assert.Equal(Verdict.Invalid, Evaluate(dead, All).Verdict);
    }

    [Fact]
    public void LockStateIsPartOfTheExpectedModifierState()
    {
        Step caps = Find("modifiers", "CapsLock+KeyA");

        Assert.Equal(Verdict.Pass, Evaluate(caps, All, Down('A', "A", Keys.kbCapsState), Up('A', Keys.kbCapsState)).Verdict);
        AssertFails(Evaluate(caps, All, Down('A', "A"), Up('A')), "modifier state lost on KeyDown: CapsLock not set");
    }

    // ── FAIL ────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(PhysicalKey.Digit2, "ě", Keys.kbEsc, "kbEsc")]
    [InlineData(PhysicalKey.Digit3, "š", (ushort)'a', "'a'")]
    [InlineData(PhysicalKey.Digit4, "č", Keys.kbEnter, "kbEnter")]
    [InlineData(PhysicalKey.Digit4, "č", Keys.kbCtrlM, "kbCtrlM")]
    [InlineData(PhysicalKey.Digit5, "ř", (ushort)'Y', "'Y'")]
    public void FabricatedIdentityForCzechTextFails(PhysicalKey key, string text, ushort code, string name) =>
        AssertFails(Evaluate(Text(Cz, key, latin1: true), All, Down(code, text), Up(code)), $"false identity {name}");

    [Fact]
    public void MissingOrWrongTextFails()
    {
        Step step = Text(Cz, PhysicalKey.Digit2);

        AssertFails(Evaluate(step, All, Down(0, "e")), "text: expected \"ě\" (U+011B), got \"e\"");
        AssertFails(Evaluate(Find("main", "KeyA"), All, Down('a'), Up('a')), "text: expected \"a\", got \"\"");
    }

    [Fact]
    public void WrongOrMissingNamedIdentityFails()
    {
        Step f5 = Find("function", "F5");

        AssertFails(Evaluate(f5, All, Down(Keys.kbF4), Up(Keys.kbF4)), "wrong identity: expected kbF5, got kbF4");
        AssertFails(Evaluate(f5, All, Down(0), Up(0)), "identity missing: expected kbF5");
        AssertFails(Evaluate(Find("main", "KeyA"), All, Down(0, "a")), "identity missing: expected 'a'");
    }

    [Fact]
    public void ReleaseMustCarryTheIdentityOfItsPressWhenReleasesAreAdvertised()
    {
        Step f5 = Find("function", "F5");

        AssertFails(Evaluate(f5, All, Down(Keys.kbF5), Up(Keys.kbF4)), "release identity kbF4 differs from press kbF5");
        AssertFails(Evaluate(f5, All, Down(Keys.kbF5)), "missing release");
    }

    // The SDL sequence the architecture report predicted for Czech Digit2: the layout keycode leaks
    // into a release although the press it belongs to was text-only.
    [Fact]
    public void ReleaseWithAnIdentityNoPressHadFails()
    {
        StepOutcome outcome = Evaluate(Text(Cz, PhysicalKey.Digit2), All, Down(0, "ě"), Up('2'));

        AssertFails(outcome, "release with identity '2' matches no press in the step");
    }

    [Fact]
    public void EventsTheCapabilitiesDoNotCoverAreFabricatedReports()
    {
        AssertFails(Evaluate(Find("function", "F5"), None, Down(Keys.kbF5), Up(Keys.kbF5)),
            "without the KeyReleaseEvents capability");
        AssertFails(Evaluate(Text(Cz, PhysicalKey.Digit2, KeyLevel.Shift), None, Mod(Shift), Down('2', "2", Shift)),
            "without the StandaloneModifierTransitions capability");
    }

    [Fact]
    public void KeypadAliasingTheMainKeyFailsOnlyWhereADistinctKeypadIsAdvertised()
    {
        Step kp1 = Find("keypad", "Numpad1");

        AssertFails(Evaluate(kp1, All, Down('1', "1"), Up('1')), "wrong identity: expected kbKeypad1, got '1'");
        AssertFails(Evaluate(kp1, All, Down(Keys.kbEnd), Up(Keys.kbEnd)), "expected kbKeypad1, got kbEnd");
        Assert.Equal(Verdict.Pass, Evaluate(kp1, All, Down(Keys.kbKeypad1, "1"), Up(Keys.kbKeypad1)).Verdict);
        // SDL: the key, then its text as a separate commit, then the key's release.
        Assert.Equal(Verdict.Pass,
            Evaluate(kp1, All, Down(Keys.kbKeypad1), Down('1', "1"), Up(Keys.kbKeypad1)).Verdict);
    }

    [Fact]
    public void LostOrStuckModifierStateFails()
    {
        Step shifted = Text(Cz, PhysicalKey.Digit2, KeyLevel.Shift);

        AssertFails(Evaluate(shifted, All, Mod(Shift), Down('2', "2"), Up('2'), Mod(0)), "modifier state lost");
        AssertFails(Evaluate(shifted, All, Mod(Shift), Down('2', "2", Shift), Up('2', Shift)), "missing modifier");
        AssertFails(Evaluate(shifted, All, Mod(Shift), Down('2', "2", Shift), Up('2', Shift), Mod(Shift)),
            "modifier stuck after the step: Shift");
        AssertFails(Evaluate(Text(Cz, PhysicalKey.Digit2), All, Down(0, "ě", Ctrl)), "unexpected modifier state");
    }

    [Fact]
    public void AltGrMustNotAlsoPublishAShortcut()
    {
        Step at = Text(Cz, PhysicalKey.Digit2, KeyLevel.AltGr);

        StepOutcome outcome = Evaluate(at, All, Down(Keys.kbAlt2, "", Ctrl | Alt), Down('@', "@", Ctrl | Alt), Up('@'));

        AssertFails(outcome, "false identity kbAlt2");
    }

    [Fact]
    public void AnEventNobodyAskedForFails()
    {
        AssertFails(Evaluate(Find("function", "F5"), All, Down(Keys.kbF5), Down(Keys.kbF6), Up(Keys.kbF5)),
            "unexpected event: KeyDown key=kbF6");
        AssertFails(Evaluate(Find("function", "F5"), All, Down(Keys.kbF5), Up(Keys.kbF5), Mod(Shift), Mod(0)),
            "unexpected event: ModifierChanged");
    }

    [Fact]
    public void TextOnAReleaseFails()
    {
        RecordedEvent release = Up('a') with { Text = "a" };

        AssertFails(Evaluate(Find("main", "KeyA"), All, Down('a', "a"), release), "KeyUp carries text");
    }

    // ── LIMITED ─────────────────────────────────────────────────────────────

    [Fact]
    public void TransportWithoutReleasesOrKeypadIdentityIsLimitedNotFailed()
    {
        StepOutcome text = Evaluate(Text(Cz, PhysicalKey.Digit2), None, Down(0, "ě"));
        Assert.Equal(Verdict.Limited, text.Verdict);
        Assert.Contains("no key release on this transport", text.Reasons);

        StepOutcome keypad = Evaluate(Find("keypad", "Numpad1"), None, Down('1', "1"));
        Assert.Equal(Verdict.Limited, keypad.Verdict);
        Assert.Contains("the keypad aliases main keys on this transport", keypad.Reasons);
    }

    [Fact]
    public void ModifierStateATransportCannotReportIsLimited()
    {
        StepOutcome outcome = Evaluate(Text(Cz, PhysicalKey.Digit2, KeyLevel.Shift), None, Down('2', "2"));

        Assert.Equal(Verdict.Limited, outcome.Verdict);
        Assert.Contains("modifier state is not reported by this transport", outcome.Reasons);
        Assert.Contains("no standalone modifier transitions on this transport", outcome.Reasons);
    }

    [Fact]
    public void CapabilityConditionalItemsApplyOnlyWhenAdvertised()
    {
        Step f5 = Find("function", "F5");
        const KeyboardCapabilities releasesOnly = KeyboardCapabilities.KeyReleaseEvents;

        Assert.Equal(Verdict.Limited, Evaluate(f5, None, Down(Keys.kbF5)).Verdict);
        Assert.Equal(Verdict.Pass, Evaluate(f5, releasesOnly, Down(Keys.kbF5), Up(Keys.kbF5)).Verdict);
        AssertFails(Evaluate(f5, releasesOnly, Down(Keys.kbF5)), "missing release");
    }

    // A Kitty terminal that reports keys but not their text: the key is real, its text unknown.
    // The identity alone is the correct, LIMITED result; text the terminal never sent is a FAIL.
    [Fact]
    public void KeyReportedWithoutItsTextIsLimitedAndMustNotBeGivenText()
    {
        Step bare = Suites.Pty(15, "unicode").Single(s => s.Id == "kitty-011B");
        Step ascii = Suites.Pty(15, "main").Single(s => s.Id == "kitty-a");

        StepOutcome outcome = Evaluate(bare, All, Down(0), Up(0));
        Assert.Equal(Verdict.Limited, outcome.Verdict);
        Assert.Equal(["the terminal did not report associated text"], outcome.Reasons);
        AssertFails(Evaluate(bare, All, Down(0, "ě"), Up(0)), "text: expected \"\", got \"ě\" (U+011B)");
        AssertFails(Evaluate(bare, All, Down(Keys.kbEsc), Up(Keys.kbEsc)), "false identity kbEsc");

        Assert.Equal(Verdict.Limited, Evaluate(ascii, All, Down('a'), Up('a')).Verdict);
        AssertFails(Evaluate(ascii, All, Down('a', "a"), Up('a')), "text: expected \"\", got \"a\"");
        AssertFails(Evaluate(ascii, All, Down(0), Up(0)), "identity missing: expected 'a'");

        Step full = Suites.Pty(31, "unicode").Single(s => s.Id == "kitty-011B");
        Assert.Null(full.Expect.Limitation);
        Assert.Equal(Verdict.Pass, Evaluate(full, All, Down(0, "ě"), Up(0)).Verdict);
    }

    // The SDL contract for a text-only key: its release is real and carries the same absent
    // identity; the layout keycode ('2' for Czech "ě") is never a release identity.
    [Fact]
    public void TextOnlyPressReleasesWithoutIdentityOrNotAtAllButNeverWithAnInventedOne()
    {
        Step step = Text(Cz, PhysicalKey.Digit2);

        Assert.Equal(Verdict.Pass, Evaluate(step, All, Down(0, "ě"), Up(0)).Verdict);
        Assert.Equal(Verdict.Pass, Evaluate(step, All, Down(0, "ě"), Down(0, "ě"), Up(0)).Verdict);
        AssertFails(Evaluate(step, All, Down(0, "ě"), Up('2')), "matches no press in the step");
        AssertFails(Evaluate(step, All, Down(0, "ě"), Up(0), Up(0)), "matches no press in the step");
    }

    // ── SKIPPED / INVALID ───────────────────────────────────────────────────

    [Fact]
    public void StepThatNeedsAnAbsentCapabilityIsSkipped()
    {
        Step bareShift = Find("modifiers", "Shift"), release = Find("release", "F5");

        Assert.Equal(Verdict.Skipped, Evaluate(bareShift, None).Verdict);
        Assert.Equal(Verdict.Skipped, Evaluate(release, None, Down(Keys.kbF5)).Verdict);
        Assert.Equal(Verdict.Pass, Evaluate(bareShift, All, Mod(Shift), Mod(0)).Verdict);
        Assert.Equal(Verdict.Pass, Evaluate(release, All, Down(Keys.kbF5), Up(Keys.kbF5)).Verdict);
    }

    [Fact]
    public void NoEventsMakeTheStepInvalidRatherThanFailed()
    {
        StepOutcome outcome = Evaluate(Find("function", "F5"), All);

        Assert.Equal(Verdict.Invalid, outcome.Verdict);
        Assert.Contains("no keyboard event was observed", outcome.Reasons);
    }

    // ── Exit code ───────────────────────────────────────────────────────────

    [Theory]
    [InlineData(new[] { Verdict.Pass, Verdict.Limited, Verdict.Skipped }, 0)]
    [InlineData(new Verdict[0], 0)]
    [InlineData(new[] { Verdict.Pass, Verdict.Fail, Verdict.Limited }, 1)]
    [InlineData(new[] { Verdict.Fail, Verdict.Invalid, Verdict.Pass }, 2)]
    [InlineData(new[] { Verdict.Invalid }, 2)]
    public void ExitCodeIgnoresLimitedAndSkipped(Verdict[] verdicts, int expected) =>
        Assert.Equal(expected, Verdicts.ExitCode(verdicts));

    [Fact]
    public void ControllerMapsDriversAndKittyFlagsLikeTheFramework()
    {
        Assert.Equal("SDLDriver", DiagnosticRun.DriverVariable("sdl"));
        Assert.Equal("SDLGpuDriver", DiagnosticRun.DriverVariable("sdl-gpu"));
        Assert.Equal("AnsiTerminalDriver", DiagnosticRun.DriverVariable("terminal"));
        Assert.Null(DiagnosticRun.DriverVariable("x11"));
        Assert.True(DiagnosticRun.IsRequestedDriver("console", "Win32ConsoleDriver"));
        Assert.True(DiagnosticRun.IsRequestedDriver("console", "AnsiTerminalDriver"));
        Assert.False(DiagnosticRun.IsRequestedDriver("console", "SDLGpuDriver"));
        Assert.False(DiagnosticRun.IsRequestedDriver("console", "NullDriver"));
        Assert.False(DiagnosticRun.IsRequestedDriver("sdl", "SDLGpuDriver"));

        Assert.Equal(None, DiagnosticRun.KittyCapabilities(0));
        Assert.Equal(None, DiagnosticRun.KittyCapabilities(1 | 2 | 4));
        Assert.Equal(KeyboardCapabilities.DistinctNumericKeypad, DiagnosticRun.KittyCapabilities(8));
        Assert.Equal(All, DiagnosticRun.KittyCapabilities(31));
    }

    [Fact]
    public void ReportSummarisesAStepAndTheTransportLimitations()
    {
        Assert.Equal("text=\"ě\" (U+011B), key=none, released", Report.Observed([Down(0, "ě"), Up(0)]));
        Assert.Equal("text=\"\", key=kbF5", Report.Observed([Down(Keys.kbF5)]));
        Assert.Equal("no events", Report.Observed([]));

        Step step = Text(Cz, PhysicalKey.Digit2);
        var report = new StepReport(step, Evaluate(step, None, Down(0, "ě")), [Down(0, "ě")]);
        IReadOnlyList<string> limitations = Report.Limitations(None, [report], "partial Kitty negotiation");

        Assert.Equal(
            ["no key release events", "no standalone modifier transitions", "the numeric keypad aliases main keys",
             "raw scan code unavailable", "partial Kitty negotiation"],
            limitations);
        Assert.Empty(Report.Limitations(All, [report with { Events = [Down(0, "ě") with { RawScan = 3 }] }], null));
    }
}
