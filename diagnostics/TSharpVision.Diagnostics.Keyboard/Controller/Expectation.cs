using TSharpVision.Drivers;

namespace TSharpVision.Diagnostics.Keyboard.Controller;

public enum ItemKind { Press, Release, Modifier }

public enum IdentityKind
{
    /// <summary>Any key code.</summary>
    Any,
    /// <summary>Exactly one key code.</summary>
    Exact,
    /// <summary>No identity: key code zero. The correct result for non-ASCII text.</summary>
    None,
    /// <summary>Zero, or one of the listed codes.</summary>
    NoneOrOneOf,
    /// <summary>The key code and legacy char/scan pair of the step's first press.</summary>
    SameAsPress,
}

/// <summary>What <c>keyCode</c> an event may carry. Judged independently of its text.</summary>
public readonly record struct IdentityRule(IdentityKind Kind, IReadOnlyList<ushort>? Codes = null)
{
    public static IdentityRule Any => new(IdentityKind.Any);
    public static IdentityRule None => new(IdentityKind.None);
    public static IdentityRule SameAsPress => new(IdentityKind.SameAsPress);
    public static IdentityRule Exact(ushort code) => new(IdentityKind.Exact, [code]);
    public static IdentityRule NoneOrOneOf(params ushort[] codes) => new(IdentityKind.NoneOrOneOf, codes);
}

/// <summary>One event the step must, or may, produce.</summary>
public sealed record ExpectedItem(ItemKind Kind, IdentityRule Identity)
{
    /// <summary>Logical modifier bits the event must carry. Checked only when the driver tracks modifiers.</summary>
    public uint RequiredState { get; init; }
    /// <summary>Logical modifier bits the event must not carry.</summary>
    public uint ForbiddenState { get; init; }
    /// <summary>Capabilities the driver must advertise for this item to apply; otherwise it is dropped and noted.</summary>
    public KeyboardCapabilities When { get; init; }
    /// <summary>Capabilities that replace this item with a more specific one.</summary>
    public KeyboardCapabilities Unless { get; init; }
    /// <summary>The item may be absent, such as a separate text commit after a keypad press.</summary>
    public bool Optional { get; init; }
    /// <summary>What the transport cannot show when <see cref="When"/> drops the item.</summary>
    public string? LimitedNote { get; init; }

    public static ExpectedItem Press(IdentityRule identity) => new(ItemKind.Press, identity);
    public static ExpectedItem Release() => new(ItemKind.Release, IdentityRule.SameAsPress)
    {
        When = KeyboardCapabilities.KeyReleaseEvents,
        LimitedNote = "no key release on this transport",
    };
    public static ExpectedItem Modifier(uint required, uint forbidden) => new(ItemKind.Modifier, IdentityRule.Any)
    {
        RequiredState = required,
        ForbiddenState = forbidden,
        When = KeyboardCapabilities.StandaloneModifierTransitions,
        LimitedNote = "no standalone modifier transitions on this transport",
    };
}

/// <summary>What one step must produce: an ordered pattern plus step-wide rules.</summary>
public sealed record Expectation(IReadOnlyList<ExpectedItem> Items)
{
    /// <summary>The concatenated text of every press in the step; null leaves text unchecked.</summary>
    public string? TextTotal { get; init; }
    /// <summary>Key codes that would be a false identity for this step, whichever event carries them.</summary>
    public IReadOnlyList<ushort> ForbiddenIdentities { get; init; } = [];
    /// <summary>Tolerate presses identical to the previous one (a held key).</summary>
    public bool AllowRepeat { get; init; }
    /// <summary>Tolerate modifier transitions that match no item (AltGr arrives as Ctrl plus Alt).</summary>
    public bool AllowModifierEvents { get; init; }
    /// <summary>The step may legitimately produce no event at all, such as a dead key pressed alone.</summary>
    public bool SilentAllowed { get; init; }
    /// <summary>What the transport cannot report for this step even when everything expected is observed.</summary>
    public string? Limitation { get; init; }
    /// <summary>Capabilities without which the step is irrelevant and SKIPPED.</summary>
    public KeyboardCapabilities Requires { get; init; }
}
