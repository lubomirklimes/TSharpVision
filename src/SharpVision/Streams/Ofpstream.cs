using System.IO;
namespace SharpVision;

/// Write-only file polymorphic stream. Mirrors upstream <c>ofpstream</c>:
/// a thin subclass of <see cref="Opstream"/> whose ctor opens a file in
/// binary write mode (truncating any existing content).
public class Ofpstream : Opstream
{
    private readonly bool _ownsStream;

    public Ofpstream(string path) : base(Fpbase.OpenWrite(path))
    {
        _ownsStream = true;
    }

    public Ofpstream(Stream s) : base(s)
    {
        _ownsStream = false;
    }

    public void Close()
    {
        if (bp != null)
        {
            bp.Flush();
            if (_ownsStream) bp.Close();
        }
    }
}
