namespace SharpVision;

/// Base class for all storable objects. Mirrors upstream
/// <c>class TStreamable</c> with its three pure virtuals
/// <c>streamableName()</c>, <c>read()</c>, and <c>write()</c>.
/// Modeled as an abstract class (not interface) so that the pre-existing
/// view hierarchy can keep its <c>override Write/Read</c> declarations.
public abstract class TStreamable
{
    // Default returns the C# type name; concrete streamables override with
    // the canonical Borland identifier (e.g. "TView", "TWindow").
    public virtual string streamableName => GetType().Name;

    public abstract object Read(Ipstream s);

    public abstract void Write(Opstream s);
}
