// Opt-in end-to-end check that NuGet-only consumers get the built-in translations.
//
// Packs src/TSharpVision into a private temp feed, packs a temp library that depends on
// TSharpVision, publishes a temp console app that references only that library (so
// TSharpVision flows transitively), and runs the published app from outside the repo with
// an app .cfg selecting the language, then switches language through the public
// TSharpVisionIntl.UseBuiltInLanguage call. Restore uses only the temp feed and a private
// NuGet packages folder (the TSharpVision pack itself restores like the normal repo build).
//
// Slow (several dotnet invocations), so it only runs when TSV_RUN_PACKAGE_CONSUMER_TESTS=1:
//   TSV_RUN_PACKAGE_CONSUMER_TESTS=1 dotnet test tests/TSharpVision.Tests --filter FullyQualifiedName~PackageConsumerLocalizationTests
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;
using Xunit;
using Xunit.Abstractions;

namespace TSharpVision.Tests.Localization;

public sealed class PackageConsumerLocalizationTests(ITestOutputHelper output)
{
    private const string OptInVariable = "TSV_RUN_PACKAGE_CONSUMER_TESTS";

    private static string RepoRoot([CallerFilePath] string path = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(path)!, "..", "..", ".."));

    // Prerelease of the repository's family version (Directory.Build.props), so the private test packages sort
    // below the real release and never go stale when the version is bumped.
    private static string RepositoryVersion() =>
        System.Xml.Linq.XDocument.Load(Path.Combine(RepoRoot(), "Directory.Build.props"))
            .Descendants("Version").Single().Value.Trim();

    [Fact]
    public void PackedTSharpVision_DeliversTranslations_ToTransitiveConsumer()
    {
        if (Environment.GetEnvironmentVariable(OptInVariable) != "1")
            return; // Opt-in; see the file header.

        string root = Path.Combine(Path.GetTempPath(), "tsv-loc-consumer-" + Guid.NewGuid().ToString("N")[..12]);
        string feed = Path.Combine(root, "feed");
        string packages = Path.Combine(root, "packages");
        string version = RepositoryVersion() + "-loccheck." + DateTime.UtcNow.ToString("yyyyMMddHHmmss");
        Directory.CreateDirectory(feed);
        try
        {
            Run(RepoRoot(), "pack", Path.Combine(RepoRoot(), "src", "TSharpVision", "TSharpVision.csproj"),
                "-c", "Release", "-o", feed, "--artifacts-path", Path.Combine(root, "artifacts"),
                "-p:Version=" + version, "-nologo");

            File.WriteAllText(Path.Combine(root, "NuGet.config"), $"""
                <?xml version="1.0" encoding="utf-8"?>
                <configuration>
                  <packageSources>
                    <clear />
                    <add key="local" value="{feed}" />
                  </packageSources>
                </configuration>
                """);
            // Keep the temp projects out of any Directory.Build.* / central package management above them.
            File.WriteAllText(Path.Combine(root, "Directory.Build.props"), "<Project />");
            File.WriteAllText(Path.Combine(root, "Directory.Build.targets"), "<Project />");
            File.WriteAllText(Path.Combine(root, "Directory.Packages.props"),
                "<Project><PropertyGroup><ManagePackageVersionsCentrally>false</ManagePackageVersionsCentrally></PropertyGroup></Project>");

            string middle = Path.Combine(root, "middle");
            Directory.CreateDirectory(middle);
            File.WriteAllText(Path.Combine(middle, "TsvLocMiddle.csproj"), $"""
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <TargetFramework>net10.0</TargetFramework>
                    <Nullable>enable</Nullable>
                    <PackageId>TsvLocMiddle</PackageId>
                    <Version>1.0.0</Version>
                    <IsPackable>true</IsPackable>
                  </PropertyGroup>
                  <ItemGroup>
                    <PackageReference Include="TSharpVision" Version="{version}" />
                  </ItemGroup>
                </Project>
                """);
            File.WriteAllText(Path.Combine(middle, "Labels.cs"), """
                namespace TsvLocMiddle;
                public static class Labels
                {
                    public static string Cancel() => TSharpVision.TSharpVisionIntl.Get("Btn_Cancel", "~C~ancel");
                }
                """);
            Run(root, "pack", Path.Combine(middle, "TsvLocMiddle.csproj"), "-c", "Release", "-o", feed,
                "--packages", packages, "-nologo");

            string app = Path.Combine(root, "app");
            Directory.CreateDirectory(app);
            File.WriteAllText(Path.Combine(app, "TsvLocConsumer.csproj"), """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <OutputType>Exe</OutputType>
                    <TargetFramework>net10.0</TargetFramework>
                    <Nullable>enable</Nullable>
                  </PropertyGroup>
                  <ItemGroup>
                    <PackageReference Include="TsvLocMiddle" Version="1.0.0" />
                    <None Update="TsvLocConsumer.cfg" CopyToOutputDirectory="PreserveNewest" CopyToPublishDirectory="PreserveNewest" />
                  </ItemGroup>
                </Project>
                """);
            File.WriteAllText(Path.Combine(app, "Program.cs"), """
                using System;
                using System.IO;
                using TSharpVision;
                var config = TSharpVisionRuntime.Configure("consumer");
                Console.OutputEncoding = System.Text.Encoding.UTF8;
                Console.WriteLine("language=" + config.Localization.Language);
                Console.WriteLine("middle=" + TsvLocMiddle.Labels.Cancel());
                Console.WriteLine("title=" + TSharpVisionIntl.Get("MsgTitle_Confirm", "Confirm"));
                Console.WriteLine("files=" + string.Join(",", Directory.GetFiles(AppContext.BaseDirectory, "*.tvr")));
                Console.WriteLine("languages=" + string.Join(",", TSharpVisionIntl.BuiltInLanguages));
                Console.WriteLine("runtime=" + TSharpVisionIntl.UseBuiltInLanguage("pl-PL") + ":" + TsvLocMiddle.Labels.Cancel());
                Console.WriteLine("unsupported=" + TSharpVisionIntl.UseBuiltInLanguage("xx") + ":" + TsvLocMiddle.Labels.Cancel());
                """);
            File.WriteAllText(Path.Combine(app, "TsvLocConsumer.cfg"), "[localization]\nlanguage=cs\n");

            string deploy = Path.Combine(root, "deploy");
            Run(root, "publish", Path.Combine(app, "TsvLocConsumer.csproj"), "-c", "Release", "-o", deploy,
                "--packages", packages, "-nologo");

            Assert.True(File.Exists(Path.Combine(deploy, "TSharpVision.dll")));
            Assert.Empty(Directory.GetFiles(deploy, "*.tvr"));
            output.WriteLine("deploy: " + string.Join(", ", Directory.GetFiles(deploy).Select(Path.GetFileName)));

            string czech = Run(Path.GetTempPath(), Path.Combine(deploy, "TsvLocConsumer.dll"));
            output.WriteLine(czech);
            Assert.Contains("language=cs", czech);
            Assert.Contains("middle=~Z~rušit", czech);
            Assert.Contains("title=Potvrzení", czech);
            // The public runtime switch reaches the embedded tables from a NuGet-only consumer.
            Assert.Contains("languages=cs,de,en,es,fr,it,pl,sk", czech);
            Assert.Contains("runtime=True:~A~nuluj", czech);
            Assert.Contains("unsupported=False:~A~nuluj", czech);

            File.WriteAllText(Path.Combine(deploy, "TsvLocConsumer.cfg"), "[localization]\nlanguage=de\n");
            string german = Run(Path.GetTempPath(), Path.Combine(deploy, "TsvLocConsumer.dll"));
            output.WriteLine(german);
            Assert.Contains("middle=~A~bbrechen", german);

            File.Delete(Path.Combine(deploy, "TsvLocConsumer.cfg"));
            string english = Run(Path.GetTempPath(), Path.Combine(deploy, "TsvLocConsumer.dll"));
            output.WriteLine(english);
            Assert.Contains("middle=~C~ancel", english);
            Assert.Contains("title=Confirm", english);
            Assert.Contains("runtime=True:~A~nuluj", english);
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }

    private static string Run(string workingDirectory, params string[] args)
    {
        var psi = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            UseShellExecute = false,
        };
        foreach (string arg in args)
            psi.ArgumentList.Add(arg);
        psi.Environment["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1";
        psi.Environment["DOTNET_NOLOGO"] = "1";
        psi.Environment.Remove("MSBuildExtensionsPath");
        psi.Environment.Remove("MSBUILD_EXE_PATH");
        psi.Environment.Remove("MSBuildSDKsPath");

        using var process = Process.Start(psi)!;
        var stderr = process.StandardError.ReadToEndAsync();
        string stdout = process.StandardOutput.ReadToEnd();
        Assert.True(process.WaitForExit(TimeSpan.FromMinutes(10)), "dotnet " + string.Join(' ', args) + " timed out.");
        string output = stdout + stderr.Result;
        Assert.True(process.ExitCode == 0, $"dotnet {string.Join(' ', args)} failed ({process.ExitCode}):\n{output}");
        return output;
    }
}
