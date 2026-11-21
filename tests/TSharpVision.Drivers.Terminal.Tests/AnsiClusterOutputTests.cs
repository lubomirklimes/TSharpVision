// TV13n / U4: width-authoritative VT output of grapheme clusters (lead/trail cells) by AnsiTerminalDriver.
// Source text is built from code points so the file stays ASCII.
using System.Text;
using TSharpVision;
using TSharpVision.Drivers;
using TSharpVision.Drivers.Terminal;
using Xunit;

namespace TSharpVision.Drivers.Terminal.Tests;

public sealed class AnsiClusterOutputTests
{
    private static readonly TColorAttr Gray = new(0x07);
    private static readonly TColorAttr Blue = new(0x1F);
    private static string Sgr(TColorAttr a) => AnsiTerminalDriver.AttrToSgr(a);
    private static string Cup(int x, int y) => $"\x1b[{y + 1};{x + 1}H";
    private const string Reset = "\x1b[0m";

    internal static string S(params int[] scalars)
    {
        var sb = new StringBuilder();
        foreach (int s in scalars) sb.Append(char.ConvertFromUtf32(s));
        return sb.ToString();
    }

    internal static readonly string Grin = S(0x1F600);
    internal static readonly string ThumbTone = S(0x1F44D, 0x1F3FD);
    internal static readonly string FlagCz = S(0x1F1E8, 0x1F1FF);
    internal static readonly string Family = S(0x1F468, 0x200D, 0x1F469, 0x200D, 0x1F467);
    internal static readonly string EAcute = S('e', 0x0301);
    internal static readonly string Kan = S(0x6F22);
    internal static readonly string HeartEmoji = S(0x2764, 0xFE0F);
    internal static readonly string HeartText = S(0x2764, 0xFE0E);

    /// <summary>Cells for a sequence of (text, width) clusters; width 1 single-unit text becomes a single cell.</summary>
    internal static TScreenChar[] Cells(TColorAttr attr, params (string Text, int Width)[] clusters)
    {
        var list = new List<TScreenChar>();
        foreach (var (text, width) in clusters)
        {
            list.Add(TScreenChar.CreateCluster(text, width, attr));
            for (int k = 1; k < width; k++) list.Add(TScreenChar.CreateTrail(attr));
        }
        return list.ToArray();
    }

    private static string Format(TScreenChar[] cells, int x = 0, int y = 0, int cols = 0, bool legacy = false,
        int caretX = 0, int caretY = 0)
        => AnsiTerminalDriver.FormatBuffer(x, y, cells.Length, 1, cells, caretX, caretY, cols, legacy);

    [Fact]
    public void WideEmojiWritesWholeClusterOnceWithEraseAndReanchor()
    {
        var cells = Cells(Gray, ("A", 1), (Grin, 2), ("B", 1));
        Assert.Equal(TScreenCellKind.Lead, cells[1].Kind);
        Assert.Equal(TScreenCellKind.Trail, cells[2].Kind);
        Assert.Equal(Cup(0, 0) + Sgr(Gray) + "A\x1b[2X" + Grin + Cup(3, 0) + "B" + Reset + Cup(0, 0), Format(cells));
    }

    [Fact]
    public void Tv13hSampleRowIsAnchoredAfterEveryCluster()
    {
        // TV13h sample 1 (A, grinning face, B, thumbs up + tone, C, flag CZ, D, family ZWJ, E): 13 cells.
        var cells = Cells(Gray, ("A", 1), (Grin, 2), ("B", 1), (ThumbTone, 2), ("C", 1), (FlagCz, 2), ("D", 1),
            (Family, 2), ("E", 1));
        Assert.Equal(13, cells.Length);
        string expected = Cup(0, 4) + Sgr(Gray)
            + "A\x1b[2X" + Grin + Cup(3, 4)
            + "B\x1b[2X" + ThumbTone + Cup(6, 4)
            + "C\x1b[2X" + FlagCz + Cup(9, 4)
            + "D\x1b[2X" + Family + Cup(12, 4)
            + "E" + Reset + Cup(1, 2);
        Assert.Equal(expected, Format(cells, y: 4, caretX: 1, caretY: 2));
    }

    [Fact]
    public void CombiningAndVariationClustersOfOneCellReanchorWithoutErase()
    {
        var cells = Cells(Gray, (EAcute, 1), ("x", 1), (HeartText, 1), ("y", 1), (HeartEmoji, 2), ("z", 1));
        Assert.Equal(Cup(2, 0) + Sgr(Gray) + EAcute + Cup(3, 0) + "x" + HeartText + Cup(5, 0) + "y\x1b[2X"
                     + HeartEmoji + Cup(8, 0) + "z" + Reset + Cup(0, 0),
            Format(cells, x: 2));
    }

    [Fact]
    public void BmpWideCharacterIsWrittenOnceAndReanchored()
    {
        var cells = Cells(Blue, (Kan, 2), ("-", 1));
        Assert.Equal(Cup(0, 0) + Sgr(Blue) + "\x1b[2X" + Kan + Cup(2, 0) + "-" + Reset + Cup(0, 0), Format(cells));
    }

    [Fact]
    public void ClusterAtTrailingEdgeOfSliceIsBlanked()
    {
        // The trail is outside the slice: never print half a cluster.
        var cells = Cells(Gray, ("A", 1), (Grin, 2));
        var slice = cells[..2];
        Assert.Equal(Cup(0, 0) + Sgr(Gray) + "A " + Reset + Cup(0, 0),
            AnsiTerminalDriver.FormatBuffer(0, 0, 2, 1, slice, 0, 0, 0, legacy: false));
    }

    [Fact]
    public void TwoCellClusterIsNeverWrittenAcrossTheLastScreenColumn()
    {
        // Screen 10 columns, the slice starts at column 8: "X" + a wide cluster that would need columns 9 and 10.
        var cells = Cells(Gray, ("X", 1), (Grin, 2));
        string output = AnsiTerminalDriver.FormatBuffer(8, 0, 3, 1, cells, 0, 0, 10, legacy: false);
        Assert.DoesNotContain(Grin, output, StringComparison.Ordinal);
        Assert.Equal(Cup(8, 0) + Sgr(Gray) + "X  " + Reset + Cup(0, 0), output);

        // Ending exactly on the last column is fine.
        string fits = AnsiTerminalDriver.FormatBuffer(7, 0, 3, 1, cells, 0, 0, 10, legacy: false);
        Assert.Contains(Grin, fits, StringComparison.Ordinal);
    }

    [Fact]
    public void OrphanTrailAtSliceStartIsBlank()
    {
        var cells = Cells(Gray, (Grin, 2), ("Z", 1));
        Assert.Equal(Cup(5, 0) + Sgr(Gray) + " Z" + Reset + Cup(0, 0),
            AnsiTerminalDriver.FormatBuffer(5, 0, 2, 1, cells.AsSpan(1).ToArray(), 0, 0, 0, legacy: false));
    }

    [Fact]
    public void ClusterTextWithControlsIsBlanked()
    {
        var cells = Cells(Gray, ("a\x1b]52;c;x\x07", 2), ("b", 1));
        string output = Format(cells);
        Assert.DoesNotContain("\x1b]", output, StringComparison.Ordinal);
        Assert.DoesNotContain("\x07", output, StringComparison.Ordinal);
        Assert.Equal(Cup(0, 0) + Sgr(Gray) + "  b" + Reset + Cup(0, 0), output);
    }

    [Fact]
    public void PerCellAttributesOfClustersAreKept()
    {
        var cells = new[]
        {
            TScreenChar.CreateCluster(Grin, 2, Blue), TScreenChar.CreateTrail(Blue), new TScreenChar('q', Gray),
        };
        Assert.Equal(Cup(0, 0) + Sgr(Blue) + "\x1b[2X" + Grin + Cup(2, 0) + Sgr(Gray) + "q" + Reset + Cup(0, 0),
            Format(cells));
    }

    [Fact]
    public void MultiRowRedrawStartsEveryRowWithCup()
    {
        var row = Cells(Gray, (Grin, 2), ("a", 1));
        var cells = row.Concat(row).ToArray();
        string output = AnsiTerminalDriver.FormatBuffer(1, 1, 3, 2, cells, 0, 0, 0, legacy: false);
        Assert.Equal(Cup(1, 1) + Sgr(Gray) + "\x1b[2X" + Grin + Cup(3, 1) + "a"
                     + Cup(1, 2) + "\x1b[2X" + Grin + Cup(3, 2) + "a" + Reset + Cup(0, 0), output);
    }

    [Fact]
    public void LegacyProfileOutputIsTheHistoricalOnePerCellOutput()
    {
        // Legacy: every cell's Character (lead fallback U+FFFD, trail '\0' as a space), no CUP re-anchoring.
        var cells = Cells(Gray, ("A", 1), (Grin, 2), (Kan, 2), ("B", 1));
        string expected = Cup(0, 0) + Sgr(Gray) + "A" + (char)0xFFFD + " " + Kan + " B" + Reset + Cup(0, 0);
        Assert.Equal(expected, Format(cells, legacy: true));
    }

    [Fact]
    public void PlainCellsAreByteIdenticalInUnicodeAndLegacyProfiles()
    {
        string text = "Hello, " + S(0x10D, 0x159) + " " + S(0x2500, 0x2502, 0x2554, 0x2591, 0xB1, 0xA7, 0x3A9) + "\0\x01";
        var cells = text.Select((ch, i) => new TScreenChar(ch, new TColorAttr((byte)(i % 3 == 0 ? 0x1F : 0x07))))
            .ToArray();
        Assert.Equal(Format(cells, legacy: true), Format(cells, legacy: false));
        Assert.Equal(Format(cells, legacy: true), AnsiTerminalDriver.FormatBuffer(0, 0, cells.Length, 1, cells, 0, 0));
    }

    [Fact]
    public void RawSingleCellsAreGuardedAgainstTerminalWidthRules()
    {
        // Raw cell writes (not through the core's cluster writers) can still put a lone combining mark, a wide BMP
        // character or a lone surrogate in one cell.
        var attr = Gray;
        var cells = new[]
        {
            new TScreenChar((char)0x0301, attr), new TScreenChar('a', attr),
            new TScreenChar((char)0x6F22, attr), new TScreenChar('b', attr),
            new TScreenChar((char)0xD83D, attr), new TScreenChar('c', attr),
            new TScreenChar((char)0xDE00, attr),
        };
        string expected = Cup(0, 0) + Sgr(attr)
            + " " + (char)0x0301 + Cup(1, 0) + "a"
            + (char)0x6F22 + Cup(3, 0) + "b"
            + (char)0xFFFD + "c" + (char)0xFFFD
            + Reset + Cup(0, 0);
        Assert.Equal(expected, Format(cells));
    }

    [Fact]
    public void RawSurrogatePairInTwoCellsIsKeptAndReanchored()
    {
        var cells = new[]
        {
            new TScreenChar((char)0xD83D, Gray), new TScreenChar((char)0xDE00, Gray), new TScreenChar('x', Gray),
        };
        Assert.Equal(Cup(0, 0) + Sgr(Gray) + Grin + Cup(2, 0) + "x" + Reset + Cup(0, 0), Format(cells));
    }

    [Fact]
    public void RandomClusterRowsNeverEmitHalfClustersOrCrossTheLastColumn()
    {
        string[] pool = { "a", "b", Grin, ThumbTone, FlagCz, Family, EAcute, Kan, HeartEmoji, HeartText };
        int[] widths = { 1, 1, 2, 2, 2, 2, 1, 2, 2, 1 };
        var random = new Random(1311);
        for (int round = 0; round < 500; round++)
        {
            int cols = random.Next(1, 30);
            var list = new List<TScreenChar>();
            while (list.Count < cols + 3)
            {
                int pick = random.Next(pool.Length);
                list.Add(TScreenChar.CreateCluster(pool[pick], widths[pick], Gray));
                for (int k = 1; k < widths[pick]; k++) list.Add(TScreenChar.CreateTrail(Gray));
            }
            int start = random.Next(0, 3);
            int x = random.Next(0, cols);
            int len = Math.Min(cols - x, list.Count - start);
            if (len <= 0) continue;
            var slice = list.GetRange(start, len).ToArray();
            string output = AnsiTerminalDriver.FormatBuffer(x, 0, len, 1, slice, 0, 0, cols, legacy: false);

            // Replay: every printed cluster must start where the core put it and end within the screen.
            int column = x, i = 0;
            foreach (var (pos, text) in Printed(output))
            {
                Assert.True(pos >= x && pos < x + len, $"round {round}: column {pos}");
                int w = Width(text);
                Assert.True(pos + w <= cols, $"round {round}: '{text}' at {pos} crosses {cols}");
                column = pos + w;
                i++;
            }
            Assert.True(column <= cols);
        }

        int Width(string text)
        {
            for (int k = 0; k < pool.Length; k++) if (pool[k] == text) return widths[k];
            return 1;
        }
    }

    // Parses formatter output into (column, cluster text) pairs, assuming each printed cluster follows a CUP or
    // the previous cluster (the core's widths).
    private static IEnumerable<(int Column, string Text)> Printed(string output)
    {
        string[] pool = { Grin, ThumbTone, FlagCz, Family, EAcute, Kan, HeartEmoji, HeartText };
        int col = 0, i = 0;
        var result = new List<(int, string)>();
        while (i < output.Length)
        {
            if (output[i] == '\x1b')
            {
                int end = i + 2;
                while (end < output.Length && !char.IsLetter(output[end])) end++;
                string body = output.Substring(i + 2, end - i - 2);
                if (output[end] == 'H' && body.Split(';') is { Length: 2 } parts) col = int.Parse(parts[1]) - 1;
                i = end + 1;
                continue;
            }
            string? cluster = pool.FirstOrDefault(p => string.CompareOrdinal(output, i, p, 0, p.Length) == 0
                && (i + p.Length >= output.Length || output[i + p.Length] == '\x1b' || p.Length > 1));
            if (cluster is not null && cluster.Length > 1)
            {
                result.Add((col, cluster));
                col += cluster == EAcute || cluster == HeartText ? 1 : 2;
                i += cluster.Length;
                continue;
            }
            result.Add((col, output[i].ToString()));
            col += output[i] == Kan[0] ? 2 : 1;
            i++;
        }
        return result;
    }

    [Fact]
    public void DriverReportsWideCellsAndClusterTextButNotColourGlyphs()
    {
        IDriver driver = new AnsiTerminalDriver();
        Assert.Equal(TextCapabilities.WideCells | TextCapabilities.ClusterText, driver.TextCapabilities);
    }

    [Fact]
    public void RestoreSequenceTurnsAutowrapBackOn()
    {
        Assert.Contains("\x1b[?7h", AnsiTerminalDriver.RestoreSequence, StringComparison.Ordinal);
        Assert.Contains("\x1b[?1049l", AnsiTerminalDriver.RestoreSequence, StringComparison.Ordinal);
    }
}
