using System.Text;
using TSharpVision.Diagnostics.SdlGlyphs;
using TSharpVision.Drivers.SDL.Rendering.GlyphComposition;

namespace TSharpVision.Drivers.SDL.Tests.Rendering.GlyphComposition;

public class Cp437GraphicsCharactersTests
{
    [Fact]
    public void Table_CoversExactly48CodesInOrder()
    {
        Assert.Equal(48, Cp437GraphicsCharacters.Count);
        Assert.Equal(48, Cp437GraphicsCharacters.All.Count);

        for (int i = 0; i < Cp437GraphicsCharacters.All.Count; i++)
            Assert.Equal(0xB0 + i, Cp437GraphicsCharacters.All[i].Code);
    }

    [Fact]
    public void Table_HasNoDuplicateCharacters()
    {
        int distinct = Cp437GraphicsCharacters.AllCharacters.Distinct().Count();

        Assert.Equal(Cp437GraphicsCharacters.Count, distinct);
    }

    /// <summary>
    /// Independent verification: the Unicode character recorded for every CP437 code must match
    /// what the platform's own code page 437 decoder produces.
    /// </summary>
    [Fact]
    public void Table_MatchesTheSystemCodePage437Mapping()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        Encoding cp437 = Encoding.GetEncoding(437);

        foreach (Cp437GraphicsCharacter entry in Cp437GraphicsCharacters.All)
        {
            string decoded = cp437.GetString([entry.Code]);

            Assert.Equal(1, decoded.Length);
            Assert.Equal(decoded[0], entry.Character);
        }
    }

    [Fact]
    public void Categories_SplitAsThreeShadingFortyBoxAndFiveBlocks()
    {
        var byCategory = Cp437GraphicsCharacters.All
            .GroupBy(static e => e.Category)
            .ToDictionary(static g => g.Key, static g => g.Count());

        Assert.Equal(3,  byCategory[Cp437GraphicsCategory.Shading]);
        Assert.Equal(40, byCategory[Cp437GraphicsCategory.BoxDrawing]);
        Assert.Equal(5,  byCategory[Cp437GraphicsCategory.Block]);
    }

    [Theory]
    [InlineData(0xB0, '░')]
    [InlineData(0xB1, '▒')]
    [InlineData(0xB2, '▓')]
    [InlineData(0xDB, '█')]
    [InlineData(0xDC, '▄')]
    [InlineData(0xDD, '▌')]
    [InlineData(0xDE, '▐')]
    [InlineData(0xDF, '▀')]
    public void ShadingAndBlockCodes_MapToTheDocumentedCharacters(int code, char expected)
    {
        Assert.Equal(expected, Cp437GraphicsCharacters.FromCode((byte)code).Character);
    }

    [Fact]
    public void EverySideStylePair_IsConsistent()
    {
        foreach (Cp437GraphicsCharacter entry in Cp437GraphicsCharacters.All)
        {
            if (entry.Category != Cp437GraphicsCategory.BoxDrawing) continue;

            // Throws when Left/Right or Up/Down disagree about line weight.
            entry.Shape.Validate(entry.Character);
        }
    }

    [Fact]
    public void EveryBoxCharacter_ConnectsAtLeastTwoSides()
    {
        foreach (Cp437GraphicsCharacter entry in Cp437GraphicsCharacters.All)
        {
            if (entry.Category != Cp437GraphicsCategory.BoxDrawing) continue;

            int connected =
                (entry.Shape.HasLeft  ? 1 : 0) +
                (entry.Shape.HasRight ? 1 : 0) +
                (entry.Shape.HasUp    ? 1 : 0) +
                (entry.Shape.HasDown  ? 1 : 0);

            Assert.True(connected >= 2, $"'{entry.Character}' connects only {connected} side(s).");
        }
    }

    [Fact]
    public void MixedCharacters_AreExactlyThe18ExpectedCodes()
    {
        byte[] expected =
        [
            0xB5, 0xB6, 0xB7, 0xB8, 0xBD, 0xBE,
            0xC6, 0xC7, 0xCF,
            0xD0, 0xD1, 0xD2, 0xD3, 0xD4, 0xD5, 0xD6, 0xD7, 0xD8,
        ];

        char[] expectedChars = expected
            .Select(static c => Cp437GraphicsCharacters.FromCode(c).Character)
            .ToArray();

        Assert.Equal(expectedChars.OrderBy(static c => c),
                     GeneratedGlyphChecks.MixedBoxCharacters.OrderBy(static c => c));
    }

    [Theory]
    [InlineData(0xC4, BoxSideStyle.Single, BoxSideStyle.Single, BoxSideStyle.None,   BoxSideStyle.None)]    // ─
    [InlineData(0xBA, BoxSideStyle.None,   BoxSideStyle.None,   BoxSideStyle.Double, BoxSideStyle.Double)]  // ║
    [InlineData(0xCE, BoxSideStyle.Double, BoxSideStyle.Double, BoxSideStyle.Double, BoxSideStyle.Double)]  // ╬
    [InlineData(0xD8, BoxSideStyle.Double, BoxSideStyle.Double, BoxSideStyle.Single, BoxSideStyle.Single)]  // ╪
    [InlineData(0xD7, BoxSideStyle.Single, BoxSideStyle.Single, BoxSideStyle.Double, BoxSideStyle.Double)]  // ╫
    [InlineData(0xD5, BoxSideStyle.None,   BoxSideStyle.Double, BoxSideStyle.None,   BoxSideStyle.Single)]  // ╒
    [InlineData(0xD6, BoxSideStyle.None,   BoxSideStyle.Single, BoxSideStyle.None,   BoxSideStyle.Double)]  // ╓
    internal void SpotCheckedShapes_MatchTheUnicodeNames(
        int code, BoxSideStyle left, BoxSideStyle right, BoxSideStyle up, BoxSideStyle down)
    {
        BoxGlyphShape shape = Cp437GraphicsCharacters.FromCode((byte)code).Shape;

        Assert.Equal(new BoxGlyphShape(left, right, up, down), shape);
    }

    [Fact]
    public void TryGetBoxShape_RejectsShadingAndBlocks()
    {
        Assert.False(Cp437GraphicsCharacters.TryGetBoxShape('░', out _));
        Assert.False(Cp437GraphicsCharacters.TryGetBoxShape('█', out _));
        Assert.False(Cp437GraphicsCharacters.TryGetBoxShape('A', out _));
        Assert.True(Cp437GraphicsCharacters.TryGetBoxShape('┼', out _));
    }
}
