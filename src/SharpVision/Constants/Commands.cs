namespace SharpVision.Constants;

public static class Commands
{
    // Reserved range for application-specific commands. Matches upstream
    // convention that commands < 100 are framework-reserved.
    public const ushort cmFirstUserCommand = 100;
}
