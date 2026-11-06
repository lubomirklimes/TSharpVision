using System.IO;
using Xunit;

namespace TSharpVision.Tests.Infrastructure;

internal static class PathAssertions
{
    public static void Equal(string expected, string? actual)
    {
        Assert.NotNull(actual);
        Assert.Equal(ResolveLinks(expected), ResolveLinks(actual));
    }

    private static string ResolveLinks(string path)
    {
        string fullPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        string root = Path.GetPathRoot(fullPath)!;
        string current = root;

        foreach (string part in fullPath[root.Length..].Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, part);
            FileSystemInfo entry = Directory.Exists(current) ? new DirectoryInfo(current) : new FileInfo(current);
            if (entry.LinkTarget is not null)
                current = entry.ResolveLinkTarget(returnFinalTarget: true)?.FullName ?? current;
        }

        return Path.TrimEndingDirectorySeparator(Path.GetFullPath(current));
    }
}
