using System.Text;
using TSharpVision;
using TSharpVision.Constants;
using TSharpVision.Drivers;
using TSharpVision.Drivers.Terminal;
using Xunit;

namespace TSharpVision.Drivers.Terminal.Tests;

public sealed class KittyKeyboardProtocolTests
{
    [Fact]
    public void NegotiationQueriesSupportBeforeEnabling()
    {
        var decoder = new TerminalInputDecoder();

        Assert.Equal("\x1b[?u\x1b[c", decoder.BeginKeyboardNegotiation());
        Assert.Equal(KeyboardCapabilities.None, decoder.KeyboardCapabilities);
        Assert.Equal(KittyNegotiationState.AwaitingSupport, decoder.NegotiationState);
    }

    [Fact]
    public void SupportedNegotiationEnablesAndConfirmsRequestedFlags()
    {
        var decoder = new TerminalInputDecoder();
        var output = new List<string>();
        decoder.BeginKeyboardNegotiation();

        decoder.Feed("\x1b[?0u"u8, output.Add);
        Assert.Equal(["\x1b[>31u\x1b[?u"], output);
        Assert.Equal(KeyboardCapabilities.None, decoder.KeyboardCapabilities);

        decoder.Feed("\x1b[?31u"u8, output.Add);
        Assert.Equal(
            KeyboardCapabilities.KeyReleaseEvents | KeyboardCapabilities.StandaloneModifierTransitions,
            decoder.KeyboardCapabilities);
        Assert.Equal(KittyNegotiationState.Active, decoder.NegotiationState);
        Assert.False(decoder.TryRead(out _));
    }

    [Fact]
    public void EventTypesWithoutAllKeysDoNotOverAdvertiseReleaseCapability()
    {
        var decoder = new TerminalInputDecoder();
        decoder.BeginKeyboardNegotiation();
        decoder.Feed("\x1b[?0u"u8, _ => { });
        decoder.Feed("\x1b[?2u"u8, _ => { });

        Assert.Equal(KeyboardCapabilities.None, decoder.KeyboardCapabilities);
    }

    [Fact]
    public void DeviceAttributesBeforeKittyReplySelectsLegacyFallback()
    {
        var decoder = new TerminalInputDecoder();
        decoder.BeginKeyboardNegotiation();

        decoder.Feed("\x1b[?1;2c"u8, _ => { });

        Assert.Equal(KittyNegotiationState.Unsupported, decoder.NegotiationState);
        Assert.Equal(KeyboardCapabilities.None, decoder.KeyboardCapabilities);
        Assert.False(decoder.TryRead(out _));
    }

    [Fact]
    public void IgnoredNegotiationLeavesLegacyInputOperational()
    {
        var decoder = new TerminalInputDecoder();
        decoder.BeginKeyboardNegotiation();

        decoder.Feed("\x1b[A"u8, _ => { });

        TEvent ev = Read(decoder);
        Assert.Equal(Events.evKeyDown, ev.What);
        Assert.Equal(Keys.kbUp, ev.keyDown.keyCode);
        Assert.Equal(KeyboardCapabilities.None, decoder.KeyboardCapabilities);
    }

    [Fact]
    public void EndPopsExactlyOnceOnlyAfterPush()
    {
        var decoder = new TerminalInputDecoder();
        decoder.BeginKeyboardNegotiation();
        decoder.Feed("\x1b[?0u"u8, _ => { });

        Assert.Equal("\x1b[<u", decoder.EndKeyboardMode());
        Assert.Null(decoder.EndKeyboardMode());
        Assert.Equal(KeyboardCapabilities.None, decoder.KeyboardCapabilities);
    }

    [Fact]
    public void UnsupportedOrPartialNegotiationNeedsNoRestore()
    {
        var unsupported = new TerminalInputDecoder();
        unsupported.BeginKeyboardNegotiation();
        unsupported.Feed("\x1b[?1c"u8, _ => { });
        Assert.Null(unsupported.EndKeyboardMode());

        var waiting = new TerminalInputDecoder();
        waiting.BeginKeyboardNegotiation();
        Assert.Null(waiting.EndKeyboardMode());
    }

    [Fact]
    public void EndingModeClearsHeldModifiersWithoutFabricatingARelease()
    {
        var decoder = new TerminalInputDecoder();
        decoder.BeginKeyboardNegotiation();
        Feed(decoder, "\x1b[57441;2:1u");
        Assert.Equal(Keys.kbShift, Read(decoder).Modifiers);

        decoder.EndKeyboardMode();

        Assert.False(decoder.TryRead(out _));
        decoder.BeginKeyboardNegotiation();
        Feed(decoder, "\x1b[57441;2:1u");
        Assert.Equal(Keys.kbShift, Read(decoder).Modifiers);
    }

    [Fact]
    public void DriverCapabilityReflectsRuntimeNegotiation()
    {
        var driver = new AnsiTerminalDriver();
        var output = new List<string>();
        Assert.Equal("\x1b[?u\x1b[c", driver.BeginKeyboardNegotiationForTesting());
        Assert.Equal(KeyboardCapabilities.None, driver.KeyboardCapabilities);

        driver.FeedInputForTesting("\x1b[?0u"u8, output.Add);
        driver.FeedInputForTesting("\x1b[?31u"u8, output.Add);

        Assert.Equal(
            KeyboardCapabilities.KeyReleaseEvents | KeyboardCapabilities.StandaloneModifierTransitions,
            driver.KeyboardCapabilities);
    }

    [Fact]
    public void PressRepeatRepeatReleaseMapToThreeDownAndOneUpWithStableIdentity()
    {
        var decoder = new TerminalInputDecoder();
        Feed(decoder, "\x1b[1;1:1P\x1b[1;1:2P\x1b[1;1:2P\x1b[1;1:3P");

        TEvent[] events = ReadAll(decoder);
        Assert.Equal([Events.evKeyDown, Events.evKeyDown, Events.evKeyDown, Events.evKeyUp],
            events.Select(e => e.What));
        Assert.All(events, e => Assert.Equal(Keys.kbF1, e.keyDown.keyCode));
    }

    [Theory]
    [InlineData("\x1b[1;1:1A", Events.evKeyDown, Keys.kbUp)]
    [InlineData("\x1b[1;1:3A", Events.evKeyUp, Keys.kbUp)]
    [InlineData("\x1b[1;5:1D", Events.evKeyDown, Keys.kbCtrlLeft)]
    [InlineData("\x1b[15;3:1~", Events.evKeyDown, Keys.kbAltF5)]
    [InlineData("\x1b[13;1:1u", Events.evKeyDown, Keys.kbEnter)]
    [InlineData("\x1b[9;2:1u", Events.evKeyDown, Keys.kbShiftTab)]
    public void EnhancedFunctionalKeysUsePublicIdentities(string sequence, ushort what, ushort keyCode)
    {
        var decoder = new TerminalInputDecoder();
        Feed(decoder, sequence);

        TEvent ev = Read(decoder);
        Assert.Equal(what, ev.What);
        Assert.Equal(keyCode, ev.keyDown.keyCode);
    }

    [Fact]
    public void CtrlAndAltLettersUseHistoricalIdentities()
    {
        var decoder = new TerminalInputDecoder();
        Feed(decoder, "\x1b[99;5:1u\x1b[120;3:1u");

        Assert.Equal(Keys.kbCtrlC, Read(decoder).keyDown.keyCode);
        Assert.Equal(Keys.kbAltX, Read(decoder).keyDown.keyCode);
    }

    [Fact]
    public void AlternateBaseLayoutSupportsShortcutsOnNonLatinLayout()
    {
        var decoder = new TerminalInputDecoder();
        Feed(decoder, "\x1b[1089::99;5:1;1089u");

        TEvent ev = Read(decoder);
        Assert.Equal(Keys.kbCtrlC, ev.keyDown.keyCode);
        Assert.Equal("с", ev.keyDown.text);
    }

    [Theory]
    [InlineData("A", "\x1b[97;2:1;65u")]
    [InlineData("é", "\x1b[233;1:1;233u")]
    [InlineData("Ж", "\x1b[1078;1:1;1046u")]
    [InlineData("😀", "\x1b[0;1:1;128512u")]
    public void AssociatedTextPreservesUnicodeWithoutInventingUnicodeKeyCodes(string expected, string sequence)
    {
        var decoder = new TerminalInputDecoder();
        Feed(decoder, sequence);

        TEvent ev = Read(decoder);
        Assert.Equal(expected, ev.keyDown.text);
        if (expected[0] > 0x7E)
            Assert.Equal(0, ev.keyDown.keyCode);
    }

    [Fact]
    public void AssociatedTextCanContainComposedMultipleCodePoints()
    {
        var decoder = new TerminalInputDecoder();
        Feed(decoder, "\x1b[0;1:1;101:769u");

        Assert.Equal("e\u0301", Read(decoder).keyDown.text);
    }

    [Fact]
    public void UnicodeReleaseRetainsThePressIdentityWithoutForcingAKeyCode()
    {
        var decoder = new TerminalInputDecoder();
        Feed(decoder, "\x1b[1078;1:1;1046u\x1b[1078;1:3u");

        TEvent down = Read(decoder);
        TEvent up = Read(decoder);
        Assert.Equal(Events.evKeyDown, down.What);
        Assert.Equal(Events.evKeyUp, up.What);
        Assert.Equal(0, down.keyDown.keyCode);
        Assert.Equal(down.keyDown.keyCode, up.keyDown.keyCode);
        Assert.Equal("Ж", down.keyDown.text);
        Assert.Same(string.Empty, up.keyDown.text);
    }

    [Fact]
    public void ModifiedPrintableCarriesCompleteModifierStateAndText()
    {
        var decoder = new TerminalInputDecoder();
        Feed(decoder, "\x1b[97;6:1;65u");

        TEvent ev = Read(decoder);
        Assert.Equal(Keys.kbCtrlA, ev.keyDown.keyCode);
        Assert.Equal(Keys.kbShift | Keys.kbCtrlShift, ev.Modifiers);
        Assert.Equal("A", ev.keyDown.text);
    }

    [Fact]
    public void TruthfullyReportedLockStateIsTranslatedOnOrdinaryKeys()
    {
        var decoder = new TerminalInputDecoder();
        Feed(decoder, "\x1b[1;193:1P"); // 1 + Caps(64) + Num(128)

        TEvent ev = Read(decoder);
        Assert.NotEqual(0u, ev.Modifiers & Keys.kbCapsState);
        Assert.NotEqual(0u, ev.Modifiers & Keys.kbNumState);
    }

    [Fact]
    public void StandaloneModifiersEmitOnlyCompleteLogicalTransitions()
    {
        var decoder = new TerminalInputDecoder();
        Feed(decoder,
            "\x1b[57441;2:1u" + // left Shift down
            "\x1b[57442;6:1u" + // left Ctrl down
            "\x1b[57441;5:3u" + // left Shift up
            "\x1b[57442;1:3u"); // left Ctrl up

        TEvent[] events = ReadAll(decoder);
        Assert.All(events, e => Assert.Equal(Events.evModifierChanged, e.What));
        Assert.Equal(
            [Keys.kbShift, Keys.kbShift | Keys.kbCtrlShift, Keys.kbCtrlShift, 0u],
            events.Select(e => e.Modifiers));
    }

    [Fact]
    public void LeftRightAggregationAndModifierRepeatSuppressRedundantEvents()
    {
        var decoder = new TerminalInputDecoder();
        Feed(decoder,
            "\x1b[57441;2:1u" + // left down: transition
            "\x1b[57441;2:2u" + // repeat: none
            "\x1b[57447;2:1u" + // right down: none
            "\x1b[57441;2:3u" + // left up, right held: none
            "\x1b[57447;1:3u"); // right up: transition

        TEvent[] events = ReadAll(decoder);
        Assert.Equal(2, events.Length);
        Assert.Equal(Keys.kbShift, events[0].Modifiers);
        Assert.Equal(0u, events[1].Modifiers);
    }

    [Fact]
    public void StandaloneAltUsesModifierChangedRatherThanOrdinaryKeyEvents()
    {
        var decoder = new TerminalInputDecoder();
        Feed(decoder, "\x1b[57443;3:1u\x1b[57443;1:3u");

        TEvent[] events = ReadAll(decoder);
        Assert.Equal(2, events.Length);
        Assert.All(events, e => Assert.Equal(Events.evModifierChanged, e.What));
        Assert.Equal(Keys.kbAltShift, events[0].Modifiers);
        Assert.Equal(0u, events[1].Modifiers);
    }

    [Fact]
    public void FragmentedSequenceWaitsAtEveryByteBoundary()
    {
        byte[] sequence = Encoding.ASCII.GetBytes("\x1b[97;1:1;97u");
        for (int split = 1; split < sequence.Length; split++)
        {
            var decoder = new TerminalInputDecoder();
            decoder.Feed(sequence.AsSpan(0, split), _ => { });
            Assert.False(decoder.TryRead(out _));
            decoder.Feed(sequence.AsSpan(split), _ => { });
            Assert.Equal("a", Read(decoder).keyDown.text);
        }
    }

    [Fact]
    public void ByteByByteInputMultipleSequencesAndFollowingUtf8RemainOrdered()
    {
        var decoder = new TerminalInputDecoder();
        byte[] bytes = Encoding.UTF8.GetBytes("\x1b[97;1:1;97u\x1b[97;1:3ué");
        foreach (byte value in bytes)
            decoder.Feed([value], _ => { });

        TEvent[] events = ReadAll(decoder);
        Assert.Equal([Events.evKeyDown, Events.evKeyUp, Events.evKeyDown], events.Select(e => e.What));
        Assert.Equal("a", events[0].keyDown.text);
        Assert.Equal((ushort)'a', events[0].keyDown.keyCode);
        Assert.Equal("é", events[2].keyDown.text);
    }

    [Fact]
    public void LegacyEscapeThenEnhancedSequenceRemainSeparate()
    {
        var decoder = new TerminalInputDecoder();
        Feed(decoder, "\x1b\x1b[1;1:1A");

        Assert.Equal(Keys.kbEsc, Read(decoder).keyDown.keyCode);
        Assert.Equal(Keys.kbUp, Read(decoder).keyDown.keyCode);
    }

    [Fact]
    public void MouseWheelAndEnhancedKeysCoexistInNativeOrder()
    {
        var decoder = new TerminalInputDecoder();
        Feed(decoder,
            "\x1b[97;1:1;97u" +
            "\x1b[<0;3;4M" +
            "\x1b[97;1:3u" +
            "\x1b[<64;3;4M" +
            "é");

        TEvent[] events = ReadAll(decoder);
        Assert.Equal(
            [Events.evKeyDown, Events.evMouseDown, Events.evKeyUp, Events.evMouseWheel, Events.evKeyDown],
            events.Select(e => e.What));
        Assert.Equal(Events.meWheelUp, events[3].mouse.eventFlags & Events.meWheelUp);
        Assert.Equal("é", events[4].keyDown.text);
    }

    [Fact]
    public void MalformedAndUnknownEnhancedSequencesAreConsumedWithoutCrashing()
    {
        var decoder = new TerminalInputDecoder();
        Feed(decoder, "\x1b[97;1:9u\x1b[99999;1:1uZ");

        TEvent[] events = ReadAll(decoder);
        Assert.Single(events);
        Assert.Equal("Z", events[0].keyDown.text);
    }

    [Fact]
    public void EditorProcessesPressAndRepeatsButNotRelease()
    {
        var decoder = new TerminalInputDecoder();
        Feed(decoder, "\x1b[1;1:1B\x1b[1;1:2B\x1b[1;1:2B\x1b[1;1:3B");
        var editor = new ProbeEditor();

        foreach (TEvent item in ReadAll(decoder))
        {
            TEvent ev = item;
            editor.HandleEvent(ref ev);
        }

        Assert.Equal(3, editor.CommandCalls);
        editor.ShutDown();
    }

    private static void Feed(TerminalInputDecoder decoder, string value)
        => decoder.Feed(Encoding.UTF8.GetBytes(value), _ => { });

    private static TEvent Read(TerminalInputDecoder decoder)
    {
        Assert.True(decoder.TryRead(out TEvent ev));
        return ev;
    }

    private static TEvent[] ReadAll(TerminalInputDecoder decoder)
    {
        var result = new List<TEvent>();
        while (decoder.TryRead(out TEvent ev)) result.Add(ev);
        return result.ToArray();
    }

    private sealed class ProbeEditor : TEditor
    {
        internal ProbeEditor() : base(new TRect(0, 0, 40, 10), null, null, null, 128) { }
        internal int CommandCalls { get; private set; }

        public override void ConvertEvent(ref TEvent ev)
        {
            base.ConvertEvent(ref ev);
            if (ev.What == Events.evCommand)
                CommandCalls++;
        }
    }
}
