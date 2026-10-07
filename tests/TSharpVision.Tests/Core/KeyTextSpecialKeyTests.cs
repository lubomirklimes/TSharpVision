using System.Reflection;
using TSharpVision.Constants;
using Xunit;

namespace TSharpVision.Tests.Core;

/// <summary>
/// A named special key is never text. kbCtrlShiftIns (0x01CD) and kbCtrlShiftDel (0x01CE) carry a low byte in the
/// printable range, and used to type 'Í' and 'Î' into every input that reads <see cref="KeyText.PrintableText"/>.
/// </summary>
public sealed class KeyTextSpecialKeyTests
{
    private static KeyDownEvent Key(ushort code, string? text = null)
        => new() { keyCode = code, charScan = new CharScanType(code), text = text ?? string.Empty };

    private static IEnumerable<(string Name, ushort Code)> NamedKeyCodes()
        => typeof(Keys).GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(field => field.IsLiteral && field.FieldType == typeof(ushort))
            .Select(field => (field.Name, (ushort)field.GetRawConstantValue()!));

    [Theory]
    [InlineData(Keys.kbCtrlShiftIns)]
    [InlineData(Keys.kbCtrlShiftDel)]
    public void CtrlShiftInsAndDelAreNotText(ushort code)
    {
        Assert.Equal(string.Empty, KeyText.PrintableText(Key(code)));
        Assert.Equal(string.Empty, KeyText.PrintableText(Key(code), includeTab: true));
        Assert.Equal(string.Empty, KeyText.PrintableText(Key(code), extendedLegacy: false));
        Assert.True(KeyText.IsNamedSpecialKey(code));
    }

    /// <summary>
    /// Structural: of all named key codes, only Space and the keypad's Gray +/- — whose low byte is the character the
    /// key types — may yield text from their code alone. A new constant with a printable low byte fails here until it
    /// is classified.
    /// </summary>
    [Fact]
    public void NoNamedKeyCodeYieldsTextExceptTheCharacterKeys()
    {
        var characterKeys = new HashSet<ushort> { Keys.kbSpace, Keys.kbGrayMinus, Keys.kbGrayPlus };
        string[] offenders = NamedKeyCodes()
            .Where(key => !characterKeys.Contains(key.Code))
            .Where(key => KeyText.PrintableText(Key(key.Code)).Length > 0)
            .Select(key => $"{key.Name}=0x{key.Code:X4}")
            .ToArray();

        Assert.Empty(offenders);
    }

    [Fact]
    public void OrdinaryLegacyCharactersAreStillText()
    {
        Assert.Equal("a", KeyText.PrintableText(Key(0x1E61)));
        Assert.Equal("Í", KeyText.PrintableText(Key(0x00CD)));
        Assert.Equal("+", KeyText.PrintableText(Key(Keys.kbGrayPlus)));
    }

    [Fact]
    public void ExplicitEventTextStillWins()
        => Assert.Equal("x", KeyText.PrintableText(Key(Keys.kbCtrlShiftIns, "x")));
}
