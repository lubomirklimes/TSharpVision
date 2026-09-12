namespace TSharpVision.Constants;

/// <summary>Command-range conventions for application-defined event messages.</summary>
public static class Commands
{
    // Reserved range for application-specific commands. Matches upstream
    // convention that commands < 100 are framework-reserved.
    //
    // There is no upper bound below ushort.MaxValue: TCommandSet covers the whole 0..65535
    // range, so any command code an application picks above cmFirstUserCommand works with
    // TView.CommandEnabled / EnableCommand / DisableCommand, menus and status-line items.
    // (The framework's own TFileDialog / TChDirDialog commands live at 1001..1008.)
    /// <summary>Start of the conventional user-command range; choose identifiers that do not collide with framework commands, including file-dialog commands at 1001–1008.</summary>
    public const ushort cmFirstUserCommand = 100;
}
