namespace TSharpVision.Drivers;

/// <summary>Optional keyboard information a driver can report reliably without polling or timing inference.</summary>
[Flags]
public enum KeyboardCapabilities
{
    /// <summary>The input transport reports only traditional ordinary key-down events.</summary>
    None = 0,
    /// <summary>The driver reports releases of ordinary non-modifier keys.</summary>
    KeyReleaseEvents = 1 << 0,
    /// <summary>The driver reports complete logical Shift, Ctrl, and Alt transitions independently of ordinary keys.</summary>
    StandaloneModifierTransitions = 1 << 1,
    /// <summary>The driver reports the numeric keypad as identities distinct from the corresponding main-keyboard keys.</summary>
    DistinctNumericKeypad = 1 << 2,
    /// <summary>
    /// The driver also reports presses and releases of the Meta key itself (Command, Windows, Super) as
    /// <see cref="TSharpVision.Constants.Keys.kbMetaShift"/> transitions. Without it Meta may still arrive on
    /// ordinary key events.
    /// </summary>
    StandaloneMetaTransitions = 1 << 3,
}
