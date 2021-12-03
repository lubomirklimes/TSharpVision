using SharpVision;
using System.Reflection;

namespace SharpVision.Drivers;

public class ScreenDriverFactory
{
    private static readonly Type[] DriverTypes;

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
        // Honor the SHARPVISION_DRIVER environment variable so tests/CI can
        // pin a specific driver (e.g., "NullDriver" for headless runs).
        string? requested = Environment.GetEnvironmentVariable("SHARPVISION_DRIVER");

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
            var requestedType = candidates
                .FirstOrDefault(x =>
                    string.Equals(
                        x.Attribute.Driver,
                        requested,
                        StringComparison.OrdinalIgnoreCase))
                ?.Type;

            if (requestedType is not null)
            {
                return requestedType;
            }
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
