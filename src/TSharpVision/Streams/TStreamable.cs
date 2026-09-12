namespace TSharpVision;

/// <summary>Base contract for objects whose type and state can be written to and restored from framework streams.</summary>
public abstract class TStreamable
{
    // Default returns the C# type name; concrete streamables override with
    // the canonical Borland identifier (e.g. "TView", "TWindow").
    /// <summary>Gets the type identifier used when serializing this object; the default is the CLR type name.</summary>
    public virtual string streamableName => GetType().Name;

    /// <summary>Restores object state from the input stream and returns the restored object.</summary>
    public abstract object Read(Ipstream s);

    /// <summary>Writes object state to the output stream for later restoration.</summary>
    public abstract void Write(Opstream s);
}
