using System;
using System.Collections.Generic;
using System.IO;
using System.Diagnostics.CodeAnalysis;

namespace TSharpVision;

/// <summary>
/// Loads localized strings from a <see cref="TStringResource"/> stored in a .tvr file.
/// </summary>
public sealed class TResourceStringProvider : ITSharpVisionStringLookupProvider
{
    /// <summary>Default resource identifier for the localized string table in a resource container.</summary>
    public const string DefaultResourceKey = "sharpvision.intl";

    private readonly Dictionary<string, string> _strings;

    // Loaded table, for translation parity checks.
    internal IReadOnlyDictionary<string, string> Entries => _strings;

    /// <summary>Copies a resource's strings into an ordinal lookup table; null creates an empty provider.</summary>
    public TResourceStringProvider(TStringResource? resource)
    {
        _strings = new Dictionary<string, string>(StringComparer.Ordinal);
        if (resource == null) return;

        foreach (var pair in resource.Strings)
            _strings[pair.Key] = pair.Value;
    }

    /// <summary>Loads and copies a string table, closing the opened stream; a missing or non-string resource yields an empty provider, while I/O errors propagate.</summary>
    public static TResourceStringProvider Load(string path, string resourceKey = DefaultResourceKey)
    {
        StreamableRegistration.RegisterAll();

        var fp = new Fpstream(path);
        try
        {
            var rf = new TResourceFile(fp);
            var table = rf.Get(resourceKey) as TStringResource;
            return table == null
                ? new TResourceStringProvider(new TStringResource())
                : new TResourceStringProvider(table);
        }
        finally
        {
            fp.Close();
        }
    }

    /// <summary>Attempts to load a resource string provider; returns null for missing files, unrecognized containers, or load failures.</summary>
    public static TResourceStringProvider? TryLoad(string? path, string resourceKey = DefaultResourceKey)
    {
        if (string.IsNullOrEmpty(path) || !File.Exists(path))
            return null;

        try
        {
            if (!ContainsResourceMagic(path))
                return null;

            return Load(path, resourceKey);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Reads the string table <paramref name="resourceKey"/> from a .tvr image that starts at the
    /// stream's current position (the layout svrc writes: resource magic at offset 0, no
    /// executable prefix). Read-only and seek-only, so it works for manifest resources and
    /// read-only files. Returns null when the image is not a resource container, lacks the key,
    /// or is corrupt; the caller owns the stream.
    /// </summary>
    internal static TResourceStringProvider? TryLoadFromStream(Stream stream, string resourceKey = DefaultResourceKey)
    {
        try
        {
            StreamableRegistration.RegisterAll();

            long basePos = stream.Position;
            if (stream.Length - basePos < 12)
                return null;

            var input = new Ipstream(stream);
            if (input.Read32() != TResourceFile.rStreamMagic)
                return null;

            input.Read32(); // lenRez
            long indexPos = input.Read32();
            input.Seekg(basePos + indexPos);
            if (input.ReadPointer() is not TResourceCollection index
                || !index.Search(resourceKey, out int i))
            {
                return null;
            }

            input.Seekg(basePos + index.At(i).pos);
            return input.ReadPointer() is TStringResource table
                ? new TResourceStringProvider(table)
                : null;
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException
                                       or ArgumentException or InvalidCastException
                                       or IndexOutOfRangeException or EndOfStreamException)
        {
            return null;
        }
    }

    private static bool ContainsResourceMagic(string path)
    {
        byte[] bytes = File.ReadAllBytes(path);
        for (int i = 0; i <= bytes.Length - 4; i++)
        {
            if (bytes[i] == 0x46 && bytes[i + 1] == 0x42
                && bytes[i + 2] == 0x50 && bytes[i + 3] == 0x52)
            {
                return true;
            }
        }

        return false;
    }

    /// <inheritdoc />
    [return: NotNullIfNotNull(nameof(fallback))]
    public string? Get(string key, string? fallback)
        => TryGet(key, out var value) ? value : fallback;

    /// <inheritdoc />
    public bool TryGet(string key, [NotNullWhen(true)] out string? value)
        => _strings.TryGetValue(key, out value);
}

/// <summary>
/// Tries multiple providers in order before returning the caller's fallback.
/// </summary>
public sealed class TSharpVisionStringProviderChain : ITSharpVisionStringLookupProvider
{
    private readonly List<ITSharpVisionStringProvider> _providers = new();

    /// <summary>References providers in lookup order, ignoring null entries; a null array creates an empty chain.</summary>
    public TSharpVisionStringProviderChain(params ITSharpVisionStringProvider?[]? providers)
    {
        if (providers == null) return;
        foreach (var provider in providers)
            if (provider != null)
                _providers.Add(provider);
    }

    /// <inheritdoc />
    [return: NotNullIfNotNull(nameof(fallback))]
    public string? Get(string key, string? fallback)
        => TryGet(key, out string? value) ? value : fallback;

    /// <inheritdoc />
    public bool TryGet(string key, [NotNullWhen(true)] out string? value)
    {
        foreach (var provider in _providers)
        {
            if (provider is ITSharpVisionStringLookupProvider lookupProvider)
            {
                if (lookupProvider.TryGet(key, out value))
                    return true;
                continue;
            }

            value = provider.Get(key, null);
            if (value != null)
                return true;
        }

        value = null;
        return false;
    }
}
