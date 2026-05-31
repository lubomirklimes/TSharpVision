using TSharpVision;
using TSharpVision.Config;
using System.Reflection;

namespace TSharpVision.Drivers;

public class ScreenDriverFactory
{
    private static readonly Type[] DriverTypes;

    /// <summary>
    /// Driver name supplied by configuration (e.g. "sdl", "console").
    /// Supports the same friendly names as <c>[driver] name</c> in a .cfg file.
    /// The <c>TSHARPVISION_DRIVER</c> environment variable takes precedence.
    /// Set this before the first <see cref="CreateScreenDriver"/> call.
    /// </summary>
    public static string? ConfiguredDriverName { get; set; }

    /// <summary>
    /// Font name supplied by configuration for the SDL driver (e.g. "Cascadia Mono").
    /// Read by the SDL driver during <c>Initialize()</c>.
    /// Set this before the first <see cref="CreateScreenDriver"/> call.
    /// </summary>
    public static string? ConfiguredSdlFontName { get; set; }

    /// <summary>
    /// Font point size supplied by configuration for the SDL driver.
    /// Read by the SDL driver during <c>Initialize()</c>.
    /// Set this before the first <see cref="CreateScreenDriver"/> call.
    /// </summary>
    public static int? ConfiguredSdlFontSize { get; set; }

    /// <summary>
    /// Full configuration loaded by the application at startup.
    /// Set this before the first <see cref="CreateScreenDriver"/> call.
    /// GPU drivers use this to read their <c>[sdlgpu]</c> options section
    /// as a fallback when the corresponding environment variable is not set.
    /// </summary>
    public static TSharpVisionConfiguration? Configuration { get; set; }

    //static ScreenDriverFactory()
    //{
    //    // Get the current directory
    //    string currentDirectory = AppDomain.CurrentDomain.BaseDirectory;

    //    // Load all assemblies in the current directory
    //    var assemblies = Directory.GetFiles(currentDirectory, "*.dll")
    //        .Select(Assembly.LoadFrom);

    //    // Find all types that implement the IDriver interface
    //    DriverTypes = assemblies
    //        .SelectMany(assembly => assembly.GetTypes())
    //        .Where(type => typeof(IDriver).IsAssignableFrom(type) && !type.IsInterface && !type.IsAbstract)
    //        .ToArray();
    //}

    static ScreenDriverFactory()
    {
        // Get the current directory
        string currentDirectory = AppDomain.CurrentDomain.BaseDirectory;

        // Load all assemblies in the current directory
        var assemblies = new List<Assembly>();
        foreach (var file in Directory.GetFiles(currentDirectory, "*.dll"))
        {
            try
            {
                assemblies.Add(Assembly.LoadFrom(file));
            }
            catch (BadImageFormatException)
            {
                // Not a valid .NET assembly (likely native DLL), skip it
            }
            catch (FileLoadException)
            {
                // Assembly could not be loaded, skip it
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Warning: Failed to load assembly '{file}': {ex.Message}");
            }
        }

        DriverTypes = assemblies
            .SelectMany(assembly =>
            {
                try
                {
                    return assembly.GetTypes();
                }
                catch (ReflectionTypeLoadException ex)
                {
                    // Return only types that were successfully loaded
                    return ex.Types.Where(t => t != null)!;
                }
            })
            .Where(type => typeof(IDriver).IsAssignableFrom(type) && !type.IsInterface && !type.IsAbstract)
            .ToArray();
    }

    private static Platform GetCurrentPlatform()
    {
        return Environment.OSVersion.Platform switch
        {
            PlatformID.Unix => Platform.Linux,
            PlatformID.MacOSX => Platform.MacOS,
            PlatformID.Win32NT => Platform.Windows,
            _ => throw new Exception("Unsupported platform.")
        };
    }

    private static Type? GetDriverTypeForPlatform(Platform platform)
    {
        // TSharpVISION_DRIVER env var takes precedence over config (useful for CI/tests).
        // Config-supplied name is the fallback when the env var is absent.
        string? requested = Environment.GetEnvironmentVariable("TSharpVision_DRIVER");
        if (string.IsNullOrWhiteSpace(requested))
            requested = ConfiguredDriverName;

        var candidates = DriverTypes
            .SelectMany(type => type
                .GetCustomAttributes<ScreenDriverAttribute>()
                .Select(attribute => new
                {
                    Type = type,
                    Attribute = attribute
                }))
            .Where(x => x.Attribute.System == platform);

        if (!string.IsNullOrWhiteSpace(requested))
        {
            // Expand friendly config names to internal driver class names.
            if (string.Equals(requested, "sdl", StringComparison.OrdinalIgnoreCase))
            {
                requested = "SDLGpuDriver";
            }
            else if (string.Equals(requested, "console", StringComparison.OrdinalIgnoreCase))
            {
                // Pick the highest-priority non-SDL driver for the platform.
                return candidates
                    .Where(x => !string.Equals(x.Attribute.Driver, "SDLDriver",
                                               StringComparison.OrdinalIgnoreCase))
                    .OrderByDescending(x => x.Attribute.Priority)
                    .FirstOrDefault()?.Type;
            }

            var requestedType = candidates
                .FirstOrDefault(x =>
                    string.Equals(
                        x.Attribute.Driver,
                        requested,
                        StringComparison.OrdinalIgnoreCase))
                ?.Type;

            if (requestedType is not null)
                return requestedType;

            Console.Error.WriteLine(
                $"Warning: no driver named '{requested}' found for platform {platform}; using default.");
        }

        return candidates
            .OrderByDescending(x => x.Attribute.Priority)
            .FirstOrDefault()?.Type;
    }

    public static IDriver CreateScreenDriver()
    {
        // Determine the current platform
        Platform platform = GetCurrentPlatform();

        // Find the best matching driver for the platform
        Type driverType = GetDriverTypeForPlatform(platform);

        if (driverType != null)
        {
            return (IDriver)Activator.CreateInstance(driverType);
        }

        throw new InvalidOperationException($"No screen driver found for platform {platform}.");
    }
}
