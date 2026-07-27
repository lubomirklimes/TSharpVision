namespace TSharpVision.Samples.TVTerm;

/// <summary>
/// Command constants used by TVTerm menu items and status line entries.
/// Numbers are in the 1000+ range reserved for user-defined commands.
/// </summary>
public static class Commands
{
    // File
    public const ushort cmNewTerminal      = 1000;
    public const ushort cmNewFakeTerminal  = 1001;
    public const ushort cmRunCommand       = 1002;
    public const ushort cmOpenLog          = 1003;
    public const ushort cmSaveOutput       = 1004;

    // Edit
    public const ushort cmCopy             = 1100;
    public const ushort cmPaste            = 1101;
    public const ushort cmSelectAll        = 1102;
    public const ushort cmClearSelection   = 1103;
    public const ushort cmFind             = 1104;
    public const ushort cmFindNext         = 1105;

    // View
    public const ushort cmClearScreen      = 1200;
    public const ushort cmScrollTop        = 1201;
    public const ushort cmScrollBottom     = 1202;
    public const ushort cmToggleWrap       = 1203;
    public const ushort cmToggleAnsi       = 1204;

    // Session
    public const ushort cmInterrupt        = 1300;
    public const ushort cmRestart          = 1301;
    public const ushort cmSendEof          = 1302;
    public const ushort cmKill             = 1303;

    // Tools
    public const ushort cmKeyInspector     = 1400;
    public const ushort cmUnicodeDemo      = 1401;
    public const ushort cmAnsiDemo         = 1402;
    public const ushort cmPerfDemo         = 1403;
    public const ushort cmSelectionDemo    = 1404;
    public const ushort cmPipeDemo         = 1405;
    public const ushort cmPtyDemo          = 1406;
    public const ushort cmShellDemo        = 1407;

    // Options
    public const ushort cmTerminalSettings = 1500;
    public const ushort cmColorMapping     = 1501;
    public const ushort cmKeyboardMapping  = 1502;
    public const ushort cmUiColors         = 1503;

    // Help
    public const ushort cmAbout            = 1600;
    public const ushort cmHelpContents     = 1601;
}
