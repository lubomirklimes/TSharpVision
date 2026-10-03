using System;
using System.Text;
using TSharpVision;
using TSharpVision.Constants;
using TSharpVision.Tests.Infrastructure;
using Xunit;

namespace TSharpVision.Tests.Editors;

/// <summary>
/// KEYBOARD-CLOSURE N2: Ctrl+Backspace and Alt+Backspace arrive with identities of their own (kbCtrlBack = 0x0E7F,
/// kbAltBack = 0x0800) from the console, SDL and Kitty drivers. TEditor binds neither, so they must do nothing — in
/// particular kbCtrlBack's low byte, DEL (0x7F), is not a printable character and must never be inserted.
/// </summary>
[Collection("NonParallel")]
public sealed class EditorBackspaceIdentityTests : IDisposable
{
    private readonly DriverScope _driver = new();

    public void Dispose() => _driver.Dispose();

    /// <summary>The event exactly as the drivers build it: the code, its packed char/scan, the modifier state.</summary>
    private static TEvent Key(ushort keyCode, uint modifiers)
    {
        var ev = new TEvent { What = Events.evKeyDown };
        ev.keyDown.keyCode = keyCode;
        ev.keyDown.charScan = new CharScanType(keyCode);
        ev.keyDown.controlKeyState = modifiers;
        return ev;
    }

    private static string Text(TEditor editor)
    {
        var builder = new StringBuilder();
        for (uint p = 0; p < editor.bufLen; p++) builder.Append(editor.BufChar(p));
        return builder.ToString();
    }

    [Theory]
    [InlineData(Keys.kbCtrlBack, Keys.kbCtrlShift)]
    [InlineData(Keys.kbAltBack, Keys.kbAltShift)]
    public void AnUnboundBackspaceIdentityLeavesTheTextAlone(ushort keyCode, uint modifiers)
    {
        var editor = new TEditor(new TRect(0, 0, 20, 4), null, null, null, 256);
        editor.InsertText("hello world");
        editor.modified = false;

        TEvent ev = Key(keyCode, modifiers);
        editor.HandleEvent(ref ev);

        Assert.Equal("hello world", Text(editor));
        Assert.False(editor.modified);
    }

    [Fact]
    public void PlainBackspaceStillDeletesTheCharacterBeforeTheCaret()
    {
        var editor = new TEditor(new TRect(0, 0, 20, 4), null, null, null, 256);
        editor.InsertText("hello");
        TEvent ev = Key(Keys.kbBack, 0);
        editor.HandleEvent(ref ev);
        Assert.Equal("hell", Text(editor));
    }

    [Fact]
    public void DelIsNotPrintableText()
    {
        var key = new KeyDownEvent { charScan = new CharScanType(0x7F, 0x0E) };
        Assert.Equal(string.Empty, KeyText.PrintableText(key));
        Assert.Equal(string.Empty, KeyText.PrintableText(key, includeTab: true, extendedLegacy: false));

        var latin1 = new KeyDownEvent { charScan = new CharScanType(0xE9, 0) };
        Assert.Equal("é", KeyText.PrintableText(latin1));
    }
}
