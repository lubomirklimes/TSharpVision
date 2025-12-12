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
    public readonly Ipstream In;
    public readonly Opstream Out;

    public Fpstream(string path) : this(Fpbase.OpenReadWrite(path), ownsStream: true) { }

    public Fpstream(FileStream stream, bool ownsStream = false)
    {
        _bp = stream;
        _ownsStream = ownsStream;
        In = new Ifpstream(stream);
        Out = new Ofpstream(stream);
    }

    private readonly bool _ownsStream;

    public long Filelength() => _bp.Length;

    public long Tellp() => _bp.Position;
    public long Tellg() => _bp.Position;
    // Truncate (or extend) the underlying file to exactly <paramref name="len"/> bytes.
    // Used by TResourceFile.Pack() to reclaim space after compaction.
    public void SetLength(long len) => _bp.SetLength(len);
    public void Close()
    {
        _bp.Flush();
        if (_ownsStream) _bp.Close();
    }
}
