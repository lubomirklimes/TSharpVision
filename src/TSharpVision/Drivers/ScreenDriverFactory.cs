using TSharpVision;
using TSharpVision.Config;
using System.Reflection;

namespace TSharpVision.Drivers;

/// <summary>Discovers and selects screen-driver implementations using platform, configuration, and registration priority.</summary>
public class ScreenDriverFactory
{
    private static readonly Type[] DriverTypes;
    private static TSharpVisionConfiguration? _configuration;
    private static readonly Dictionary<Type, IConfigurationSection> _registeredSections = new();

    /// <summary>
    /// Application window / console title set at startup by
    /// <see cref="TSharpVisionRuntime.Configure"/>.
    /// Graphical drivers use it as the OS window title; text-mode drivers
    /// set the console/terminal title where the platform supports it.
    /// </summary>
    public static string? WindowTitle { get; set; }

    /// <summary>
    /// Full configuration loaded by the application at startup.
    /// Set this before the first <see cref="CreateScreenDriver"/> call
    /// (or use <see cref="TSharpVisionRuntime.Configure"/>).
    /// Assigning a new configuration clears all previously registered sections.
    /// </summary>
    public static TSharpVisionConfiguration? Configuration
    {
        get => _configuration;
        set
        {
            _configuration = value;
            _registeredSections.Clear();
        }
    }

    /// <summary>
    /// Registers a driver-specific configuration section and binds it from
    /// <see cref="Configuration"/>'s raw sections. Idempotent — repeated calls
    /// with the same type return the cached (already-bound) instance.
    /// </summary>
    public static T RegisterConfigSection<T>() where T : IConfigurationSection, new()
    {
        if (_registeredSections.TryGetValue(typeof(T), out var existing))
            return (T)existing;

        var instance = new T();
        if (_configuration?.GetRawSection(instance.SectionName) is { } raw)
            instance.Bind(raw);
        _registeredSections[typeof(T)] = instance;
        return instance;
    }

    /// <summary>Returns a previously registered section, or throws if not registered.</summary>
    public static T GetConfigSection<T>() where T : IConfigurationSection
    {
        if (_registeredSections.TryGetValue(typeof(T), out var section))
            return (T)section;
        throw new InvalidOperationException(
            $"Configuration section {typeof(T).Name} has not been registered. " +
            $"Call RegisterConfigSection<{typeof(T).Name}>() first.");
    }

    /// <summary>Returns a previously registered section, or null if not registered.</summary>
    public static T? TryGetConfigSection<T>() where T : class, IConfigurationSection
        => _registeredSections.TryGetValue(typeof(T), out var section) ? (T)section : null;

    static ScreenDriverFactory()
    {
        string currentDirectory = AppDomain.CurrentDomain.BaseDirectory;

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
                    return ex.Types.OfType<Type>();
                }
            })
            .Where(type => typeof(IDriver).IsAssignableFrom(type) && !type.IsInterface && !type.IsAbstract)
            .ToArray();
    }

    private static Platform GetCurrentPlatform() => ClassifyPlatform(
        OperatingSystem.IsWindows(), OperatingSystem.IsMacOS(), OperatingSystem.IsLinux());

    private static Platform ClassifyPlatform(bool windows, bool macOS, bool linux)
    {
        if (windows) return Platform.Windows;
        if (macOS) return Platform.MacOS;
        if (linux) return Platform.Linux;
        throw new PlatformNotSupportedException("Unsupported platform.");
    }

    private static Type? GetDriverTypeForPlatform(Platform platform)
    {
        // TSHARPVISION_DRIVER env var takes precedence over config (useful for CI/tests).
        string? requested = Environment.GetEnvironmentVariable("TSHARPVISION_DRIVER");
        if (string.IsNullOrWhiteSpace(requested))
            requested = _configuration?.Driver.Name;

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
            if (string.Equals(requested, "sdl", StringComparison.OrdinalIgnoreCase))
            {
                requested = "SDLGpuDriver";
            }
            else if (string.Equals(requested, "console", StringComparison.OrdinalIgnoreCase))
            {
                return candidates
                    .Where(x => !x.Attribute.Driver.StartsWith("SDL", StringComparison.OrdinalIgnoreCase))
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

    /// <summary>Constructs the selected driver for the current platform without initializing it; throws InvalidOperationException if none is available.</summary>
    public static IDriver CreateScreenDriver()
    {
        Platform platform = GetCurrentPlatform();
        Type? driverType = GetDriverTypeForPlatform(platform);

        if (driverType != null)
        {
            object instance = Activator.CreateInstance(driverType)
                ?? throw new InvalidOperationException($"Driver type '{driverType.FullName}' could not be constructed.");
            return instance as IDriver
                ?? throw new InvalidOperationException($"Driver type '{driverType.FullName}' does not implement IDriver.");
        }

        throw new InvalidOperationException($"No screen driver found for platform {platform}.");
    }
}
