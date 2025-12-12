// svrc — TSharpVision Resource Compiler.
//
// Compiles .trc text resource scripts into TSharpVision-native .tvr
// binary resource files using the existing Opstream / TResourceFile
// infrastructure.
//
// Usage:
//   svrc compile <input.trc> <output.tvr>
//
// The output .tvr can be opened at runtime with TResourceFile.

using System;
using System.IO;
using TSharpVision.ResourceCompiler;

namespace TSharpVision.ResourceCompiler;

internal static class Program
{
    private static int Main(string[] args)
    {
        Console.WriteLine("TSharpVision Resource Compiler v1\n");

        if (args.Length < 1)
        {
            PrintUsage();
            return 1;
        }

        return args[0].ToLowerInvariant() switch
        {
            "compile" => RunCompile(args),
            "format"  => RunFormat(args),
            _         => UnknownCommand(args[0]),
        };
    }

    // ── compile ───────────────────────────────────────────────────────────────

    private static int RunCompile(string[] args)
    {
        if (args.Length < 3)
        {
            Console.Error.WriteLine("Usage: svrc compile <input.trc> <output.tvr>");
            return 1;
        }

        string trcPath = args[1];
        string tvrPath = args[2];

        if (!File.Exists(trcPath))
        {
            Console.Error.WriteLine($"Error: source file not found: {trcPath}");
            return 1;
        }

        Console.WriteLine($"  Input:  {trcPath}");
        Console.WriteLine($"  Output: {tvrPath}\n");

        var result = Compiler.Compile(trcPath, tvrPath);

        foreach (var d in result.Diagnostics)
            Console.Error.WriteLine(d);

        if (!result.Success)
        {
            Console.Error.WriteLine($"\n  {result.Diagnostics.Count} error(s). Compilation failed.");
            return 1;
        }

        Console.WriteLine($"  OK — {result.ItemsEmitted} resource(s) written to '{tvrPath}'.");
        return 0;
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static int UnknownCommand(string cmd)
    {
        Console.Error.WriteLine($"Unknown command '{cmd}'.\n");
        PrintUsage();
        return 1;
    }

    // ── format ────────────────────────────────────────────────────────────────

    private static int RunFormat(string[] args)
    {
        // svrc format <file.trc> [--inplace]
        if (args.Length < 2)
        {
            Console.Error.WriteLine("Usage: svrc format <input.trc> [--inplace]");
            return 1;
        }

        string trcPath = args[1];
        bool inplace = args.Length >= 3 &&
                       args[2].Equals("--inplace", StringComparison.OrdinalIgnoreCase);

        if (!File.Exists(trcPath))
        {
            Console.Error.WriteLine($"Error: source file not found: {trcPath}");
            return 1;
        }

        var result = Compiler.FormatFile(trcPath);

        if (!result.Success)
        {
            foreach (var d in result.Diagnostics)
                Console.Error.WriteLine(d);
            Console.Error.WriteLine($"\n  {result.Diagnostics.Count} error(s). Format failed.");
            return 1;
        }

        if (inplace)
        {
            File.WriteAllText(trcPath, result.Text, System.Text.Encoding.UTF8);
            Console.WriteLine($"  OK — '{trcPath}' formatted in place.");
        }
        else
        {
            Console.Write(result.Text);
        }

        return 0;
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static void PrintUsage()
    {
        Console.WriteLine(
            "  Commands:\n" +
            "    compile <input.trc> <output.tvr>   Compile a .trc script to .tvr\n" +
            "    format  <input.trc> [--inplace]    Format .trc to canonical style\n\n" +
            "  Examples:\n" +
            "    svrc compile hello.trc hello.tvr\n" +
            "    svrc format  hello.trc\n" +
            "    svrc format  hello.trc --inplace\n");
    }
}
