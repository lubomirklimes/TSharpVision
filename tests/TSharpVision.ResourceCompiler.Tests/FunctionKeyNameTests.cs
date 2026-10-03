using System.Globalization;
using System.Reflection;
using TSharpVision.Constants;
using TSharpVision.ResourceCompiler;
using Xunit;

namespace TSharpVision.Tests.ResourceCompiler;

/// <summary>
/// KEYBOARD-CLOSURE N6: the .trc key-name table covers the whole function-key model systematically — F1–F12 bare and
/// with Shift, Ctrl and Alt — and resolves each name to the named constant, so no name carries a stale code (F11 was
/// once Shift+F4's 0x5700).
/// </summary>
public sealed class FunctionKeyNameTests
{
    public static TheoryData<string, string> FunctionKeyNames()
    {
        var data = new TheoryData<string, string>();
        (string Prefix, string Family)[] families = [("", "kbF"), ("Shift+", "kbShiftF"), ("Ctrl+", "kbCtrlF"), ("Alt+", "kbAltF")];
        foreach ((string prefix, string family) in families)
            for (int n = 1; n <= 12; n++)
            {
                string number = n.ToString(CultureInfo.InvariantCulture);
                data.Add(prefix + "F" + number, family + number);
            }
        return data;
    }

    [Theory]
    [MemberData(nameof(FunctionKeyNames))]
    public void EveryFunctionKeyNameResolvesToItsConstant(string name, string constant)
    {
        ushort expected = (ushort)typeof(Keys).GetField(constant, BindingFlags.Public | BindingFlags.Static)!.GetRawConstantValue()!;
        Assert.True(CommandIds.TryResolveKey(name, out ushort code), $"'{name}' is not a key name");
        Assert.Equal(expected, code);
    }

    [Theory]
    [InlineData("F11", Keys.kbF11)]
    [InlineData("F12", Keys.kbF12)]
    [InlineData("Shift+F11", Keys.kbShiftF11)]
    [InlineData("Shift+F12", Keys.kbShiftF12)]
    [InlineData("Ctrl+F11", Keys.kbCtrlF11)]
    [InlineData("Ctrl+F12", Keys.kbCtrlF12)]
    [InlineData("Alt+F11", Keys.kbAltF11)]
    [InlineData("Alt+F12", Keys.kbAltF12)]
    [InlineData("shift+f11", Keys.kbShiftF11)]
    public void ModifiedF11AndF12Resolve(string name, ushort expected)
    {
        Assert.True(CommandIds.TryResolveKey(name, out ushort code));
        Assert.Equal(expected, code);
    }

    /// <summary>No two names share a code, so F11 can never resolve to Shift+F4's code (or F12 to Shift+F5's).</summary>
    [Fact]
    public void NoTwoKeyNamesShareACode()
    {
        var clashes = CommandIds.KeyNames
            .Select(name => (Name: name, Resolved: CommandIds.TryResolveKey(name, out ushort code), Code: code))
            .GroupBy(entry => entry.Code)
            .Where(group => group.Count() > 1)
            .Select(group => $"0x{group.Key:X4}: {string.Join(", ", group.Select(entry => entry.Name))}")
            .ToArray();
        Assert.True(clashes.Length == 0, string.Join("; ", clashes));

        CommandIds.TryResolveKey("F11", out ushort f11);
        CommandIds.TryResolveKey("Shift+F4", out ushort shiftF4);
        Assert.Equal(0x8500, f11);
        Assert.Equal(0x5700, shiftF4);
    }
}
