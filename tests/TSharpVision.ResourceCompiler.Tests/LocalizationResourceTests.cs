using System.Runtime.CompilerServices;
using TSharpVision;
using TSharpVision.ResourceCompiler;
using TSharpVision.Tests.Infrastructure;
using Xunit;

namespace TSharpVision.Tests.ResourceCompiler;

// The built-in framework translations are compiled by svrc and the resulting .tvr images
// are committed beside their .trc sources, then embedded in TSharpVision.dll (svrc
// references TSharpVision, so the library build cannot run it). These tests keep the
// committed images honest: every .trc compiles without diagnostics (duplicates and
// malformed entries are compiler errors) and byte-for-byte to the committed .tvr.
//
// These tests only read the committed images. Regenerate them after editing a .trc with:
//   dotnet build tools/TSharpVision.ResourceCompiler -c Release -t:CompileFrameworkLocalization
[Collection("NonParallel")]
public sealed class LocalizationResourceTests : IDisposable
{
    private readonly TempDirectory _tmp = new();

    public void Dispose()
    {
        _tmp.Dispose();
        Pstream.DeInitTypes();
        StreamableRegistration.RegisterAll();
    }

    private static string LocalizationDirectory([CallerFilePath] string path = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(path)!, "..", "..", "resources", "localization"));

    private static readonly string[] LanguageCodes = { "cs", "de", "en", "es", "fr", "it", "pl", "sk" };

    public static TheoryData<string> Languages()
    {
        var data = new TheoryData<string>();
        foreach (string language in LanguageCodes)
            data.Add(language);
        return data;
    }

    [Fact]
    public void LocalizationDirectory_HoldsExactlyTheEightSourcesAndImages()
    {
        string dir = LocalizationDirectory();
        string[] files = Directory.GetFiles(dir).Select(f => Path.GetFileName(f)).Order(StringComparer.Ordinal).ToArray();
        string[] expected = LanguageCodes
            .SelectMany(l => new[] { $"tsharpvision_{l}.trc", $"tsharpvision_{l}.tvr" })
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(expected, files);
    }

    [Theory]
    [MemberData(nameof(Languages))]
    public void Translation_CompilesCleanly_AndMatchesCommittedImage(string language)
    {
        string dir = LocalizationDirectory();
        string source = Path.Combine(dir, $"tsharpvision_{language}.trc");
        string committed = Path.Combine(dir, $"tsharpvision_{language}.tvr");
        string compiled = Path.Combine(_tmp.Path, $"tsharpvision_{language}.tvr");

        var result = Compiler.Compile(source, compiled);
        Assert.True(result.Success, string.Join("; ", result.Diagnostics));
        Assert.Equal(1, result.ItemsEmitted);

        byte[] fresh = File.ReadAllBytes(compiled);
        Assert.True(File.Exists(committed), $"Missing compiled translation '{committed}'.");
        Assert.True(fresh.AsSpan().SequenceEqual(File.ReadAllBytes(committed)),
            $"'{committed}' is stale. Regenerate it with: dotnet build tools/TSharpVision.ResourceCompiler -c Release -t:CompileFrameworkLocalization");
    }

    [Theory]
    [MemberData(nameof(Languages))]
    public void Compilation_IsDeterministic(string language)
    {
        string source = Path.Combine(LocalizationDirectory(), $"tsharpvision_{language}.trc");
        string first = Path.Combine(_tmp.Path, "first.tvr");
        string second = Path.Combine(_tmp.Path, "second.tvr");

        Assert.True(Compiler.Compile(source, first).Success);
        Assert.True(Compiler.Compile(source, second).Success);

        Assert.Equal(File.ReadAllBytes(first), File.ReadAllBytes(second));
    }
}
