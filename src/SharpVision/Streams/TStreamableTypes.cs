namespace SharpVision;

/// Maintains a database of all registered <see cref="TStreamableClass"/>
/// instances in the application. Used by <see cref="Opstream"/> and
/// <see cref="Ipstream"/> to find the functions to read and write objects.
public sealed class TStreamableTypes
{
    // Upstream uses a sorted collection keyed by class name; a hash map is
    // semantically equivalent for the registry/lookup operations.
    private readonly Dictionary<string, TStreamableClass> _byName = new();

    public void RegisterType(TStreamableClass c)
    {
        _byName[c.name] = c;
    }

    public TStreamableClass Lookup(string name)
    {
        return _byName.TryGetValue(name, out var c) ? c : null;
    }

    public int Count => _byName.Count;
}
