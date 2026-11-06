// Root-level --version of the svres tool, run through its entry point.
using System.IO;
using System.Linq;
using System.Reflection;
using System.Xml.Linq;
using TSharpVision.Tests.Infrastructure;
using Xunit;

namespace TSharpVision.Tests.ResourceTools;

// Console output is redirected for the call.
[Collection("NonParallel")]
public sealed class VersionOptionTests
{
    [Fact]
    public void Version_PrintsToolNameAndRepositoryVersionOnly()
    {
        var (exitCode, stdout, stderr) = RunEntryPoint("--version");

        Assert.Equal(0, exitCode);
        Assert.Equal("TSharpVision ResourceTools " + RepositoryVersion() + Environment.NewLine, stdout);
        Assert.Equal("", stderr);
    }

    [Fact]
    public void Version_IsNotAnOptionAfterACommand()
    {
        var (exitCode, stdout, _) = RunEntryPoint("list", "--version");

        Assert.Equal(1, exitCode);
        Assert.DoesNotContain("TSharpVision ResourceTools ", stdout);
    }

    private static (int ExitCode, string Out, string Err) RunEntryPoint(params string[] args)
    {
        MethodInfo main = Assembly.Load("svres").EntryPoint!;
        TextWriter saveOut = Console.Out, saveErr = Console.Error;
        using var stdout = new StringWriter();
        using var stderr = new StringWriter();
        Console.SetOut(stdout);
        Console.SetError(stderr);
        try
        {
            int exitCode = (int)main.Invoke(null, new object[] { args })!;
            return (exitCode, stdout.ToString(), stderr.ToString());
        }
        finally
        {
            Console.SetOut(saveOut);
            Console.SetError(saveErr);
        }
    }

    // The central package version in Directory.Build.props.
    private static string RepositoryVersion() =>
        XDocument.Load(Path.Combine(RepositoryLayout.Root(typeof(VersionOptionTests).Assembly), "Directory.Build.props"))
            .Descendants("Version").Single().Value;
}
