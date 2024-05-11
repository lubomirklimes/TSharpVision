// Help palette color mapping.
//
// Verifies:
//   - TProgram.cpColor palette slots used by the help window are correct teal/cyan values.
//   - THelpWindow._palette indexes are correct (verified via GetPalette()).
//   - THelpViewer._palette has the expected 3-slot chain palette.
//   - No black-on-black (0x00) attrs for visible text or frame colors.
//   - Normal text, keyword, and selected-keyword attributes are visually distinct.
//
// All tests are headless (DriverScope / NullDriver) — no real screen, no Demo01.
using System.Reflection;
using SharpVision;
using SharpVision.Tests.Infrastructure;
using Xunit;

namespace SharpVision.Tests.Help;

[Collection("NonParallel")]
public sealed class HelpPaletteTests : IDisposable
{
    private readonly StreamableRegistryScope _streams;
    private readonly TempDirectory _tmp;

    public HelpPaletteTests()
    {
        _streams = new StreamableRegistryScope();
        Pstream.DeInitTypes();
        Pstream.RegisterType(THelpTopic.StreamableClass);
        Pstream.RegisterType(THelpIndex.StreamableClass);
        _tmp = new TempDirectory();
    }

    public void Dispose()
    {
        _tmp.Dispose();
        _streams.Dispose();
    }

    // Helper: 1-based access into TProgram.cpColor (mirrors the Demo helper).
    private static byte AppColor(int n) => (byte)TProgram.cpColor[n - 1];

    // Build the minimal .hlp file used by the window tests.
    private string BuildHelpFile()
    {
        string path = System.IO.Path.Combine(_tmp.Path, "pal.hlp");
        var fp = new Fpstream(path);
        var hf = new THelpFile(fp);
        var t1 = new THelpTopic();
        var b1 = System.Text.Encoding.Latin1.GetBytes("Palette test.\n");
        t1.AddParagraph(new TParagraph { text = b1, size = (ushort)b1.Length, wrap = false });
        hf.RecordPositionInIndex(1);
        hf.PutTopic(t1);
        hf.Flush();
        fp.Close();
        return path;
    }

    // =========================================================================
    // TProgram.cpColor — help-window-relevant palette slots
    // =========================================================================

    [Fact]
    public void CpColor_Slot17_IsWhiteOnDarkCyan()
    {
        // Slot 17 = frame/scrollbar color (0x3F = white on dark cyan).
        Assert.Equal(0x3F, AppColor(17));
    }

    [Fact]
    public void CpColor_Slot17_IsNotGray_0x70()
    {
        Assert.NotEqual(0x70, AppColor(17));
    }

    [Fact]
    public void CpColor_Slot17_IsNotGray_0x7F()
    {
        Assert.NotEqual(0x7F, AppColor(17));
    }

    [Fact]
    public void CpColor_Slot47_IsBlackOnDarkCyan()
    {
        // Slot 47 = normal text (0x30 = black on dark cyan).
        Assert.Equal(0x30, AppColor(47));
    }

    [Fact]
    public void CpColor_Slot21_IsYellowOnDarkCyan()
    {
        // Slot 21 = keyword (0x3E = yellow on dark cyan).
        Assert.Equal(0x3E, AppColor(21));
    }

    [Fact]
    public void CpColor_Slot50_IsWhiteOnDarkBlue()
    {
        // Slot 50 = selected keyword (0x1F = white on dark blue).
        Assert.Equal(0x1F, AppColor(50));
    }

    [Fact]
    public void CpColor_NormalText_NotEqualKeyword()
    {
        Assert.NotEqual(AppColor(47), AppColor(21));
    }

    [Fact]
    public void CpColor_NormalText_NotEqualSelected()
    {
        Assert.NotEqual(AppColor(47), AppColor(50));
    }

    [Fact]
    public void CpColor_FrameAttr_NotEqualNormalText()
    {
        // Frame (17) should be lighter than body (47) — distinct visual bands.
        Assert.NotEqual(AppColor(17), AppColor(47));
    }

    [Fact]
    public void CpColor_FrameAttr_NotBlackOnBlack()
    {
        Assert.NotEqual(0x00, AppColor(17));
    }

    [Fact]
    public void CpColor_NormalText_NotBlackOnBlack()
    {
        Assert.NotEqual(0x00, AppColor(47));
    }

    [Fact]
    public void CpColor_Keyword_NotBlackOnBlack()
    {
        Assert.NotEqual(0x00, AppColor(21));
    }

    [Fact]
    public void CpColor_Selected_NotBlackOnBlack()
    {
        Assert.NotEqual(0x00, AppColor(50));
    }

    // =========================================================================
    // TProgram.cpColor total length — 63 entries (1-based access up to 63)
    // =========================================================================

    [Fact]
    public void CpColor_Length_Is63()
    {
        Assert.Equal(63, TProgram.cpColor.Length);
    }

    // =========================================================================
    // THelpWindow palette — GetPalette() returns 8-slot palette, correct indexes
    // =========================================================================

    [Fact]
    public void HelpWindow_Palette_Length_Is8()
    {
        using var driver = new DriverScope();
        string path = BuildHelpFile();
        var fp = new Fpstream(path);
        var hf = new THelpFile(fp);
        var hw = new THelpWindow(hf, 1);
        var pal = hw.GetPalette();
        fp.Close();
        Assert.Equal(8, pal.Size);
    }

    [Fact]
    public void HelpWindow_Palette_Slot1_IsFrameInactive()
    {
        // Slot 1 == 0x11 → maps to cpColor[17] (1-based) = frame inactive.
        using var driver = new DriverScope();
        string path = BuildHelpFile();
        var fp = new Fpstream(path);
        var hf = new THelpFile(fp);
        var hw = new THelpWindow(hf, 1);
        var pal = hw.GetPalette();
        fp.Close();
        Assert.Equal(0x11, pal[1]);
    }

    [Fact]
    public void HelpWindow_Palette_Slot2_IsFrameActive()
    {
        using var driver = new DriverScope();
        string path = BuildHelpFile();
        var fp = new Fpstream(path);
        var hf = new THelpFile(fp);
        var hw = new THelpWindow(hf, 1);
        var pal = hw.GetPalette();
        fp.Close();
        Assert.Equal(0x11, pal[2]);
    }

    [Fact]
    public void HelpWindow_Palette_Slot3_IsFrameHiByte()
    {
        using var driver = new DriverScope();
        string path = BuildHelpFile();
        var fp = new Fpstream(path);
        var hf = new THelpFile(fp);
        var hw = new THelpWindow(hf, 1);
        var pal = hw.GetPalette();
        fp.Close();
        Assert.Equal(0x11, pal[3]);
    }

    [Fact]
    public void HelpWindow_Palette_Slot4_IsScrollbarTrack()
    {
        // Slot 4 == 0x2F → maps to cpColor[47] = scrollbar track.
        using var driver = new DriverScope();
        string path = BuildHelpFile();
        var fp = new Fpstream(path);
        var hf = new THelpFile(fp);
        var hw = new THelpWindow(hf, 1);
        var pal = hw.GetPalette();
        fp.Close();
        Assert.Equal(0x2F, pal[4]);
    }

    [Fact]
    public void HelpWindow_Palette_Slot5_IsScrollbarArrows()
    {
        using var driver = new DriverScope();
        string path = BuildHelpFile();
        var fp = new Fpstream(path);
        var hf = new THelpFile(fp);
        var hw = new THelpWindow(hf, 1);
        var pal = hw.GetPalette();
        fp.Close();
        Assert.Equal(0x11, pal[5]);
    }

    [Fact]
    public void HelpWindow_Palette_Slot6_IsNormalText()
    {
        // Slot 6 == 0x2F → maps to cpColor[47] = normal text.
        using var driver = new DriverScope();
        string path = BuildHelpFile();
        var fp = new Fpstream(path);
        var hf = new THelpFile(fp);
        var hw = new THelpWindow(hf, 1);
        var pal = hw.GetPalette();
        fp.Close();
        Assert.Equal(0x2F, pal[6]);
    }

    [Fact]
    public void HelpWindow_Palette_Slot7_IsKeyword()
    {
        // Slot 7 == 0x15 → maps to cpColor[21] = keyword (yellow on cyan).
        using var driver = new DriverScope();
        string path = BuildHelpFile();
        var fp = new Fpstream(path);
        var hf = new THelpFile(fp);
        var hw = new THelpWindow(hf, 1);
        var pal = hw.GetPalette();
        fp.Close();
        Assert.Equal(0x15, pal[7]);
    }

    [Fact]
    public void HelpWindow_Palette_Slot8_IsSelectedKeyword()
    {
        // Slot 8 == 0x32 → maps to cpColor[50] = selected (white on blue).
        using var driver = new DriverScope();
        string path = BuildHelpFile();
        var fp = new Fpstream(path);
        var hf = new THelpFile(fp);
        var hw = new THelpWindow(hf, 1);
        var pal = hw.GetPalette();
        fp.Close();
        Assert.Equal(0x32, pal[8]);
    }

    // =========================================================================
    // THelpViewer palette chain — 3-slot "\x06\x07\x08"
    // =========================================================================

    [Fact]
    public void HelpViewer_Palette_Slot1_Is6()
    {
        using var driver = new DriverScope();
        string path = BuildHelpFile();
        var fp = new Fpstream(path);
        var hf = new THelpFile(fp);
        var bounds = new TRect(0, 0, 50, 10);
        var vsb = new TScrollBar(new TRect(49, 1, 50, 9));
        var hsb = new TScrollBar(new TRect(1, 9, 48, 10));
        var v = new THelpViewer(bounds, hsb, vsb, hf, 1);
        var pal = v.GetPalette();
        fp.Close();
        Assert.Equal(0x06, pal[1]);
    }

    [Fact]
    public void HelpViewer_Palette_Slot2_Is7()
    {
        using var driver = new DriverScope();
        string path = BuildHelpFile();
        var fp = new Fpstream(path);
        var hf = new THelpFile(fp);
        var bounds = new TRect(0, 0, 50, 10);
        var vsb = new TScrollBar(new TRect(49, 1, 50, 9));
        var hsb = new TScrollBar(new TRect(1, 9, 48, 10));
        var v = new THelpViewer(bounds, hsb, vsb, hf, 1);
        var pal = v.GetPalette();
        fp.Close();
        Assert.Equal(0x07, pal[2]);
    }

    [Fact]
    public void HelpViewer_Palette_Slot3_Is8()
    {
        using var driver = new DriverScope();
        string path = BuildHelpFile();
        var fp = new Fpstream(path);
        var hf = new THelpFile(fp);
        var bounds = new TRect(0, 0, 50, 10);
        var vsb = new TScrollBar(new TRect(49, 1, 50, 9));
        var hsb = new TScrollBar(new TRect(1, 9, 48, 10));
        var v = new THelpViewer(bounds, hsb, vsb, hf, 1);
        var pal = v.GetPalette();
        fp.Close();
        Assert.Equal(0x08, pal[3]);
    }

    [Fact]
    public void HelpViewer_Palette_Length_Is3()
    {
        using var driver = new DriverScope();
        string path = BuildHelpFile();
        var fp = new Fpstream(path);
        var hf = new THelpFile(fp);
        var bounds = new TRect(0, 0, 50, 10);
        var vsb = new TScrollBar(new TRect(49, 1, 50, 9));
        var hsb = new TScrollBar(new TRect(1, 9, 48, 10));
        var v = new THelpViewer(bounds, hsb, vsb, hf, 1);
        var pal = v.GetPalette();
        fp.Close();
        Assert.Equal(3, pal.Size);
    }

    // =========================================================================
    // THelpWindow title routes through SharpVisionIntl
    // =========================================================================

    [Fact]
    public void HelpWindow_DefaultTitle_IsHelp()
    {
        using var driver = new DriverScope();
        string path = BuildHelpFile();
        var fp = new Fpstream(path);
        var hf = new THelpFile(fp);
        var hw = new THelpWindow(hf, 1);
        fp.Close();
        Assert.Equal("Help", hw.title);
    }

    [Fact]
    public void HelpWindow_Title_RoutesThrough_IntlProvider()
    {
        using var driver = new DriverScope();
        using var intl = new IntlProviderScope(
            new DictStringProvider(new System.Collections.Generic.Dictionary<string, string>
            {
                ["Help_WindowTitle"] = "HELP_TEST"
            }));
        string path = BuildHelpFile();
        var fp = new Fpstream(path);
        var hf = new THelpFile(fp);
        var hw = new THelpWindow(hf, 1);
        fp.Close();
        Assert.Equal("HELP_TEST", hw.title);
    }

    [Fact]
    public void HelpWindow_Size_Is50x18()
    {
        using var driver = new DriverScope();
        string path = BuildHelpFile();
        var fp = new Fpstream(path);
        var hf = new THelpFile(fp);
        var hw = new THelpWindow(hf, 1);
        fp.Close();
        Assert.Equal(50, hw.size.x);
        Assert.Equal(18, hw.size.y);
    }

    // =========================================================================
    // THelpWindow._palette is private: confirm it via GetPalette() return value
    // equality (ensure no regression to old 128-135 out-of-range slots).
    // =========================================================================

    [Fact]
    public void HelpWindow_AllPaletteSlots_InRange_0x00_0xFF()
    {
        using var driver = new DriverScope();
        string path = BuildHelpFile();
        var fp = new Fpstream(path);
        var hf = new THelpFile(fp);
        var hw = new THelpWindow(hf, 1);
        var pal = hw.GetPalette();
        fp.Close();
        for (int i = 1; i <= pal.Size; i++)
            Assert.InRange(pal[i], 0x00, 0x7F); // valid cpColor 1-based range 1..63
    }
}

// ── File-local test helper ────────────────────────────────────────────────────

file sealed class DictStringProvider(System.Collections.Generic.Dictionary<string, string> dict)
    : ISharpVisionStringProvider
{
    public string Get(string key, string fallback)
        => dict.TryGetValue(key, out var v) ? v : fallback;
}
