// TV13n / U4: Win32ConsoleDriver output tiers for grapheme clusters.
//   T1 (conhost, unknown hosts): CHAR_INFO rows with COMMON_LVB_LEADING_BYTE/TRAILING_BYTE pairs for wide BMP
//   characters, unflagged surrogate halves for two-cell supplementary clusters, placeholders otherwise.
//   T2 (positively identified Windows Terminal): rows with clusters as VT text, re-anchored with CUP.
// Source text is built from code points so the file stays ASCII.
using System.Text;
using TSharpVision;
using TSharpVision.Drivers;
using TSharpVision.Drivers.Console;
using Xunit;

namespace TSharpVision.Tests.Drivers;

public sealed class Win32ClusterOutputTests
{
    private const ushort Lead = Win32ConsoleDriver.COMMON_LVB_LEADING_BYTE;
    private const ushort Trail = Win32ConsoleDriver.COMMON_LVB_TRAILING_BYTE;
    private static readonly TColorAttr Gray = new(0x07);
    private static readonly TColorAttr Blue = new(0x1F);
    private static readonly TColorAttr Green = new(0x2E);

    private static string S(params int[] scalars)
    {
        var sb = new StringBuilder();
        foreach (int s in scalars) sb.Append(char.ConvertFromUtf32(s));
        return sb.ToString();
    }

    private static readonly string Grin = S(0x1F600);
    private static readonly string ThumbTone = S(0x1F44D, 0x1F3FD);
    private static readonly string FlagCz = S(0x1F1E8, 0x1F1FF);
    private static readonly string Family = S(0x1F468, 0x200D, 0x1F469, 0x200D, 0x1F467);
    private static readonly string EAcute = S('e', 0x0301);
    private static readonly string HeartEmoji = S(0x2764, 0xFE0F);
    private static readonly string MathBoldA = S(0x1D400);
    private const char Kan = (char)0x6F22;
    private const char Ji = (char)0x5B57;

    private static TScreenChar[] Cells(TColorAttr attr, params (string Text, int Width)[] clusters)
    {
        var list = new List<TScreenChar>();
        foreach (var (text, width) in clusters)
        {
            list.Add(TScreenChar.CreateCluster(text, width, attr));
            for (int k = 1; k < width; k++) list.Add(TScreenChar.CreateTrail(attr));
        }
        return list.ToArray();
    }

    private static (char Ch, ushort Attr)[] Row(TScreenChar[] cells, int x = 0, int cols = 0, bool legacy = false)
    {
        var line = new Win32ConsoleDriver.CHAR_INFO[cells.Length];
        Win32ConsoleDriver.FillCharInfoRow(cells, line, x, cols, legacy);
        return line.Select(c => (c.UnicodeChar, c.Attributes)).ToArray();
    }

    [Fact]
    public void BmpWideCharactersBecomeFlaggedLeadTrailPairsWithPerHalfAttributes()
    {
        var cells = new[]
        {
            TScreenChar.CreateCluster(Kan.ToString(), 2, new TColorAttr(0x1E)), TScreenChar.CreateTrail(new TColorAttr(0x1E)),
            TScreenChar.CreateCluster(Ji.ToString(), 2, new TColorAttr(0x2F)), TScreenChar.CreateTrail(new TColorAttr(0x4F)),
            new TScreenChar('X', Gray),
        };
        Assert.Equal(new[]
        {
            (Kan, (ushort)(0x1E | Lead)), (Kan, (ushort)(0x1E | Trail)),
            (Ji, (ushort)(0x2F | Lead)), (Ji, (ushort)(0x4F | Trail)),
            ('X', (ushort)0x07),
        }, Row(cells));
    }

    [Fact]
    public void TwoCellSupplementaryClustersBecomeUnflaggedSurrogateHalvesOfTheirFirstScalar()
    {
        var cells = Cells(Gray, (Grin, 2), (ThumbTone, 2), (FlagCz, 2), (Family, 2));
        var row = Row(cells);
        Assert.Equal(new[]
        {
            ((char)0xD83D, (ushort)7), ((char)0xDE00, (ushort)7),
            ((char)0xD83D, (ushort)7), ((char)0xDC4D, (ushort)7),
            ((char)0xD83C, (ushort)7), ((char)0xDDE8, (ushort)7),
            ((char)0xD83D, (ushort)7), ((char)0xDC68, (ushort)7),
        }, row);
    }

    [Fact]
    public void OtherClustersDrawTheirBaseOrPrecomposedCharacterAndBlanks()
    {
        var cells = Cells(Blue, (EAcute, 1), (HeartEmoji, 2), (MathBoldA, 1), ("z", 1));
        Assert.Equal(new[]
        {
            ((char)0x00E9, (ushort)0x1F),            // e + U+0301 precomposes to U+00E9
            ((char)0x2764, (ushort)0x1F), (' ', (ushort)0x1F), // narrow base: no lead/trail flags
            ((char)0xFFFD, (ushort)0x1F),            // a one-cell supplementary character cannot be shown
            ('z', (ushort)0x1F),
        }, Row(cells));
    }

    [Fact]
    public void ClustersThatDoNotFitAndOrphanTrailsAreBlank()
    {
        var cells = Cells(Green, ("a", 1), (Kan.ToString(), 2), (Grin, 2));
        // Screen of 4 columns, slice at column 0: the emoji would need columns 4 and 5.
        Assert.Equal(new[]
        {
            ('a', (ushort)0x2E), (Kan, (ushort)(0x2E | Lead)), (Kan, (ushort)(0x2E | Trail)),
            (' ', (ushort)0x2E), (' ', (ushort)0x2E),
        }, Row(cells, cols: 4));
        // Trail cut off at the slice end, orphan trail at the slice start.
        Assert.Equal(new[] { ('a', (ushort)0x2E), (' ', (ushort)0x2E) }, Row(cells[..2]));
        Assert.Equal(new[] { (' ', (ushort)0x2E), ((char)0xD83D, (ushort)0x2E), ((char)0xDE00, (ushort)0x2E) }, Row(cells[2..5]));
    }

    [Fact]
    public void LegacyProfileWritesEveryCellsCharacterAsBefore()
    {
        var cells = Cells(Gray, ("A", 1), (Grin, 2), (Kan.ToString(), 2), ("\0", 1));
        Assert.Equal(new[]
        {
            ('A', (ushort)7), ((char)0xFFFD, (ushort)7), (' ', (ushort)7), (Kan, (ushort)7), (' ', (ushort)7),
            (' ', (ushort)7),
        }, Row(cells, legacy: true));
    }

    [Fact]
    public void SingleCellsAreUnchangedInBothProfiles()
    {
        string text = "Hello " + S(0x10D, 0x2500, 0x2554, 0x2591, 0xB1) + "\x01\0" + Kan;
        var cells = text.Select((ch, i) => new TScreenChar(ch, new TColorAttr((byte)i))).ToArray();
        Assert.Equal(Row(cells, legacy: true), Row(cells, legacy: false));
        Assert.Equal(' ', Row(cells)[text.IndexOf('\0')].Ch);
    }

    [Fact]
    public void RandomClusterRowsNeverFlagSurrogatesOrSplitClusters()
    {
        string[] pool = { "a", Grin, ThumbTone, FlagCz, Family, EAcute, Kan.ToString(), HeartEmoji, MathBoldA };
        int[] widths = { 1, 2, 2, 2, 2, 1, 2, 2, 1 };
        var random = new Random(4242);
        for (int round = 0; round < 1000; round++)
        {
            var list = new List<TScreenChar>();
            int n = random.Next(1, 40);
            while (list.Count < n)
            {
                int pick = random.Next(pool.Length);
                var attr = new TColorAttr((byte)random.Next(256));
                list.Add(TScreenChar.CreateCluster(pool[pick], widths[pick], attr));
                for (int k = 1; k < widths[pick]; k++) list.Add(TScreenChar.CreateTrail(attr));
            }
            int start = random.Next(0, Math.Min(2, list.Count));
            var cells = list.Skip(start).ToArray();
            int cols = random.Next(1, cells.Length + 3);
            int x = random.Next(0, cols);
            var row = Row(cells, x, cols);
            for (int i = 0; i < row.Length; i++)
            {
                var (ch, attr) = row[i];
                if (char.IsSurrogate(ch)) Assert.Equal(0, attr & (Lead | Trail));
                if (char.IsHighSurrogate(ch))
                {
                    Assert.True(i + 1 < row.Length && char.IsLowSurrogate(row[i + 1].Ch), $"round {round} cell {i}");
                    Assert.True(x + i + 2 <= cols);
                }
                if ((attr & Lead) != 0)
                {
                    Assert.True(i + 1 < row.Length && (row[i + 1].Attr & Trail) != 0 && row[i + 1].Ch == ch);
                    Assert.True(x + i + 2 <= cols);
                }
                if ((attr & Trail) != 0) Assert.True(i > 0 && (row[i - 1].Attr & Lead) != 0);
                // Each cell keeps its own colour byte.
                Assert.Equal((byte)cells[i].Attr, (byte)attr);
            }
        }
    }

    [Fact]
    public void OnlyRowsWithNonBmpOrMultiUnitClustersNeedVtOutput()
    {
        Assert.False(Win32ConsoleDriver.NeedsVtOutput(Cells(Gray, ("a", 1), (Kan.ToString(), 2))));
        Assert.True(Win32ConsoleDriver.NeedsVtOutput(Cells(Gray, ("a", 1), (Grin, 2))));
        Assert.True(Win32ConsoleDriver.NeedsVtOutput(Cells(Gray, (EAcute, 1))));
        Assert.True(Win32ConsoleDriver.NeedsVtOutput(Cells(Gray, (HeartEmoji, 2))));
    }

    [Fact]
    public void VtRowUsesConsoleColoursAndReanchorsAfterClusters()
    {
        var cells = new[]
        {
            new TScreenChar('A', Gray), TScreenChar.CreateCluster(Family, 2, Blue), TScreenChar.CreateTrail(Blue),
            new TScreenChar('B', Blue),
        };
        var sb = new StringBuilder();
        Win32ConsoleDriver.AppendVtRow(sb, 3, 1, cells, 80, 0x07);
        Assert.Equal("\x1b[2;4H\x1b[0;39;49mA\x1b[0;97;44m\x1b[2X" + Family + "\x1b[2;7HB", sb.ToString());
    }

    [Theory]
    [InlineData(0x07, 0x07, "\x1b[0;39;49m")]
    [InlineData(0x1F, 0x07, "\x1b[0;97;44m")]
    [InlineData(0x4E, 0x07, "\x1b[0;93;41m")]
    [InlineData(0x70, 0x07, "\x1b[0;30;47m")]
    [InlineData(0x8C, 0x07, "\x1b[0;91;100m")]
    [InlineData(0x07, 0x1F, "\x1b[0;37;40m")]
    [InlineData(0x13, 0x1F, "\x1b[0;36;49m")]
    public void ConsoleAttributesMapToSgr(int attribute, int defaults, string expected)
        => Assert.Equal(expected, Win32ConsoleDriver.AttributeToSgr((byte)attribute, (ushort)defaults));

    [Theory]
    [InlineData("ConsoleWindowClass", null, null, null, "Cells")]
    [InlineData("PseudoConsoleWindow", "CASCADIA_HOSTING_WINDOW_CLASS", null, null, "Vt")]
    [InlineData("PseudoConsoleWindow", null, @"C:\Program Files\WindowsApps\Microsoft.WindowsTerminal_1.24\WindowsTerminal.exe", null, "Vt")]
    [InlineData("PseudoConsoleWindow", null, @"C:/Program Files/WindowsApps/WindowsTerminal.exe", null, "Vt")]
    [InlineData("PseudoConsoleWindow", null, @"C:\Program Files/WindowsApps\windowsterminal.EXE", null, "Vt")]
    [InlineData("PseudoConsoleWindow", null, @"\\server\share\WindowsTerminal.exe", null, "Vt")]
    [InlineData("PseudoConsoleWindow", null, @"\\?\C:\WindowsApps\WindowsTerminal.exe", null, "Vt")]
    [InlineData("PseudoConsoleWindow", null, "WindowsTerminal.exe", null, "Vt")]
    [InlineData("PseudoConsoleWindow", null, "", null, "Cells")]
    [InlineData("PseudoConsoleWindow", null, @"C:\WindowsTerminal.exe\conhost.exe", null, "Cells")]
    [InlineData("PseudoConsoleWindow", null, @"C:\WindowsTerminal.exe\", null, "Cells")]
    [InlineData("PseudoConsoleWindow", null, @"C:\NotWindowsTerminal.exe", null, "Cells")]
    [InlineData("PseudoConsoleWindow", null, @"C:\WindowsTerminal.exe.bak", null, "Cells")]
    [InlineData("ConsoleWindowClass", null, @"C:\WindowsTerminal.exe", null, "Cells")]
    [InlineData("PseudoConsoleWindow", null, @"C:\WindowsTerminal.exe", "CELLS", "Cells")]
    [InlineData(null, null, null, "VT", "Vt")]
    [InlineData("PseudoConsoleWindow", null, null, null, "Cells")]
    [InlineData("PseudoConsoleWindow", "Chrome_WidgetWin_1", @"C:\Program Files\Microsoft VS Code\Code.exe", null, "Cells")]
    [InlineData(null, null, null, null, "Cells")]
    [InlineData("ConsoleWindowClass", "CASCADIA_HOSTING_WINDOW_CLASS", null, null, "Cells")]
    [InlineData("ConsoleWindowClass", null, null, "vt", "Vt")]
    [InlineData("PseudoConsoleWindow", "CASCADIA_HOSTING_WINDOW_CLASS", null, "cells", "Cells")]
    [InlineData("PseudoConsoleWindow", "CASCADIA_HOSTING_WINDOW_CLASS", null, "bogus", "Vt")]
    public void HostDetectionIdentifiesWindowsTerminalOnlyPositively(string? consoleClass, string? ownerClass,
        string? ownerProcess, string? overrideValue, string expected)
        => Assert.Equal(expected, Win32ConsoleDriver.SelectTextTier(consoleClass, ownerClass, ownerProcess, overrideValue).ToString());

    [Fact]
    public void TextCapabilitiesFollowTheTier()
    {
        using var driver = new Win32ConsoleDriver();
        Assert.Equal(TextCapabilities.None, ((IDriver)driver).TextCapabilities);
        driver.AttachOutputForTesting(IntPtr.Zero, 80, 25);
        try
        {
            Assert.Equal(TextCapabilities.WideCells, ((IDriver)driver).TextCapabilities);
            driver.SetTextTierForTesting(Win32TextTier.Vt);
            Assert.Equal(TextCapabilities.WideCells | TextCapabilities.ClusterText | TextCapabilities.ColorGlyphs,
                ((IDriver)driver).TextCapabilities);
        }
        finally
        {
            driver.SetTextTierForTesting(Win32TextTier.Cells);
            driver.DetachOutputForTesting();
        }
    }
}
