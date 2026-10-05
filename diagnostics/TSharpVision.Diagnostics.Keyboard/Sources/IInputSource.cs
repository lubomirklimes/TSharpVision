using TSharpVision.Diagnostics.Keyboard.Profiles;
using TSharpVision.Diagnostics.Keyboard.Protocol;

namespace TSharpVision.Diagnostics.Keyboard.Sources;

/// <summary>How to start the target role of this executable.</summary>
public sealed record TargetLaunch(string FileName, IReadOnlyList<string> Arguments,
    IReadOnlyDictionary<string, string> Environment)
{
    /// <summary>The running executable relaunched as the target, with the session passed by environment only.</summary>
    public static TargetLaunch ForThisExecutable(IReadOnlyDictionary<string, string> environment)
    {
        string process = System.Environment.ProcessPath
            ?? throw new InvalidOperationException("The path of the running executable is unknown.");
        var arguments = new List<string>();
        // "dotnet App.dll": the host needs the assembly again; an apphost does not.
        if (Path.GetFileNameWithoutExtension(process).Equals("dotnet", StringComparison.OrdinalIgnoreCase))
            arguments.Add(typeof(TargetLaunch).Assembly.Location);
        arguments.Add("--target");
        return new TargetLaunch(process, arguments, environment);
    }
}

/// <summary>What a result from a source proves. The two injected levels must never be confused.</summary>
public static class SourceLabels
{
    public const string RealLayout = "REAL LAYOUT E2E";
    public const string NativeInjected = "NATIVE INJECTED — LAYOUT BYPASSED";
    public const string TransportBytes = "TRANSPORT BYTES — NO LAYOUT INVOLVED";
}

/// <summary>The run cannot continue soundly: the step is INVALID and nothing more is delivered.</summary>
public sealed class InputAbortedException(string reason) : Exception(reason);

/// <summary>Owns the target process and delivers the input of one step to it.</summary>
public interface IInputSource : IDisposable
{
    string Name { get; }
    /// <summary>The test level and path, for the report.</summary>
    string Level { get; }
    /// <summary>One of <see cref="SourceLabels"/>.</summary>
    string Label { get; }
    /// <summary>The target draws on the controller's own terminal, so the controller must stay silent while it runs.</summary>
    bool SharesConsole { get; }
    /// <summary>A person performs the steps: prompts, generous timing, operator skip.</summary>
    bool Interactive { get; }
    /// <summary>Input goes through the active layout, so the layout must be verified before the suite.</summary>
    bool VerifiesLayout { get; }
    /// <summary>The steps are byte sequences for a terminal rather than key positions of a profile.</summary>
    bool TransportSteps { get; }
    int SettleMs { get; }
    int TimeoutMs { get; }

    void Start(TargetLaunch launch);
    /// <summary>The target's process id when the launched process is the target itself; null otherwise.</summary>
    int? TargetProcessId { get; }
    bool TargetExited { get; }

    /// <summary>
    /// Called once the target has identified itself. Returns why this source cannot drive that target
    /// (the run is then INVALID), or null.
    /// </summary>
    string? Attach(PipeChannel channel, Hello hello, LayoutProfile profile, TextWriter output);

    /// <summary>Waits for anything the source negotiates with the target's driver before the first step.</summary>
    bool WaitReady(TimeSpan timeout);

    /// <summary>Why the step is not something this source can perform (it is then SKIPPED), or null.</summary>
    string? CannotDeliver(Step step);

    /// <summary>Delivers the step's input. A human source has nothing to do: the target shows the prompt.</summary>
    /// <exception cref="InputAbortedException">Delivery was stopped for safety; the run ends INVALID.</exception>
    void Deliver(Step step, int stepId);
}
