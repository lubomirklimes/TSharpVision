namespace TSharpVision;

/// Maintains a database of all registered <see cref="TStreamableClass"/>
/// instances in the application. Used by <see cref="Opstream"/> and
/// <see cref="Ipstream"/> to find the functions to read and write objects.
public sealed class TStreamableTypes
{
    // Upstream uses a sorted collection keyed by class name; a hash map is
    // semantically equivalent for the registry/lookup operations.
    private readonly Dictionary<string, TStreamableClass> _byName = new();

    /// <summary>Registers a descriptor by its case-sensitive name, replacing an existing descriptor with that name.</summary>
    public void RegisterType(TStreamableClass c)
    {
        _byName[c.name] = c;
    }

    /// <summary>Returns the descriptor registered under the case-sensitive name, or null when absent.</summary>
    public TStreamableClass Lookup(string name)
    {
        return _byName.TryGetValue(name, out var c) ? c : null;
    }

    /// <summary>Number of distinct type names currently registered.</summary>
    public int Count => _byName.Count;
}
