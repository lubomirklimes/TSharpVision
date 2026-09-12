using System.IO;
namespace TSharpVision;

/// Read-only file polymorphic stream. Mirrors upstream <c>ifpstream</c>:
/// a thin subclass of <see cref="Ipstream"/> whose ctor opens a file in
/// binary read mode.
public class Ifpstream : Ipstream
{
    // Tracks whether this instance owns the underlying FileStream and must
    // close it when the caller invokes Close(). External streams handed via
    // the (Stream) ctor are left untouched (mirroring fpbase's behaviour
    // when constructed from an already-opened fd).
    private readonly bool _ownsStream;

    /// <summary>Opens an existing file for object input and owns its lifetime until Close.</summary>
    public Ifpstream(string path) : base(Fpbase.OpenRead(path))
    {
        _ownsStream = true;
    }

    /// <summary>Reads objects from a caller-owned stream without taking responsibility for closing it.</summary>
    public Ifpstream(Stream s) : base(s)
    {
        _ownsStream = false;
    }

    /// <summary>Closes the underlying stream only when this reader opened it from a path.</summary>
    public void Close()
    {
        if (_ownsStream && bp != null)
        {
            bp.Close();
        }
    }
}
