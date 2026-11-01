using TSharpVision.Constants;
using TSharpVision.Tests.Infrastructure;
using Xunit;

namespace TSharpVision.Tests.Samples;

/// <summary>
/// TV12g: the composed attributes TVDemo actually draws for accelerators next to normal text, with the logical
/// Color palette. The SDL GPU Amber profile is tuned for exactly these pairs
/// (TSharpVision.Drivers.SDL.Tests: AmberAcceleratorContrastTests); a change here must be reflected there.
/// </summary>
[Collection("NonParallel")]
public sealed class TVDemoAcceleratorAttributeTests
{
    private static ScreenBuffer Screen(DemoTestScope scope)
    {
        scope.App.Redraw();
        return Assert.IsType<ScreenBuffer>(scope.App.buffer);
    }

    private static (char Ch, byte Attr)[] Row(ScreenBuffer buf, int y) =>
        buf.Data.Slice(y * TScreen.ScreenWidth, TScreen.ScreenWidth).ToArray()
            .Select(c => (c.Character, (byte)c.Attr)).ToArray();

    [Fact]
    public void MenuBarAcceleratorsAreRedOnLightGrayNextToBlackText()
    {
        using var scope = new DemoTestScope();
        var row = Row(Screen(scope), 0);
        string text = new(row.Select(c => c.Ch).ToArray());

        foreach (string caption in new[] { "File", "Demo", "Diagnostics", "Options", "Window", "Help" })
        {
            int at = text.IndexOf(caption, StringComparison.Ordinal);
            Assert.True(at >= 0, $"'{caption}' not on the menu bar: '{text}'");
            int hot = caption == "Diagnostics" ? at + 1 : at; // D~i~agnostics
            Assert.Equal(0x74, row[hot].Attr);                 // accelerator: red (4) on light gray (7)
            int plain = caption == "Diagnostics" ? at : at + 1;
            Assert.Equal(0x70, row[plain].Attr);               // normal text: black (0) on light gray (7)
        }
    }

    [Fact]
    public void StatusLineAcceleratorsUseTheSameMenuPair()
    {
        using var scope = new DemoTestScope();
        var row = Row(Screen(scope), TScreen.ScreenHeight - 1);
        string text = new(row.Select(c => c.Ch).ToArray());
        int f10 = text.IndexOf("F10 Menu", StringComparison.Ordinal);
        Assert.True(f10 >= 0, text);
        Assert.Equal(0x74, row[f10].Attr);       // "F10" is the highlighted key text
        Assert.Equal(0x70, row[f10 + 4].Attr);   // "Menu"
    }

    [Fact]
    public void DialogButtonAndLabelAcceleratorsUseYellow()
    {
        // Inside the real TVDemo application so the colours resolve through the gray dialog and Color app palettes.
        using var scope = new DemoTestScope();
        var dialog = new TDialog(new TRect(0, 0, 40, 10), "Test");
        var input = new TInputLine(new TRect(12, 2, 30, 3), 20);
        dialog.Insert(input);
        dialog.Insert(new TLabel(new TRect(2, 2, 12, 3), "~N~ame", input));
        dialog.Insert(new TButton(new TRect(2, 5, 14, 7), "~O~K", Views.cmOK, ButtonConstants.bfNormal));
        scope.App.DeskTop!.Insert(dialog);
        TScreenChar[] data = Screen(scope).Data.ToArray();
        int width = TScreen.ScreenWidth;
        int top = dialog.origin.y + 1, left = dialog.origin.x; // desktop starts below the menu bar

        int Find(int y, string s)
        {
            string line = new(data.Skip((top + y) * width).Take(width).Select(c => c.Character).ToArray());
            int i = line.IndexOf(s, left, StringComparison.Ordinal);
            Assert.True(i >= 0, $"'{s}' not in dialog row {y}: '{line}'");
            return (top + y) * width + i;
        }

        int label = Find(2, "Name");
        Assert.Equal(0x7E, (byte)data[label].Attr);     // label shortcut: yellow (14) on light gray
        Assert.Equal(0x70, (byte)data[label + 1].Attr); // label text: black on light gray
        int ok = Find(5, "OK");
        byte shortcut = (byte)data[ok].Attr, caption = (byte)data[ok + 1].Attr;
        Assert.Equal(0x2, shortcut >> 4);   // button face: green (2)
        Assert.Equal(0xE, shortcut & 0x0F); // button shortcut: yellow (14)
        Assert.Equal(0x2, caption >> 4);
        Assert.Contains(caption & 0x0F, new[] { 0x0, 0xF });   // caption: black (normal) or white (focused)
    }
}
