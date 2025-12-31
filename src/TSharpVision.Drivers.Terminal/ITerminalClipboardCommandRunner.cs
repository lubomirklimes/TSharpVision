using System.Diagnostics;
using System.Text;

namespace TSharpVision.Drivers.Terminal;

internal interface ITerminalClipboardCommandRunner
{
    bool CommandExists(string fileName);
    string ReadText(string fileName, string[] args);
    bool WriteText(string fileName, string[] args, string text);
}

internal sealed class TerminalClipboardCommandRunner : ITerminalClipboardCommandRunner
{
    private const int TimeoutMs = 1500;

    public static readonly TerminalClipboardCommandRunner Instance = new();

    private TerminalClipboardCommandRunner() { }

    public bool CommandExists(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName)) return false;
        if (Path.IsPathRooted(fileName)) return File.Exists(fileName);

        string path = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrEmpty(path)) return false;

        foreach (string dir in path.Split(Path.PathSeparator))
        {
            if (string.IsNullOrWhiteSpace(dir)) continue;
            try
            {
                string candidate = Path.Combine(dir, fileName);
                if (File.Exists(candidate)) return true;
            }
            catch
            {
                // Ignore malformed PATH entries.
            }
        }
        return false;
    }

    public string ReadText(string fileName, string[] args)
    {
        try
        {
            using var process = Start(fileName, args, redirectInput: false);
            Task<string> output = process.StandardOutput.ReadToEndAsync();
            Task<string> error = process.StandardError.ReadToEndAsync();

            if (!process.WaitForExit(TimeoutMs))
            {
                TryKill(process);
                return null;
            }

            _ = error.GetAwaiter().GetResult();
            if (process.ExitCode != 0) return null;
            return output.GetAwaiter().GetResult();
        }
        catch
        {
            return null;
        }
    }

    public bool WriteText(string fileName, string[] args, string text)
    {
        try
        {
            using var process = Start(fileName, args, redirectInput: true);
            process.StandardInput.Write(text ?? string.Empty);
            process.StandardInput.Close();
            Task<string> output = process.StandardOutput.ReadToEndAsync();
            Task<string> error = process.StandardError.ReadToEndAsync();

            if (!process.WaitForExit(TimeoutMs))
            {
                TryKill(process);
                return false;
            }

            _ = output.GetAwaiter().GetResult();
            _ = error.GetAwaiter().GetResult();
            return process.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    private static Process Start(string fileName, string[] args, bool redirectInput)
    {
        var info = new ProcessStartInfo(fileName)
        {
            UseShellExecute = false,
            RedirectStandardInput = redirectInput,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };

        if (redirectInput)
            info.StandardInputEncoding = Encoding.UTF8;

        foreach (string arg in args)
            info.ArgumentList.Add(arg);

        var process = new Process { StartInfo = info };
        process.Start();
        return process;
    }

    private static void TryKill(Process process)
    {
        try { process.Kill(entireProcessTree: true); }
        catch { }
    }
}
