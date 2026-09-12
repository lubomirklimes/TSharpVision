namespace TSharpVision;

/// <summary>Button presentation flags and the command used to record dialog history.</summary>
public static class ButtonConstants
{
    /// <summary>A centered button without default-action or broadcast behavior.</summary>
    public const byte bfNormal = 0x00;
    /// <summary>Makes the button the dialog's default action.</summary>
    public const byte bfDefault = 0x01;
    /// <summary>Aligns the button title to the left rather than centering it.</summary>
    public const byte bfLeftJust = 0x02;
    /// <summary>Broadcasts the button command to its owner instead of queuing a command event.</summary>
    public const byte bfBroadcast = 0x04;

    /// <summary>Requests that linked history controls record their current input.</summary>
    public const ushort cmRecordHistory = 60;
}
