// TSharpVision Resource Tools
// CLI entry point for svres: list / show / dump commands.
using System;
using TSharpVision.ResourceTools;

const string Usage = @"svres — TSharpVision .tvr resource inspector

Usage:
  svres list     <file.tvr>
  svres show     <file.tvr> <key>
  svres dump     <file.tvr> <key> [--hex]
  svres validate <file.tvr>

Commands:
  list      List all resources in a .tvr file (key, position, size, type).
  show      Show metadata and interpreted summary for one resource.
  dump      Dump raw payload bytes for a resource; --hex for hex output.
  validate  Validate all resources in a .tvr file; exit 0 = pass, 1 = fail.

Exit codes:
  0  success
  1  user/input error";

if (args.Length == 0)
{
    Console.Error.WriteLine(Usage);
    return 1;
}

string command = args[0].ToLowerInvariant();

CommandResult result;

switch (command)
{
    case "list":
        if (args.Length < 2)
        {
            Console.Error.WriteLine("svres list: missing <file.tvr>");
            Console.Error.WriteLine();
            Console.Error.WriteLine(Usage);
            return 1;
        }
        result = InspectorCommands.List(args[1]);
        break;

    case "show":
        if (args.Length < 3)
        {
            Console.Error.WriteLine("svres show: missing <file.tvr> and/or <key>");
            Console.Error.WriteLine();
            Console.Error.WriteLine(Usage);
            return 1;
        }
        result = InspectorCommands.Show(args[1], args[2]);
        break;

    case "dump":
        if (args.Length < 3)
        {
            Console.Error.WriteLine("svres dump: missing <file.tvr> and/or <key>");
            Console.Error.WriteLine();
            Console.Error.WriteLine(Usage);
            return 1;
        }
        {
            bool hexMode = false;
            for (int i = 3; i < args.Length; i++)
            {
                if (args[i] == "--hex") { hexMode = true; continue; }
                Console.Error.WriteLine($"svres dump: unknown option: {args[i]}");
                return 1;
            }
            result = InspectorCommands.Dump(args[1], args[2], hexMode);
        }
        break;

    case "validate":
        if (args.Length < 2)
        {
            Console.Error.WriteLine("svres validate: missing <file.tvr>");
            Console.Error.WriteLine();
            Console.Error.WriteLine(Usage);
            return 1;
        }
        result = InspectorCommands.Validate(args[1]);
        break;

    default:
        Console.Error.WriteLine($"svres: unknown command: {command}");
        Console.Error.WriteLine();
        Console.Error.WriteLine(Usage);
        return 1;
}

if (!result.Success)
{
    Console.Error.WriteLine(result.Error);
    return result.ExitCode;
}

if (!string.IsNullOrEmpty(result.Output))
    Console.WriteLine(result.Output);

return 0;
