namespace TSharpVision.Config;

/// <summary>
/// Minimal INI-style reader.
/// Supports [section], key=value pairs, ; and # comments, blank lines,
/// case-insensitive section and key names, and whitespace trimming.
/// Unknown sections and keys are silently ignored.
/// </summary>
public sealed class IniConfigurationReader
{
    private readonly Dictionary<string, Dictionary<string, string>> _sections =
        new(StringComparer.OrdinalIgnoreCase);

    private IniConfigurationReader() { }

    /// <summary>
    /// Parses an INI-format string and returns a populated reader.
    /// </summary>
    public static IniConfigurationReader Parse(string text)
    {
        var reader = new IniConfigurationReader();
        string currentSection = string.Empty;

        foreach (string rawLine in text.Split('\n'))
        {
            string line = rawLine.Trim();

            // Skip blank lines and comment lines.
            if (line.Length == 0 || line[0] == ';' || line[0] == '#')
                continue;

            // Section header.
            if (line[0] == '[')
            {
                int close = line.IndexOf(']');
                if (close > 1)
                    currentSection = line.Substring(1, close - 1).Trim();
                continue;
            }

            // key=value pair.
            int eq = line.IndexOf('=');
            if (eq <= 0)
                continue;

            string key   = line.Substring(0, eq).Trim();
            string value = line.Substring(eq + 1).Trim();

            if (!reader._sections.TryGetValue(currentSection, out var dict))
            {
                dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                reader._sections[currentSection] = dict;
            }

            dict[key] = value;
        }

        return reader;
    }

    /// <summary>
    /// Returns the value for the given section and key, or <c>null</c> if not found.
    /// Section and key matching is case-insensitive.
    /// </summary>
    public string? Get(string section, string key)
    {
        if (_sections.TryGetValue(section, out var dict) &&
            dict.TryGetValue(key, out string? value))
            return value;
        return null;
    }
}
