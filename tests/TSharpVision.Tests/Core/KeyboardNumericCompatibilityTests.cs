using System.Globalization;
using System.Reflection;
using TSharpVision.Constants;
using Xunit;

namespace TSharpVision.Tests.Core;

public sealed class KeyboardNumericCompatibilityTests
{
    private const string HistoricalKeys = """
kbAlt0 8100
kbAlt1 7800
kbAlt2 7900
kbAlt3 7A00
kbAlt4 7B00
kbAlt5 7C00
kbAlt6 7D00
kbAlt7 7E00
kbAlt8 7F00
kbAlt9 8000
kbAltA 1E00
kbAltB 3000
kbAltBack 0800
kbAltC 2E00
kbAltD 2000
kbAltE 1200
kbAltEqual 8300
kbAltF 2100
kbAltF1 6800
kbAltF10 7100
kbAltF11 8B00
kbAltF12 8C00
kbAltF2 6900
kbAltF3 6A00
kbAltF4 6B00
kbAltF5 6C00
kbAltF6 6D00
kbAltF7 6E00
kbAltF8 6F00
kbAltF9 7000
kbAltG 2200
kbAltH 2300
kbAltI 1700
kbAltJ 2400
kbAltK 2500
kbAltL 2600
kbAltM 3200
kbAltMinus 8200
kbAltN 3100
kbAltO 1800
kbAltP 1900
kbAltQ 1000
kbAltR 1300
kbAltS 1F00
kbAltShift 0008
kbAltSpace 0200
kbAltT 1400
kbAltU 1600
kbAltV 2F00
kbAltW 1100
kbAltX 2D00
kbAltY 1500
kbAltZ 2C00
kbBack 0E08
kbCapsState 0040
kbCtrlA 0001
kbCtrlB 0002
kbCtrlBack 0E7F
kbCtrlC 0003
kbCtrlD 0004
kbCtrlDel 0600
kbCtrlE 0005
kbCtrlEnd 7500
kbCtrlEnter 1C0A
kbCtrlF 0006
kbCtrlF1 5E00
kbCtrlF10 6700
kbCtrlF11 8900
kbCtrlF12 8A00
kbCtrlF2 5F00
kbCtrlF3 6000
kbCtrlF4 6100
kbCtrlF5 6200
kbCtrlF6 6300
kbCtrlF7 6400
kbCtrlF8 6500
kbCtrlF9 6600
kbCtrlG 0007
kbCtrlH 0008
kbCtrlHome 7700
kbCtrlI 0009
kbCtrlIns 0400
kbCtrlJ 000A
kbCtrlK 000B
kbCtrlL 000C
kbCtrlLeft 7300
kbCtrlM 000D
kbCtrlN 000E
kbCtrlO 000F
kbCtrlP 0010
kbCtrlPgDn 7600
kbCtrlPgUp 8400
kbCtrlPrtSc 7200
kbCtrlQ 0011
kbCtrlR 0012
kbCtrlRight 7400
kbCtrlS 0013
kbCtrlShift 0004
kbCtrlT 0014
kbCtrlU 0015
kbCtrlV 0016
kbCtrlW 0017
kbCtrlX 0018
kbCtrlY 0019
kbCtrlZ 001A
kbDel 5300
kbDown 5000
kbEnd 4F00
kbEnter 1C0D
kbEsc 011B
kbF1 3B00
kbF10 4400
kbF11 5700
kbF12 5800
kbF2 3C00
kbF3 3D00
kbF4 3E00
kbF5 3F00
kbF6 4000
kbF7 4100
kbF8 4200
kbF9 4300
kbGrayMinus 4A2D
kbGrayPlus 4E2B
kbHome 4700
kbIns 5200
kbInsState 0080
kbLeft 4B00
kbLeftAlt 0008
kbLeftCtrl 0004
kbLeftShift 0001
kbNoKey 0000
kbNumState 0020
kbPgDn 5100
kbPgUp 4900
kbRight 4D00
kbRightAlt 0008
kbRightCtrl 0004
kbRightShift 0002
kbScrollState 0010
kbShift 0003
kbShiftDel 0700
kbShiftF1 5400
kbShiftF10 5D00
kbShiftF11 8700
kbShiftF12 8800
kbShiftF2 5500
kbShiftF3 5600
kbShiftF4 5700
kbShiftF5 5800
kbShiftF6 5900
kbShiftF7 5A00
kbShiftF8 5B00
kbShiftF9 5C00
kbShiftIns 0500
kbShiftTab 0F00
kbTab 0F09
kbUp 4800
""";

    [Fact]
    public void EveryHistoricalKbConstantMatchesTurboVision20()
    {
        string[] lines = HistoricalKeys.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(158, lines.Length);
        foreach (string line in lines)
        {
            string[] parts = line.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
            FieldInfo field = typeof(Keys).GetField(parts[0], BindingFlags.Public | BindingFlags.Static)!;
            uint actual = Convert.ToUInt32(field.GetRawConstantValue(), CultureInfo.InvariantCulture);
            uint expected = uint.Parse(parts[1], NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            Assert.Equal(expected, actual);
        }
    }

    [Fact]
    public void ExtensionsRemainDistinctAndPublicModifierPayloadIs32Bit()
    {
        HashSet<uint> historical = HistoricalKeys.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(line => uint.Parse(line.Trim().Split(' ')[1], NumberStyles.HexNumber, CultureInfo.InvariantCulture))
            .ToHashSet();
        Assert.DoesNotContain((uint)Keys.kbSpace, historical);
        Assert.DoesNotContain((uint)Keys.kbCtrlShiftIns, historical);
        Assert.DoesNotContain((uint)Keys.kbCtrlShiftDel, historical);
        Assert.Equal(typeof(uint), typeof(KeyDownEvent).GetField(nameof(KeyDownEvent.controlKeyState))!.FieldType);
        Assert.Equal(typeof(uint), typeof(TEvent).GetProperty(nameof(TEvent.Modifiers))!.PropertyType);
    }

    [Fact]
    public void HistoricalEventAndEditorCommandValuesAreExact()
    {
        Assert.Equal(0x000Fu, Events.evMouse);
        Assert.Equal(0x0020u, Events.evMouseWheel);
        Assert.Equal(30u, Views.cmNew);
        Assert.Equal(31u, Views.cmOpen);
        Assert.Equal(32u, Views.cmSave);
        Assert.Equal(33u, Views.cmSaveAs);
        Assert.Equal(34u, Views.cmSaveAll);
        Assert.Equal(35u, Views.cmChDir);
        Assert.Equal(36u, Views.cmDosShell);
        Assert.Equal(37u, Views.cmCloseAll);
    }

    [Fact]
    public void MenuAltLookupDoesNotAssumeHistoricalScanCodesAreContiguous()
    {
        for (char character = 'A'; character <= 'Z'; character++)
        {
            ushort keyCode = (ushort)typeof(Keys).GetField($"kbAlt{character}")!.GetRawConstantValue()!;
            Assert.Equal(character, TMenuView.GetAltChar(keyCode, 0, Keys.kbAltShift));
        }

        for (char character = '0'; character <= '9'; character++)
        {
            ushort keyCode = (ushort)typeof(Keys).GetField($"kbAlt{character}")!.GetRawConstantValue()!;
            Assert.Equal(character, TMenuView.GetAltChar(keyCode, 0, Keys.kbAltShift));
        }
    }
}
