// The 1.0 driver-selection contract, pinned against the resolver itself.
//
// Two kinds of name select a driver. Portable aliases mean the same thing on every OS: `sdl` is the
// GPU SDL driver, `console` and `terminal` are the platform's native text driver (Win32ConsoleDriver
// on Windows, AnsiTerminalDriver on Linux and macOS), so one config file moves between systems.
// Exact driver names are literal and never reinterpreted. Both kinds are trimmed and matched
// case-insensitively, and TSHARPVISION_DRIVER and `[driver] name` go through the same resolver.
//
// Automatic selection takes the highest priority registered for the platform:
// SDLGpuDriver 300, SDLDriver 200, native text 50, NullDriver 0.
//
// The registrations below mirror the real [ScreenDriver] attributes; the driver packages' own
// attributes are checked against this contract in TSharpVision.Drivers.Console.Tests.
using System.Reflection;
using TSharpVision.Config;
using TSharpVision.Drivers;
using Xunit;

namespace TSharpVision.Tests.Core;

public sealed class DriverSelectionTests
{
    private sealed class SDLGpuDriver;
    private sealed class SDLDriver;
    private sealed class Win32ConsoleDriver;
    private sealed class AnsiTerminalDriver;

    private static (Type, ScreenDriverAttribute) Registration<T>(Platform platform, int priority) =>
        (typeof(T), new ScreenDriverAttribute { System = platform, Driver = typeof(T).Name, Priority = priority });

    /// <summary>Everything registered on <paramref name="platform"/>, as the shipped packages register it.</summary>
    private static List<(Type, ScreenDriverAttribute)> AllRegistrations(Platform platform)
    {
        var registrations = new List<(Type, ScreenDriverAttribute)>
        {
            Registration<SDLGpuDriver>(platform, 300),
            Registration<SDLDriver>(platform, 200),
            (typeof(NullDriver), new ScreenDriverAttribute { System = platform, Driver = nameof(NullDriver), Priority = 0 }),
        };
        if (platform == Platform.Windows)
        {
            registrations.Add(Registration<Win32ConsoleDriver>(platform, 50));
            registrations.Add(Registration<AnsiTerminalDriver>(platform, 10));
        }
        else
        {
            registrations.Add(Registration<AnsiTerminalDriver>(platform, 50));
        }
        // Another platform's registrations never take part.
        Platform other = platform == Platform.Windows ? Platform.Linux : Platform.Windows;
        registrations.Add((typeof(string), new ScreenDriverAttribute { System = other, Driver = "Elsewhere", Priority = 1000 }));
        return registrations;
    }

    private static Type? Resolve(Platform platform, string? requested, IEnumerable<(Type, ScreenDriverAttribute)>? registrations = null) =>
        ScreenDriverFactory.ResolveDriverType(platform, registrations ?? AllRegistrations(platform), requested, warnings: null);

    // ---- portable aliases and exact names ----------------------------------------------------

    [Theory]
    [InlineData("sdl", typeof(SDLGpuDriver))]
    [InlineData("console", typeof(Win32ConsoleDriver))]
    [InlineData("terminal", typeof(Win32ConsoleDriver))]
    [InlineData("SDLGpuDriver", typeof(SDLGpuDriver))]
    [InlineData("SDLDriver", typeof(SDLDriver))]
    [InlineData("Win32ConsoleDriver", typeof(Win32ConsoleDriver))]
    [InlineData("AnsiTerminalDriver", typeof(AnsiTerminalDriver))]
    [InlineData("NullDriver", typeof(NullDriver))]
    public void WindowsResolvesEachNameToItsDriver(string requested, Type expected) =>
        Assert.Equal(expected, Resolve(Platform.Windows, requested));

    [Theory]
    [InlineData(Platform.Linux)]
    [InlineData(Platform.MacOS)]
    public void UnixResolvesEachNameToItsDriver(Platform platform)
    {
        Assert.Equal(typeof(SDLGpuDriver), Resolve(platform, "sdl"));
        Assert.Equal(typeof(AnsiTerminalDriver), Resolve(platform, "console"));
        Assert.Equal(typeof(AnsiTerminalDriver), Resolve(platform, "terminal"));
        Assert.Equal(typeof(SDLGpuDriver), Resolve(platform, "SDLGpuDriver"));
        Assert.Equal(typeof(SDLDriver), Resolve(platform, "SDLDriver"));
        Assert.Equal(typeof(AnsiTerminalDriver), Resolve(platform, "AnsiTerminalDriver"));
        Assert.Equal(typeof(NullDriver), Resolve(platform, "NullDriver"));
    }

    [Theory]
    [InlineData(Platform.Windows, "sdl", "SDLGpuDriver")]
    [InlineData(Platform.Linux, "sdl", "SDLGpuDriver")]
    [InlineData(Platform.MacOS, "sdl", "SDLGpuDriver")]
    [InlineData(Platform.Windows, "console", "Win32ConsoleDriver")]
    [InlineData(Platform.Windows, "terminal", "Win32ConsoleDriver")]
    [InlineData(Platform.Linux, "console", "AnsiTerminalDriver")]
    [InlineData(Platform.Linux, "terminal", "AnsiTerminalDriver")]
    [InlineData(Platform.MacOS, "console", "AnsiTerminalDriver")]
    [InlineData(Platform.MacOS, "terminal", "AnsiTerminalDriver")]
    [InlineData(Platform.Windows, "TERMINAL", "Win32ConsoleDriver")]
    [InlineData(Platform.MacOS, "Sdl", "SDLGpuDriver")]
    public void PortableAliasesNameOneExactDriverPerPlatform(Platform platform, string alias, string driver) =>
        Assert.Equal(driver, ScreenDriverFactory.ResolveAlias(platform, alias));

    [Theory]
    [InlineData("SDLDriver")]
    [InlineData("Win32ConsoleDriver")]
    [InlineData("AnsiTerminalDriver")]
    [InlineData("NullDriver")]
    [InlineData("SomethingElse")]
    public void ExactNamesAreNotAliases(string name)
    {
        foreach (Platform platform in Enum.GetValues<Platform>())
            Assert.Equal(name, ScreenDriverFactory.ResolveAlias(platform, name));
    }

    [Theory]
    [InlineData(Platform.Linux)]
    [InlineData(Platform.MacOS)]
    public void Win32ConsoleDriverIsNotRemappedToTheAnsiDriverOnUnix(Platform platform)
    {
        var warnings = new StringWriter();
        Type? selected = ScreenDriverFactory.ResolveDriverType(platform, AllRegistrations(platform), "Win32ConsoleDriver", warnings);

        // Unknown here: the warning, then automatic selection - never the ANSI driver as a stand-in.
        Assert.Equal(typeof(SDLGpuDriver), selected);
        Assert.Contains("no driver named 'Win32ConsoleDriver' found for platform", warnings.ToString());
    }

    [Fact]
    public void AnsiTerminalDriverIsNotRemappedToTheWin32DriverOnWindows() =>
        Assert.Equal(typeof(AnsiTerminalDriver), Resolve(Platform.Windows, "AnsiTerminalDriver"));

    [Fact]
    public void TheNativeAliasesOnWindowsIgnoreTheLowerPriorityAnsiRegistration()
    {
        // AnsiTerminalDriver stays registered on Windows (priority 10, for decoder testing), but the
        // portable aliases mean the native console there, whatever else is registered.
        var registrations = AllRegistrations(Platform.Windows);
        registrations.Add(Registration<AnsiTerminalDriver>(Platform.Windows, 60));

        Assert.Equal(typeof(Win32ConsoleDriver), Resolve(Platform.Windows, "console", registrations));
        Assert.Equal(typeof(Win32ConsoleDriver), Resolve(Platform.Windows, "terminal", registrations));
    }

    // ---- case and whitespace ------------------------------------------------------------------

    [Theory]
    [InlineData("  sdl  ", typeof(SDLGpuDriver))]
    [InlineData("SDL", typeof(SDLGpuDriver))]
    [InlineData("\tTerminal\t", typeof(AnsiTerminalDriver))]
    [InlineData(" CONSOLE", typeof(AnsiTerminalDriver))]
    [InlineData("sdldriver ", typeof(SDLDriver))]
    [InlineData(" sdlgpudriver", typeof(SDLGpuDriver))]
    [InlineData("ansiterminaldriver", typeof(AnsiTerminalDriver))]
    [InlineData(" nulldriver ", typeof(NullDriver))]
    public void NamesAreTrimmedAndMatchedCaseInsensitively(string requested, Type expected) =>
        Assert.Equal(expected, Resolve(Platform.MacOS, requested));

    // ---- one resolver for both sources --------------------------------------------------------

    [Theory]
    [InlineData("sdl")]
    [InlineData(" Console ")]
    [InlineData("terminal")]
    [InlineData("SDLDriver")]
    [InlineData("sdlgpudriver")]
    [InlineData("Win32ConsoleDriver")]
    [InlineData("AnsiTerminalDriver")]
    [InlineData("NullDriver")]
    [InlineData("nonsense")]
    public void TheEnvironmentAndTheConfigFileResolveTheSameName(string name)
    {
        foreach (Platform platform in Enum.GetValues<Platform>())
        {
            string? fromEnvironment = ScreenDriverFactory.SelectRequestedName(name, configuredValue: null);
            string? fromConfig = ScreenDriverFactory.SelectRequestedName(environmentValue: null, name);

            Assert.Equal(fromEnvironment, fromConfig);
            Assert.Equal(Resolve(platform, fromEnvironment), Resolve(platform, fromConfig));
        }
    }

    [Theory]
    [InlineData("sdl", typeof(SDLGpuDriver))]
    [InlineData("  terminal ", typeof(AnsiTerminalDriver))]
    [InlineData("Console", typeof(AnsiTerminalDriver))]
    [InlineData(" SDLDriver", typeof(SDLDriver))]
    public void AConfigFileNameIsResolvedLikeTheEnvironmentVariable(string value, Type expected)
    {
        string path = Path.Combine(Path.GetTempPath(), "tsv-driver-" + Guid.NewGuid().ToString("N") + ".cfg");
        File.WriteAllText(path, $"[driver]\nname={value}\n");
        try
        {
            TSharpVisionConfiguration config = TSharpVisionConfigurationLoader.LoadFromPath(path);
            string? requested = ScreenDriverFactory.SelectRequestedName(environmentValue: null, config.Driver.Name);

            Assert.Equal(expected, Resolve(Platform.Linux, requested));
            Assert.Equal(expected, Resolve(Platform.Linux, ScreenDriverFactory.SelectRequestedName(value, null)));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void TheEnvironmentVariableTakesPrecedenceOverTheConfigFile()
    {
        Assert.Equal("SDLDriver", ScreenDriverFactory.SelectRequestedName(" SDLDriver ", "console"));
        Assert.Equal("console", ScreenDriverFactory.SelectRequestedName(null, " console "));
        Assert.Equal("console", ScreenDriverFactory.SelectRequestedName("   ", "console"));
        Assert.Equal("console", ScreenDriverFactory.SelectRequestedName("", "console"));
        Assert.Null(ScreenDriverFactory.SelectRequestedName(null, null));
        Assert.Null(ScreenDriverFactory.SelectRequestedName(" ", "\t"));
    }

    [Fact]
    public void TheFactoryReadsTheDocumentedVariable() =>
        Assert.Equal("TSHARPVISION_DRIVER", ScreenDriverFactory.DriverEnvironmentVariable);

    // ---- automatic priority -------------------------------------------------------------------

    [Theory]
    [InlineData(Platform.Windows)]
    [InlineData(Platform.Linux)]
    [InlineData(Platform.MacOS)]
    public void AutomaticSelectionPrefersTheGpuSdlDriver(Platform platform)
    {
        Assert.Equal(typeof(SDLGpuDriver), Resolve(platform, null));
        Assert.Equal(typeof(SDLGpuDriver), Resolve(platform, "   "));
    }

    [Theory]
    [InlineData(Platform.Windows, typeof(Win32ConsoleDriver))]
    [InlineData(Platform.Linux, typeof(AnsiTerminalDriver))]
    [InlineData(Platform.MacOS, typeof(AnsiTerminalDriver))]
    public void AutomaticSelectionFallsDownThePriorityOrder(Platform platform, Type native)
    {
        var registrations = AllRegistrations(platform);
        Assert.Equal(typeof(SDLGpuDriver), Resolve(platform, null, registrations));

        registrations.RemoveAll(x => x.Item1 == typeof(SDLGpuDriver));
        Assert.Equal(typeof(SDLDriver), Resolve(platform, null, registrations));

        registrations.RemoveAll(x => x.Item1 == typeof(SDLDriver));
        Assert.Equal(native, Resolve(platform, null, registrations));

        registrations.RemoveAll(x => x.Item1 == typeof(Win32ConsoleDriver) || x.Item1 == typeof(AnsiTerminalDriver));
        Assert.Equal(typeof(NullDriver), Resolve(platform, null, registrations));

        registrations.RemoveAll(x => x.Item1 == typeof(NullDriver));
        Assert.Null(Resolve(platform, null, registrations));
    }

    [Fact]
    public void SdlDriverStaysDirectlySelectableWhileTheGpuDriverIsPreferred() =>
        Assert.Equal(typeof(SDLDriver), Resolve(Platform.MacOS, "SDLDriver"));

    // ---- invalid names ------------------------------------------------------------------------

    [Fact]
    public void AnUnknownNameWarnsAndFallsBackToAutomaticSelection()
    {
        var warnings = new StringWriter();
        Type? selected = ScreenDriverFactory.ResolveDriverType(Platform.Linux, AllRegistrations(Platform.Linux), " x11 ", warnings);

        Assert.Equal(typeof(SDLGpuDriver), selected);
        Assert.Equal("Warning: no driver named 'x11' found for platform Linux; using default." + Environment.NewLine, warnings.ToString());
    }

    [Fact]
    public void AnAliasWhoseDriverIsNotInstalledWarnsWithTheDriverItStandsFor()
    {
        // `console` on Windows without the console package: the alias names Win32ConsoleDriver
        // exactly, so the usual invalid-name handling applies rather than a different text driver.
        var registrations = AllRegistrations(Platform.Windows);
        registrations.RemoveAll(x => x.Item1 == typeof(Win32ConsoleDriver));
        var warnings = new StringWriter();

        Type? selected = ScreenDriverFactory.ResolveDriverType(Platform.Windows, registrations, "console", warnings);

        Assert.Equal(typeof(SDLGpuDriver), selected);
        Assert.Contains("no driver named 'console' (Win32ConsoleDriver) found for platform Windows", warnings.ToString());
    }

    [Fact]
    public void ARequestedSdlDriverThatIsNotInstalledFallsBackToTheNativeDriver()
    {
        var registrations = AllRegistrations(Platform.MacOS);
        registrations.RemoveAll(x => x.Item1 == typeof(SDLGpuDriver) || x.Item1 == typeof(SDLDriver));
        var warnings = new StringWriter();

        Type? selected = ScreenDriverFactory.ResolveDriverType(Platform.MacOS, registrations, "sdl", warnings);

        Assert.Equal(typeof(AnsiTerminalDriver), selected);
        Assert.Contains("'sdl' (SDLGpuDriver)", warnings.ToString());
    }

    [Fact]
    public void AKnownNameDoesNotWarn()
    {
        var warnings = new StringWriter();
        ScreenDriverFactory.ResolveDriverType(Platform.MacOS, AllRegistrations(Platform.MacOS), "terminal", warnings);
        Assert.Empty(warnings.ToString());
    }

    // ---- the core package's own registration -------------------------------------------------

    [Fact]
    public void NullDriverIsRegisteredAtTheBottomOnEveryPlatform()
    {
        var attributes = typeof(NullDriver).GetCustomAttributes<ScreenDriverAttribute>().ToList();

        Assert.Equal(Enum.GetValues<Platform>().OrderBy(p => p), attributes.Select(a => a.System).OrderBy(p => p));
        Assert.All(attributes, a =>
        {
            Assert.Equal(nameof(NullDriver), a.Driver);
            Assert.Equal(0, a.Priority);
        });
    }
}
