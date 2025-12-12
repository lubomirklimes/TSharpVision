// TSharpVision Resource Compiler
// .tvr emitter — writes compiled resources to a TResourceFile.
// Accepts any TStreamable (TDialog, TMenuBar, TStatusLine).
using System.IO;
using System.Linq;

namespace TSharpVision.ResourceCompiler;

/// <summary>
/// Opens (or creates) a <c>.tvr</c> file and writes a list of compiled
/// (key, object) pairs into it via <see cref="TResourceFile"/>.
/// </summary>
public static class Emitter
{
    /// <summary>
    /// Writes <paramref name="resources"/> to <paramref name="tvrPath"/>.
    /// Any existing file at that path is overwritten.
    /// Accepts any <see cref="TStreamable"/> object (TDialog, TMenuBar, TStatusLine, …).
    /// </summary>
    public static void Emit(
        string tvrPath,
        IEnumerable<(string key, TStreamable obj)> resources)
    {
        // Ensure the directory exists.
        string dir = Path.GetDirectoryName(tvrPath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        // Overwrite: delete any existing file so TResourceFile gets a fresh start.
        if (File.Exists(tvrPath)) File.Delete(tvrPath);

        var fp = new Fpstream(tvrPath);
        var rf = new TResourceFile(fp);

        foreach (var (key, obj) in resources)
            rf.Put(obj, key);

        rf.Flush();
        fp.Close();

        // Call ShutDown on every TView-derived object to release TV ownership chains.
        foreach (var (_, obj) in resources)
        {
            if (obj is TView v)
                try { v.ShutDown(); } catch { /* best effort */ }
        }
    }

    // Backward-compat overload used by any callers that still pass (string, TDialog) tuples.
    public static void Emit(
        string tvrPath,
        IEnumerable<(string key, TDialog dialog)> resources)
        => Emit(tvrPath, resources.Select(r => (r.key, (TStreamable)r.dialog)));
}

