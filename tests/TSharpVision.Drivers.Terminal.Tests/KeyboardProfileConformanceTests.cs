using System.Text;
using TSharpVision.Constants;
using TSharpVision.Diagnostics.Keyboard.Controller;
using TSharpVision.Diagnostics.Keyboard.Profiles;
using TSharpVision.Diagnostics.Keyboard.Protocol;
using TSharpVision.Drivers;
using Xunit;

namespace TSharpVision.Drivers.Terminal.Tests;

/// <summary>
/// Level A against the shared keyboard profiles and transport steps. A terminal delivers text, not key
/// positions, so the profile contributes only what the layout types; the diagnostics matcher judges
/// the decoded events relative to the capabilities the negotiation produced.
/// </summary>
public sealed class KeyboardProfileConformanceTests
{
    private const string NoAssociatedText = "the terminal did not report associated text";

    /// <summary>A decoder whose terminal confirmed <paramref name="flags"/>; zero is a terminal without the protocol.</summary>
    private static TerminalInputDecoder Negotiated(int flags)
    {
        var decoder = new TerminalInputDecoder();
        decoder.BeginKeyboardNegotiation();
        if (flags == 0)
            decoder.Feed("\x1b[?62c"u8, _ => { });
        else
        {
            decoder.Feed("\x1b[?0u"u8, _ => { });
            decoder.Feed(Encoding.ASCII.GetBytes($"\x1b[?{flags}u"), _ => { });
        }
        Assert.False(decoder.TryRead(out _));
        return decoder;
    }

    private static List<TEvent> Feed(TerminalInputDecoder decoder, params byte[][] chunks)
    {
        foreach (byte[] chunk in chunks) decoder.Feed(chunk, _ => { });
        var events = new List<TEvent>();
        while (decoder.TryRead(out TEvent ev)) events.Add(ev);
        return events;
    }

    private static List<TEvent> Feed(TerminalInputDecoder decoder, string ascii) =>
        Feed(decoder, Encoding.ASCII.GetBytes(ascii));

    private static StepOutcome Evaluate(Step step, TerminalInputDecoder decoder, List<TEvent> events) =>
        SequenceMatcher.Evaluate(step.Expect, RecordedEvent.FromAll(events), decoder.KeyboardCapabilities);

    private static string Explain(Step step, StepOutcome outcome, List<TEvent> events) =>
        $"{step.Id}: {outcome.Verdict} — {string.Join("; ", outcome.Reasons)} — " +
        string.Join(", ", RecordedEvent.FromAll(events).Select(KeyNames.Describe));

    public static IEnumerable<object[]> Profiles() => LayoutProfile.All.Select(p => new object[] { p.Id });

    private static IEnumerable<Step> PlainTextSteps(LayoutProfile profile) =>
        Suites.Build(profile, Suites.Full, latin1Identity: false)
            .Where(s => s.Key is { } key && s.Id == key.ToString() && profile.Text(key, KeyLevel.Plain) != null);

    // ── Legacy ANSI: text only, and correctly LIMITED ───────────────────────

    [Theory, MemberData(nameof(Profiles))]
    public void AnsiTextOfEveryProfileKeyIsCorrectAndLimitedToWhatAnsiCanReport(string profileId)
    {
        LayoutProfile profile = LayoutProfile.Find(profileId)!;
        foreach (Step step in PlainTextSteps(profile))
        {
            TerminalInputDecoder decoder = Negotiated(0);
            List<TEvent> events = Feed(decoder, Encoding.UTF8.GetBytes(profile.Text(step.Key!.Value, KeyLevel.Plain)!));

            StepOutcome outcome = Evaluate(step, decoder, events);

            Assert.True(outcome.Verdict == Verdict.Limited, Explain(step, outcome, events));
            Assert.Equal(["no key release on this transport"], outcome.Reasons);
        }
    }

    // ── Kitty, fully negotiated: text, identity and release ─────────────────

    [Theory, MemberData(nameof(Profiles))]
    public void KittyTextOfEveryProfileKeyPassesWithItsRelease(string profileId)
    {
        LayoutProfile profile = LayoutProfile.Find(profileId)!;
        foreach (Step step in PlainTextSteps(profile))
        {
            TerminalInputDecoder decoder = Negotiated(31);
            int key = profile.Text(step.Key!.Value, KeyLevel.Plain)![0];
            List<TEvent> events = Feed(decoder, $"\x1b[{key};;{key}u\x1b[{key};1:3u");

            StepOutcome outcome = Evaluate(step, decoder, events);

            Assert.True(outcome.Verdict == Verdict.Pass, Explain(step, outcome, events));
            Assert.Equal(2, events.Count);
        }
    }

    // ── The transport steps of the PTY source, through the decoder ──────────

    [Theory]
    [InlineData(0, Verdict.Limited)]
    [InlineData(1, Verdict.Limited)]
    [InlineData(8, Verdict.Limited)]
    [InlineData(27, Verdict.Pass)]
    [InlineData(31, Verdict.Pass)]
    public void PtyTransportStepsAreCorrectForEveryNegotiationOutcome(int flags, Verdict expected)
    {
        foreach (Step step in Suites.Pty(flags, Suites.Full))
        {
            TerminalInputDecoder decoder = Negotiated(flags);
            Assert.Equal(DiagnosticRun.KittyCapabilities(flags), decoder.KeyboardCapabilities);
            List<TEvent> events = Feed(decoder, step.Chunks.ToArray());

            StepOutcome outcome = Evaluate(step, decoder, events);

            Assert.True(outcome.Verdict == expected, Explain(step, outcome, events));
        }
    }

    // Everything but associated text: releases, modifiers and keypad are complete, while every
    // text key is LIMITED to its identity because the terminal never said what it typed.
    [Fact]
    public void PtyTransportStepsWithoutAssociatedTextLimitOnlyTheTextKeys()
    {
        IReadOnlyList<Step> steps = Suites.Pty(15, Suites.Full);
        foreach (Step step in steps)
        {
            TerminalInputDecoder decoder = Negotiated(15);
            List<TEvent> events = Feed(decoder, step.Chunks.ToArray());

            StepOutcome outcome = Evaluate(step, decoder, events);

            bool textKey = step.Expect.Limitation != null;
            Assert.True(outcome.Verdict == (textKey ? Verdict.Limited : Verdict.Pass), Explain(step, outcome, events));
            Assert.All(events, e => Assert.Empty(e.keyDown.text));
            if (textKey) Assert.Equal([NoAssociatedText], outcome.Reasons);
        }
        // "a", six non-ASCII keys, Shift+"ě" and the held "a".
        Assert.Equal(9, steps.Count(s => s.Expect.Limitation == NoAssociatedText));
    }

    // ── Kitty without associated text: a key code is an identity, never text ─

    // Report-all-keys (8) confirmed, associated text (16) not. "ě" arrives as the bare key code
    // CSI 283 u: a real key press with no legacy identity and no reported text. It is published as
    // exactly that, neither dropped nor given a text the terminal never sent.
    [Theory]
    [InlineData(8)]
    [InlineData(1 | 8)]
    [InlineData(2 | 8)]
    [InlineData(1 | 2 | 4 | 8)]
    public void NonAsciiKeyWithoutAssociatedTextIsAPressWithNeitherIdentityNorText(int flags)
    {
        TEvent press = Assert.Single(Feed(Negotiated(flags), "\x1b[283u"));

        Assert.Equal(Events.evKeyDown, press.What);
        Assert.Empty(press.keyDown.text);
        Assert.Equal(0, press.keyDown.keyCode);
        Assert.Equal(0, press.keyDown.charScan.ToUShort());
        Assert.Equal(0u, press.keyDown.controlKeyState);
    }

    [Theory]
    [InlineData(283)]       // ě
    [InlineData(353)]       // š
    [InlineData(269)]       // č
    [InlineData(345)]       // ř
    [InlineData(382)]       // ž
    [InlineData(253)]       // ý
    [InlineData(225)]       // á
    [InlineData(237)]       // í
    [InlineData(233)]       // é
    [InlineData(8364)]      // €
    [InlineData(128512)]    // 😀
    public void NoNonAsciiKeyCodeIsEverTurnedIntoTextOrAnIdentity(int key)
    {
        foreach (int flags in new[] { 8, 15, 31 })
        {
            TEvent press = Assert.Single(Feed(Negotiated(flags), $"\x1b[{key}u"));

            Assert.Empty(press.keyDown.text);
            Assert.Equal(0, press.keyDown.keyCode);
            Assert.Equal(0, press.keyDown.charScan.ToUShort());
        }
    }

    [Fact]
    public void AsciiKeyWithoutAssociatedTextKeepsItsIdentityAndHasNoText()
    {
        List<TEvent> events = Feed(Negotiated(15), "\x1b[97u\x1b[97;1:2u\x1b[97;1:3u");

        Assert.Equal([Events.evKeyDown, Events.evKeyDown, Events.evKeyUp], events.Select(e => e.What));
        Assert.All(events, e => Assert.Equal((ushort)'a', e.keyDown.keyCode));
        Assert.All(events, e => Assert.Empty(e.keyDown.text));
    }

    [Fact]
    public void UnidentifiedKeyKeepsRepeatAndReleaseSemantics()
    {
        TerminalInputDecoder decoder = Negotiated(15);

        List<TEvent> events = Feed(decoder, "\x1b[283u\x1b[283;1:2u\x1b[283;1:2u\x1b[283;1:3u");

        Assert.Equal([Events.evKeyDown, Events.evKeyDown, Events.evKeyDown, Events.evKeyUp], events.Select(e => e.What));
        Assert.All(events, e => Assert.Equal(0, e.keyDown.keyCode));
        Assert.All(events, e => Assert.Empty(e.keyDown.text));
        // A release that no press preceded has nothing to report.
        Assert.Empty(Feed(decoder, "\x1b[283;1:3u"));
    }

    // The shifted alternate (flag 4) is the key's shifted identity, not its text.
    [Fact]
    public void ShiftedAlternateSuppliesIdentityAndNeverText()
    {
        TerminalInputDecoder decoder = Negotiated(15);

        TEvent shifted = Assert.Single(Feed(decoder, "\x1b[283:50;2u"));
        Assert.Equal((ushort)'2', shifted.keyDown.keyCode);
        Assert.Empty(shifted.keyDown.text);
        Assert.Equal(Keys.kbShift, shifted.keyDown.controlKeyState);
        TEvent released = Assert.Single(Feed(decoder, "\x1b[283:50;2:3u"));
        Assert.Equal(Events.evKeyUp, released.What);
        Assert.Equal((ushort)'2', released.keyDown.keyCode);

        TEvent upper = Assert.Single(Feed(Negotiated(15), "\x1b[97:65;2u"));
        Assert.Equal((ushort)'A', upper.keyDown.keyCode);
        Assert.Empty(upper.keyDown.text);

        // No alternate reported: Shift is known, what the layout made of it is not.
        TEvent bare = Assert.Single(Feed(Negotiated(15), "\x1b[283;2u"));
        Assert.Equal(0, bare.keyDown.keyCode);
        Assert.Empty(bare.keyDown.text);
        Assert.Equal(Keys.kbShift, bare.keyDown.controlKeyState);

        // Ctrl+Shift+A stays the shortcut it is: the alternate is neither its identity nor text.
        TEvent chord = Assert.Single(Feed(Negotiated(15), "\x1b[97:65;6u"));
        Assert.Equal(Keys.kbCtrlA, chord.keyDown.keyCode);
        Assert.Equal(1, chord.keyDown.charScan.charCode);
        Assert.Empty(chord.keyDown.text);
    }

    // The base-layout alternate names the shortcut position on a non-Latin key; modifiers and lock
    // state are reported as sent whether or not the key has an identity.
    [Fact]
    public void BaseLayoutAlternateAndModifiersAreIdentityAndStateOnly()
    {
        TEvent based = Assert.Single(Feed(Negotiated(15), "\x1b[283::50;3u"));
        Assert.Equal(Keys.kbAlt2, based.keyDown.keyCode);
        Assert.Empty(based.keyDown.text);

        TEvent control = Assert.Single(Feed(Negotiated(15), "\x1b[283;5u"));
        Assert.Equal(0, control.keyDown.keyCode);
        Assert.Empty(control.keyDown.text);
        Assert.Equal(Keys.kbCtrlShift, control.keyDown.controlKeyState);

        TEvent locked = Assert.Single(Feed(Negotiated(15), "\x1b[283;193u"));
        Assert.Equal(Keys.kbCapsState | Keys.kbNumState, locked.keyDown.controlKeyState);
        Assert.Empty(locked.keyDown.text);
    }

    // Functional keys live in the Unicode private-use area: without a legacy identity they stay
    // unpublished, as before, and are never mistaken for a key of the layout.
    [Theory]
    [InlineData("\x1b[57358u")]    // Caps Lock
    [InlineData("\x1b[57428u")]    // media play
    public void FunctionalPrivateUseKeysAreNotPublishedAsUnidentifiedKeys(string sequence) =>
        Assert.Empty(Feed(Negotiated(15), sequence));

    [Theory]
    [InlineData(15, "")]
    [InlineData(31, "1")]
    public void KeypadKeyKeepsItsIdentityAndOnlyTheTextTheTerminalSent(int flags, string text)
    {
        string press = text.Length == 0 ? "\x1b[57400u" : "\x1b[57400;;49u";

        List<TEvent> events = Feed(Negotiated(flags), press + "\x1b[57400;1:3u");

        Assert.Equal([Events.evKeyDown, Events.evKeyUp], events.Select(e => e.What));
        Assert.All(events, e => Assert.Equal(Keys.kbKeypad1, e.keyDown.keyCode));
        Assert.Equal(text, events[0].keyDown.text);
    }

    // With associated text confirmed, typing is unchanged: the text field is the text. A bare
    // non-ASCII report there is a key that produced none (a dead key), published without any.
    [Theory]
    [InlineData(16)]
    [InlineData(8 | 16)]
    [InlineData(31)]
    public void AssociatedTextIsTheOnlySourceOfText(int flags)
    {
        TerminalInputDecoder decoder = Negotiated(flags);

        TEvent typed = Assert.Single(Feed(decoder, "\x1b[283;;283u"));
        Assert.Equal("ě", typed.keyDown.text);
        Assert.Equal(0, typed.keyDown.keyCode);
        TEvent shifted = Assert.Single(Feed(decoder, "\x1b[283:50;2;50u"));
        Assert.Equal("2", shifted.keyDown.text);
        Assert.Equal((ushort)'2', shifted.keyDown.keyCode);
        TEvent ascii = Assert.Single(Feed(decoder, "\x1b[97;;97u"));
        Assert.Equal("a", ascii.keyDown.text);
        Assert.Equal((ushort)'a', ascii.keyDown.keyCode);

        TEvent dead = Assert.Single(Feed(decoder, "\x1b[180u"));
        Assert.Empty(dead.keyDown.text);
        Assert.Equal(0, dead.keyDown.keyCode);
    }

    // A sequence the driver never negotiated for is not a key of the active layout.
    [Fact]
    public void WithoutNegotiationAnUnidentifiedKeyReportStaysUnpublished()
    {
        Assert.Empty(Feed(new TerminalInputDecoder(), "\x1b[283u"));
        Assert.Empty(Feed(Negotiated(0), "\x1b[283u"));
    }
}
