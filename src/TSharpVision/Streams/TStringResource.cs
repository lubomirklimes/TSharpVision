using System;
using System.Collections.Generic;

namespace TSharpVision;

/// <summary>
/// Streamable key/value string table used by compiled .tvr localization resources.
/// </summary>
public sealed class TStringResource : TStreamable
{
    /// <summary>Registry name identifying a serialized string resource table.</summary>
    public const string Name = "TStringResource";
    /// <inheritdoc />
    public override string streamableName => Name;

    private readonly Dictionary<string, string> _strings = new(StringComparer.Ordinal);

    /// <summary>Creates an empty table with ordinal, case-sensitive string keys.</summary>
    public TStringResource()
    {
    }

    /// <summary>Copies the supplied entries into an ordinal key table; null creates an empty table.</summary>
    public TStringResource(IDictionary<string, string> strings)
    {
        if (strings == null) return;
        foreach (var pair in strings)
            _strings[pair.Key] = pair.Value;
    }

    /// <summary>Stream registry descriptor and factory for restoring this concrete type.</summary>
    public static readonly TStreamableClass StreamableClass =
        new TStreamableClass(Name, () => new TStringResource(), 0);

    /// <summary>Read-only view of the current string table, updated when this object is deserialized.</summary>
    public IReadOnlyDictionary<string, string> Strings => _strings;

    /// <summary>Looks up an ordinal key, returning true and its value when present, or false and null when absent.</summary>
    public bool TryGetValue(string key, out string value)
        => _strings.TryGetValue(key, out value);

    /// <inheritdoc />
    public override void Write(Opstream s)
    {
        var keys = new List<string>(_strings.Keys);
        keys.Sort(StringComparer.Ordinal);

        s.WriteInt((uint)keys.Count);
        foreach (string key in keys)
        {
            s.WriteString(key);
            s.WriteString(_strings[key]);
        }
    }

    /// <inheritdoc />
    public override object Read(Ipstream s)
    {
        _strings.Clear();
        int count = (int)s.ReadInt();
        for (int i = 0; i < count; i++)
        {
            string key = s.ReadString() ?? string.Empty;
            string value = s.ReadString() ?? string.Empty;
            _strings[key] = value;
        }
        return this;
    }
}
