using System.Reflection;
using TSharpVision.Drivers.Terminal;
using Xunit;

namespace TSharpVision.Drivers.Terminal.Tests;

public sealed class PublicSurfaceCleanupTests
{
    [Fact]
    public void DecoderAndFormattingHelpersAreInternal()
    {
        Assert.False(typeof(AnsiKeyDecoder).IsPublic);
        Assert.False(typeof(AnsiMouseDecoder).IsPublic);
        MethodInfo method = typeof(AnsiTerminalDriver).GetMethod(
            "AttrToSgr", BindingFlags.Static | BindingFlags.NonPublic)!;
        Assert.True(method.IsAssembly);
    }

    [Fact]
    public void AnsiKeyAndModifierPayloadsAlwaysExposeNonNullText()
    {
        Assert.Equal(1, AnsiKeyDecoder.TryDecode([(byte)'a'], out TEvent key, out _));
        Assert.Equal("a", key.keyDown.text);

        var protocol = new KittyKeyboardDecoder();
        byte[] modifier = System.Text.Encoding.ASCII.GetBytes("\u001b[57441;1:1u");
        protocol.TryDecode(modifier, out TEvent changed, out _);
        Assert.NotNull(changed.keyDown.text);
        Assert.Same(string.Empty, changed.keyDown.text);
    }
}
