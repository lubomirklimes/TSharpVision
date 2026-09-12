using System.IO;
namespace TSharpVision;

/// Read/write file polymorphic stream. Mirrors upstream <c>fpstream</c>,
/// which uses virtual inheritance to combine <c>opstream</c> and
/// <c>ipstream</c> over a single shared <c>CLY_filebuf</c>.
///
/// C# is single-inheritance, so this class composes a private
/// <see cref="Ifpstream"/> and <see cref="Ofpstream"/> over the same
/// <see cref="FileStream"/>. Both halves keep their own object tables —
/// matching upstream, where <c>opstream::objs</c> and <c>ipstream::objs</c>
/// are independent fields.
public class Fpstream
{
    // The shared underlying file. Both reader/writer keep references and
    // we close it exactly once via <see cref="Close"/>.
    private readonly FileStream _bp;
    /// <summary>Object reader sharing the underlying file position with Out.</summary>
    public readonly Ipstream In;
    /// <summary>Object writer sharing the underlying file position with In.</summary>
    public readonly Opstream Out;

    /// <summary>Opens or creates a read/write file and takes responsibility for closing it through Close.</summary>
    public Fpstream(string path) : this(Fpbase.OpenReadWrite(path), ownsStream: true) { }

    /// <summary>Wraps a file with readers and writers sharing one position; Close closes the file only when ownsStream is true.</summary>
    public Fpstream(FileStream stream, bool ownsStream = false)
    {
        _bp = stream;
        _ownsStream = ownsStream;
        In = new Ifpstream(stream);
        Out = new Ofpstream(stream);
    }

    private readonly bool _ownsStream;

    /// <summary>Returns the underlying file length in bytes.</summary>
    public long Filelength() => _bp.Length;

    /// <summary>Returns the shared read/write byte position in the file.</summary>
    public long Tellp() => _bp.Position;
    /// <summary>Returns the shared read/write byte position in the file.</summary>
    public long Tellg() => _bp.Position;
    /// <summary>Truncates or extends the underlying file to the specified nonnegative byte length.</summary>
    public void SetLength(long len) => _bp.SetLength(len);
    /// <summary>Flushes the underlying file and closes it only if this wrapper owns it.</summary>
    public void Close()
    {
        _bp.Flush();
        if (_ownsStream) _bp.Close();
    }
}
