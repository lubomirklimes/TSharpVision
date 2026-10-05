using System.Text.Json;
using TSharpVision.Diagnostics.Keyboard.Profiles;
using TSharpVision.Diagnostics.Keyboard.Protocol;
using TSharpVision.Drivers;

namespace TSharpVision.Diagnostics.Keyboard.Controller;

/// <summary>What a run was: nothing in a report may be read without it.</summary>
public sealed record RunInfo(string Profile, string Driver, string Source, string Level, string Label,
    string Host, string Os, string ActiveLayout, KeyboardCapabilities Capabilities);

/// <summary>One finished step.</summary>
public sealed record StepReport(Step Step, StepOutcome Outcome, IReadOnlyList<RecordedEvent> Events);

/// <summary>The human-readable table and the optional JSON Lines file.</summary>
public static class Report
{
    private const int StepWidth = 24, ExpectedWidth = 22, ObservedWidth = 40;

    public static void WriteHeader(TextWriter output, RunInfo info, IReadOnlyList<Sources.OracleDisagreement> disagreements)
    {
        output.WriteLine($"*** {info.Label} ***");
        output.WriteLine($"Level: {info.Level}");
        output.WriteLine($"Source: {info.Source}");
        output.WriteLine($"Profile: {info.Profile}");
        output.WriteLine($"Driver: {info.Driver}");
        output.WriteLine($"Host: {info.Host}");
        output.WriteLine($"OS: {info.Os}");
        output.WriteLine($"Active layout: {info.ActiveLayout}");
        output.WriteLine($"Capabilities: {info.Capabilities}");
        // The pinned profile and the OS layout table are two witnesses; a difference between them
        // is a profile problem to resolve, not a driver failure.
        foreach (Sources.OracleDisagreement disagreement in disagreements)
            output.WriteLine($"PROFILE/ORACLE DISAGREEMENT: {disagreement}");
        output.WriteLine();
        output.WriteLine($"{"Step",-StepWidth} {"Expected",-ExpectedWidth} {"Observed",-ObservedWidth} Verdict");
        output.WriteLine(new string('-', StepWidth + ExpectedWidth + ObservedWidth + 10));
    }

    public static void WriteRow(TextWriter output, StepReport report)
    {
        output.WriteLine($"{Fit(report.Step.Id, StepWidth)} {Fit(report.Step.Expected, ExpectedWidth)} " +
            $"{Fit(Observed(report.Events), ObservedWidth)} {Verdicts.Label(report.Outcome.Verdict)}");
        foreach (string reason in report.Outcome.Reasons) output.WriteLine($"    {reason}");
        if (report.Outcome.Verdict is Verdict.Fail or Verdict.Invalid)
            foreach (RecordedEvent ev in report.Events) output.WriteLine($"      #{ev.Seq} {KeyNames.Describe(ev)}");
    }

    public static void WriteSummary(TextWriter output, IReadOnlyList<StepReport> reports,
        IReadOnlyList<string> limitations, int exitCode)
    {
        output.WriteLine();
        if (limitations.Count != 0)
        {
            output.WriteLine("Transport limitations (LIMITED, not failures):");
            foreach (string limitation in limitations) output.WriteLine($"  - {limitation}");
            output.WriteLine();
        }
        output.WriteLine(string.Join("  ", Enum.GetValues<Verdict>().Select(v =>
            $"{Verdicts.Label(v)}: {reports.Count(r => r.Outcome.Verdict == v)}")));
        output.WriteLine($"Exit code: {exitCode}");
    }

    /// <summary>The step's total text and the identity of its first press.</summary>
    public static string Observed(IReadOnlyList<RecordedEvent> events)
    {
        if (events.Count == 0) return "no events";
        RecordedEvent? press = events.FirstOrDefault(e => e.What == RecordedEvent.KeyDown);
        if (press is null) return string.Join(", ", events.Select(KeyNames.Describe));
        string text = string.Concat(events.Where(e => e.What == RecordedEvent.KeyDown).Select(e => e.Text).Distinct());
        string release = events.Any(e => e.What == RecordedEvent.KeyUp) ? ", released" : string.Empty;
        return $"text={KeyNames.Quote(text)}, key={KeyNames.Describe(press.KeyCode)}{release}";
    }

    /// <summary>Facts about the transport that hold for the whole run rather than for one step.</summary>
    public static IReadOnlyList<string> Limitations(KeyboardCapabilities capabilities, IEnumerable<StepReport> reports,
        string? negotiation)
    {
        var notes = new List<string>();
        if (!capabilities.HasFlag(KeyboardCapabilities.KeyReleaseEvents)) notes.Add("no key release events");
        if (!capabilities.HasFlag(KeyboardCapabilities.StandaloneModifierTransitions))
            notes.Add("no standalone modifier transitions");
        if (!capabilities.HasFlag(KeyboardCapabilities.DistinctNumericKeypad))
            notes.Add("the numeric keypad aliases main keys");
        RecordedEvent[] presses = reports.SelectMany(r => r.Events)
            .Where(e => e.What == RecordedEvent.KeyDown).ToArray();
        if (presses.Length != 0 && presses.All(e => e.RawScan == 0)) notes.Add("raw scan code unavailable");
        if (negotiation != null) notes.Add(negotiation);
        return notes;
    }

    public static void WriteJsonLines(string path, RunInfo info, IReadOnlyList<StepReport> reports,
        IReadOnlyList<string> limitations, IReadOnlyList<Sources.OracleDisagreement> disagreements, int exitCode)
    {
        var options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
        using var writer = new StreamWriter(path);
        void Line(object value) => writer.WriteLine(JsonSerializer.Serialize(value, options));

        Line(new
        {
            Type = "run", info.Label, info.Level, info.Source, info.Profile, info.Driver, info.Host, info.Os,
            info.ActiveLayout, Capabilities = (int)info.Capabilities, ProtocolVersion = Wire.Version,
            OracleDisagreements = disagreements.Select(d => d.ToString()),
        });
        foreach (StepReport report in reports)
            Line(new
            {
                Type = "step", report.Step.Id, report.Step.Suite, report.Step.Expected,
                Verdict = Verdicts.Label(report.Outcome.Verdict), report.Outcome.Reasons, report.Events,
            });
        Line(new
        {
            Type = "summary", Limitations = limitations, ExitCode = exitCode,
            Counts = Enum.GetValues<Verdict>().ToDictionary(Verdicts.Label,
                v => reports.Count(r => r.Outcome.Verdict == v)),
        });
    }

    private static string Fit(string value, int width) =>
        value.Length <= width ? value.PadRight(width) : value[..(width - 1)] + "~";
}
