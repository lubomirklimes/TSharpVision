// SdlClipboardService unit tests.
// All tests use a fake ISdlClipboard implementation — no SDL runtime or window required.
using SharpVision.Drivers.SDL;
using Xunit;

namespace SharpVision.Tests.Drivers;

public sealed class SdlClipboardServiceTests
{
    // ── Helpers ─────────────────────────────────────────────────────────────

    private sealed class FakeSdlClipboard : ISdlClipboard
    {
        public string ClipboardText { get; set; }
        public bool HasClipboardText() => !string.IsNullOrEmpty(ClipboardText);
        public string GetClipboardText() => ClipboardText;
        public bool SetClipboardText(string text) { ClipboardText = text; return true; }
    }

    // ── IsAvailable ─────────────────────────────────────────────────────────

    [Fact]
    public void IsAvailable_AlwaysTrue()
    {
        var svc = new SdlClipboardService(new FakeSdlClipboard());
        Assert.True(svc.IsAvailable);
    }

    // ── GetText ─────────────────────────────────────────────────────────────

    [Fact]
    public void GetText_EmptyClipboard_ReturnsNull()
    {
        var fake = new FakeSdlClipboard { ClipboardText = null };
        var svc = new SdlClipboardService(fake);
        Assert.Null(svc.GetText());
    }

    [Fact]
    public void GetText_WithText_ReturnsText()
    {
        var fake = new FakeSdlClipboard { ClipboardText = "Hello" };
        var svc = new SdlClipboardService(fake);
        Assert.Equal("Hello", svc.GetText());
    }

    [Fact]
    public void GetText_NormalisesCarriageReturnLineFeed_ToLineFeed()
    {
        var fake = new FakeSdlClipboard { ClipboardText = "line1\r\nline2" };
        var svc = new SdlClipboardService(fake);
        Assert.Equal("line1\nline2", svc.GetText());
    }

    [Fact]
    public void GetText_NormalisesBareCR_ToLineFeed()
    {
        var fake = new FakeSdlClipboard { ClipboardText = "line1\rline2" };
        var svc = new SdlClipboardService(fake);
        Assert.Equal("line1\nline2", svc.GetText());
    }

    [Fact]
    public void GetText_Unicode_PreservesText()
    {
        const string czech = "Příliš žluťoučký kůň";
        var fake = new FakeSdlClipboard { ClipboardText = czech };
        var svc = new SdlClipboardService(fake);
        Assert.Equal(czech, svc.GetText());
    }

    // ── TryGetText ──────────────────────────────────────────────────────────

    [Fact]
    public void TryGetText_EmptyClipboard_ReturnsFalseAndEmptyString()
    {
        var fake = new FakeSdlClipboard { ClipboardText = null };
        var svc = new SdlClipboardService(fake);
        bool ok = svc.TryGetText(out string text);
        Assert.False(ok);
        Assert.Equal(string.Empty, text);
    }

    [Fact]
    public void TryGetText_WithText_ReturnsTrueAndText()
    {
        var fake = new FakeSdlClipboard { ClipboardText = "world" };
        var svc = new SdlClipboardService(fake);
        bool ok = svc.TryGetText(out string text);
        Assert.True(ok);
        Assert.Equal("world", text);
    }

    // ── SetText ─────────────────────────────────────────────────────────────

    [Fact]
    public void SetText_PassesTextToSdl()
    {
        var fake = new FakeSdlClipboard();
        var svc = new SdlClipboardService(fake);
        bool ok = svc.SetText("clipboard content");
        Assert.True(ok);
        Assert.Equal("clipboard content", fake.ClipboardText);
    }

    [Fact]
    public void SetText_Null_TreatedAsEmptyString()
    {
        var fake = new FakeSdlClipboard();
        var svc = new SdlClipboardService(fake);
        bool ok = svc.SetText(null!);
        Assert.True(ok);
        Assert.Equal(string.Empty, fake.ClipboardText);
    }

    [Fact]
    public void SetText_Unicode_PreservesText()
    {
        const string czech = "Příliš žluťoučký kůň";
        var fake = new FakeSdlClipboard();
        var svc = new SdlClipboardService(fake);
        svc.SetText(czech);
        Assert.Equal(czech, fake.ClipboardText);
    }

    // ── Round-trip ──────────────────────────────────────────────────────────

    [Fact]
    public void RoundTrip_SetThenGet_ReturnsSameText()
    {
        var fake = new FakeSdlClipboard();
        var svc = new SdlClipboardService(fake);
        svc.SetText("round-trip");
        Assert.Equal("round-trip", svc.GetText());
    }

    [Fact]
    public void RoundTrip_HasTextFalseAfterEmptySet()
    {
        var fake = new FakeSdlClipboard { ClipboardText = "old" };
        var svc = new SdlClipboardService(fake);
        svc.SetText(string.Empty);
        Assert.Null(svc.GetText());
    }
}
