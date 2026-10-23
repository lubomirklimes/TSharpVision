// The shipped driver packages' [ScreenDriver] registrations, run through the factory's resolver.
//
// The resolver's rules are pinned with synthetic registrations in TSharpVision.Tests
// (DriverSelectionTests). This project's output holds every driver package, so here the real
// attributes are checked against the 1.0 contract: automatic selection prefers SDLGpuDriver, then
// SDLDriver, then the native text driver, then NullDriver; `sdl` is SDLGpuDriver everywhere and
// `console`/`terminal` are the native text driver on each platform.
using System.Reflection;
using TSharpVision.Drivers.Console;
using TSharpVision.Drivers.SDL;
using TSharpVision.Drivers.SDL.Gpu;
using TSharpVision.Drivers.Terminal;
using Xunit;

namespace TSharpVision.Drivers.Console.Tests;

public sealed class DriverRegistrationContractTests
{
    private static readonly Type[] Drivers =
        [typeof(SDLGpuDriver), typeof(SDLDriver), typeof(Win32ConsoleDriver), typeof(AnsiTerminalDriver), typeof(NullDriver)];

    private static IEnumerable<(Type Type, ScreenDriverAttribute Attribute)> Registrations(params Type[] types) =>
        types.SelectMany(type => type.GetCustomAttributes<ScreenDriverAttribute>().Select(attribute => (type, attribute)));

    private static Type? Resolve(Platform platform, string? requested, params Type[] installed)
    {
        // ResolveDriverType is internal to the core assembly.
        MethodInfo method = typeof(ScreenDriverFactory)
            .GetMethod("ResolveDriverType", BindingFlags.Static | BindingFlags.NonPublic)!;
        return (Type?)method.Invoke(null, [platform, Registrations(installed.Length == 0 ? Drivers : installed), requested, null]);
    }

    private static Type Native(Platform platform) =>
        platform == Platform.Windows ? typeof(Win32ConsoleDriver) : typeof(AnsiTerminalDriver);

    [Theory]
    [InlineData(Platform.Windows)]
    [InlineData(Platform.Linux)]
    [InlineData(Platform.MacOS)]
    public void AutomaticSelectionFollowsTheShippedPriorityOrder(Platform platform)
    {
        Assert.Equal(typeof(SDLGpuDriver), Resolve(platform, null));
        Assert.Equal(typeof(SDLDriver), Resolve(platform, null, typeof(SDLDriver), typeof(Win32ConsoleDriver), typeof(AnsiTerminalDriver), typeof(NullDriver)));
        Assert.Equal(Native(platform), Resolve(platform, null, typeof(Win32ConsoleDriver), typeof(AnsiTerminalDriver), typeof(NullDriver)));
        Assert.Equal(typeof(NullDriver), Resolve(platform, null, typeof(NullDriver)));
    }

    [Theory]
    [InlineData(Platform.Windows)]
    [InlineData(Platform.Linux)]
    [InlineData(Platform.MacOS)]
    public void PortableAliasesSelectTheShippedDrivers(Platform platform)
    {
        Assert.Equal(typeof(SDLGpuDriver), Resolve(platform, "sdl"));
        Assert.Equal(Native(platform), Resolve(platform, "console"));
        Assert.Equal(Native(platform), Resolve(platform, "terminal"));
        Assert.Equal(typeof(SDLDriver), Resolve(platform, "SDLDriver"));
        Assert.Equal(typeof(SDLGpuDriver), Resolve(platform, "SDLGpuDriver"));
        Assert.Equal(typeof(NullDriver), Resolve(platform, "NullDriver"));
        Assert.Equal(typeof(AnsiTerminalDriver), Resolve(platform, "AnsiTerminalDriver"));
    }

    [Theory]
    [InlineData(Platform.Linux)]
    [InlineData(Platform.MacOS)]
    public void Win32ConsoleDriverIsOnlyRegisteredOnWindows(Platform platform)
    {
        Assert.DoesNotContain(typeof(Win32ConsoleDriver).GetCustomAttributes<ScreenDriverAttribute>(), a => a.System == platform);
        // Unknown on this platform: automatic selection, not the ANSI driver as a stand-in.
        Assert.Equal(typeof(SDLGpuDriver), Resolve(platform, "Win32ConsoleDriver"));
    }

    [Fact]
    public void TheAnsiDriverStaysRegisteredOnWindowsBelowTheNativeConsole()
    {
        int ansi = typeof(AnsiTerminalDriver).GetCustomAttributes<ScreenDriverAttribute>().Single(a => a.System == Platform.Windows).Priority;
        int win32 = typeof(Win32ConsoleDriver).GetCustomAttributes<ScreenDriverAttribute>().Single(a => a.System == Platform.Windows).Priority;
        Assert.True(ansi < win32);
    }

    [Fact]
    public void EveryRegistrationUsesItsClassNameAsTheExactDriverName()
    {
        foreach (var (type, attribute) in Registrations(Drivers))
            Assert.Equal(type.Name, attribute.Driver);
    }
}
