// MsgBox.MessageBox automatic sizing.
//
// The ordinary MessageBox used a fixed 40×9 dialog whose text view holds 35×4 cells, so longer
// messages were clipped without notice. These tests run the real MessageBox path against a host
// that keeps the dialog instead of executing it, and read the rows the dialog drew into the
// host's buffer.
using System.Text;
using TSharpVision;
using TSharpVision.Constants;
using TSharpVision.Tests.Infrastructure;
using Xunit;

namespace TSharpVision.Tests.Dialogs;

[Collection("NonParallel")]
public sealed class MsgBoxAutoSizingTests : IDisposable
{
    private readonly DriverScope _ds;

    public MsgBoxAutoSizingTests()
    {
        _ds = new DriverScope(80, 25);
        TEventQueue.Resume();
    }

    public void Dispose() => _ds.Dispose();

    private const ushort InfoOk = MsgBox.mfInformation | MsgBox.mfOKButton;

    // Stands in for the desktop: ExecView inserts (and so draws) the dialog and answers at once.
    private sealed class CaptureHost : TGroup
    {
        public TDialog? Dialog;

        public CaptureHost(int width, int height) : base(new TRect(0, 0, width, height))
        {
            options = (ushort)(options & ~Views.ofSelectable);
            buffer = new ScreenBuffer(width * height * ScreenBuffer.GetSize());
            state |= (ushort)(Views.sfVisible | Views.sfExposed);
        }

        public override ushort ExecView(TView p)
        {
            Dialog = (TDialog)p;
            Insert(p);
            p.DrawView();
            return Views.cmOK;
        }

        public string Row(int y, int x, int count)
        {
            var sb = new StringBuilder(count);
            for (int i = 0; i < count; i++)
            {
                char c = buffer!.Data[y * size.x + x + i].Character;
                sb.Append(c == '\0' ? ' ' : c);
            }
            return sb.ToString();
        }
    }

    private static CaptureHost Show(int width, int height, string msg, ushort options = InfoOk)
    {
        var host = new CaptureHost(width, height);
        Assert.Equal(Views.cmOK, MsgBox.MessageBox(host, msg, options));
        Assert.NotNull(host.Dialog);
        return host;
    }

    // The rows of the dialog's text view as drawn, right-trimmed.
    private static List<string> TextRows(CaptureHost host)
    {
        var d = host.Dialog!;
        var rows = new List<string>();
        for (int y = 2; y < d.size.y - 3; y++)
            rows.Add(host.Row(d.origin.y + y, d.origin.x + 3, d.size.x - 5).TrimEnd());
        return rows;
    }

    private static string Squeeze(string s)
    {
        var sb = new StringBuilder(s.Length);
        foreach (char c in s)
            if (!char.IsWhiteSpace(c)) sb.Append(c);
        return sb.ToString();
    }

    private static void AssertAllTextDrawn(CaptureHost host, string msg)
        => Assert.Equal(Squeeze(msg), Squeeze(string.Concat(TextRows(host))));

    private static void AssertInsideHost(CaptureHost host)
    {
        var d = host.Dialog!;
        Assert.True(d.size.x > 0 && d.size.y > 0, $"size {d.size.x}x{d.size.y}");
        Assert.True(d.origin.x >= 0 && d.origin.y >= 0, $"origin {d.origin.x},{d.origin.y}");
        Assert.True(d.origin.x + d.size.x <= host.size.x, $"right edge {d.origin.x + d.size.x} > {host.size.x}");
        Assert.True(d.origin.y + d.size.y <= host.size.y, $"bottom edge {d.origin.y + d.size.y} > {host.size.y}");
    }

    private static void AssertCentred(CaptureHost host)
    {
        var d = host.Dialog!;
        Assert.Equal((host.size.x - d.size.x) / 2, d.origin.x);
        Assert.Equal((host.size.y - d.size.y) / 2, d.origin.y);
    }

    private static List<TButton> Buttons(TDialog dialog)
    {
        var buttons = new List<TButton>();
        if (dialog.last is TView last && last.Next is TView first)
        {
            TView p = first;
            do
            {
                if (p is TButton b) buttons.Add(b);
                p = Assert.IsAssignableFrom<TView>(p.Next);
            } while (p != first);
        }
        buttons.Sort((a, b) => a.origin.x.CompareTo(b.origin.x));
        return buttons;
    }

    private static void AssertButtonsUsable(CaptureHost host, int expected)
    {
        var d = host.Dialog!;
        var buttons = Buttons(d);
        Assert.Equal(expected, buttons.Count);
        int previousEnd = 1;
        foreach (var b in buttons)
        {
            Assert.Equal(d.size.y - 3, b.origin.y);
            Assert.True(b.origin.x >= previousEnd, $"button at {b.origin.x} overlaps or leaves the frame");
            previousEnd = b.origin.x + b.size.x;
        }
        Assert.True(previousEnd <= d.size.x - 1, $"buttons end at {previousEnd}, dialog is {d.size.x} wide");
    }

    // ── 1. short messages keep the classic box ───────────────────────────────

    [Fact]
    public void ShortMessage_KeepsClassicCentredBox()
    {
        var host = Show(80, 23, "Hello");
        var d = host.Dialog!;
        Assert.Equal(40, d.size.x);
        Assert.Equal(9, d.size.y);
        AssertCentred(host);
        Assert.Equal("Hello", TextRows(host)[0]);
        AssertButtonsUsable(host, 1);
    }

    [Fact]
    public void MessageThatFitsTheClassicBox_IsLaidOutAsBefore()
    {
        // Four wrapped rows of 35 columns: the most the classic box holds.
        const string msg = "Saved 12 demonstration windows and palette.\nSkipped 3 unsupported windows (games/accessories/viewers).";
        var host = Show(80, 23, msg);
        Assert.Equal(MsgBox.DefaultRect(host), host.Dialog!.GetBounds());
        AssertAllTextDrawn(host, msg);
    }

    // ── 2. long single line ──────────────────────────────────────────────────

    [Fact]
    public void LongSingleLine_WrapsAndShowsAllText()
    {
        string msg = string.Join(" ", Enumerable.Range(1, 60).Select(i => $"word{i}"));
        var host = Show(80, 23, msg);
        AssertInsideHost(host);
        AssertCentred(host);
        AssertAllTextDrawn(host, msg);
        Assert.True(host.Dialog!.size.x < 80, "prose should wrap, not fill the screen width");
        Assert.True(host.Dialog!.size.y > 9);
    }

    // ── 3. explicit lines ────────────────────────────────────────────────────

    [Fact]
    public void ExplicitLines_EachGetARow()
    {
        string[] lines = Enumerable.Range(1, 8).Select(i => $"Line number {i}").ToArray();
        var host = Show(80, 23, string.Join("\n", lines));
        Assert.Equal(lines, TextRows(host));
        Assert.Equal(lines.Length + 5, host.Dialog!.size.y);
        Assert.Equal(40, host.Dialog!.size.x);
    }

    [Fact]
    public void BlankLines_ArePreservedAndCounted()
    {
        var host = Show(80, 23, "First\n\nSecond\n\n\nThird\nFourth");
        Assert.Equal(new[] { "First", "", "Second", "", "", "Third", "Fourth" }, TextRows(host));
    }

    [Theory]
    [InlineData("\n")]
    [InlineData("\r\n")]
    [InlineData("\r")]
    public void LineBreakStyles_RenderTheSameRows(string newline)
    {
        string[] lines = { "Alpha", "Beta", "", "Gamma", "Delta", "Epsilon" };
        var host = Show(80, 23, string.Join(newline, lines));
        Assert.Equal(lines, TextRows(host));
    }

    // ── 4. the message that the fixed 40×9 box clipped ───────────────────────

    [Fact]
    public void SynchronizationPreview_IsNotClipped()
    {
        const string msg =
            "Source:      C:\\Projects\\Commander\\left\n" +
            "Destination: C:\\Projects\\Commander\\right\n" +
            "\n" +
            "3 files to copy, 1 directory to create.\n" +
            "\n" +
            "  copy   docs\\readme.md\n" +
            "  copy   src\\Program.cs\n" +
            "  copy   src\\Panels\\Layout.cs\n" +
            "  mkdir  tests\\Fixtures";
        var host = Show(80, 23, msg, MsgBox.mfConfirmation | MsgBox.mfYesButton | MsgBox.mfNoButton);
        AssertInsideHost(host);
        AssertAllTextDrawn(host, msg);
        var rows = TextRows(host);
        Assert.Equal(9, rows.Count);
        Assert.Equal("Destination: C:\\Projects\\Commander\\right", rows[1]);
        Assert.Equal("mkdir  tests\\Fixtures", rows[8].TrimStart());
        AssertButtonsUsable(host, 2);
    }

    // ── 5 / 11. buttons ──────────────────────────────────────────────────────

    public static TheoryData<ushort, int> ButtonSets => new()
    {
        { MsgBox.mfOKButton, 1 },
        { MsgBox.mfOKCancel, 2 },
        { MsgBox.mfYesButton | MsgBox.mfNoButton, 2 },
        { MsgBox.mfYesNoCancel, 3 },
        { MsgBox.mfYesButton | MsgBox.mfNoButton | MsgBox.mfOKButton | MsgBox.mfCancelButton, 4 },
    };

    [Theory]
    [MemberData(nameof(ButtonSets))]
    public void Buttons_FitInsideTheBox(ushort buttons, int count)
    {
        foreach (string msg in new[] { "x", string.Join("\n", Enumerable.Repeat("A line of the message", 10)) })
        {
            var host = Show(80, 23, msg, (ushort)(MsgBox.mfConfirmation | buttons));
            AssertInsideHost(host);
            AssertCentred(host);
            AssertButtonsUsable(host, count);
            AssertAllTextDrawn(host, msg);
        }
    }

    [Fact]
    public void Buttons_AreDrawnOnTheButtonRow()
    {
        var host = Show(80, 23, string.Join("\n", Enumerable.Repeat("A line of the message", 10)),
            MsgBox.mfConfirmation | MsgBox.mfYesNoCancel);
        var d = host.Dialog!;
        string row = host.Row(d.origin.y + d.size.y - 3, d.origin.x, d.size.x);
        Assert.Contains("Yes", row);
        Assert.Contains("No", row);
        Assert.Contains("Cancel", row);
    }

    // ── 6. width limit ───────────────────────────────────────────────────────

    [Fact]
    public void VeryLongText_IsLimitedToTheHostWidth()
    {
        // 3000 characters: too tall at the comfortable width, so the box widens to the host.
        string msg = string.Join(" ", Enumerable.Range(1, 500).Select(i => $"w{i:0000}"));
        var host = Show(80, 23, msg);
        AssertInsideHost(host);
        Assert.Equal(80, host.Dialog!.size.x);
        Assert.Equal(23, host.Dialog!.size.y);
        Assert.Equal(new TPoint { x = 0, y = 0 }, host.Dialog!.origin);
    }

    [Fact]
    public void TextTooTallAtComfortableWidth_WidensUntilItFits()
    {
        // 1100 characters need 21 rows at 55 columns but only 18 are available on 80×23.
        string msg = string.Join(" ", Enumerable.Range(1, 200).Select(i => $"w{i:000}"));
        var host = Show(80, 23, msg);
        AssertInsideHost(host);
        AssertAllTextDrawn(host, msg);
        Assert.InRange(host.Dialog!.size.x, 61, 80);
    }

    // ── 7. height limit ──────────────────────────────────────────────────────

    [Fact]
    public void VeryTallText_IsLimitedToTheHostHeight()
    {
        string[] lines = Enumerable.Range(1, 100).Select(i => $"Line {i}").ToArray();
        var host = Show(80, 23, string.Join("\n", lines));
        AssertInsideHost(host);
        Assert.Equal(23, host.Dialog!.size.y);
        // Documented limitation: the first rows that fit are shown, the rest is cut.
        Assert.Equal(lines.Take(18), TextRows(host));
        AssertButtonsUsable(host, 1);
    }

    // ── 8. small hosts ───────────────────────────────────────────────────────

    [Theory]
    [InlineData(40, 9)]
    [InlineData(30, 10)]
    [InlineData(20, 8)]
    [InlineData(18, 6)]
    public void SmallHost_StaysInsideAndKeepsTheButton(int width, int height)
    {
        const string msg = "The file could not be saved because the disk is full.";
        var host = Show(width, height, msg);
        AssertInsideHost(host);
        AssertButtonsUsable(host, 1);
        var d = host.Dialog!;
        Assert.Contains("OK", host.Row(d.origin.y + d.size.y - 3, d.origin.x, d.size.x));
        Assert.StartsWith("The", TextRows(host)[0]);
    }

    [Fact]
    public void SmallHost_ManyButtons_DoesNotThrow()
    {
        var host = Show(20, 8, "Overwrite?", MsgBox.mfConfirmation | MsgBox.mfYesNoCancel);
        AssertInsideHost(host);
        Assert.Equal(3, Buttons(host.Dialog!).Count);
    }

    [Theory]
    [InlineData(16, 6)]
    [InlineData(10, 4)]
    [InlineData(0, 0)]
    public void HostBelowMinimumWindowSize_BuildsAMinimumWindow(int width, int height)
    {
        // Not drawn: TFrame cannot draw a titled window narrower than 18 columns.
        var probe = new RectProbe(new TRect(0, 0, width, height));
        MsgBox.MessageBox(probe, "The file could not be saved because the disk is full.", InfoOk);
        Assert.Equal(new TRect(0, 0, TWindow.MinWinSize.x, TWindow.MinWinSize.y), probe.Bounds);
    }

    private sealed class RectProbe : TGroup
    {
        public TRect Bounds;
        public RectProbe(TRect bounds) : base(bounds) { }
        public override ushort ExecView(TView p)
        {
            Bounds = p.GetBounds();
            foreach (var view in new[] { p }.Concat(Children((TGroup)p)))
                Assert.True(view.size.x >= 0 && view.size.y >= 0, $"{view.GetType().Name} size {view.size.x}x{view.size.y}");
            return Views.cmOK;
        }

        private static IEnumerable<TView> Children(TGroup g)
        {
            if (g.last is not TView last || last.Next is not TView first) yield break;
            TView p = first;
            do
            {
                yield return p;
                if (p.Next is not TView next) yield break;
                p = next;
            } while (p != first);
        }
    }

    // ── 9. long unbroken tokens ──────────────────────────────────────────────

    [Fact]
    public void LongPath_WidensTheBoxInsteadOfBeingCut()
    {
        const string path = @"C:\Users\someone\Documents\Projects\Commander\artifacts\report-2026.md";
        string msg = "Cannot open the file\n" + path + "\nbecause it is in use by another process.";
        var host = Show(80, 23, msg);
        AssertInsideHost(host);
        AssertCentred(host);
        Assert.Contains(path, TextRows(host));
        Assert.Equal(path.Length + 5, host.Dialog!.size.x);
    }

    [Fact]
    public void TokenLongerThanTheHost_IsCutAcrossRowsWithoutBreakingGeometry()
    {
        string token = "https://example.invalid/" + new string('a', 300);
        string msg = "Open " + token + " now";
        var host = Show(80, 23, msg);
        AssertInsideHost(host);
        Assert.Equal(80, host.Dialog!.size.x);
        AssertAllTextDrawn(host, msg);
        Assert.All(TextRows(host), row => Assert.True(row.Length <= 75));
        AssertButtonsUsable(host, 1);
    }

    // ── 10. MessageBoxRect ───────────────────────────────────────────────────

    [Fact]
    public void MessageBoxRect_KeepsTheSuppliedBounds()
    {
        string msg = string.Join("\n", Enumerable.Repeat("A line of the message", 12));
        var host = new CaptureHost(80, 23);
        var r = new TRect(5, 3, 37, 11);
        MsgBox.MessageBoxRect(host, r, msg, InfoOk);
        var d = host.Dialog!;
        Assert.Equal(r, d.GetBounds());
        // The caller's rectangle wins even when the text does not fit it.
        Assert.Equal(Enumerable.Repeat("A line of the message", 3), TextRows(host));
    }

    [Fact]
    public void DefaultRect_IsStillTheClassicBox()
    {
        var host = new CaptureHost(80, 23);
        Assert.Equal(new TRect(20, 7, 60, 16), MsgBox.DefaultRect(host));
    }

    // ── 12. Unicode ──────────────────────────────────────────────────────────

    [Fact]
    public void UnicodeText_IsSizedAndDrawn()
    {
        const string line = "Příliš žluťoučký kůň úpěl ďábelské ódy 😀 nad řekou.";
        string msg = string.Join("\n", Enumerable.Repeat(line, 6)) + "\n" +
                     string.Join(" ", Enumerable.Repeat("žluťoučký 😀", 20));
        var host = Show(80, 23, msg);
        AssertInsideHost(host);
        AssertCentred(host);
        AssertAllTextDrawn(host, msg);
        Assert.Equal(line, TextRows(host)[0]);
    }

    // ── sizing and rendering agree ───────────────────────────────────────────

    [Theory]
    [InlineData(80, 23)]
    [InlineData(132, 43)]
    [InlineData(60, 20)]
    public void GrownBox_HasExactlyTheRowsItsTextNeeds(int width, int height)
    {
        string msg = "Heading\n\n" +
                     string.Join(" ", Enumerable.Range(1, 70).Select(i => $"word{i}")) +
                     "\n\nLast line";
        var host = Show(width, height, msg);
        AssertInsideHost(host);
        AssertAllTextDrawn(host, msg);
        var rows = TextRows(host);
        Assert.Equal("Last line", rows[^1]);
    }

    [Fact]
    public void NullHost_ReturnsZero()
    {
        Assert.Equal(0, MsgBox.MessageBox(null, string.Join("\n", Enumerable.Repeat("line", 50)), InfoOk));
    }
}
