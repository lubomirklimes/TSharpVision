using System;
using System.Collections.Generic;

namespace TSharpVision;

/// <summary>
/// Streamable key/value string table used by compiled .tvr localization resources.
/// </summary>
public sealed class TStringResource : TStreamable
{
    public const string Name = "TStringResource";
    public override string streamableName => Name;

    private readonly Dictionary<string, string> _strings = new(StringComparer.Ordinal);

    public TStringResource()
    {
    }

    public TStringResource(IDictionary<string, string> strings)
    {
        if (strings == null) return;
        foreach (var pair in strings)
            _strings[pair.Key] = pair.Value;
    }

    public static readonly TStreamableClass StreamableClass =
        new TStreamableClass(Name, () => new TStringResource(), 0);

    public IReadOnlyDictionary<string, string> Strings => _strings;

    public bool TryGetValue(string key, out string value)
        => _strings.TryGetValue(key, out value);

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
