// Holds: command codes, sf* state flags, of* option flags, gf* grow modes,
// dm* drag modes, sb* scrollbar parts, wf*/wn*/wp* window flags, hc* help
// contexts, no* inhibit flags, message broadcasts. The values must match
// upstream Turbo Vision verbatim so streams and palettes are compatible.
using TSharpVision.Constants;

namespace TSharpVision.Constants;

public static class Views
{
    // Standard command codes (views.h: __COMMAND_CODES)
    public const ushort cmValid  = 0;
    public const ushort cmQuit   = 1;
    public const ushort cmError  = 2;
    public const ushort cmMenu   = 3;
    public const ushort cmClose  = 4;
    public const ushort cmZoom   = 5;
    public const ushort cmResize = 6;
    public const ushort cmNext   = 7;
    public const ushort cmPrev   = 8;
    public const ushort cmHelp   = 9;

    // TDialog standard commands
    public const ushort cmOK      = 10;
    public const ushort cmCancel  = 11;
    public const ushort cmYes     = 12;
    public const ushort cmNo      = 13;
    public const ushort cmDefault = 14;

    // Application command codes
    public const ushort cmCut     = 20;
    public const ushort cmCopy    = 21;
    public const ushort cmPaste   = 22;
    public const ushort cmUndo    = 23;
    public const ushort cmClear   = 24;
    public const ushort cmTile    = 25;
    public const ushort cmCascade = 26;

    // Standard messages
    public const ushort cmReceivedFocus     = 50;
    public const ushort cmReleasedFocus     = 51;
    public const ushort cmCommandSetChanged = 52;

    // TScrollBar messages
    public const ushort cmScrollBarChanged = 53;
    public const ushort cmScrollBarClicked = 54;

    // TWindow select messages
    public const ushort cmSelectWindowNum = 55;

    // TListViewer messages
    public const ushort cmListItemSelected = 56;

    // SET extensions (always enabled in this port; matches upstream's
    // EXT-block constants from views.h committed unconditionally)
    public const ushort cmClosingWindow  = 57;
    public const ushort cmClusterMovedTo = 58;
    public const ushort cmClusterPress   = 59;
    public const ushort cmRecordHistory  = 60;
    public const ushort cmListItemFocused = 61;
    public const ushort cmGrabDefault    = 62;
    public const ushort cmReleaseDefault = 63;
    public const ushort cmUpdateCodePage = 64;
    public const ushort cmCallShell      = 65;

    // Help navigation commands.
    public const ushort cmHelpBack  = 67;  // Navigate back in help topic history
    public const ushort cmHelpIndex = 68;  // Navigate to the help index topic

    // Host console resize notification. Enqueued by the Win32
    // driver when a WINDOW_BUFFER_SIZE_EVENT is received; handled by TProgram.
    public const ushort cmScreenResized  = 66;

    // TFileDialog command codes.
    public const ushort cmFileOpen     = 1001;
    public const ushort cmFileReplace  = 1002;
    public const ushort cmFileClear    = 1003;
    public const ushort cmFileInit     = 1004;
    public const ushort cmChangeDir    = 1005;
    public const ushort cmRevert       = 1006;
    public const ushort cmFileSelect   = 1007;
    public const ushort cmDirSelection = 1008;
    // Editor command codes.
    public const ushort cmSave         = 80;
    public const ushort cmSaveAs       = 81;
    public const ushort cmOpen         = 100;
    public const ushort cmNew          = 101;
    public const ushort cmFind         = 82;
    public const ushort cmReplace      = 83;
    public const ushort cmSearchAgain  = 84;
    public const ushort cmCharLeft     = 500;
    public const ushort cmCharRight    = 501;
    public const ushort cmWordLeft     = 502;
    public const ushort cmWordRight    = 503;
    public const ushort cmLineStart    = 504;
    public const ushort cmLineEnd      = 505;
    public const ushort cmLineUp       = 506;
    public const ushort cmLineDown     = 507;
    public const ushort cmPageUp       = 508;
    public const ushort cmPageDown     = 509;
    public const ushort cmTextStart    = 510;
    public const ushort cmTextEnd      = 511;
    public const ushort cmNewLine      = 512;
    public const ushort cmBackSpace    = 513;
    public const ushort cmDelChar      = 514;
    public const ushort cmDelWord      = 515;
    public const ushort cmDelStart     = 516;
    public const ushort cmDelEnd       = 517;
    public const ushort cmDelLine      = 518;
    public const ushort cmInsMode      = 519;
    public const ushort cmStartSelect  = 520;
    public const ushort cmHideSelect   = 521;
    public const ushort cmIndentMode   = 522;
    public const ushort cmUpdateTitle  = 523;
    public const ushort cmInsertText   = 524;

    // Editor update flags.
    public const byte ufUpdate = 0x01;
    public const byte ufLine   = 0x02;
    public const byte ufView   = 0x04;

    // Editor selection modes.
    public const byte smExtend = 0x01;
    public const byte smDouble = 0x02;

    // Editor flags (efXxx).
    public const ushort efCaseSensitive   = 0x0001;
    public const ushort efWholeWordsOnly  = 0x0002;
    public const ushort efPromptOnReplace = 0x0004;
    public const ushort efReplaceAll      = 0x0008;
    public const ushort efDoReplace       = 0x0010;
    public const ushort efBackupFiles     = 0x0100;

    public const int maxLineLength    = 256;
    public const int maxFindStrLen    = 80;
    public const int maxReplaceStrLen = 80;
    public const uint sfSearchFailed  = uint.MaxValue;

    // Editor dialog IDs (edXxx).
    public const int edOutOfMemory   = 0;
    public const int edReadError     = 1;
    public const int edWriteError    = 2;
    public const int edCreateError   = 3;
    public const int edSaveModify    = 4;
    public const int edSaveUntitled  = 5;
    public const int edSaveAs        = 6;
    public const int edFind          = 7;
    public const int edSearchFailed  = 8;
    public const int edReplace       = 9;
    public const int edReplacePrompt = 10;
    public const int edEncodingWriteError = 11;

    // TFileList broadcast messages.
    public const ushort cmFileFocused        = 102;
    public const ushort cmFileDoubleClicked  = 103;

    // Event masks (views.h)
    public const ushort positionalEvents = Events.evMouse;
    public const ushort focusedEvents    = Events.evKeyboard | Events.evCommand;

    // TView state masks (views.h)
    public const ushort sfVisible   = 0x001;
    public const ushort sfCursorVis = 0x002;
    public const ushort sfCursorIns = 0x004;
    public const ushort sfShadow    = 0x008;
    public const ushort sfActive    = 0x010;
    public const ushort sfSelected  = 0x020;
    public const ushort sfFocused   = 0x040;
    public const ushort sfDragging  = 0x080;
    public const ushort sfDisabled  = 0x100;
    public const ushort sfModal     = 0x200;
    public const ushort sfDefault   = 0x400;
    public const ushort sfExposed   = 0x800;

    // TView option masks
    public const ushort ofSelectable  = 0x001;
    public const ushort ofTopSelect   = 0x002;
    public const ushort ofFirstClick  = 0x004;
    public const ushort ofFramed      = 0x008;
    public const ushort ofPreProcess  = 0x010;
    public const ushort ofPostProcess = 0x020;
    public const ushort ofBuffered    = 0x040;
    public const ushort ofTileable    = 0x080;
    public const ushort ofCenterX     = 0x100;
    public const ushort ofCenterY     = 0x200;
    public const ushort ofCentered    = 0x300;

    // TView GrowMode masks
    public const byte gfGrowLoX = 0x01;
    public const byte gfGrowLoY = 0x02;
    public const byte gfGrowHiX = 0x04;
    public const byte gfGrowHiY = 0x08;
    public const byte gfGrowAll = 0x0F;
    public const byte gfGrowRel = 0x10;

    // TView DragMode masks
    public const byte dmDragMove  = 0x01;
    public const byte dmDragGrow  = 0x02;
    public const byte dmLimitLoX  = 0x10;
    public const byte dmLimitLoY  = 0x20;
    public const byte dmLimitHiX  = 0x40;
    public const byte dmLimitHiY  = 0x80;
    public const byte dmLimitAll  = dmLimitLoX | dmLimitLoY | dmLimitHiX | dmLimitHiY;

    // TView Help context codes
    public const ushort hcNoContext = 0;
    public const ushort hcDragging  = 1;

    // TDeskTop tile-partition priority
    public const ushort dsktTileHorizontal = 0;
    public const ushort dsktTileVertical   = 1;

    // TScrollBar part codes
    public const ushort sbLeftArrow  = 0;
    public const ushort sbRightArrow = 1;
    public const ushort sbPageLeft   = 2;
    public const ushort sbPageRight  = 3;
    public const ushort sbUpArrow    = 4;
    public const ushort sbDownArrow  = 5;
    public const ushort sbPageUp     = 6;
    public const ushort sbPageDown   = 7;
    public const ushort sbIndicator  = 8;

    // TScrollBar options for standard scroll bar
    public const ushort sbHorizontal     = 0x000;
    public const ushort sbVertical       = 0x001;
    public const ushort sbHandleKeyboard = 0x002;

    // TWindow Flags masks
    public const byte wfMove  = 0x01;
    public const byte wfGrow  = 0x02;
    public const byte wfClose = 0x04;
    public const byte wfZoom  = 0x08;

    // TView inhibit flags
    public const ushort noMenuBar    = 0x0001;
    public const ushort noDeskTop    = 0x0002;
    public const ushort noStatusLine = 0x0004;
    public const ushort noBackground = 0x0008;
    public const ushort noFrame      = 0x0010;
    public const ushort noViewer     = 0x0020;
    public const ushort noHistory    = 0x0040;

    // TWindow number constants
    public const ushort wnNoNumber = 0;

    // TWindow palette entries
    public const ushort wpBlueWindow = 0;
    public const ushort wpCyanWindow = 1;
    public const ushort wpGrayWindow = 2;

    // Color dialog broadcast commands.
    public const ushort cmColorForegroundChanged = 71;
    public const ushort cmColorBackgroundChanged = 72;
    public const ushort cmColorSet               = 73;
    public const ushort cmNewColorItem           = 74;
    public const ushort cmNewColorIndex          = 75;
    public const ushort cmTryColors              = 76;
    public const ushort cmUpdateColorsChanged    = 77;
}
