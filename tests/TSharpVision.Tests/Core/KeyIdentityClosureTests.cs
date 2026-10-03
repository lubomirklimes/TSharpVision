using System.Reflection;
using TSharpVision.Constants;
using Xunit;

namespace TSharpVision.Tests.Core;

/// <summary>
/// KEYBOARD-CLOSURE: the named key codes, classified. A named key code is one of
/// <list type="bullet">
///   <item><description>a C0 control identity — kbCtrlA…kbCtrlZ are the control characters 0x01…0x1A;</description></item>
///   <item><description>the printable identity of Space — kbSpace is ' ' (0x20), the code every driver reports for the
///   Space bar, and the only named key in the character range;</description></item>
///   <item><description>an extended identity — a BIOS scan code in the high byte (0x0100 and above), or one of the two
///   documented extensions kbCtrlShiftIns / kbCtrlShiftDel.</description></item>
/// </list>
/// No named key code may equal the code of a different printable character: kbSpace was 0x0034, the code of a typed
/// '4', which no driver ever produced for Space.
/// </summary>
public sealed class KeyIdentityClosureTests
{
    private static IEnumerable<(string Name, ushort Code)> NamedKeyCodes()
        => typeof(Keys).GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(field => field.IsLiteral && field.FieldType == typeof(ushort))
            .Select(field => (field.Name, (ushort)field.GetRawConstantValue()!));

    [Fact]
    public void SpaceIsTheSpaceCharacter()
    {
        Assert.Equal(0x0020, Keys.kbSpace);
        Assert.Equal((ushort)' ', Keys.kbSpace);
        Assert.NotEqual((ushort)'4', Keys.kbSpace);
    }

    [Fact]
    public void EveryNamedKeyCodeHasAClass()
    {
        var unclassified = new List<string>();
        foreach ((string name, ushort code) in NamedKeyCodes())
        {
            bool noKey = name == nameof(Keys.kbNoKey) && code == 0;
            bool control = name.StartsWith("kbCtrl", StringComparison.Ordinal) && name.Length == 7
                && code == name[6] - 'A' + 1;
            bool space = name == nameof(Keys.kbSpace) && code == ' ';
            bool extended = code >= 0x0100;
            if (!(noKey || control || space || extended)) unclassified.Add($"{name}=0x{code:X4}");
        }

        Assert.True(unclassified.Count == 0, "Named key codes in the character range: " + string.Join(", ", unclassified));
    }

    [Fact]
    public void NoNamedKeyCodeIsADifferentPrintableCharacter()
    {
        var clashes = NamedKeyCodes()
            .Where(key => key.Code is >= 0x21 and <= 0xFF)
            .Select(key => $"{key.Name}=0x{key.Code:X4} ('{(char)key.Code}')")
            .ToArray();

        Assert.True(clashes.Length == 0, "Named keys equal to a printable character: " + string.Join(", ", clashes));
    }

    /// <summary>The keyboard-state masks are uint and are not key codes; left/right masks alias on purpose.</summary>
    [Fact]
    public void ModifierMasksAreNotKeyCodesAndTheirAliasesAreIntentional()
    {
        FieldInfo[] masks = typeof(Keys).GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(field => field.IsLiteral && field.FieldType == typeof(uint))
            .ToArray();
        Assert.NotEmpty(masks);

        var aliases = masks.GroupBy(field => (uint)field.GetRawConstantValue()!)
            .Where(group => group.Count() > 1)
            .Select(group => string.Join("=", group.Select(field => field.Name).OrderBy(n => n, StringComparer.Ordinal)))
            .OrderBy(s => s, StringComparer.Ordinal)
            .ToArray();
        Assert.Equal(["kbAltShift=kbLeftAlt=kbRightAlt", "kbCtrlShift=kbLeftCtrl=kbRightCtrl"], aliases);
    }
}
