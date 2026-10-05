using System.Reflection;
using System.Text;
using TSharpVision.Constants;
using TSharpVision.Diagnostics.Keyboard.Protocol;
using TSharpVision.Drivers;

namespace TSharpVision.Diagnostics.Keyboard.Controller;

/// <summary>Compares the events of one step with its expectation, relative to the driver's capabilities.</summary>
/// <remarks>
/// Identity and text are judged independently: <c>keyCode == 0</c> with the right text is correct, a wrong
/// identity is not. Required items match as an ordered subsequence; an event that matches nothing fails the step.
/// </remarks>
public static class SequenceMatcher
{
    private const uint LogicalModifiers = Keys.kbShift | Keys.kbCtrlShift | Keys.kbAltShift;

    public static StepOutcome Evaluate(
        Expectation expect, IReadOnlyList<RecordedEvent> events, KeyboardCapabilities capabilities)
    {
        if ((capabilities & expect.Requires) != expect.Requires)
            return StepOutcome.Of(Verdict.Skipped, $"driver does not advertise {expect.Requires & ~capabilities}");

        var failures = new List<string>();
        var limits = new List<string>();
        var items = new List<ExpectedItem>();
        foreach (ExpectedItem item in expect.Items)
        {
            if (item.Unless != 0 && (capabilities & item.Unless) == item.Unless) continue;
            if ((capabilities & item.When) == item.When) items.Add(item);
            else if (item.LimitedNote != null && !limits.Contains(item.LimitedNote)) limits.Add(item.LimitedNote);
        }

        if (expect.Limitation != null) limits.Add(expect.Limitation);

        if (events.Count == 0)
        {
            return expect.SilentAllowed
                ? Finish(failures, limits)
                : StepOutcome.Of(Verdict.Invalid, "no keyboard event was observed");
        }

        bool releases = capabilities.HasFlag(KeyboardCapabilities.KeyReleaseEvents);
        bool transitions = capabilities.HasFlag(KeyboardCapabilities.StandaloneModifierTransitions);
        int next = 0;
        RecordedEvent? press = null, lastModifier = null;
        var seenPresses = new HashSet<(ushort, string)>();
        var text = new StringBuilder();

        foreach (RecordedEvent ev in events)
        {
            ItemKind kind = KindOf(ev);
            if (kind != ItemKind.Modifier && expect.ForbiddenIdentities.Contains(ev.KeyCode))
                failures.Add($"false identity {KeyNames.Describe(ev.KeyCode)} on {ev.What}");
            if (kind != ItemKind.Press && ev.Text.Length != 0)
                failures.Add($"{ev.What} carries text {KeyNames.Quote(ev.Text)}");
            if (kind == ItemKind.Release && !releases)
            {
                failures.Add("a release was reported without the KeyReleaseEvents capability");
                continue;
            }
            if (kind == ItemKind.Modifier)
            {
                lastModifier = ev;
                if (!transitions)
                {
                    failures.Add("a modifier transition was reported without the StandaloneModifierTransitions capability");
                    continue;
                }
            }

            // A held key repeats presses the step already matched; their text is not typed twice.
            if (kind == ItemKind.Press && expect.AllowRepeat && seenPresses.Contains((ev.KeyCode, ev.Text)))
                continue;

            int match = Find(items, next, kind, ev, press);
            if (match >= 0)
            {
                next = match + 1;
                Check(items[match], ev, press, transitions, failures, limits);
                if (kind == ItemKind.Press)
                {
                    press ??= ev;
                    seenPresses.Add((ev.KeyCode, ev.Text));
                    text.Append(ev.Text);
                }
                continue;
            }

            if (kind == ItemKind.Modifier && expect.AllowModifierEvents) continue;
            failures.Add(kind == ItemKind.Release
                ? $"release with identity {KeyNames.Describe(ev.KeyCode)} matches no press in the step"
                : $"unexpected event: {KeyNames.Describe(ev)}");
        }

        for (int i = next; i < items.Count; i++)
            if (!IsOptional(items[i], press))
                failures.Add($"missing {items[i].Kind.ToString().ToLowerInvariant()}{Describe(items[i])}");

        if (expect.TextTotal != null && text.ToString() != expect.TextTotal)
            failures.Add($"text: expected {KeyNames.Quote(expect.TextTotal)}, got {KeyNames.Quote(text.ToString())}");
        if (transitions && lastModifier != null && (lastModifier.State & LogicalModifiers) != 0)
            failures.Add($"modifier stuck after the step: {KeyNames.State(lastModifier.State & LogicalModifiers)}");

        return Finish(failures, limits);
    }

    private static StepOutcome Finish(List<string> failures, List<string> limits) =>
        failures.Count != 0 ? new StepOutcome(Verdict.Fail, failures)
        : limits.Count != 0 ? new StepOutcome(Verdict.Limited, limits)
        : new StepOutcome(Verdict.Pass, []);

    private static ItemKind KindOf(RecordedEvent ev) => ev.What switch
    {
        RecordedEvent.KeyDown => ItemKind.Press,
        RecordedEvent.KeyUp => ItemKind.Release,
        _ => ItemKind.Modifier,
    };

    // The first item of the event's kind, skipping optional items the event does not satisfy.
    // A required item of another kind ends the search: required items keep their order.
    private static int Find(List<ExpectedItem> items, int next, ItemKind kind, RecordedEvent ev, RecordedEvent? press)
    {
        for (int i = next; i < items.Count; i++)
        {
            bool optional = IsOptional(items[i], press);
            if (items[i].Kind == kind && (!optional || IdentityMatches(items[i].Identity, ev, press))) return i;
            if (!optional) return -1;
        }
        return -1;
    }

    // A text-only press has no identity, so there is nothing a release would have to match.
    private static bool IsOptional(ExpectedItem item, RecordedEvent? press) =>
        item.Optional
        || (item.Kind == ItemKind.Release && item.Identity.Kind == IdentityKind.SameAsPress && press is { KeyCode: 0 });

    private static bool IdentityMatches(IdentityRule rule, RecordedEvent ev, RecordedEvent? press) => rule.Kind switch
    {
        IdentityKind.Any => true,
        IdentityKind.None => ev.KeyCode == 0,
        IdentityKind.Exact => ev.KeyCode == rule.Codes![0],
        IdentityKind.NoneOrOneOf => ev.KeyCode == 0 || rule.Codes!.Contains(ev.KeyCode),
        _ => press != null && ev.KeyCode == press.KeyCode
            && ev.CharCode == press.CharCode && ev.LegacyScan == press.LegacyScan,
    };

    private static void Check(ExpectedItem item, RecordedEvent ev, RecordedEvent? press, bool transitions,
        List<string> failures, List<string> limits)
    {
        if (!IdentityMatches(item.Identity, ev, press))
        {
            string got = KeyNames.Describe(ev.KeyCode);
            failures.Add(item.Identity.Kind switch
            {
                IdentityKind.SameAsPress when press == null => $"release {got} without a press",
                IdentityKind.SameAsPress =>
                    $"release identity {got} differs from press {KeyNames.Describe(press.KeyCode)}",
                IdentityKind.Exact when ev.KeyCode == 0 =>
                    $"identity missing: expected {KeyNames.Describe(item.Identity.Codes![0])}",
                IdentityKind.Exact =>
                    $"wrong identity: expected {KeyNames.Describe(item.Identity.Codes![0])}, got {got}",
                _ => $"false identity {got} for a key that has none",
            });
        }

        if ((ev.State & item.ForbiddenState) != 0)
            failures.Add($"unexpected modifier state on {ev.What}: {KeyNames.State(ev.State & item.ForbiddenState)}");
        // The state is logical: either the left or the right key satisfies a required modifier.
        uint missing = 0;
        foreach (uint group in new[] { Keys.kbShift, Keys.kbCtrlShift, Keys.kbAltShift, Keys.kbCapsState, Keys.kbNumState })
            if ((item.RequiredState & group) != 0 && (ev.State & group) == 0) missing |= group;
        if (missing == 0) return;
        if (transitions || item.Kind == ItemKind.Modifier)
            failures.Add($"modifier state lost on {ev.What}: {KeyNames.State(missing)} not set");
        else if (!limits.Contains(ModifierStateNote))
            limits.Add(ModifierStateNote);
    }

    private const string ModifierStateNote = "modifier state is not reported by this transport";

    private static string Describe(ExpectedItem item) => item.Identity.Kind switch
    {
        IdentityKind.Exact => $" {KeyNames.Describe(item.Identity.Codes![0])}",
        IdentityKind.SameAsPress => " matching the press",
        _ when item.Kind == ItemKind.Modifier => $" transition ({KeyNames.State(item.RequiredState)})",
        _ => string.Empty,
    };
}

/// <summary>Readable names for key codes, modifier masks and text in reports.</summary>
public static class KeyNames
{
    private static readonly Dictionary<ushort, string> Names = BuildNames();

    private static Dictionary<ushort, string> BuildNames()
    {
        var names = new Dictionary<ushort, string>();
        foreach (FieldInfo field in typeof(Keys).GetFields(BindingFlags.Public | BindingFlags.Static))
            if (field.FieldType == typeof(ushort) && field.Name != "kbNoKey")
                names.TryAdd((ushort)field.GetRawConstantValue()!, field.Name);
        return names;
    }

    public static string Describe(ushort keyCode)
    {
        if (keyCode == 0) return "none";
        if (keyCode is >= 0x20 and <= 0x7E) return $"'{(char)keyCode}'";
        return Names.TryGetValue(keyCode, out string? name) ? name : $"0x{keyCode:X4}";
    }

    public static string Describe(RecordedEvent ev) => ev.What == RecordedEvent.ModifierChanged
        ? $"{ev.What} state={State(ev.State)}"
        : $"{ev.What} key={Describe(ev.KeyCode)} text={Quote(ev.Text)}";

    public static string State(uint state)
    {
        var parts = new List<string>();
        if ((state & Keys.kbShift) != 0) parts.Add("Shift");
        if ((state & Keys.kbCtrlShift) != 0) parts.Add("Ctrl");
        if ((state & Keys.kbAltShift) != 0) parts.Add("Alt");
        if ((state & Keys.kbCapsState) != 0) parts.Add("CapsLock");
        if ((state & Keys.kbNumState) != 0) parts.Add("NumLock");
        return parts.Count == 0 ? "none" : string.Join('+', parts);
    }

    /// <summary>Quoted text, with code points spelled out when it is not plain ASCII.</summary>
    public static string Quote(string text)
    {
        if (text.All(c => c is >= ' ' and <= '~')) return $"\"{text}\"";
        string printable = string.Concat(text.Select(c => char.IsControl(c) ? '?' : c));
        return $"\"{printable}\" ({string.Join(' ', text.EnumerateRunes().Select(r => $"U+{r.Value:X4}"))})";
    }
}
