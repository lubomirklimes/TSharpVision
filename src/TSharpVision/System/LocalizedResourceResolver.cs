using System;
using System.Collections.Generic;
using System.IO;

namespace TSharpVision;

/// <summary>
/// Resolves localized resource paths using the app_cs/app_en/app convention.
/// </summary>
public static class LocalizedResourceResolver
{
    /// <summary>Enumerates distinct paths in requested-language, fallback-language, then unsuffixed order; language suffixes are normalized to at most two lowercase characters.</summary>
    public static IEnumerable<string> GetCandidatePaths(
        string basePath,
        string extension,
        string language,
        string fallbackLanguage = "en")
    {
        string normalizedBase = StripExtension(basePath, extension);
        string normalizedExt = NormalizeExtension(extension);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (string suffix in new[] { NormalizeLanguage(language), NormalizeLanguage(fallbackLanguage), null })
        {
            string candidate = suffix == null
                ? normalizedBase + normalizedExt
                : normalizedBase + "_" + suffix + normalizedExt;

            if (seen.Add(candidate))
                yield return candidate;
        }
    }

    /// <summary>Returns the first existing localized candidate path, or null when none exists.</summary>
    public static string Resolve(
        string basePath,
        string extension,
        string language,
        string fallbackLanguage = "en")
    {
        foreach (string candidate in GetCandidatePaths(basePath, extension, language, fallbackLanguage))
            if (File.Exists(candidate))
                return candidate;

        return null;
    }

    private static string StripExtension(string basePath, string extension)
    {
        string normalizedExt = NormalizeExtension(extension);
        return basePath.EndsWith(normalizedExt, StringComparison.OrdinalIgnoreCase)
            ? basePath[..^normalizedExt.Length]
            : basePath;
    }

    private static string NormalizeExtension(string extension)
    {
        if (string.IsNullOrEmpty(extension))
            return string.Empty;

        return extension[0] == '.' ? extension : "." + extension;
    }

    private static string NormalizeLanguage(string language)
    {
        if (string.IsNullOrWhiteSpace(language))
            return null;

        string trimmed = language.Trim().ToLowerInvariant();
        return trimmed.Length >= 2 ? trimmed[..2] : trimmed;
    }
}
