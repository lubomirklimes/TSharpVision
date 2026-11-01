using System.Diagnostics;
using System.Security.Cryptography;
using TSharpVision.Tests.Infrastructure;
using Xunit;
using Xunit.Abstractions;

namespace TSharpVision.Tests.ResourceCompiler;

/// <summary>Runs only with <c>TSV_RUN_TOOLING_TESTS=1</c> (it builds the compiler, which takes a while).</summary>
public sealed class ToolingFactAttribute : FactAttribute
{
    public const string EnvironmentVariable = "TSV_RUN_TOOLING_TESTS";

    public ToolingFactAttribute()
    {
        if (Environment.GetEnvironmentVariable(EnvironmentVariable) != "1")
            Skip = $"Tooling build check: set {EnvironmentVariable}=1.";
    }
}

/// <summary>
/// TV12r regression check for the <c>CompileFrameworkLocalization</c> target of tools/TSharpVision.ResourceCompiler
/// under redirected build output: with <c>--artifacts-path</c> and with MSBuild <c>UseArtifactsOutput</c>/<c>ArtifactsPath</c>,
/// each with a scratch <c>FrameworkLocalizationOutputDir</c>. Every built-in language must compile to the exact bytes
/// of the checked-in image, and the checked-in sources and images must not be written. Only the temporary directory
/// this test creates is deleted.
/// </summary>
public sealed class FrameworkLocalizationBuildTests(ITestOutputHelper output)
{
    private static readonly string[] Languages = ["cs", "de", "en", "es", "fr", "it", "pl", "sk"];

    private static Dictionary<string, string> Hashes(string directory) =>
        Directory.GetFiles(directory, "tsharpvision_*.*")
            .Where(f => f.EndsWith(".trc", StringComparison.Ordinal) || f.EndsWith(".tvr", StringComparison.Ordinal))
            .ToDictionary(f => Path.GetFileName(f), f => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(f))));

    private void Run(string repo, IReadOnlyDictionary<string, string> environment, params string[] arguments)
    {
        var start = new ProcessStartInfo("dotnet") { WorkingDirectory = repo, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (string a in arguments) start.ArgumentList.Add(a);
        foreach (var (k, v) in environment) start.Environment[k] = v;
        using var process = Process.Start(start)!;
        string stdout = process.StandardOutput.ReadToEnd(), stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();
        output.WriteLine(stdout);
        Assert.True(process.ExitCode == 0, $"dotnet {string.Join(' ', arguments)} failed ({process.ExitCode}):\n{stdout}\n{stderr}");
    }

    [ToolingFact]
    public void Redirected_output_compiles_every_language_to_the_checked_in_bytes_without_writing_sources()
    {
        string repo = RepositoryLayout.Root(typeof(FrameworkLocalizationBuildTests).Assembly);
        string tracked = Path.Combine(repo, "resources", "localization");
        string project = Path.Combine(repo, "tools", "TSharpVision.ResourceCompiler", "TSharpVision.ResourceCompiler.csproj");
        var before = Hashes(tracked);
        Assert.Equal(16, before.Count);

        string temp = Path.Combine(Path.GetTempPath(), "tsv-framework-l10n-" + Guid.NewGuid().ToString("N")[..12]);
        Directory.CreateDirectory(temp);
        try
        {
            string viaOption = Path.Combine(temp, "out-artifacts-path");
            Run(repo, new Dictionary<string, string>(), "build", project, "-c", "Release", "-t:CompileFrameworkLocalization",
                "--artifacts-path", Path.Combine(temp, "artifacts-a"), "-p:FrameworkLocalizationOutputDir=" + viaOption, "-nologo");

            string viaMsBuild = Path.Combine(temp, "out-use-artifacts-output");
            Run(repo, new Dictionary<string, string> { ["UseArtifactsOutput"] = "true", ["ArtifactsPath"] = Path.Combine(temp, "artifacts-b") },
                "msbuild", project, "-restore", "-p:Configuration=Release", "-t:CompileFrameworkLocalization",
                "-p:FrameworkLocalizationOutputDir=" + viaMsBuild, "-nologo");

            foreach (string outputDir in new[] { viaOption, viaMsBuild })
            {
                var produced = Hashes(outputDir);
                Assert.Equal(Languages.Select(l => $"tsharpvision_{l}.tvr").Order(), produced.Keys.Order());
                foreach (var (name, hash) in produced)
                    Assert.True(before[name] == hash, $"{outputDir}/{name} differs from the checked-in image");
            }
            Assert.Equal(before, Hashes(tracked));
        }
        finally
        {
            Directory.Delete(temp, recursive: true);
        }
    }
}
