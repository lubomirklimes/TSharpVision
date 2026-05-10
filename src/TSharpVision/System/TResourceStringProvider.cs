using System;
using System.Collections.Generic;
using System.IO;

namespace TSharpVision;

/// <summary>
/// Loads localized strings from a <see cref="TStringResource"/> stored in a .tvr file.
/// </summary>
public sealed class TResourceStringProvider : ITSharpVisionStringLookupProvider
{
    public const string DefaultResourceKey = "sharpvision.intl";

    private readonly Dictionary<string, string> _strings;

    public TResourceStringProvider(TStringResource resource)
    {
        _strings = new Dictionary<string, string>(StringComparer.Ordinal);
        if (resource == null) return;

        foreach (var pair in resource.Strings)
            _strings[pair.Key] = pair.Value;
    }

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

    public static TResourceStringProvider TryLoad(string path, string resourceKey = DefaultResourceKey)
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

    public string Get(string key, string fallback)
        => TryGet(key, out var value) ? value : fallback;

    public bool TryGet(string key, out string value)
        => _strings.TryGetValue(key, out value);
}

/// <summary>
/// Tries multiple providers in order before returning the caller's fallback.
/// </summary>
public sealed class TSharpVisionStringProviderChain : ITSharpVisionStringLookupProvider
{
    private readonly List<ITSharpVisionStringProvider> _providers = new();

    public TSharpVisionStringProviderChain(params ITSharpVisionStringProvider[] providers)
    {
        if (providers == null) return;
        foreach (var provider in providers)
            if (provider != null)
                _providers.Add(provider);
    }

    public string Get(string key, string fallback)
        => TryGet(key, out string value) ? value : fallback;

    public bool TryGet(string key, out string value)
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
