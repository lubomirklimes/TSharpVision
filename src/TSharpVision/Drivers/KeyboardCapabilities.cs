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
}
