using System;
using System.Collections.Generic;
using System.IO;

namespace TSharpVision;

/// <summary>
/// Built-in framework translations and the configuration-driven language switch.
/// </summary>
/// <remarks>
/// <para>
/// The translations live in <c>resources/localization/tsharpvision_{lang}.trc</c>. They are
/// compiled by svrc to the committed <c>.tvr</c> images beside them, which are embedded in
/// this assembly as manifest resources named <see cref="ManifestResourcePrefix"/><c>{lang}.tvr</c>.
/// Embedding keeps NuGet consumers, single-file and trimmed deployments working without
/// any copy step or file probing.
/// </para>
/// <para>
/// Precedence when <see cref="ApplyConfiguredLanguage"/> runs (from
/// <see cref="TSharpVisionRuntime.Configure"/>):
/// <list type="number">
/// <item>A provider the application assigned to <see cref="TSharpVisionIntl.Current"/> is never
/// replaced; configuration is ignored.</item>
/// <item>Otherwise the configured language installs a chain: the optional
/// <c>{AppContext.BaseDirectory}/tsharpvision_{lang}.tvr</c> override, then the embedded
/// translation, then the built-in English provider, so missing keys stay English.</item>
/// <item>A missing, empty, <c>en</c> or unknown language leaves (or restores) the built-in English
/// provider.</item>
/// </list>
/// The operating-system UI culture is never consulted.
/// </para>
/// <para>
/// The public runtime switch, <see cref="TSharpVisionIntl.UseBuiltInLanguage"/> (through
/// <see cref="UseLanguage"/>), builds the same chain but always replaces the current provider,
/// because the application asks for it. The provider it installs is neither
/// <see cref="TSharpVisionIntl.BuiltInDefault"/> nor the configuration-installed one, so later
/// configuration passes treat it as the application's provider and leave it alone.
/// </para>
/// </remarks>
internal static class TSharpVisionLocalization
{
    internal const string ManifestResourcePrefix = "TSharpVision.Localization.tsharpvision_";
    internal const string ApplicationFilePrefix = "tsharpvision_";
    internal const string EnglishLanguage = "en";

    /// <summary>Languages compiled into the assembly, in ordinal order. Read-only (not castable to an array).</summary>
    internal static readonly IReadOnlyList<string> BuiltInLanguages =
        Array.AsReadOnly(new[] { "cs", "de", "en", "es", "fr", "it", "pl", "sk" });

    private static readonly object Gate = new();

    // The provider the last configuration installed; Current equal to it is still "ours".
    private static ITSharpVisionStringProvider? _configuredProvider;

    /// <summary>
    /// Reduces a configured language value to its lowercase primary subtag
    /// (<c>"cs-CZ"</c>, <c>"CS_cz"</c> and <c>" cs "</c> all become <c>"cs"</c>).
    /// Returns null for a missing or blank value, or one whose primary subtag is not
    /// 2-8 ASCII letters.
    /// </summary>
    internal static string? NormalizeLanguage(string? language)
    {
        if (string.IsNullOrWhiteSpace(language))
            return null;

        string trimmed = language.Trim();
        int end = trimmed.IndexOfAny(new[] { '-', '_', '.', '@' });
        string primary = (end < 0 ? trimmed : trimmed[..end]).ToLowerInvariant();
        if (primary.Length < 2 || primary.Length > 8)
            return null;

        foreach (char c in primary)
            if (c < 'a' || c > 'z')
                return null;

        return primary;
    }

    /// <summary>Loads an embedded translation; null when the language is not built in.</summary>
    internal static TResourceStringProvider? LoadBuiltIn(string language)
    {
        string? normalized = NormalizeLanguage(language);
        if (normalized == null)
            return null;

        using Stream? stream = typeof(TSharpVisionLocalization).Assembly
            .GetManifestResourceStream(ManifestResourcePrefix + normalized + ".tvr");
        return stream == null ? null : TResourceStringProvider.TryLoadFromStream(stream);
    }

    /// <summary>
    /// Loads <c>tsharpvision_{lang}.tvr</c> from <paramref name="directory"/> read-only;
    /// null when the file is absent or not a string resource container.
    /// </summary>
    internal static TResourceStringProvider? LoadFromDirectory(string? directory, string language)
    {
        string? normalized = NormalizeLanguage(language);
        if (normalized == null || string.IsNullOrEmpty(directory))
            return null;

        string path = Path.Combine(directory, ApplicationFilePrefix + normalized + ".tvr");
        if (!File.Exists(path))
            return null;

        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            return TResourceStringProvider.TryLoadFromStream(stream);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>
    /// Creates the provider for <paramref name="language"/>: the application-directory file
    /// (if any), then the embedded translation (if any, and not English), then the built-in
    /// English provider. Returns null when neither translation source exists, which includes
    /// English without an application file and a missing or blank value.
    /// </summary>
    internal static ITSharpVisionStringProvider? CreateProvider(string? language, string? applicationDirectory)
    {
        string? normalized = NormalizeLanguage(language);
        if (normalized == null)
            return null;

        TResourceStringProvider? applicationFile = LoadFromDirectory(applicationDirectory, normalized);
        // The embedded English table equals the built-in provider; it only serves parity checks.
        TResourceStringProvider? builtIn = normalized == EnglishLanguage ? null : LoadBuiltIn(normalized);
        if (applicationFile == null && builtIn == null)
            return null;

        return new TSharpVisionStringProviderChain(applicationFile, builtIn, TSharpVisionIntl.BuiltInDefault);
    }

    /// <summary>
    /// Creates the provider an explicit runtime call installs for <paramref name="language"/>:
    /// the same chain as <see cref="CreateProvider"/>, except that English always yields a new
    /// chain over the built-in English provider (plus an application-directory
    /// <c>tsharpvision_en.tvr</c> override, if present). The result is never
    /// <see cref="TSharpVisionIntl.BuiltInDefault"/>, so a later configuration pass treats it as
    /// the application's own choice. Null when the language has no translation source.
    /// </summary>
    internal static ITSharpVisionStringProvider? CreateRuntimeProvider(string? language, string? applicationDirectory)
    {
        string? normalized = NormalizeLanguage(language);
        if (normalized == null)
            return null;

        if (normalized == EnglishLanguage)
        {
            return new TSharpVisionStringProviderChain(
                LoadFromDirectory(applicationDirectory, normalized), TSharpVisionIntl.BuiltInDefault);
        }

        return CreateProvider(normalized, applicationDirectory);
    }

    /// <summary>
    /// Installs the runtime provider for <paramref name="language"/> as
    /// <see cref="TSharpVisionIntl.Current"/>, replacing whatever provider is active. Leaves
    /// <see cref="TSharpVisionIntl.Current"/> unchanged and returns false when the language has
    /// no translation source.
    /// </summary>
    internal static bool UseLanguage(string? language, string? applicationDirectory = null)
    {
        applicationDirectory ??= AppContext.BaseDirectory;

        // Load outside the lock; only the swap is serialized with ApplyConfiguredLanguage.
        ITSharpVisionStringProvider? provider = CreateRuntimeProvider(language, applicationDirectory);
        if (provider == null)
            return false;

        lock (Gate)
        {
            TSharpVisionIntl.Current = provider;
        }

        return true;
    }

    /// <summary>
    /// Applies a configured language to <see cref="TSharpVisionIntl.Current"/> following the
    /// documented precedence. Returns true when a configuration-selected translation is active
    /// afterwards.
    /// </summary>
    internal static bool ApplyConfiguredLanguage(string? language, string? applicationDirectory = null)
    {
        applicationDirectory ??= AppContext.BaseDirectory;

        lock (Gate)
        {
            ITSharpVisionStringProvider current = TSharpVisionIntl.Current;
            bool ownedByConfiguration = _configuredProvider != null
                && ReferenceEquals(current, _configuredProvider);

            if (!ReferenceEquals(current, TSharpVisionIntl.BuiltInDefault) && !ownedByConfiguration)
                return false; // The application chose its own provider; it always wins.

            ITSharpVisionStringProvider? provider = CreateProvider(language, applicationDirectory);
            if (provider == null)
            {
                string? normalized = NormalizeLanguage(language);
                if (!string.IsNullOrWhiteSpace(language) && normalized != EnglishLanguage)
                {
                    Console.Error.WriteLine(
                        $"Warning: no TSharpVision translation for [localization] language '{language.Trim()}'; using English.");
                }

                TSharpVisionIntl.Current = TSharpVisionIntl.BuiltInDefault;
                _configuredProvider = null;
                return false;
            }

            TSharpVisionIntl.Current = provider;
            _configuredProvider = provider;
            return true;
        }
    }

    /// <summary>
    /// Restores the built-in English provider and forgets the configuration-installed provider.
    /// Test support only.
    /// </summary>
    internal static void ResetToBuiltInDefault()
    {
        lock (Gate)
        {
            TSharpVisionIntl.Current = TSharpVisionIntl.BuiltInDefault;
            _configuredProvider = null;
        }
    }
}
