using System.Reflection;

namespace TSharpVision.Config;

/// <summary>
/// Loads <see cref="TSharpVisionConfiguration"/> from a .cfg file named after
/// the entry assembly (e.g., <c>TVDemo.cfg</c>, <c>TVEdit.cfg</c>).
/// The file is looked up in <see cref="AppContext.BaseDirectory"/>.
/// If the file does not exist, or cannot be read, a default (all-null)
/// configuration is returned so existing behavior is preserved.
/// </summary>
public static class TSharpVisionConfigurationLoader
{
    /// <summary>
    /// Resolves the config file path for the running executable.
    /// Returns <c>null</c> if the entry assembly name cannot be determined.
    /// </summary>
    public static string? ResolveConfigPath()
    {
        string? assemblyName = Assembly.GetEntryAssembly()?.GetName().Name;
        if (string.IsNullOrEmpty(assemblyName))
            return null;

        string fileName = assemblyName + ".cfg";
        return Path.Combine(AppContext.BaseDirectory, fileName);
    }

    /// <summary>
    /// Loads the configuration. Returns defaults when no file exists.
    /// </summary>
    public static TSharpVisionConfiguration Load()
    {
        string? path = ResolveConfigPath();
        if (path == null || !File.Exists(path))
            return new TSharpVisionConfiguration();

        return LoadFromPath(path);
    }

    /// <summary>
    /// Loads configuration from an explicit file path.
    /// Returns defaults when the file does not exist or cannot be read.
    /// </summary>
    public static TSharpVisionConfiguration LoadFromPath(string path)
    {
        if (!File.Exists(path))
            return new TSharpVisionConfiguration();

        try
        {
            string text = File.ReadAllText(path);
            var ini = IniConfigurationReader.Parse(text);
            return new TSharpVisionConfiguration
            {
                DriverName  = ini.Get("driver", "name"),
                SdlFontName = ini.Get("sdl",    "fontName"),
            };
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Warning: failed to read config '{path}': {ex.Message}");
            return new TSharpVisionConfiguration();
        }
    }
}
