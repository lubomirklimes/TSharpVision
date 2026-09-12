using System.IO;
namespace TSharpVision;

/// Write-only file polymorphic stream. Mirrors upstream <c>ofpstream</c>:
/// a thin subclass of <see cref="Opstream"/> whose ctor opens a file in
/// binary write mode (truncating any existing content).
public class Ofpstream : Opstream
{
    private readonly bool _ownsStream;

    /// <summary>Creates or truncates a file for object output and owns its lifetime until Close.</summary>
    public Ofpstream(string path) : base(Fpbase.OpenWrite(path))
    {
        _ownsStream = true;
    }

    /// <summary>Writes objects to a caller-owned stream without taking responsibility for closing it.</summary>
    public Ofpstream(Stream s) : base(s)
    {
        _ownsStream = false;
    }

    /// <summary>Flushes output and closes the underlying stream only when this writer opened it from a path.</summary>
    public void Close()
    {
        if (bp != null)
        {
            bp.Flush();
            if (_ownsStream) bp.Close();
        }
    }
}
