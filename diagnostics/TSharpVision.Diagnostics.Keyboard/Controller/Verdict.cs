namespace TSharpVision.Diagnostics.Keyboard.Controller;

public enum Verdict
{
    /// <summary>Observed behaviour matches the profile and the active capabilities.</summary>
    Pass,
    /// <summary>Wrong text, false or missing identity, lost or stuck modifier, or an event nobody asked for.</summary>
    Fail,
    /// <summary>Correct for this transport, which cannot report everything the step describes.</summary>
    Limited,
    /// <summary>The capability or environment makes the step irrelevant, or the operator skipped it.</summary>
    Skipped,
    /// <summary>The run itself was unsound: no events, wrong layout, target gone, IPC failure.</summary>
    Invalid,
}

/// <summary>The verdict of one step with the reasons behind it.</summary>
public sealed record StepOutcome(Verdict Verdict, IReadOnlyList<string> Reasons)
{
    public static StepOutcome Of(Verdict verdict, params string[] reasons) => new(verdict, reasons);
}

public static class Verdicts
{
    public const int ExitOk = 0;
    public const int ExitFailed = 1;
    public const int ExitInvalid = 2;

    public static string Label(Verdict verdict) => verdict.ToString().ToUpperInvariant();

    /// <summary>0 without a FAIL, 1 with one, 2 when any step was INVALID. LIMITED and SKIPPED never count.</summary>
    public static int ExitCode(IEnumerable<Verdict> verdicts)
    {
        int code = ExitOk;
        foreach (Verdict verdict in verdicts)
        {
            if (verdict == Verdict.Invalid) return ExitInvalid;
            if (verdict == Verdict.Fail) code = ExitFailed;
        }
        return code;
    }
}
