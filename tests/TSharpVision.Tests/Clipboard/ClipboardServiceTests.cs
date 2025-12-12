// All tests touch global ClipboardService.Current → NonParallel collection.
using System.Text;
using TSharpVision;
using TSharpVision.Tests.Infrastructure;
using Xunit;

namespace TSharpVision.Tests.Clipboard;

[Collection("NonParallel")]
public sealed class ClipboardServiceTests : IDisposable
{
    private readonly ClipboardServiceScope _scope;
    private readonly DriverScope _driver;

    public ClipboardServiceTests()
    {
        _driver = new DriverScope();
        _scope  = new ClipboardServiceScope();
    }

    public void Dispose()
    {
        _scope.Dispose();
        _driver.Dispose();
    }

    // ── Helpers ───────────────────────────────────────────────────────────

    private static TEditor MakeEditor(string text)
    {
        var ed = new TEditor(new TRect(0, 0, 40, 10), null, null, null, 1024);
        byte[] bytes = Encoding.Latin1.GetBytes(text);
        ed.bufLen = (uint)bytes.Length;
        ed.gapLen = ed.bufSize - ed.bufLen;
        Array.Copy(bytes, 0, ed.buffer, (int)ed.gapLen, bytes.Length);
        ed.curPtr = 0;
        ed.curPos = default;
        ed.delta  = default;
        ed.drawLine = 0;
        ed.drawPtr  = 0;
        ed.limit.x  = TSharpVision.Constants.Views.maxLineLength;
        int lines = 1;
        for (int i = 0; i < bytes.Length; i++)
            if (bytes[i] == 0x0A) lines++;
        ed.limit.y = lines;
        return ed;
    }

    private static string ReadAll(TEditor ed)
    {
        var sb = new StringBuilder();
        for (uint p = 0; p < ed.bufLen; p++)
            sb.Append((char)ed.BufChar(p));
        return sb.ToString();
    }

    private static TEditor MakeClipEditor()
        => new TEditor(new TRect(0, 0, 10, 5), null, null, null, 1024);

    // ── 26b.1 — Default null service ─────────────────────────────────────

    [Fact]
    public void DefaultService_NotNull()
    {
        Assert.NotNull(ClipboardService.Current);
    }

    [Fact]
    public void DefaultService_NotAvailable()
    {
        Assert.False(ClipboardService.Current.IsAvailable);
    }

    [Fact]
    public void DefaultService_TryGetText_ReturnsFalse()
    {
        Assert.False(ClipboardService.Current.TryGetText(out _));
    }

    [Fact]
    public void DefaultService_GetText_ReturnsNull()
    {
        Assert.Null(ClipboardService.Current.GetText());
    }

    [Fact]
    public void DefaultService_SetText_ReturnsFalse()
    {
        Assert.False(ClipboardService.Current.SetText("x"));
    }

    // ── 26b.2 — InMemoryClipboardService ─────────────────────────────────

    [Fact]
    public void InMemory_IsAvailable()
    {
        Assert.True(new InMemoryClipboardService().IsAvailable);
    }

    [Fact]
    public void InMemory_Empty_TryGetText_False()
    {
        Assert.False(new InMemoryClipboardService().TryGetText(out _));
    }

    [Fact]
    public void InMemory_SetGetText()
    {
        var svc = new InMemoryClipboardService();
        Assert.True(svc.SetText("hello"));
        Assert.Equal("hello", svc.GetText());
    }

    [Fact]
    public void InMemory_TryGetText_AfterSet()
    {
        var svc = new InMemoryClipboardService();
        svc.SetText("hello");
        bool got = svc.TryGetText(out string t);
        Assert.True(got);
        Assert.Equal("hello", t);
    }

    [Fact]
    public void InMemory_Clear_Resets()
    {
        var svc = new InMemoryClipboardService();
        svc.SetText("hello");
        svc.Clear();
        Assert.False(svc.TryGetText(out _));
        Assert.Null(svc.GetText());
    }

    // ── 26b.3 — Copy mirrors to OS clipboard ─────────────────────────────

    [Fact]
    public void ClipCopy_MirrorsToOsClipboard()
    {
        var svc = new InMemoryClipboardService();
        ClipboardService.Current = svc;
        var clip = MakeClipEditor();
        TEditor.clipboard = clip;
        try
        {
            var ed = MakeEditor("hello world");
            ed.SetSelect(0, 5, false);
            Assert.True(ed.ClipCopy());
            Assert.Equal("hello", svc.GetText());
            Assert.Equal("hello", ReadAll(clip));
        }
        finally { TEditor.clipboard = null; ClipboardService.Reset(); }
    }

    // ── 26b.4 — Cut mirrors to OS clipboard and removes selection ─────────

    [Fact]
    public void ClipCut_MirrorsAndRemoves()
    {
        var svc = new InMemoryClipboardService();
        ClipboardService.Current = svc;
        var clip = MakeClipEditor();
        TEditor.clipboard = clip;
        try
        {
            var ed = MakeEditor("hello");
            ed.SetSelect(0, 5, false);
            ed.ClipCut();
            Assert.Equal("hello", svc.GetText());
            Assert.Equal("", ReadAll(ed));
            Assert.Equal("hello", ReadAll(clip));
        }
        finally { TEditor.clipboard = null; ClipboardService.Reset(); }
    }

    // ── 26b.5 — Paste uses OS clipboard first ─────────────────────────────

    [Fact]
    public void ClipPaste_UsesOsClipboardFirst()
    {
        var svc = new InMemoryClipboardService();
        svc.SetText("world");
        ClipboardService.Current = svc;
        var clip = MakeClipEditor();
        TEditor.clipboard = clip;
        try
        {
            var ed = MakeEditor("");
            ed.ClipPaste();
            Assert.Equal("world", ReadAll(ed));
        }
        finally { TEditor.clipboard = null; ClipboardService.Reset(); }
    }

    // ── 26b.6 — Fallback to internal clipboard when OS unavailable ────────

    [Fact]
    public void ClipPaste_FallsBackToInternal()
    {
        ClipboardService.Reset(); // Null/unavailable
        var clip = MakeClipEditor();
        TEditor.clipboard = clip;
        try
        {
            var seed = MakeEditor("internal");
            seed.SetSelect(0, 8, false);
            seed.ClipCopy();
            Assert.Equal("internal", ReadAll(clip));

            var ed = MakeEditor("");
            ed.ClipPaste();
            Assert.Equal("internal", ReadAll(ed));
        }
        finally { TEditor.clipboard = null; ClipboardService.Reset(); }
    }

    // ── 26b.7 — Newline normalization on paste ────────────────────────────

    [Fact]
    public void ClipPaste_NormalizesCrLf_To_Lf()
    {
        var svc = new InMemoryClipboardService();
        svc.SetText("line1\r\nline2");
        ClipboardService.Current = svc;
        var clip = MakeClipEditor();
        TEditor.clipboard = clip;
        try
        {
            var ed = MakeEditor("");
            ed.ClipPaste();
            Assert.Equal("line1\nline2", ReadAll(ed));
        }
        finally { TEditor.clipboard = null; ClipboardService.Reset(); }
    }

    [Fact]
    public void ClipPaste_NormalizesBareCarriageReturn()
    {
        var svc = new InMemoryClipboardService();
        svc.SetText("a\rb");
        ClipboardService.Current = svc;
        var clip = MakeClipEditor();
        TEditor.clipboard = clip;
        try
        {
            var ed = MakeEditor("");
            ed.ClipPaste();
            Assert.Equal("a\nb", ReadAll(ed));
        }
        finally { TEditor.clipboard = null; ClipboardService.Reset(); }
    }

    [Fact]
    public void ClipboardEncoding_NormalizeToCrLf_AddsCarriageReturn()
    {
        Assert.Equal("a\r\nb", ClipboardEncoding.NormalizeToCrLf("a\nb"));
    }

    [Fact]
    public void ClipboardEncoding_NormalizeToCrLf_PreservesExistingCrLf()
    {
        Assert.Equal("a\r\nb", ClipboardEncoding.NormalizeToCrLf("a\r\nb"));
    }

    // ── 26b.8 — Latin-1 0xFF round-trip ──────────────────────────────────

    [Fact]
    public void ClipPaste_Latin1_0xFF_Preserved()
    {
        var svc = new InMemoryClipboardService();
        svc.SetText("\u00FF");
        ClipboardService.Current = svc;
        var clip = MakeClipEditor();
        TEditor.clipboard = clip;
        try
        {
            var ed = MakeEditor("");
            ed.ClipPaste();
            Assert.Equal(1u, ed.bufLen);
            Assert.Equal(0xFF, ed.BufChar(0));
        }
        finally { TEditor.clipboard = null; ClipboardService.Reset(); }
    }
}
