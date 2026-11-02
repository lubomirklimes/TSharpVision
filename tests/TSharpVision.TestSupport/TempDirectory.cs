using System.IO;

namespace TSharpVision.Tests.Infrastructure;

/// <summary>
/// Creates a unique temporary directory and deletes it (recursively) on Dispose.
/// </summary>
public sealed class TempDirectory : IDisposable
{
    public string Path { get; }

    public TempDirectory()
    {
        Path = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            "sv_test_" + System.Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
        Path = ResolveLinks(Path);
    }

    private static string ResolveLinks(string path)
    {
        string fullPath = System.IO.Path.GetFullPath(path);
        string current = System.IO.Path.GetPathRoot(fullPath) ?? string.Empty;

        foreach (string part in fullPath[current.Length..].Split(
                     [System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar],
                     StringSplitOptions.RemoveEmptyEntries))
        {
            var directory = new DirectoryInfo(System.IO.Path.Combine(current, part));
            current = directory.ResolveLinkTarget(returnFinalTarget: true)?.FullName ?? directory.FullName;
        }

        return current;
    }

    /// <summary>Creates a file inside the temp directory and returns its full path.</summary>
    public string CreateFile(string name, string content = "")
    {
        var full = System.IO.Path.Combine(Path, name);
        File.WriteAllText(full, content, System.Text.Encoding.UTF8);
        return full;
    }

    /// <summary>Creates a subdirectory inside the temp directory and returns its full path.</summary>
    public string CreateSubDir(string name)
    {
        var full = System.IO.Path.Combine(Path, name);
        Directory.CreateDirectory(full);
        return full;
    }

    public void Dispose()
    {
        try { Directory.Delete(Path, recursive: true); }
        catch { /* best-effort */ }
    }
}
