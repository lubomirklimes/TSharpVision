using System;
namespace TSharpVision;

/// Used internally by <see cref="TStreamableTypes"/> and <see cref="Pstream"/>.
/// Each <see cref="TStreamable"/> descendant registers a singleton of this
/// class so that the streams know how to construct an empty instance.
public sealed class TStreamableClass
{
    public readonly string name;
    public readonly Func<TStreamable> build;
    public readonly int delta;

    // The upstream `delta` accounts for the void* offset between a derived
    // class and its TStreamable sub-object under multiple inheritance. C# has
    // no equivalent, so it is preserved as a field but always 0 in practice.
    public TStreamableClass(string n, Func<TStreamable> b, int d = 0)
    {
        name = n;
        build = b;
        delta = d;
       
        Pstream.RegisterType(this);
    }
}
