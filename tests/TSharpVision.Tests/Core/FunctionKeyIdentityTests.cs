using System.Globalization;
using System.Reflection;
using TSharpVision.Constants;
using Xunit;

namespace TSharpVision.Tests.Core;

/// <summary>
/// Every physical function key and modifier combination has a key code of its own. F11 and F12 use the BIOS extended
/// codes 0x85 / 0x86 (as tvision does), the family that Shift/Ctrl/Alt+F11/F12 (0x87…0x8C) already continue; the
/// codes 0x57 / 0x58 are Shift+F4 / Shift+F5 and nothing else.
/// </summary>
public sealed class FunctionKeyIdentityTests
{
    private static readonly string[] Families = ["kbF", "kbShiftF", "kbCtrlF", "kbAltF"];

    private static ushort Code(string name)
        => (ushort)typeof(Keys).GetField(name, BindingFlags.Public | BindingFlags.Static)!.GetRawConstantValue()!;

    [Fact]
    public void F11AndF12HaveTheirCanonicalCodes()
    {
        Assert.Equal(0x8500, Keys.kbF11);
        Assert.Equal(0x8600, Keys.kbF12);
        Assert.Equal(0x5700, Keys.kbShiftF4);
        Assert.Equal(0x5800, Keys.kbShiftF5);
        Assert.NotEqual(Keys.kbShiftF4, Keys.kbF11);
        Assert.NotEqual(Keys.kbShiftF5, Keys.kbF12);
    }

    [Fact]
    public void ModifiedF11AndF12ContinueTheSameFamily()
    {
        Assert.Equal(0x8700, Keys.kbShiftF11);
        Assert.Equal(0x8800, Keys.kbShiftF12);
        Assert.Equal(0x8900, Keys.kbCtrlF11);
        Assert.Equal(0x8A00, Keys.kbCtrlF12);
        Assert.Equal(0x8B00, Keys.kbAltF11);
        Assert.Equal(0x8C00, Keys.kbAltF12);
    }

    [Fact]
    public void TheFortyEightFunctionKeyIdentitiesAreDistinct()
    {
        var seen = new Dictionary<ushort, string>();
        foreach (string family in Families)
            for (int n = 1; n <= 12; n++)
            {
                string name = family + n.ToString(CultureInfo.InvariantCulture);
                ushort code = Code(name);
                Assert.False(seen.TryGetValue(code, out string? other), $"{name} and {other} share 0x{code:X4}");
                seen.Add(code, name);
            }
        Assert.Equal(48, seen.Count);
    }

    /// <summary>
    /// No two named key codes share a value: an alias between two different keys is always an accident. (The
    /// keyboard-state masks are uint and are not key codes; left/right masks alias on purpose.)
    /// </summary>
    [Fact]
    public void NoTwoNamedKeyCodesShareAValue()
    {
        var duplicates = typeof(Keys).GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(field => field.IsLiteral && field.FieldType == typeof(ushort))
            .GroupBy(field => (ushort)field.GetRawConstantValue()!)
            .Where(group => group.Count() > 1)
            .Select(group => $"0x{group.Key:X4}: {string.Join(", ", group.Select(field => field.Name))}")
            .ToArray();

        Assert.True(duplicates.Length == 0, "Shared key codes: " + string.Join("; ", duplicates));
    }
}
