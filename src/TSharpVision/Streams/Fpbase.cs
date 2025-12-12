using System.IO;
namespace TSharpVision;

/// Common file-handling helpers shared by <see cref="Ifpstream"/>,
/// <see cref="Ofpstream"/>, and <see cref="Fpstream"/>. Upstream
/// <c>fpbase</c> wraps a <c>CLY_filebuf*</c> and exposes
/// <c>open</c>/<c>close</c>/<c>setbuf</c>/<c>rdbuf</c>; in C# the
/// underlying <see cref="FileStream"/> already provides these primitives,
/// so this class is little more than a friendly façade and a holder of
/// the path so smoke tests can reopen the same file.
public static class Fpbase
{
    // Maps OpenMode flags to a stdio fopen mode string. We translate to .NET FileMode/FileAccess.
    public static FileStream OpenRead(string path)
    {
        return new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
    }

    public static FileStream OpenWrite(string path)
    {
        // Upstream truncates by default (no CLY_IOSApp); FileMode.Create matches.
        return new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read);
    }

    public static FileStream OpenReadWrite(string path)
    {
        // Use OpenOrCreate so callers can either resume or seed a new file.
        return new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.ReadWrite);
    }
}
