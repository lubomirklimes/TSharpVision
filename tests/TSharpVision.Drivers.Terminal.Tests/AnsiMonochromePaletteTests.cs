using System.Text.RegularExpressions;
using TSharpVision;
using TSharpVision.Drivers.Terminal;
using Xunit;

namespace TSharpVision.Tests.Drivers;

// TV12: the ANSI driver emits explicit 24-bit SGR colors, so BlackWhite and Monochrome attributes are neutral grays
// whatever the terminal's own color theme is.
public sealed class AnsiMonochromePaletteTests
{
    public static IEnumerable<object[]> NeutralAttributes() =>
        (TProgram.cpBlackWhite + TProgram.cpMonochrome + "\x08")
            .Where(c => c != '\0').Select(c => (byte)c).Distinct().Select(a => new object[] { a });

    [Theory]
    [MemberData(nameof(NeutralAttributes))]
    public void BlackWhiteAndMonochromeAttributesEmitGrayTrueColor(byte attr)
    {
        string sgr = AnsiTerminalDriver.AttrToSgr(new TColorAttr(attr));
        var match = Regex.Match(sgr, @"^\x1b\[0;38;2;(\d+);(\d+);(\d+);48;2;(\d+);(\d+);(\d+)m$");
        Assert.True(match.Success, sgr);
        int[] v = Enumerable.Range(1, 6).Select(i => int.Parse(match.Groups[i].Value)).ToArray();
        Assert.True(v[0] == v[1] && v[1] == v[2], $"{attr:X2} foreground {sgr}");
        Assert.True(v[3] == v[4] && v[4] == v[5], $"{attr:X2} background {sgr}");
        Assert.NotEqual(v[0], v[3]);
    }
}
