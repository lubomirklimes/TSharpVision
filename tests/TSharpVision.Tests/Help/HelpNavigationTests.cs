// Help navigation tests.
using System;
using System.IO;
using System.Text;
using TSharpVision;
using TSharpVision.Constants;
using TSharpVision.Tests.Infrastructure;
using Xunit;

namespace TSharpVision.Tests.Help;

// ═══════════════════════════════════════════════════════════════════════════
//             Tab/ShiftTab cross-ref selection, Enter follows cross-ref.
// ═══════════════════════════════════════════════════════════════════════════
[Collection("NonParallel")]
public sealed class HelpNavigationTests : IDisposable
{
    // ── lifecycle ─────────────────────────────────────────────────────────

    private readonly StreamableRegistryScope _streams;
    private readonly TempDirectory _tmp;
    private readonly DriverScope _driver;
    private readonly string _hlpPath;

    public HelpNavigationTests()
    {
        _streams = new StreamableRegistryScope();
        Pstream.DeInitTypes();
        Pstream.RegisterType(THelpTopic.StreamableClass);
        Pstream.RegisterType(THelpIndex.StreamableClass);
        _tmp = new TempDirectory();
        _driver = new DriverScope();
        _hlpPath = BuildNavHelpFile();
    }

    public void Dispose()
    {
        _driver.Dispose();
        _tmp.Dispose();
        _streams.Dispose();
    }

    // ── help-file builder ─────────────────────────────────────────────────
    // Topic 1 = "Index topic.\n"        (0 cross-refs)
    // Topic 2 = "Topic cross ref.\n"    (1 cross-ref: offset=6, len=5 → topic 3)
    // Topic 3 = "Back topic here.\n"    (1 cross-ref: offset=5, len=4 → topic 2)
    private string BuildNavHelpFile()
    {
        string path = Path.Combine(_tmp.Path, "nav.hlp");
        var fp = new Fpstream(path);
        var hf = new THelpFile(fp);

        var t1 = new THelpTopic();
        var b1 = Encoding.Latin1.GetBytes("Index topic.\n");
        t1.AddParagraph(new TParagraph { text = b1, size = (ushort)b1.Length, wrap = false });
        hf.RecordPositionInIndex(1);
        hf.PutTopic(t1);

        var t2 = new THelpTopic();
        var b2 = Encoding.Latin1.GetBytes("Topic cross ref.\n");
        t2.AddParagraph(new TParagraph { text = b2, size = (ushort)b2.Length, wrap = false });
        t2.AddCrossRef(new TCrossRef { @ref = 3, offset = 6, length = 5 });
        hf.RecordPositionInIndex(2);
        hf.PutTopic(t2);

        var t3 = new THelpTopic();
        var b3 = Encoding.Latin1.GetBytes("Back topic here.\n");
        t3.AddParagraph(new TParagraph { text = b3, size = (ushort)b3.Length, wrap = false });
        t3.AddCrossRef(new TCrossRef { @ref = 2, offset = 5, length = 4 });
        hf.RecordPositionInIndex(3);
        hf.PutTopic(t3);

        hf.Flush();
        fp.Close();
        return path;
    }

    // Returns (viewer, fp-open-for-reading). Caller must call fp.Close() when done.
    private (THelpViewer viewer, Fpstream fp) OpenViewer(ushort ctx)
    {
        var fp = new Fpstream(_hlpPath);
        var hf = new THelpFile(fp);
        var vsb = new TScrollBar(new TRect(49, 1, 50, 9));
        var hsb = new TScrollBar(new TRect(1, 9, 48, 10));
        var v = new THelpViewer(new TRect(0, 0, 50, 10), hsb, vsb, hf, ctx);
        return (v, fp);
    }

    // ── constants ─────────────────────────────────────────────────────────

    [Fact]
    public void IndexContext_Is_1()
    {
        Assert.Equal(1, THelpViewer.IndexContext);
    }

    [Fact]
    public void CmHelpBack_IsNonZero()
    {
        Assert.NotEqual(0, (int)Views.cmHelpBack);
    }

    [Fact]
    public void CmHelpIndex_IsNonZero()
    {
        Assert.NotEqual(0, (int)Views.cmHelpIndex);
    }

    [Fact]
    public void CmHelpBack_CmHelpIndex_AreDistinct()
    {
        Assert.NotEqual(Views.cmHelpBack, Views.cmHelpIndex);
    }

    // ── GoBack on empty stack ─────────────────────────────────────────────

    [Fact]
    public void GoBack_OnEmptyStack_LeavesTopicNonNull()
    {
        var (v, fp) = OpenViewer(2);
        v.GoBack(); // stack empty — must not throw
        Assert.NotNull(v.topic);
        fp.Close();
    }

    // ── NavigateTo / GoBack ───────────────────────────────────────────────

    [Fact]
    public void NavigateTo_SwitchesTopic()
    {
        var (v, fp) = OpenViewer(2);
        Assert.Equal(1, v.topic.GetNumCrossRefs()); // topic 2 has 1 cross-ref
        v.NavigateTo(3);
        Assert.Equal(1, v.topic.GetNumCrossRefs()); // topic 3 also has 1 cross-ref
        fp.Close();
    }

    [Fact]
    public void GoBack_AfterNavigateTo_ReturnsToPriorTopic()
    {
        var (v, fp) = OpenViewer(2);
        v.NavigateTo(3);
        v.GoBack();
        Assert.Equal(1, v.topic.GetNumCrossRefs()); // back to topic 2
        fp.Close();
    }

    [Fact]
    public void ExhaustedBackStack_IsNoOp()
    {
        var (v, fp) = OpenViewer(2);
        v.NavigateTo(3);
        v.NavigateTo(1);
        v.GoBack(); // back to 3
        v.GoBack(); // back to 2
        v.GoBack(); // stack empty — must not throw
        Assert.NotNull(v.topic);
        fp.Close();
    }

    // ── GoToIndex ─────────────────────────────────────────────────────────

    [Fact]
    public void GoToIndex_LoadsIndexTopic()
    {
        var (v, fp) = OpenViewer(2);
        v.GoToIndex();
        Assert.Equal(0, v.topic.GetNumCrossRefs()); // index topic (id=1) has 0 cross-refs
        fp.Close();
    }

    [Fact]
    public void GoBack_FromIndex_ReturnsToPriorTopic()
    {
        var (v, fp) = OpenViewer(2);
        v.GoToIndex();
        v.GoBack();
        Assert.Equal(1, v.topic.GetNumCrossRefs()); // back to topic 2
        fp.Close();
    }

    // ── keyboard events ───────────────────────────────────────────────────

    [Fact]
    public void KbBack_ConsumesEventAndGoesBack()
    {
        var (v, fp) = OpenViewer(2);
        v.NavigateTo(3);
        TEvent ev = default;
        ev.What = Events.evKeyDown;
        ev.keyDown.keyCode = Keys.kbBack;
        v.HandleEvent(ref ev);
        Assert.Equal(Events.evNothing, ev.What);
        Assert.Equal(1, v.topic.GetNumCrossRefs()); // back to topic 2
        fp.Close();
    }

    [Fact]
    public void KbAltI_ConsumesEventAndGoesToIndex()
    {
        var (v, fp) = OpenViewer(2);
        TEvent ev = default;
        ev.What = Events.evKeyDown;
        ev.keyDown.keyCode = Keys.kbAltI;
        v.HandleEvent(ref ev);
        Assert.Equal(Events.evNothing, ev.What);
        Assert.Equal(0, v.topic.GetNumCrossRefs()); // index topic
        fp.Close();
    }

    // ── evCommand events ──────────────────────────────────────────────────

    [Fact]
    public void CmHelpBack_Command_ConsumesEventAndGoesBack()
    {
        var (v, fp) = OpenViewer(2);
        v.NavigateTo(3);
        TEvent ev = default;
        ev.What = Events.evCommand;
        ev.message.command = Views.cmHelpBack;
        v.HandleEvent(ref ev);
        Assert.Equal(Events.evNothing, ev.What);
        Assert.Equal(1, v.topic.GetNumCrossRefs()); // back to topic 2
        fp.Close();
    }

    [Fact]
    public void CmHelpIndex_Command_ConsumesEventAndGoesToIndex()
    {
        var (v, fp) = OpenViewer(2);
        TEvent ev = default;
        ev.What = Events.evCommand;
        ev.message.command = Views.cmHelpIndex;
        v.HandleEvent(ref ev);
        Assert.Equal(Events.evNothing, ev.What);
        Assert.Equal(0, v.topic.GetNumCrossRefs()); // index topic
        fp.Close();
    }

    // ── Tab / ShiftTab cross-ref selection ────────────────────────────────

    [Fact]
    public void Tab_OnSingleCrossRefTopic_WrapsSelection()
    {
        var (v, fp) = OpenViewer(2);
        Assert.Equal(1, v.selected); // initial selected == 1
        TEvent ev = default;
        ev.What = Events.evKeyDown;
        ev.keyDown.keyCode = Keys.kbTab;
        v.HandleEvent(ref ev);
        // topic 2 has 1 cross-ref; selected wraps from 1 back to 1
        Assert.Equal(1, v.selected);
        fp.Close();
    }

    [Fact]
    public void ShiftTab_OnSingleCrossRefTopic_WrapsSelection()
    {
        var (v, fp) = OpenViewer(2);
        TEvent ev = default;
        ev.What = Events.evKeyDown;
        ev.keyDown.keyCode = Keys.kbShiftTab;
        v.HandleEvent(ref ev);
        Assert.Equal(1, v.selected);
        fp.Close();
    }

    // ── Enter follows selected cross-ref ──────────────────────────────────

    [Fact]
    public void Enter_FollowsSelectedCrossRef_ThenGoBackRestores()
    {
        var (v, fp) = OpenViewer(2);
        TEvent ev = default;
        ev.What = Events.evKeyDown;
        ev.keyDown.keyCode = Keys.kbEnter;
        v.HandleEvent(ref ev);
        // Enter follows cross-ref from topic 2 → topic 3 (1 cross-ref)
        Assert.Equal(1, v.topic.GetNumCrossRefs());
        v.GoBack();
        Assert.Equal(1, v.topic.GetNumCrossRefs()); // topic 2 restored
        fp.Close();
    }

    // ── NavigateTo invalid ref ────────────────────────────────────────────

    [Fact]
    public void NavigateTo_InvalidRef_ReturnsSafeTopic()
    {
        var (v, fp) = OpenViewer(2);
        v.NavigateTo(999); // no such topic — must not throw
        Assert.NotNull(v.topic);
        fp.Close();
    }
}

// ═══════════════════════════════════════════════════════════════════════════
// WrapText bounds safety, cmHelpIndex constant, resize.
// ═══════════════════════════════════════════════════════════════════════════
[Collection("NonParallel")]
public sealed class WrapTextBoundsTests : IDisposable
{
    // ── lifecycle ─────────────────────────────────────────────────────────

    private readonly StreamableRegistryScope _streams;
    private readonly TempDirectory _tmp;
    private readonly DriverScope _driver;

    public WrapTextBoundsTests()
    {
        _streams = new StreamableRegistryScope();
        Pstream.DeInitTypes();
        Pstream.RegisterType(THelpTopic.StreamableClass);
        Pstream.RegisterType(THelpIndex.StreamableClass);
        _tmp = new TempDirectory();
        _driver = new DriverScope();
    }

    public void Dispose()
    {
        _driver.Dispose();
        _tmp.Dispose();
        _streams.Dispose();
    }

    // ── cmHelpIndex constants ─────────────────────────────────────────────

    [Fact]
    public void CmHelpIndex_IsNonZero()
    {
        Assert.NotEqual(0, (int)Views.cmHelpIndex);
    }

    [Fact]
    public void CmHelpIndex_DiffersFromCmHelp()
    {
        Assert.NotEqual(Views.cmHelpIndex, Views.cmHelp);
    }

    // ── WrapText safety ───────────────────────────────────────────────────

    [Fact]
    public void NumLines_EmptyParagraph_DoesNotThrow()
    {
        var t = new THelpTopic();
        t.AddParagraph(new TParagraph { text = Array.Empty<byte>(), size = 0, wrap = false });
        t.SetWidth(20);
        var ex = Record.Exception(() => t.NumLines());
        Assert.Null(ex);
    }

    [Fact]
    public void GetLine_PastLastLine_DoesNotThrow()
    {
        var t = new THelpTopic();
        var b = Encoding.Latin1.GetBytes("Hello\n");
        t.AddParagraph(new TParagraph { text = b, size = (ushort)b.Length, wrap = true });
        t.SetWidth(20);
        var lineBuf = new byte[256];
        var ex = Record.Exception(() =>
        {
            t.GetLine(1, lineBuf);
            t.GetLine(99, lineBuf); // way past end
        });
        Assert.Null(ex);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void NumLines_VerySmallWidth_DoesNotThrow(int width)
    {
        var b = Encoding.Latin1.GetBytes("Hello World\n");
        var t = new THelpTopic();
        t.AddParagraph(new TParagraph { text = b, size = (ushort)b.Length, wrap = true });
        t.SetWidth(width);
        var ex = Record.Exception(() => t.NumLines());
        Assert.Null(ex);
    }

    [Fact]
    public void NumLines_LongWordNoSpaces_ProducesAtLeastOneLine()
    {
        // A 20-byte word with no spaces, width=5 → multiple wrapped lines.
        var b = Encoding.Latin1.GetBytes("ABCDEFGHIJKLMNOPQRST\n");
        var t = new THelpTopic();
        t.AddParagraph(new TParagraph { text = b, size = (ushort)b.Length, wrap = true });
        t.SetWidth(5);
        int lines = 0;
        var ex = Record.Exception(() => lines = t.NumLines());
        Assert.Null(ex);
        Assert.True(lines >= 1);
    }

    [Fact]
    public void NumLines_ExactWidth_DoesNotThrow()
    {
        // "Hello World" = 11 bytes (no \n), width = 11.
        // i = offset + width = 11 = text.Length → was OOB crash before fix.
        var b = Encoding.Latin1.GetBytes("Hello World");
        var t = new THelpTopic();
        t.AddParagraph(new TParagraph { text = b, size = (ushort)b.Length, wrap = true });
        t.SetWidth(11);
        int lines = 0;
        var ex = Record.Exception(() => lines = t.NumLines());
        Assert.Null(ex);
        Assert.True(lines >= 1);
    }

    [Fact]
    public void NumLines_TrailingWhitespace_DoesNotThrow()
    {
        var b = Encoding.Latin1.GetBytes("Hello   ");
        var t = new THelpTopic();
        t.AddParagraph(new TParagraph { text = b, size = (ushort)b.Length, wrap = true });
        t.SetWidth(5);
        var ex = Record.Exception(() => t.NumLines());
        Assert.Null(ex);
    }

    // ── THelpViewer resize ────────────────────────────────────────────────

    [Fact]
    public void ChangeBounds_NarrowAndWider_DoesNotThrow()
    {
        string path = BuildResizeHelpFile();
        var fp = new Fpstream(path);
        var hf = new THelpFile(fp);
        var vsb = new TScrollBar(new TRect(49, 1, 50, 17));
        var hsb = new TScrollBar(new TRect(1, 17, 48, 18));
        var viewer = new THelpViewer(new TRect(0, 0, 50, 18), hsb, vsb, hf, 1);

        var ex = Record.Exception(() =>
        {
            for (int w = 50; w >= 10; w -= 5)
                viewer.ChangeBounds(new TRect(0, 0, w, 18));
            for (int w = 10; w <= 80; w += 10)
                viewer.ChangeBounds(new TRect(0, 0, w, 18));
        });
        Assert.Null(ex);
        fp.Close();
    }

    // ── cmHelpIndex via HandleEvent ───────────────────────────────────────

    [Fact]
    public void CmHelpIndex_ViaHandleEvent_LoadsIndexAndConsumes()
    {
        string path = Build2TopicHelpFile();
        var fp = new Fpstream(path);
        var hf = new THelpFile(fp);
        var vsb = new TScrollBar(new TRect(49, 1, 50, 9));
        var hsb = new TScrollBar(new TRect(1, 9, 48, 10));
        var v = new THelpViewer(new TRect(0, 0, 50, 10), hsb, vsb, hf, 2);

        TEvent ev = default;
        ev.What = Events.evCommand;
        ev.message.command = Views.cmHelpIndex;
        v.HandleEvent(ref ev);
        Assert.Equal(Events.evNothing, ev.What);
        Assert.Equal(0, v.topic.GetNumCrossRefs()); // index topic (id=1) has 0 cross-refs
        fp.Close();
    }

    // ── helpers ───────────────────────────────────────────────────────────

    private string BuildResizeHelpFile()
    {
        string path = Path.Combine(_tmp.Path, "resize.hlp");
        var fp = new Fpstream(path);
        var hf = new THelpFile(fp);
        var t = new THelpTopic();
        var b = Encoding.Latin1.GetBytes(
            "Short text.\nA longer line with several words to force wrapping at narrow widths.\n" +
            "LONGWORDWITHOUTBLANKS\nTrailing spaces   ");
        t.AddParagraph(new TParagraph { text = b, size = (ushort)b.Length, wrap = true });
        t.AddCrossRef(new TCrossRef { @ref = 1, offset = 0, length = 5 });
        hf.RecordPositionInIndex(1);
        hf.PutTopic(t);
        hf.Flush();
        fp.Close();
        return path;
    }

    private string Build2TopicHelpFile()
    {
        string path = Path.Combine(_tmp.Path, "2topic.hlp");
        var fp = new Fpstream(path);
        var hf = new THelpFile(fp);

        var t1 = new THelpTopic();
        var b1 = Encoding.Latin1.GetBytes("Index.\n");
        t1.AddParagraph(new TParagraph { text = b1, size = (ushort)b1.Length, wrap = false });
        hf.RecordPositionInIndex(1);
        hf.PutTopic(t1);

        var t2 = new THelpTopic();
        var b2 = Encoding.Latin1.GetBytes("Body.\n");
        t2.AddParagraph(new TParagraph { text = b2, size = (ushort)b2.Length, wrap = false });
        hf.RecordPositionInIndex(2);
        hf.PutTopic(t2);

        hf.Flush();
        fp.Close();
        return path;
    }
}

// ═══════════════════════════════════════════════════════════════════════════
// Cross-ref decoding: GetCrossRef loc.x alignment, GetLine 0xFF raw-byte preservation, mouse hit-test ranges.
// ═══════════════════════════════════════════════════════════════════════════
[Collection("NonParallel")]
public sealed class CrossRefDecodingTests
{
    // ── GetCrossRef loc.x alignment ───────────────────────────────────────

    [Fact]
    public void GetCrossRef_LocX_EqualsCrossRefOffset()
    {
        // " Main Menu\n" — leading space at col 0; link "Main Menu" at cols 1..9.
        // svhc stores 0xFF for the space inside the link span.
        byte[] raw =
        [
            (byte)' ',
            (byte)'M', (byte)'a', (byte)'i', (byte)'n',
            0xFF, // in-link space
            (byte)'M', (byte)'e', (byte)'n', (byte)'u',
            (byte)'\n'
        ];
        var t = new THelpTopic();
        t.AddParagraph(new TParagraph { text = raw, size = (ushort)raw.Length, wrap = false });
        t.AddCrossRef(new TCrossRef { @ref = 2, offset = 1, length = 9 });
        t.SetWidth(40);

        var kp = new TPoint(0, 0);
        t.GetCrossRef(0, ref kp, out byte klen, out int kref);

        Assert.Equal(1, kp.x);
        Assert.Equal(9, (int)klen);
        Assert.Equal(2, kref);
    }

    // ── GetLine raw byte preservation ─────────────────────────────────────

    [Fact]
    public void GetLine_Preserves0xFF_RawByte()
    {
        byte[] raw =
        [
            (byte)'H', (byte)'e', (byte)'l', (byte)'l', (byte)'o',
            0xFF, // in-link space — must pass through GetLine unchanged
            (byte)'W', (byte)'o', (byte)'r', (byte)'l', (byte)'d',
            (byte)'\n'
        ];
        var t = new THelpTopic();
        t.AddParagraph(new TParagraph { text = raw, size = (ushort)raw.Length, wrap = false });
        t.SetWidth(40);

        var buf = new byte[256];
        t.GetLine(1, buf);
        Assert.Equal(0xFF, buf[5]);  // decode happens only in viewer Draw()
        Assert.Equal((byte)'H', buf[0]);
        Assert.Equal((byte)'W', buf[6]);
    }

    // ── Mouse hit-test range checks ("Main Menu" link) ────────────────────

    [Fact]
    public void MouseHit_FirstChar_IsInLinkSpan()
    {
        var (kp, klen) = GetMainMenuCrossRef();
        Assert.True(1 >= kp.x && 1 < kp.x + klen, $"col 1 not in [{kp.x},{kp.x + klen})");
    }

    [Fact]
    public void MouseHit_InternalSpace_IsInLinkSpan()
    {
        // 0xFF-encoded space sits at col 5 in the raw buffer.
        var (kp, klen) = GetMainMenuCrossRef();
        Assert.True(5 >= kp.x && 5 < kp.x + klen, $"col 5 not in [{kp.x},{kp.x + klen})");
    }

    [Fact]
    public void MouseHit_LastChar_IsInLinkSpan()
    {
        // Last char 'u' at col 9 (offset 1 + length 9 - 1 = 9).
        var (kp, klen) = GetMainMenuCrossRef();
        Assert.True(9 >= kp.x && 9 < kp.x + klen, $"col 9 not in [{kp.x},{kp.x + klen})");
    }

    [Fact]
    public void MouseHit_LeadingSpace_IsOutsideLink()
    {
        var (kp, klen) = GetMainMenuCrossRef();
        Assert.False(0 >= kp.x && 0 < kp.x + klen, "col 0 (leading space) should be outside link");
    }

    [Fact]
    public void MouseHit_PastLink_IsOutsideLink()
    {
        // col 10 = one past the 9-char span that starts at col 1.
        var (kp, klen) = GetMainMenuCrossRef();
        Assert.False(10 >= kp.x && 10 < kp.x + klen, "col 10 (past link) should be outside link");
    }

    // ── Single-char link ──────────────────────────────────────────────────

    [Fact]
    public void SingleCharLink_LocX_Is_0()
    {
        var t = new THelpTopic();
        var raw = Encoding.Latin1.GetBytes("A link B\n");
        t.AddParagraph(new TParagraph { text = raw, size = (ushort)raw.Length, wrap = false });
        t.AddCrossRef(new TCrossRef { @ref = 5, offset = 0, length = 1 });
        t.SetWidth(40);

        var kp = new TPoint(0, 0);
        t.GetCrossRef(0, ref kp, out byte klen, out _);

        Assert.Equal(0, kp.x);
        Assert.Equal(1, (int)klen);
    }

    [Fact]
    public void SingleCharLink_Col0_Inside_Col1_Outside()
    {
        var t = new THelpTopic();
        var raw = Encoding.Latin1.GetBytes("A link B\n");
        t.AddParagraph(new TParagraph { text = raw, size = (ushort)raw.Length, wrap = false });
        t.AddCrossRef(new TCrossRef { @ref = 5, offset = 0, length = 1 });
        t.SetWidth(40);

        var kp = new TPoint(0, 0);
        t.GetCrossRef(0, ref kp, out byte klen, out _);

        Assert.True(0 >= kp.x && 0 < kp.x + klen);
        Assert.False(1 >= kp.x && 1 < kp.x + klen);
    }

    // ── helper ────────────────────────────────────────────────────────────

    private static (TPoint kp, int klen) GetMainMenuCrossRef()
    {
        // " Main Menu\n" with 0xFF for in-link space, cross-ref at offset=1, len=9.
        byte[] raw =
        [
            (byte)' ',
            (byte)'M', (byte)'a', (byte)'i', (byte)'n',
            0xFF,
            (byte)'M', (byte)'e', (byte)'n', (byte)'u',
            (byte)'\n'
        ];
        var t = new THelpTopic();
        t.AddParagraph(new TParagraph { text = raw, size = (ushort)raw.Length, wrap = false });
        t.AddCrossRef(new TCrossRef { @ref = 2, offset = 1, length = 9 });
        t.SetWidth(40);

        var kp = new TPoint(0, 0);
        t.GetCrossRef(0, ref kp, out byte klen, out _);
        return (kp, (int)klen);
    }
}

// ═══════════════════════════════════════════════════════════════════════════
// Help closure: mouse click navigation, multi-word link hit-testing, THelpWindow size/title, idempotent registration.
// ═══════════════════════════════════════════════════════════════════════════
[Collection("NonParallel")]
public sealed class HelpClosureTests : IDisposable
{
    // ── lifecycle ─────────────────────────────────────────────────────────

    private readonly StreamableRegistryScope _streams;
    private readonly TempDirectory _tmp;
    private readonly DriverScope _driver;
    private readonly string _navHlp;

    public HelpClosureTests()
    {
        _streams = new StreamableRegistryScope();
        Pstream.DeInitTypes();
        Pstream.RegisterType(THelpTopic.StreamableClass);
        Pstream.RegisterType(THelpIndex.StreamableClass);
        _tmp = new TempDirectory();
        _driver = new DriverScope();
        _navHlp = BuildNavHelpFile();
    }

    public void Dispose()
    {
        _driver.Dispose();
        _tmp.Dispose();
        _streams.Dispose();
    }

    // ── nav help file builder ─────────────────────────────────────────────
    // navA (ctx=2): "See Main\xffMenu now\n" — cross-ref offset=4, len=9 → navB (ctx=3).
    //   Cols: S=0 e=1 e=2 ' '=3 M=4 a=5 i=6 n=7 0xFF=8 M=9 e=10 n=11 u=12 ' '=13 ...
    // navB (ctx=3): "Back here.\n" — 0 cross-refs.
    private string BuildNavHelpFile()
    {
        string path = Path.Combine(_tmp.Path, "nav19f3.hlp");
        byte[] aBytes =
        [
            (byte)'S', (byte)'e', (byte)'e', (byte)' ',
            (byte)'M', (byte)'a', (byte)'i', (byte)'n',
            0xFF, // in-link space
            (byte)'M', (byte)'e', (byte)'n', (byte)'u',
            (byte)' ', (byte)'n', (byte)'o', (byte)'w', (byte)'\n'
        ];
        byte[] bBytes = Encoding.Latin1.GetBytes("Back here.\n");

        var fp = new Fpstream(path);
        var hf = new THelpFile(fp);

        var tA = new THelpTopic();
        tA.AddParagraph(new TParagraph { text = aBytes, size = (ushort)aBytes.Length, wrap = false });
        tA.AddCrossRef(new TCrossRef { @ref = 3, offset = 4, length = 9 });
        hf.RecordPositionInIndex(2);
        hf.PutTopic(tA);

        var tB = new THelpTopic();
        tB.AddParagraph(new TParagraph { text = bBytes, size = (ushort)bBytes.Length, wrap = false });
        hf.RecordPositionInIndex(3);
        hf.PutTopic(tB);

        hf.Flush();
        fp.Close();
        return path;
    }

    private (THelpViewer viewer, Fpstream fp) OpenNavViewer(ushort ctx)
    {
        var fp = new Fpstream(_navHlp);
        var hf = new THelpFile(fp);
        var vsb = new TScrollBar(new TRect(49, 1, 50, 9));
        var hsb = new TScrollBar(new TRect(1, 9, 48, 10));
        var v = new THelpViewer(new TRect(0, 0, 50, 10), hsb, vsb, hf, ctx);
        return (v, fp);
    }

    // ── 1. Mouse click at link position navigates ─────────────────────────

    [Fact]
    public void MouseClick_AtLinkFirstChar_Navigates()
    {
        var (v, fp) = OpenNavViewer(2);
        Assert.Equal(1, v.topic.GetNumCrossRefs()); // navA has 1 cross-ref
        v.SetState(Views.sfSelected, true);

        // keyPoint.y == mouse.y + 1 → first line: mouse.y = 0.
        // Link starts at col 4 ('M' of "Main Menu").
        TEvent ev = default;
        ev.What = Events.evMouseDown;
        ev.mouse.where = new TPoint(4, 0);
        v.HandleEvent(ref ev);

        Assert.Equal(Events.evNothing, ev.What);
        Assert.Equal(0, v.topic.GetNumCrossRefs()); // navB has 0 cross-refs
        fp.Close();
    }

    // ── 2. Multi-word link hit-test spans ────────────────────────────────

    [Fact]
    public void MultiWord_FirstChar_IsInLinkSpan()
    {
        var (kp, klen) = GetNavACrossRef();
        // 'M' at col 4 — first char of "Main Menu"
        Assert.True(4 >= kp.x && 4 < kp.x + klen);
    }

    [Fact]
    public void MultiWord_InternalSpace_IsInLinkSpan()
    {
        var (kp, klen) = GetNavACrossRef();
        // 0xFF at col 8 — in-link space (counted as part of span)
        Assert.True(8 >= kp.x && 8 < kp.x + klen);
    }

    [Fact]
    public void MultiWord_LastChar_IsInLinkSpan()
    {
        var (kp, klen) = GetNavACrossRef();
        // 'u' at col 12 = offset 4 + length 9 - 1
        Assert.True(12 >= kp.x && 12 < kp.x + klen);
    }

    [Fact]
    public void MultiWord_BeforeLink_IsOutsideSpan()
    {
        var (kp, klen) = GetNavACrossRef();
        // ' ' at col 3 — just before the link
        Assert.False(3 >= kp.x && 3 < kp.x + klen);
    }

    [Fact]
    public void MultiWord_AfterLink_IsOutsideSpan()
    {
        var (kp, klen) = GetNavACrossRef();
        // ' ' at col 13 — just after the link (4 + 9 = 13)
        Assert.False(13 >= kp.x && 13 < kp.x + klen);
    }

    // ── 3. Mouse click outside link does not navigate ─────────────────────

    [Fact]
    public void MouseClick_OutsideLink_DoesNotNavigate()
    {
        var (v, fp) = OpenNavViewer(2);
        v.SetState(Views.sfSelected, true);

        // Click at col 0, row 0 — 'S' of "See", well outside the link (col 4+).
        TEvent ev = default;
        ev.What = Events.evMouseDown;
        ev.mouse.where = new TPoint(0, 0);
        v.HandleEvent(ref ev);

        Assert.Equal(Events.evMouseDown, ev.What); // event not consumed
        Assert.Equal(1, v.topic.GetNumCrossRefs()); // still on navA
        fp.Close();
    }

    // ── 4. GoBack after mouse click returns prior topic ───────────────────

    [Fact]
    public void GoBack_AfterMouseClick_ReturnsPriorTopic()
    {
        var (v, fp) = OpenNavViewer(2);
        v.SetState(Views.sfSelected, true);

        TEvent ev = default;
        ev.What = Events.evMouseDown;
        ev.mouse.where = new TPoint(4, 0);
        v.HandleEvent(ref ev);
        Assert.Equal(0, v.topic.GetNumCrossRefs()); // on navB

        v.GoBack();
        Assert.Equal(1, v.topic.GetNumCrossRefs()); // back to navA
        fp.Close();
    }

    // ── 5. THelpWindow size 50×18 ─────────────────────────────────────────

    [Fact]
    public void THelpWindow_Size_Is_50x18()
    {
        string path = BuildWinHelpFile();
        var fp = new Fpstream(path);
        var hf = new THelpFile(fp);
        var hw = new THelpWindow(hf, 1);
        Assert.Equal(50, hw.size.x);
        Assert.Equal(18, hw.size.y);
        fp.Close();
    }

    [Fact]
    public void THelpWindow_Title_MatchesIntlGet()
    {
        string path = BuildWinHelpFile();
        var fp = new Fpstream(path);
        var hf = new THelpFile(fp);
        var hw = new THelpWindow(hf, 1);
        Assert.Equal(TSharpVisionIntl.Get("Help_WindowTitle", "Help"), hw.title);
        fp.Close();
    }

    // ── 6. RegisterStreamableTypes() is idempotent ────────────────────────

    [Fact]
    public void RegisterStreamableTypes_IsIdempotent()
    {
        // Call twice in a row; the second call must not corrupt the registry.
        THelpFile.RegisterStreamableTypes();
        THelpFile.RegisterStreamableTypes();

        string path = Path.Combine(_tmp.Path, "idem.hlp");
        var fpW = new Fpstream(path);
        var hfW = new THelpFile(fpW);
        var t = new THelpTopic();
        var b = Encoding.Latin1.GetBytes("Idempotent.\n");
        t.AddParagraph(new TParagraph { text = b, size = (ushort)b.Length, wrap = false });
        hfW.RecordPositionInIndex(5);
        hfW.PutTopic(t);
        hfW.Flush();
        fpW.Close();

        var fpR = new Fpstream(path);
        var hfR = new THelpFile(fpR);
        var top5 = hfR.GetTopic(5);
        fpR.Close();

        Assert.NotNull(top5);
        string txt = top5!.paragraphs?.text != null
            ? Encoding.Latin1.GetString(top5.paragraphs!.text).TrimEnd('\n')
            : "";
        Assert.Equal("Idempotent.", txt);
    }

    // ── helpers ───────────────────────────────────────────────────────────

    // Returns the cross-ref data for navA's first (and only) cross-ref.
    // Uses an in-memory topic replica to avoid file-handle ownership issues.
    private static (TPoint kp, int klen) GetNavACrossRef()
    {
        byte[] aBytes =
        [
            (byte)'S', (byte)'e', (byte)'e', (byte)' ',
            (byte)'M', (byte)'a', (byte)'i', (byte)'n',
            0xFF,
            (byte)'M', (byte)'e', (byte)'n', (byte)'u',
            (byte)' ', (byte)'n', (byte)'o', (byte)'w', (byte)'\n'
        ];
        var t = new THelpTopic();
        t.AddParagraph(new TParagraph { text = aBytes, size = (ushort)aBytes.Length, wrap = false });
        t.AddCrossRef(new TCrossRef { @ref = 3, offset = 4, length = 9 });
        t.SetWidth(50);

        var kp = new TPoint(0, 0);
        t.GetCrossRef(0, ref kp, out byte klen, out _);
        return (kp, (int)klen);
    }

    private string BuildWinHelpFile()
    {
        string path = Path.Combine(_tmp.Path, "win.hlp");
        var fp = new Fpstream(path);
        var hf = new THelpFile(fp);
        var t1 = new THelpTopic();
        var b1 = Encoding.Latin1.GetBytes("Index.\n");
        t1.AddParagraph(new TParagraph { text = b1, size = (ushort)b1.Length, wrap = false });
        hf.RecordPositionInIndex(1);
        hf.PutTopic(t1);
        hf.Flush();
        fp.Close();
        return path;
    }
}
