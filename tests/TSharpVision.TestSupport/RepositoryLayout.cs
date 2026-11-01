using System.Reflection;

namespace TSharpVision.Tests.Infrastructure;

/// <summary>
/// Finds the repository root for tests that read checked-in sources, in the normal layout (output under the
/// repository's bin/) and with redirected output (<c>--artifacts-path</c>, <c>UseArtifactsOutput</c>) outside it.
/// </summary>
/// <remarks>
/// 1. Walk up from <see cref="AppContext.BaseDirectory"/> to the first directory holding <see cref="Marker"/>.
/// 2. Otherwise use the output-relative path the test project records at build time as assembly metadata
///    <see cref="MetadataKey"/> (a relative path, so no absolute path is embedded), verified by the same marker.
/// 3. Otherwise fail with the locations that were tried.
/// </remarks>
public static class RepositoryLayout
{
    /// <summary>The file that identifies the repository root.</summary>
    public const string Marker = "TSharpVision.slnx";

    /// <summary>Assembly metadata key holding the path from the test output directory to the repository root.</summary>
    public const string MetadataKey = "TSharpVision.RepositoryRootFromOutput";

    /// <summary>The repository root for the test assembly <paramref name="testAssembly"/>.</summary>
    /// <exception cref="InvalidOperationException">Neither location holds <see cref="Marker"/>.</exception>
    public static string Root(Assembly testAssembly)
    {
        ArgumentNullException.ThrowIfNull(testAssembly);
        string output = AppContext.BaseDirectory;
        for (var dir = new DirectoryInfo(output); dir != null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, Marker))) return dir.FullName;

        string? relative = testAssembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(a => a.Key == MetadataKey)?.Value;
        string? candidate = string.IsNullOrEmpty(relative) ? null : Path.GetFullPath(Path.Combine(output, relative));
        if (candidate != null && File.Exists(Path.Combine(candidate, Marker)))
            return candidate.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        throw new InvalidOperationException(
            $"Repository root not found: no {Marker} above the test output '{output}'" +
            (candidate == null
                ? $", and assembly '{testAssembly.GetName().Name}' records no '{MetadataKey}' metadata."
                : $", nor at '{candidate}' recorded in '{MetadataKey}'."));
    }
}
