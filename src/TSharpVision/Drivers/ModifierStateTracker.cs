using TSharpVision.Constants;

namespace TSharpVision.Drivers.Internal;

internal enum PhysicalModifier
{
    LeftShift,
    RightShift,
    LeftCtrl,
    RightCtrl,
    LeftAlt,
    RightAlt,
}

/// <summary>Projects six physical modifier keys onto the three logical Turbo Vision flags.</summary>
internal sealed class ModifierStateTracker
{
    private byte _physicalState;

    public uint LogicalState => Project(_physicalState);

    public bool Set(PhysicalModifier modifier, bool down, out uint state)
    {
        byte old = _physicalState;
        byte bit = (byte)(1 << (int)modifier);
        _physicalState = down ? (byte)(old | bit) : (byte)(old & ~bit);
        uint previous = Project(old);
        state = Project(_physicalState);
        return previous != state;
    }

    public bool Reset(out uint state)
    {
        bool changed = _physicalState != 0;
        _physicalState = 0;
        state = 0;
        return changed;
    }

    private static uint Project(byte physicalState)
    {
        uint state = 0;
        if ((physicalState & 0b000011) != 0) state |= Keys.kbShift;
        if ((physicalState & 0b001100) != 0) state |= Keys.kbCtrlShift;
        if ((physicalState & 0b110000) != 0) state |= Keys.kbAltShift;
        return state;
    }
}
