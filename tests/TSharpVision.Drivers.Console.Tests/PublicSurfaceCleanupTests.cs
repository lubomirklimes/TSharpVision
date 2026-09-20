using System.Reflection;
using TSharpVision.Drivers.Console;
using Xunit;

namespace TSharpVision.Drivers.Console.Tests;

public sealed class PublicSurfaceCleanupTests
{
    [Fact]
    public void TranslationHelpersAreInternal()
    {
        Assert.False(typeof(Win32KeyTranslator).IsPublic);
        const BindingFlags allStatic = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        Assert.True(typeof(Win32ConsoleDriver).GetProperty("CharInfoMarshaledSize", allStatic)!.GetMethod!.IsAssembly);
        Assert.DoesNotContain(typeof(Win32ConsoleDriver).GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static),
            method => method.Name is "TryTranslateKey" or "TranslateMouse");
    }

    [Fact]
    public void Win32TranslatedKeysAlwaysExposeNonNullText()
    {
        Assert.True(Win32KeyTranslator.TryTranslate(true, (ushort)'A', 'a', 0, out TEvent ev));
        Assert.NotNull(ev.keyDown.text);
        Assert.Same(string.Empty, new TEvent().keyDown.text);
    }
}
