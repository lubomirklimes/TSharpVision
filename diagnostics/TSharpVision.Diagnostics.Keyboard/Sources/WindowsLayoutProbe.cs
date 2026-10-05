using System.Runtime.Versioning;
using System.Text;
using Microsoft.Win32;
using TSharpVision.Diagnostics.Keyboard.Profiles;

namespace TSharpVision.Diagnostics.Keyboard.Sources;

/// <summary>The keyboard layout of a window's input thread, as far as it can be read from outside.</summary>
/// <param name="Klid">The layout identifier such as <c>00010405</c>; empty when it cannot be resolved.</param>
public sealed record ActiveLayout(long Hkl, string Klid, string Name)
{
    public override string ToString() =>
        Hkl == 0 ? "unknown" : $"{(Klid.Length != 0 ? Klid : "?")} {Name} (HKL 0x{Hkl:X8})".Replace("  ", " ");
}

/// <summary>What the operating system says one key produces on one level.</summary>
public readonly record struct OracleText(string Text, bool Dead);

/// <summary>One place where the pinned profile and the operating system's own layout table differ.</summary>
public sealed record OracleDisagreement(PhysicalKey Key, KeyLevel Level, string Profile, string System)
{
    public override string ToString() => $"{Level} {Key}: profile {Profile}, OS layout {System}";
}

/// <summary>Layout verification rules. Pure, so they are tested without a keyboard.</summary>
public static class LayoutCheck
{
    /// <summary>
    /// Why the run must not proceed, or null. The identifier is compared only where the query is
    /// reliable; otherwise the behavioural fingerprint decides.
    /// </summary>
    public static string? Mismatch(LayoutProfile profile, ActiveLayout active, bool reliable)
    {
        if (!reliable || active.Klid.Length == 0) return null;
        return string.Equals(active.Klid, profile.LayoutId, StringComparison.OrdinalIgnoreCase)
            ? null
            : $"active layout is not {profile.Name} (the target's layout is {active})";
    }

    /// <summary>The fingerprint verdict: the text of a discriminating key must be exactly the profile's.</summary>
    public static string? FingerprintMismatch(LayoutProfile profile, string expected, string observed) =>
        observed == expected ? null : $"active layout is not {profile.Name}";

    /// <summary>
    /// Compares every level the profile pins with an independent oracle. A difference is a profile
    /// problem to report, never a driver failure and never a silent correction of the profile.
    /// </summary>
    public static IReadOnlyList<OracleDisagreement> Disagreements(
        LayoutProfile profile, Func<PhysicalKey, KeyLevel, OracleText?> oracle)
    {
        var differences = new List<OracleDisagreement>();
        foreach ((PhysicalKey key, KeyLevels levels) in profile.Keys.OrderBy(k => k.Key))
            foreach (KeyLevel level in Enum.GetValues<KeyLevel>())
            {
                string? pinned = level switch { KeyLevel.Plain => levels.Plain, KeyLevel.Shift => levels.Shift, _ => levels.AltGr };
                bool dead = profile.IsDead(key, level);
                if (oracle(key, level) is not { } system) continue;
                if ((pinned ?? string.Empty) == system.Text && dead == system.Dead) continue;
                differences.Add(new OracleDisagreement(key, level, Describe(pinned, dead), Describe(system.Text, system.Dead)));
            }
        return differences;
    }

    private static string Describe(string? text, bool dead) =>
        string.IsNullOrEmpty(text) ? "nothing" : (dead ? "dead " : string.Empty) + $"\"{text}\"";
}

/// <summary>
/// Read-only Windows layout queries. Nothing is installed, loaded or activated: the layout is whatever
/// the operator selected, and it must already be loaded for the oracle to be asked about it.
/// </summary>
[SupportedOSPlatform("windows")]
public static class WindowsLayoutProbe
{
    /// <summary>The layout of the thread that owns <paramref name="window"/>.</summary>
    public static ActiveLayout OfWindow(long window)
    {
        uint thread = WindowsNative.GetWindowThreadProcessId((IntPtr)window, out _);
        long hkl = thread == 0 ? 0 : WindowsNative.GetKeyboardLayout(thread).ToInt64() & 0xFFFFFFFF;
        if (hkl == 0) return new ActiveLayout(0, string.Empty, string.Empty);
        string klid = Klid(hkl);
        return new ActiveLayout(hkl, klid, LayoutName(klid));
    }

    /// <summary>
    /// The KLID of an HKL. The low word is only the language, which Czech QWERTY and QWERTZ share;
    /// a high word of 0xFnnn is a layout id that the registry maps back to its KLID.
    /// </summary>
    public static string Klid(long hkl)
    {
        int language = (int)(hkl & 0xFFFF), device = (int)((hkl >> 16) & 0xFFFF);
        if ((device & 0xF000) != 0xF000) return $"{device:X8}";
        string layoutId = $"{device & 0x0FFF:X4}";
        using RegistryKey? layouts = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Keyboard Layouts");
        if (layouts is null) return string.Empty;
        foreach (string name in layouts.GetSubKeyNames())
        {
            if (!name.EndsWith($"{language:X4}", StringComparison.OrdinalIgnoreCase)) continue;
            using RegistryKey? layout = layouts.OpenSubKey(name);
            if (string.Equals(layout?.GetValue("Layout Id") as string, layoutId, StringComparison.OrdinalIgnoreCase))
                return name.ToUpperInvariant();
        }
        return string.Empty;
    }

    private static string LayoutName(string klid)
    {
        if (klid.Length == 0) return string.Empty;
        using RegistryKey? layout = Registry.LocalMachine.OpenSubKey(
            @"SYSTEM\CurrentControlSet\Control\Keyboard Layouts\" + klid);
        return layout?.GetValue("Layout Text") as string ?? string.Empty;
    }

    /// <summary>
    /// What the layout produces for a key position, by MapVirtualKeyEx and ToUnicodeEx with the flag
    /// that leaves the kernel keyboard state untouched. Null when the position has no virtual key.
    /// </summary>
    public static OracleText? Text(long hkl, PhysicalKey key, KeyLevel level)
    {
        var layout = (IntPtr)hkl;
        (byte scan, bool extended) = PhysicalKeys.ScanCode(key);
        if (extended) return null;
        uint virtualKey = WindowsNative.MapVirtualKeyExW(scan, WindowsNative.MapVscToVkEx, layout);
        if (virtualKey == 0) return null;

        byte[] state = new byte[256];
        if (level == KeyLevel.Shift) state[WindowsNative.VkShift] = 0x80;
        if (level == KeyLevel.AltGr) state[WindowsNative.VkControl] = state[WindowsNative.VkMenu] = 0x80;
        var buffer = new StringBuilder(8);
        // Bit 2: do not change the keyboard state, so a dead key is not left pending.
        int length = WindowsNative.ToUnicodeEx(virtualKey, scan, state, buffer, buffer.Capacity, 0x4, layout);
        if (length == 0) return new OracleText(string.Empty, false);
        string text = buffer.ToString(0, Math.Min(Math.Abs(length), buffer.Length));
        // A control character is what Ctrl+Alt gives on a level the layout does not define.
        if (text.Length == 0 || text.Any(char.IsControl)) return new OracleText(string.Empty, false);
        return new OracleText(text[..1], length < 0);
    }
}
