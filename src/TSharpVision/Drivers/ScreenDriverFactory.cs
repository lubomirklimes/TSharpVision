using TSharpVision;
using TSharpVision.Config;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

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
            // Loading an assembly and enumerating its types is the expensive part of discovery,
            // and an application directory is mostly libraries that have nothing to do with the
            // screen - and views and editors that draw on it without being drivers. Those are told
            // apart from their metadata alone and never loaded here.
            if (!MayDefineDriver(file))
                continue;

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

    /// <summary>
    /// Whether the file at <paramref name="file"/> could contain a selectable screen driver: it is
    /// this assembly, or a managed assembly that uses <see cref="ScreenDriverAttribute"/>.
    /// </summary>
    /// <remarks>
    /// A driver is only ever selected through a <see cref="ScreenDriverAttribute"/> on its own
    /// class - the attribute is not inherited - so an assembly that never names that attribute
    /// cannot contribute one. Only the file's metadata is read; nothing is loaded. A file that is
    /// not a managed assembly is not a candidate - loading it would have failed and been skipped.
    /// A file whose metadata cannot be read is left to the loader, which decides as it always did.
    /// </remarks>
    private static bool MayDefineDriver(string file)
    {
        Type marker = typeof(ScreenDriverAttribute);
        string core = marker.Assembly.GetName().Name!;

        try
        {
            using var stream = new FileStream(
                file, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
            using var image = new PEReader(stream);

            if (!image.HasMetadata)
                return false;

            MetadataReader metadata = image.GetMetadataReader();
            if (!metadata.IsAssembly)
                return false;

            if (metadata.StringComparer.Equals(metadata.GetAssemblyDefinition().Name, core))
                return true;

            foreach (TypeReferenceHandle handle in metadata.TypeReferences)
            {
                TypeReference reference = metadata.GetTypeReference(handle);
                if (metadata.StringComparer.Equals(reference.Name, marker.Name)
                    && metadata.StringComparer.Equals(reference.Namespace, marker.Namespace!))
                    return true;
            }

            return false;
        }
        catch (BadImageFormatException)
        {
            // Not a portable executable at all.
            return false;
        }
        catch (Exception)
        {
            return true;
        }
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

    /// <summary>The environment variable that overrides <c>[driver] name</c>.</summary>
    internal const string DriverEnvironmentVariable = "TSHARPVISION_DRIVER";

    private static Type? GetDriverTypeForPlatform(Platform platform)
    {
        var registrations = DriverTypes
            .SelectMany(type => type
                .GetCustomAttributes<ScreenDriverAttribute>()
                .Select(attribute => (type, attribute)));

        string? requested = SelectRequestedName(
            Environment.GetEnvironmentVariable(DriverEnvironmentVariable),
            _configuration?.Driver.Name);

        // Diagnostic activation must never qualify an unintended fallback backend.
        if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(DecoratorEnvironmentVariable))
            && requested is not null)
        {
            string name = ResolveAlias(platform, requested);
            return registrations.FirstOrDefault(x => x.attribute.System == platform
                && string.Equals(x.attribute.Driver, name, StringComparison.OrdinalIgnoreCase)).type
                ?? throw new InvalidOperationException($"Requested backend '{requested}' is unavailable; no fallback performed.");
        }
        return ResolveDriverType(platform, registrations, requested, Console.Error);
    }

    /// <summary>
    /// The one requested driver name, wherever it came from: <c>TSHARPVISION_DRIVER</c> takes
    /// precedence over <c>[driver] name</c> (useful for CI/tests). Both are trimmed; a blank
    /// value means "not set". Null means automatic selection.
    /// </summary>
    internal static string? SelectRequestedName(string? environmentValue, string? configuredValue)
    {
        if (!string.IsNullOrWhiteSpace(environmentValue))
            return environmentValue.Trim();
        if (!string.IsNullOrWhiteSpace(configuredValue))
            return configuredValue.Trim();
        return null;
    }

    /// <summary>
    /// Maps a portable alias to the exact driver name it stands for on <paramref name="platform"/>;
    /// any other name is returned unchanged and is matched literally.
    /// </summary>
    /// <remarks>
    /// <c>sdl</c> is the GPU SDL driver everywhere. <c>console</c> and <c>terminal</c> are the same
    /// portable choice - the platform's native text driver - so one config file works on every OS.
    /// Exact names are never reinterpreted: <c>Win32ConsoleDriver</c> does not become
    /// <c>AnsiTerminalDriver</c> on Unix, nor the other way round.
    /// </remarks>
    internal static string ResolveAlias(Platform platform, string requested)
    {
        if (string.Equals(requested, "sdl", StringComparison.OrdinalIgnoreCase))
            return "SDLGpuDriver";

        if (string.Equals(requested, "console", StringComparison.OrdinalIgnoreCase)
            || string.Equals(requested, "terminal", StringComparison.OrdinalIgnoreCase))
            return platform == Platform.Windows ? "Win32ConsoleDriver" : "AnsiTerminalDriver";

        return requested;
    }

    /// <summary>
    /// Picks the driver for <paramref name="platform"/> among <paramref name="registrations"/>.
    /// A requested name (alias or exact, case-insensitive) wins when it is registered for the
    /// platform; an unknown one is reported to <paramref name="warnings"/> and selection falls
    /// back to the highest-priority registration, as it does when nothing is requested.
    /// </summary>
    internal static Type? ResolveDriverType(
        Platform platform,
        IEnumerable<(Type Type, ScreenDriverAttribute Attribute)> registrations,
        string? requested,
        TextWriter? warnings)
    {
        var candidates = registrations
            .Where(x => x.Attribute.System == platform)
            .ToList();

        requested = requested?.Trim();
        if (!string.IsNullOrEmpty(requested))
        {
            string driverName = ResolveAlias(platform, requested);

            var requestedType = candidates
                .FirstOrDefault(x =>
                    string.Equals(
                        x.Attribute.Driver,
                        driverName,
                        StringComparison.OrdinalIgnoreCase))
                .Type;

            if (requestedType is not null)
                return requestedType;

            string shown = string.Equals(driverName, requested, StringComparison.OrdinalIgnoreCase)
                ? $"'{requested}'"
                : $"'{requested}' ({driverName})";
            warnings?.WriteLine(
                $"Warning: no driver named {shown} found for platform {platform}; using default.");
        }

        return candidates
            .OrderByDescending(x => x.Attribute.Priority)
            .FirstOrDefault()
            .Type;
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
            if (instance is not IDriver backend)
            {
                (instance as IDisposable)?.Dispose();
                throw new InvalidOperationException($"Driver type '{driverType.FullName}' does not implement IDriver.");
            }
            string? providerName = Environment.GetEnvironmentVariable(DecoratorEnvironmentVariable);
            if (string.IsNullOrWhiteSpace(providerName)) return backend;
            // Resolve only after ordinary backend selection; no recursive factory call.
            return Decorate(backend, providerName, SelectRequestedName(
                Environment.GetEnvironmentVariable(DriverEnvironmentVariable), _configuration?.Driver.Name));
        }

        throw new InvalidOperationException($"No screen driver found for platform {platform}.");
    }

    /// <summary>The environment variable naming an <see cref="IDriverDecorator"/> provider type.</summary>
    internal const string DecoratorEnvironmentVariable = "TSHARPVISION_DRIVER_DECORATOR";

    /// <summary>
    /// Hands <paramref name="backend"/> to the named provider. Ownership moves only when a non-null driver
    /// comes back; on every failure the never-initialized backend is disposed here and the error rethrown.
    /// </summary>
    internal static IDriver Decorate(IDriver backend, string providerName, string? requestedBackend)
    {
        try
        {
            Type providerType = Type.GetType(providerName.Trim(), throwOnError: true)!;
            var provider = Activator.CreateInstance(providerType) as IDriverDecorator
                ?? throw new InvalidOperationException("Driver decorator must implement IDriverDecorator.");
            return provider.Decorate(backend, requestedBackend)
                ?? throw new InvalidOperationException("Driver decorator returned null.");
        }
        catch (Exception failure)
        {
            try { (backend as IDisposable)?.Dispose(); }
            catch (Exception cleanup)
            {
                throw new AggregateException("Driver decoration and backend cleanup failed.", failure, cleanup);
            }
            throw;
        }
    }
}
