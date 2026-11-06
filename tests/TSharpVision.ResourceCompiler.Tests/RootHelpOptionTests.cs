// Root-level --help and -h of the svrc tool, run through its entry point.
using System.IO;
using System.Linq;
using System.Reflection;
using TSharpVision.Tests.Infrastructure;
using Xunit;

namespace TSharpVision.Tests.ResourceCompiler;

// Console output and the current directory are redirected for the call.
[Collection("NonParallel")]
public sealed class RootHelpOptionTests
{
    [Theory]
    [InlineData("--help")]
    [InlineData("-h")]
    public void Help_PrintsTheUsageShownWithoutArguments_AndSucceeds(string option)
    {
        var help = RunEntryPoint(option);
        var noArguments = RunEntryPoint();

        Assert.Equal(0, help.ExitCode);
        Assert.Equal("", help.Err);
        Assert.Contains("compile <input.trc> <output.tvr>", help.Out);
        Assert.Equal(noArguments.Out, help.Out);
        Assert.Empty(help.Files);
    }

    [Fact]
    public void NoArguments_StillPrintsUsageAndFails()
    {
        var r = RunEntryPoint();

        Assert.Equal(1, r.ExitCode);
        Assert.Contains("compile <input.trc> <output.tvr>", r.Out);
    }

    // Only the single root argument is help; after a command it keeps the command's own handling.
    [Theory]
    [InlineData(new[] { "compile", "--help" }, "Usage: svrc compile")]
    [InlineData(new[] { "--help", "extra" }, "Unknown command '--help'")]
    [InlineData(new[] { "bogus" }, "Unknown command 'bogus'")]
    public void Help_IsNotRecognisedOutsideTheRoot(string[] args, string error)
    {
        var r = RunEntryPoint(args);

        Assert.Equal(1, r.ExitCode);
        Assert.Contains(error, r.Err);
    }

    private static (int ExitCode, string Out, string Err, string[] Files) RunEntryPoint(params string[] args)
    {
        MethodInfo main = Assembly.Load("svrc").EntryPoint!;
        TextWriter saveOut = Console.Out, saveErr = Console.Error;
        string saveDirectory = Directory.GetCurrentDirectory();
        using var directory = new TempDirectory();
        using var stdout = new StringWriter();
        using var stderr = new StringWriter();
        Console.SetOut(stdout);
        Console.SetError(stderr);
        Directory.SetCurrentDirectory(directory.Path);
        try
        {
            int exitCode = (int)main.Invoke(null, new object[] { args })!;
            return (exitCode, stdout.ToString(), stderr.ToString(), Directory.GetFileSystemEntries(directory.Path));
        }
        finally
        {
            Directory.SetCurrentDirectory(saveDirectory);
            Console.SetOut(saveOut);
            Console.SetError(saveErr);
        }
    }
}
