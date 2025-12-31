namespace TSharpVision.Drivers.Terminal;

/// <summary>
/// Clipboard bridge for the ANSI terminal driver.
/// </summary>
public sealed class TerminalClipboardService : IClipboardService
{
    private readonly ITerminalClipboardCommandRunner _runner;
    private readonly TerminalClipboardPlatform _platform;

    public TerminalClipboardService()
        : this(TerminalClipboardCommandRunner.Instance, DetectPlatform()) { }

    internal TerminalClipboardService(
        ITerminalClipboardCommandRunner runner,
        TerminalClipboardPlatform platform)
    {
        _runner = runner;
        _platform = platform;
    }

    public bool IsAvailable => TryFindPair(out _);

    public string GetText()
    {
        if (!TryFindPair(out var pair)) return null;

        string text = _runner.ReadText(pair.ReadFileName, pair.ReadArgs);
        if (string.IsNullOrEmpty(text)) return null;
        return ClipboardEncoding.NormalizeFromCrLf(text);
    }

    public bool TryGetText(out string text)
    {
        text = string.Empty;
        string s = GetText();
        if (s == null) return false;
        text = s;
        return true;
    }

    public bool SetText(string text)
    {
        if (!TryFindPair(out var pair)) return false;
        return _runner.WriteText(
            pair.WriteFileName,
            pair.WriteArgs,
            text ?? string.Empty);
    }

    private bool TryFindPair(out TerminalClipboardCommandPair selected)
    {
        foreach (var pair in CandidatePairs())
        {
            if (_runner.CommandExists(pair.ReadFileName)
                && _runner.CommandExists(pair.WriteFileName))
            {
                selected = pair;
                return true;
            }
        }
        selected = default;
        return false;
    }

    private IEnumerable<TerminalClipboardCommandPair> CandidatePairs()
    {
        if (_platform == TerminalClipboardPlatform.MacOS)
        {
            yield return new TerminalClipboardCommandPair(
                "pbpaste", Array.Empty<string>(),
                "pbcopy", Array.Empty<string>());
            yield break;
        }

        if (_platform == TerminalClipboardPlatform.Linux)
        {
            yield return new TerminalClipboardCommandPair(
                "wl-paste", Array.Empty<string>(),
                "wl-copy", Array.Empty<string>());
            yield return new TerminalClipboardCommandPair(
                "xclip", new[] { "-selection", "clipboard", "-out" },
                "xclip", new[] { "-selection", "clipboard" });
            yield return new TerminalClipboardCommandPair(
                "xsel", new[] { "--clipboard", "--output" },
                "xsel", new[] { "--clipboard", "--input" });
        }
    }

    private static TerminalClipboardPlatform DetectPlatform()
    {
        if (OperatingSystem.IsMacOS()) return TerminalClipboardPlatform.MacOS;
        if (OperatingSystem.IsLinux()) return TerminalClipboardPlatform.Linux;
        return TerminalClipboardPlatform.Unsupported;
    }
}

internal enum TerminalClipboardPlatform
{
    Unsupported,
    Linux,
    MacOS,
}

internal readonly struct TerminalClipboardCommandPair
{
    public TerminalClipboardCommandPair(
        string readFileName,
        string[] readArgs,
        string writeFileName,
        string[] writeArgs)
    {
        ReadFileName = readFileName;
        ReadArgs = readArgs;
        WriteFileName = writeFileName;
        WriteArgs = writeArgs;
    }

    public string ReadFileName { get; }
    public string[] ReadArgs { get; }
    public string WriteFileName { get; }
    public string[] WriteArgs { get; }
}
