using System;
namespace TSharpVision;

/// Used internally by <see cref="TStreamableTypes"/> and <see cref="Pstream"/>.
/// Each <see cref="TStreamable"/> descendant registers a singleton of this
/// class so that the streams know how to construct an empty instance.
public sealed class TStreamableClass
{
    /// <summary>Serialized type name used as the registry lookup key.</summary>
    public readonly string name;
    /// <summary>Factory creating an instance whose persisted state is subsequently supplied by Read.</summary>
    public readonly Func<TStreamable> build;
    /// <summary>Compatibility offset retained from the native descriptor; managed restoration does not use it.</summary>
    public readonly int delta;

    // The upstream `delta` accounts for the void* offset between a derived
    // class and its TStreamable sub-object under multiple inheritance. C# has
    // no equivalent, so it is preserved as a field but always 0 in practice.
    /// <summary>Creates and immediately registers a named restoration factory; the optional compatibility offset is retained without affecting managed restoration.</summary>
    public TStreamableClass(string n, Func<TStreamable> b, int d = 0)
    {
        name = n;
        build = b;
        delta = d;
       
        Pstream.RegisterType(this);
    }
}
