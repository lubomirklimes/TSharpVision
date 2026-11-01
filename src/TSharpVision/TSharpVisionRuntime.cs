using System.Reflection;
using TSharpVision.Config;
using TSharpVision.Drivers;

namespace TSharpVision;

/// <summary>
/// Entry point for application-level TSharpVision initialisation.
/// </summary>
public static class TSharpVisionRuntime
{
    /// <summary>
    /// Loads configuration from .cfg files, applies it to
    /// <see cref="ScreenDriverFactory.Configuration"/>, applies the
    /// <c>[localization] language</c> setting, and sets the application
    /// window / console title.
    /// Call once at startup, before the first
    /// <see cref="ScreenDriverFactory.CreateScreenDriver"/> call.
    /// </summary>
    /// <remarks>
    /// The configured language installs the matching built-in framework translation
    /// (with English as the fallback for missing keys) only while
    /// <see cref="TSharpVisionIntl.Current"/> still holds the built-in English provider.
    /// A provider the application assigned before or after this call is never replaced.
    /// A missing, empty, <c>en</c> or unknown language keeps English. The operating-system
    /// UI culture is not consulted.
    /// </remarks>
    /// <param name="title">
    /// Window / console title passed to the screen driver.
    /// When <see langword="null"/> the entry assembly name is used as the title.
    /// </param>
    /// <returns>The loaded configuration (useful if the caller needs to inspect it).</returns>
    public static TSharpVisionConfiguration Configure(string? title = null)
    {
        var config = TSharpVisionConfigurationLoader.Load();
        Apply(config, title);
        return config;
    }

    // Shared by Configure and tests: everything Configure does after the files are read.
    internal static void Apply(TSharpVisionConfiguration config, string? title)
    {
        ScreenDriverFactory.Configuration = config;
        ScreenDriverFactory.WindowTitle   = title
            ?? Assembly.GetEntryAssembly()?.GetName().Name
            ?? "TSharpVision";
        TSharpVisionLocalization.ApplyConfiguredLanguage(config.Localization.Language);
    }

    /// <summary>
    /// Full application bootstrap in one call:
    /// registers streamable types, loads configuration, sets the window title,
    /// constructs the application via <paramref name="appFactory"/>, and runs it
    /// under <see cref="AppLifecycleGuard.Run"/>.
    /// </summary>
    /// <param name="appFactory">
    /// Factory that produces the application instance.
    /// Invoked after configuration is loaded so the constructor can already
    /// access <see cref="ScreenDriverFactory.Configuration"/>.
    /// </param>
    /// <param name="title">
    /// Window / console title. When <see langword="null"/> the entry assembly
    /// name is used.
    /// </param>
    /// <remarks>
    /// Select the driver before calling this method, for example with the
    /// TSHARPVISION_DRIVER environment variable. The driver package must be installed.
    /// Event-loop exceptions are rethrown after application and driver shutdown.
    /// Configuration and application-construction exceptions propagate to the caller.
    /// </remarks>
    /// <returns>0 on clean exit.</returns>
    public static int Run(Func<TApplication> appFactory, string? title = null)
    {
        StreamableRegistration.RegisterAll();
        Configure(title);
        return AppLifecycleGuard.Run(appFactory());
    }
}
