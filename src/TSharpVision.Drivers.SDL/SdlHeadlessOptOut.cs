namespace TSharpVision.Drivers.SDL;

/// <summary>
/// The headless opt-out shared by both SDL drivers: when set to <c>1</c>, initialization skips SDL
/// entirely and the driver stays detached. Read on every initialization attempt, so tests can toggle it.
/// </summary>
internal static class SdlHeadlessOptOut
{
    /// <summary>The documented name, uppercase like every other TSharpVision variable; names are case-sensitive on macOS and Linux.</summary>
    internal const string EnvironmentVariable = "TSHARPVISION_NO_SDL";
    // The mixed-case spelling the drivers once read, still honoured when the documented name is unset.
    internal const string LegacyEnvironmentVariable = "TSharpVision_NO_SDL";

    internal static bool IsRequested() => IsRequested(Environment.GetEnvironmentVariable);

    internal static bool IsRequested(Func<string, string?> getVariable)
    {
        string? value = getVariable(EnvironmentVariable) ?? getVariable(LegacyEnvironmentVariable);
        return string.Equals(value, "1", StringComparison.Ordinal);
    }
}
