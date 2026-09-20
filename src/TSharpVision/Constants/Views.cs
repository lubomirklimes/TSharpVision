// Holds: command codes, sf* state flags, of* option flags, gf* grow modes,
// dm* drag modes, sb* scrollbar parts, wf*/wn*/wp* window flags, hc* help
// contexts, no* inhibit flags, message broadcasts. The values must match
// upstream Turbo Vision verbatim so streams and palettes are compatible.
using TSharpVision.Constants;

namespace TSharpVision.Constants;

/// <summary>Command identifiers and bit masks controlling view state, layout, selection, windows, editors, and dialogs.</summary>
public static class Views
{
    // Standard command codes (views.h: __COMMAND_CODES)
    /// <summary>Validate whether a view was constructed successfully.</summary>
    public const ushort cmValid  = 0;
    /// <summary>Request application termination.</summary>
    public const ushort cmQuit   = 1;
    /// <summary>Report an unhandled or invalid operation.</summary>
    public const ushort cmError  = 2;
    /// <summary>Activate the application menu.</summary>
    public const ushort cmMenu   = 3;
    /// <summary>Request closure of the selected or addressed window.</summary>
    public const ushort cmClose  = 4;
    /// <summary>Toggle the selected window between zoomed and saved bounds.</summary>
    public const ushort cmZoom   = 5;
    /// <summary>Enter interactive window movement or resizing.</summary>
    public const ushort cmResize = 6;
    /// <summary>Select the next eligible window.</summary>
    public const ushort cmNext   = 7;
    /// <summary>Select the previous eligible window.</summary>
    public const ushort cmPrev   = 8;
    /// <summary>Open help for the current context.</summary>
    public const ushort cmHelp   = 9;

    // TDialog standard commands
    /// <summary>Accept a dialog and return its data.</summary>
    public const ushort cmOK      = 10;
    /// <summary>Dismiss a dialog without acceptance.</summary>
    public const ushort cmCancel  = 11;
    /// <summary>Return an affirmative dialog response.</summary>
    public const ushort cmYes     = 12;
    /// <summary>Return a negative dialog response.</summary>
    public const ushort cmNo      = 13;
    /// <summary>Activate the current default dialog button.</summary>
    public const ushort cmDefault = 14;

    // Application command codes
    /// <summary>Copy the selection to the clipboard and remove it.</summary>
    public const ushort cmCut     = 20;
    /// <summary>Copy the selection to the clipboard.</summary>
    public const ushort cmCopy    = 21;
    /// <summary>Insert clipboard content at the current selection.</summary>
    public const ushort cmPaste   = 22;
    /// <summary>Undo the last supported editor operation.</summary>
    public const ushort cmUndo    = 23;
    /// <summary>Remove the current selection.</summary>
    public const ushort cmClear   = 24;
    /// <summary>Arrange eligible desktop windows in nonoverlapping tiles.</summary>
    public const ushort cmTile    = 25;
    /// <summary>Arrange eligible desktop windows with staggered origins.</summary>
    public const ushort cmCascade = 26;

    // Standard messages
    /// <summary>Broadcast that a view received focus.</summary>
    public const ushort cmReceivedFocus     = 50;
    /// <summary>Broadcast that a view released focus.</summary>
    public const ushort cmReleasedFocus     = 51;
    /// <summary>Broadcast that enabled command state has changed.</summary>
    public const ushort cmCommandSetChanged = 52;

    // TScrollBar messages
    /// <summary>Broadcast that a scrollbar value changed, with the scrollbar as payload.</summary>
    public const ushort cmScrollBarChanged = 53;
    /// <summary>Broadcast interaction with a scrollbar.</summary>
    public const ushort cmScrollBarClicked = 54;

    // TWindow select messages
    /// <summary>Select a window whose number matches the message integer payload.</summary>
    public const ushort cmSelectWindowNum = 55;

    // TListViewer messages
    /// <summary>Notify activation of a list item.</summary>
    public const ushort cmListItemSelected = 56;

    // TOutlineViewer messages and traversal flags (outline.h).
    /// <summary>Notify that an outline item was activated.</summary>
    public const ushort cmOutlineItemSelected = 301;
    /// <summary>Indicates that an outline node is a leaf or is currently expanded.</summary>
    public const ushort ovExpanded = 0x01;
    /// <summary>Indicates that an expanded outline node has visible children.</summary>
    public const ushort ovChildren = 0x02;
    /// <summary>Indicates that an outline node is the last child of its parent.</summary>
    public const ushort ovLast = 0x04;

    // SET extensions (always enabled in this port; matches upstream's
    // EXT-block constants from views.h committed unconditionally)
    /// <summary>Notify that a window is closing.</summary>
    public const ushort cmClosingWindow  = 57;
    /// <summary>Broadcast movement of the focused cluster option when verbose mode is enabled.</summary>
    public const ushort cmClusterMovedTo = 58;
    /// <summary>Broadcast activation of a cluster option when verbose mode is enabled.</summary>
    public const ushort cmClusterPress   = 59;
    /// <summary>Request recording of the current input in history.</summary>
    public const ushort cmRecordHistory  = 60;
    /// <summary>Notify a change in the focused list item.</summary>
    public const ushort cmListItemFocused = 61;
    /// <summary>Notify other buttons that a button is taking the default role.</summary>
    public const ushort cmGrabDefault    = 62;
    /// <summary>Notify other buttons that the temporary default role was released.</summary>
    public const ushort cmReleaseDefault = 63;
    /// <summary>Command identifier reserved for code-page update notifications.</summary>
    public const ushort cmUpdateCodePage = 64;
    /// <summary>Command identifier reserved for invoking a host shell.</summary>
    public const ushort cmCallShell      = 65;

    // Help navigation commands.
    /// <summary>Return to the previous topic in help navigation history.</summary>
    public const ushort cmHelpBack  = 67;  // Navigate back in help topic history
    /// <summary>Navigate to the help index topic.</summary>
    public const ushort cmHelpIndex = 68;  // Navigate to the help index topic

    // Host console resize notification. Enqueued by the Win32
    // driver when a WINDOW_BUFFER_SIZE_EVENT is received; handled by TProgram.
    /// <summary>Notify the root that the driver has updated screen dimensions and buffer.</summary>
    public const ushort cmScreenResized  = 66;

    // TFileDialog command codes.
    /// <summary>Accept the file dialog's open action.</summary>
    public const ushort cmFileOpen     = 1001;
    /// <summary>Accept the file dialog's replace action.</summary>
    public const ushort cmFileReplace  = 1002;
    /// <summary>Accept the file dialog's clear action without filename validation.</summary>
    public const ushort cmFileClear    = 1003;
    /// <summary>Validate and initialize file-dialog directory and wildcard state.</summary>
    public const ushort cmFileInit     = 1004;
    /// <summary>Change to the directory selected in the directory dialog.</summary>
    public const ushort cmChangeDir    = 1005;
    /// <summary>Restore the directory dialog's original directory.</summary>
    public const ushort cmRevert       = 1006;
    /// <summary>Accept the file dialog's select action.</summary>
    public const ushort cmFileSelect   = 1007;
    /// <summary>Notify that the directory list selection changed.</summary>
    public const ushort cmDirSelection = 1008;
    // Editor command codes.
    /// <summary>Save the editor's current file.</summary>
    public const ushort cmSave         = 32;
    /// <summary>Choose a filename and save the editor's content.</summary>
    public const ushort cmSaveAs       = 33;
    /// <summary>Save all open editor documents (historical command identifier; no built-in handler is implied).</summary>
    public const ushort cmSaveAll      = 34;
    /// <summary>Change the current directory (historical command identifier; no built-in handler is implied).</summary>
    public const ushort cmChDir        = 35;
    /// <summary>Open a DOS shell (historical command identifier; symbolic compatibility only).</summary>
    public const ushort cmDosShell     = 36;
    /// <summary>Close all open editor documents (historical command identifier; no built-in handler is implied).</summary>
    public const ushort cmCloseAll     = 37;
    /// <summary>Open a file in an editor window.</summary>
    public const ushort cmOpen         = 31;
    /// <summary>Create a new editor document.</summary>
    public const ushort cmNew          = 30;
    /// <summary>Open the editor search dialog.</summary>
    public const ushort cmFind         = 82;
    /// <summary>Open the editor replacement dialog.</summary>
    public const ushort cmReplace      = 83;
    /// <summary>Repeat the current search.</summary>
    public const ushort cmSearchAgain  = 84;
    /// <summary>Move the editor caret one character left.</summary>
    public const ushort cmCharLeft     = 500;
    /// <summary>Move the editor caret one character right.</summary>
    public const ushort cmCharRight    = 501;
    /// <summary>Move the editor caret to the previous word boundary.</summary>
    public const ushort cmWordLeft     = 502;
    /// <summary>Move the editor caret to the next word boundary.</summary>
    public const ushort cmWordRight    = 503;
    /// <summary>Move the editor caret to the start of its line.</summary>
    public const ushort cmLineStart    = 504;
    /// <summary>Move the editor caret to the end of its line.</summary>
    public const ushort cmLineEnd      = 505;
    /// <summary>Move the editor caret up one line.</summary>
    public const ushort cmLineUp       = 506;
    /// <summary>Move the editor caret down one line.</summary>
    public const ushort cmLineDown     = 507;
    /// <summary>Move the editor caret up one viewport page.</summary>
    public const ushort cmPageUp       = 508;
    /// <summary>Move the editor caret down one viewport page.</summary>
    public const ushort cmPageDown     = 509;
    /// <summary>Move the editor caret to the start of the document.</summary>
    public const ushort cmTextStart    = 510;
    /// <summary>Move the editor caret to the end of the document.</summary>
    public const ushort cmTextEnd      = 511;
    /// <summary>Insert an editor line break.</summary>
    public const ushort cmNewLine      = 512;
    /// <summary>Delete the character before the editor caret.</summary>
    public const ushort cmBackSpace    = 513;
    /// <summary>Delete the character at the editor caret.</summary>
    public const ushort cmDelChar      = 514;
    /// <summary>Delete text through the next word boundary.</summary>
    public const ushort cmDelWord      = 515;
    /// <summary>Delete text from the line start to the caret.</summary>
    public const ushort cmDelStart     = 516;
    /// <summary>Delete text from the caret to the line end.</summary>
    public const ushort cmDelEnd       = 517;
    /// <summary>Delete the current editor line.</summary>
    public const ushort cmDelLine      = 518;
    /// <summary>Toggle editor insertion versus overwrite mode.</summary>
    public const ushort cmInsMode      = 519;
    /// <summary>Start extending the editor selection.</summary>
    public const ushort cmStartSelect  = 520;
    /// <summary>Stop extending and hide the editor selection.</summary>
    public const ushort cmHideSelect   = 521;
    /// <summary>Toggle automatic indentation of new editor lines.</summary>
    public const ushort cmIndentMode   = 522;
    /// <summary>Request an editor-window title refresh.</summary>
    public const ushort cmUpdateTitle  = 523;
    /// <summary>Insert text carried by a TextInfo message payload.</summary>
    public const ushort cmInsertText   = 524;

    // Editor update flags.
    /// <summary>Editor update flag for refreshing caret, scrollbars, and status.</summary>
    public const byte ufUpdate = 0x01;
    /// <summary>Editor update flag requesting redraw of the current line.</summary>
    public const byte ufLine   = 0x02;
    /// <summary>Editor update flag requesting redraw of the whole viewport.</summary>
    public const byte ufView   = 0x04;

    // Editor selection modes.
    /// <summary>Extend the editor selection from its existing anchor.</summary>
    public const byte smExtend = 0x01;
    /// <summary>Expand editor selection to word boundaries.</summary>
    public const byte smDouble = 0x02;

    // Editor flags (efXxx).
    /// <summary>Match case when searching editor text.</summary>
    public const ushort efCaseSensitive   = 0x0001;
    /// <summary>Match only complete words when searching.</summary>
    public const ushort efWholeWordsOnly  = 0x0002;
    /// <summary>Ask before replacing each search match.</summary>
    public const ushort efPromptOnReplace = 0x0004;
    /// <summary>Continue replacing all matching occurrences.</summary>
    public const ushort efReplaceAll      = 0x0008;
    /// <summary>Perform replacements during the search operation.</summary>
    public const ushort efDoReplace       = 0x0010;
    /// <summary>Create a backup when saving an existing editor file.</summary>
    public const ushort efBackupFiles     = 0x0100;

    /// <summary>Configured editor line-display limit in characters.</summary>
    public const int maxLineLength    = 256;
    /// <summary>Maximum search-text length in characters used by editor dialogs.</summary>
    public const int maxFindStrLen    = 80;
    /// <summary>Maximum replacement-text length in characters used by editor dialogs.</summary>
    public const int maxReplaceStrLen = 80;
    /// <summary>Managed unsigned sentinel returned when an editor search has no match; deliberately widened from the original 16-bit value.</summary>
    public const uint sfSearchFailed  = uint.MaxValue;

    // Editor dialog IDs (edXxx).
    /// <summary>Editor dialog callback identifier for insufficient memory.</summary>
    public const int edOutOfMemory   = 0;
    /// <summary>Editor dialog callback identifier for a file-read failure.</summary>
    public const int edReadError     = 1;
    /// <summary>Editor dialog callback identifier for a file-write failure.</summary>
    public const int edWriteError    = 2;
    /// <summary>Editor dialog callback identifier for a file-creation failure.</summary>
    public const int edCreateError   = 3;
    /// <summary>Editor dialog callback identifier for confirming unsaved changes.</summary>
    public const int edSaveModify    = 4;
    /// <summary>Editor dialog callback identifier for saving an unnamed document.</summary>
    public const int edSaveUntitled  = 5;
    /// <summary>Editor dialog callback identifier for choosing a save path.</summary>
    public const int edSaveAs        = 6;
    /// <summary>Editor dialog callback identifier for entering search criteria.</summary>
    public const int edFind          = 7;
    /// <summary>Editor dialog callback identifier for reporting no search match.</summary>
    public const int edSearchFailed  = 8;
    /// <summary>Editor dialog callback identifier for entering replacement criteria.</summary>
    public const int edReplace       = 9;
    /// <summary>Editor dialog callback identifier for confirming an individual replacement.</summary>
    public const int edReplacePrompt = 10;
    /// <summary>Editor dialog callback identifier for text that cannot be saved in the chosen encoding.</summary>
    public const int edEncodingWriteError = 11;

    // TFileList broadcast messages.
    /// <summary>Broadcast the focused file entry to related file-dialog controls.</summary>
    public const ushort cmFileFocused        = 102;
    /// <summary>Notify activation of a file entry by double click.</summary>
    public const ushort cmFileDoubleClicked  = 103;

    // Event masks (views.h)
    /// <summary>Event kinds routed to the view under the mouse position.</summary>
    public const ushort positionalEvents = Events.evMouse;
    /// <summary>Event kinds routed through the selected view chain.</summary>
    public const ushort focusedEvents    = Events.evKeyboard | Events.evCommand;

    // TView state masks (views.h)
    /// <summary>The view participates in visible layout and drawing.</summary>
    public const ushort sfVisible   = 0x001;
    /// <summary>The view requests a visible caret when focused.</summary>
    public const ushort sfCursorVis = 0x002;
    /// <summary>The view requests a block-style caret.</summary>
    public const ushort sfCursorIns = 0x004;
    /// <summary>The view casts a shadow over underlying views.</summary>
    public const ushort sfShadow    = 0x008;
    /// <summary>The view belongs to the active window or group.</summary>
    public const ushort sfActive    = 0x010;
    /// <summary>The view is selected within its owner.</summary>
    public const ushort sfSelected  = 0x020;
    /// <summary>The view lies on the focused chain from the active root.</summary>
    public const ushort sfFocused   = 0x040;
    /// <summary>The view is undergoing interactive movement or resizing.</summary>
    public const ushort sfDragging  = 0x080;
    /// <summary>The view is disabled for normal selection and interaction.</summary>
    public const ushort sfDisabled  = 0x100;
    /// <summary>The view is executing a modal event loop.</summary>
    public const ushort sfModal     = 0x200;
    /// <summary>The control has the default-action state.</summary>
    public const ushort sfDefault   = 0x400;
    /// <summary>The view is eligible for drawing through the owner chain.</summary>
    public const ushort sfExposed   = 0x800;

    // TView option masks
    /// <summary>Allow the view to become the owner's selected child.</summary>
    public const ushort ofSelectable  = 0x001;
    /// <summary>Bring the view to the front when selected.</summary>
    public const ushort ofTopSelect   = 0x002;
    /// <summary>Deliver the initial selection click to the view's interaction handler.</summary>
    public const ushort ofFirstClick  = 0x004;
    /// <summary>Allow the view to participate in its containing frame.</summary>
    public const ushort ofFramed      = 0x008;
    /// <summary>Offer focused events before dispatch to the selected child.</summary>
    public const ushort ofPreProcess  = 0x010;
    /// <summary>Offer unconsumed focused events after dispatch to the selected child.</summary>
    public const ushort ofPostProcess = 0x020;
    /// <summary>Retain a group's rendered content in a cell buffer.</summary>
    public const ushort ofBuffered    = 0x040;
    /// <summary>Include the view in desktop tile and cascade arrangements.</summary>
    public const ushort ofTileable    = 0x080;
    /// <summary>Center the view horizontally when inserted into an owner.</summary>
    public const ushort ofCenterX     = 0x100;
    /// <summary>Center the view vertically when inserted into an owner.</summary>
    public const ushort ofCenterY     = 0x200;
    /// <summary>Center the view on both axes when inserted into an owner.</summary>
    public const ushort ofCentered    = 0x300;
    /// <summary>Validate the selected view before focus is released.</summary>
    public const ushort ofValidate    = 0x400;

    // TView GrowMode masks
    /// <summary>Adjust the left edge when the owner width changes.</summary>
    public const byte gfGrowLoX = 0x01;
    /// <summary>Adjust the top edge when the owner height changes.</summary>
    public const byte gfGrowLoY = 0x02;
    /// <summary>Adjust the right edge when the owner width changes.</summary>
    public const byte gfGrowHiX = 0x04;
    /// <summary>Adjust the bottom edge when the owner height changes.</summary>
    public const byte gfGrowHiY = 0x08;
    /// <summary>Adjust all edges when the owner size changes.</summary>
    public const byte gfGrowAll = 0x0F;
    /// <summary>Scale grow-enabled coordinates proportionally to the owner size.</summary>
    public const byte gfGrowRel = 0x10;
    /// <summary>Do not clamp the view's maximum size to its owner's current size.</summary>
    public const byte gfFixed = 0x20;

    // TView DragMode masks
    /// <summary>Allow interactive translation of the view.</summary>
    public const byte dmDragMove  = 0x01;
    /// <summary>Allow interactive resizing of the view.</summary>
    public const byte dmDragGrow  = 0x02;
    /// <summary>Constrain the left edge to the drag limits.</summary>
    public const byte dmLimitLoX  = 0x10;
    /// <summary>Constrain the top edge to the drag limits.</summary>
    public const byte dmLimitLoY  = 0x20;
    /// <summary>Constrain the right edge to the drag limits.</summary>
    public const byte dmLimitHiX  = 0x40;
    /// <summary>Constrain the bottom edge to the drag limits.</summary>
    public const byte dmLimitHiY  = 0x80;
    /// <summary>Constrain all edges to the drag limits.</summary>
    public const byte dmLimitAll  = dmLimitLoX | dmLimitLoY | dmLimitHiX | dmLimitHiY;

    // TView Help context codes
    /// <summary>No specific help topic is associated with the view.</summary>
    public const ushort hcNoContext = 0;
    /// <summary>Help context used during interactive movement or resizing.</summary>
    public const ushort hcDragging  = 1;

    // TDeskTop tile-partition priority
    /// <summary>Use horizontal priority when partitioning the desktop into tiles.</summary>
    public const ushort dsktTileHorizontal = 0;
    /// <summary>Use vertical priority when partitioning the desktop into tiles.</summary>
    public const ushort dsktTileVertical   = 1;

    // TScrollBar part codes
    /// <summary>Scrollbar hit-test part for the left endpoint arrow.</summary>
    public const ushort sbLeftArrow  = 0;
    /// <summary>Scrollbar hit-test part for the right endpoint arrow.</summary>
    public const ushort sbRightArrow = 1;
    /// <summary>Scrollbar hit-test part for the track before the horizontal thumb.</summary>
    public const ushort sbPageLeft   = 2;
    /// <summary>Scrollbar hit-test part for the track after the horizontal thumb.</summary>
    public const ushort sbPageRight  = 3;
    /// <summary>Scrollbar hit-test part for the upper endpoint arrow.</summary>
    public const ushort sbUpArrow    = 4;
    /// <summary>Scrollbar hit-test part for the lower endpoint arrow.</summary>
    public const ushort sbDownArrow  = 5;
    /// <summary>Scrollbar hit-test part for the track above the vertical thumb.</summary>
    public const ushort sbPageUp     = 6;
    /// <summary>Scrollbar hit-test part for the track below the vertical thumb.</summary>
    public const ushort sbPageDown   = 7;
    /// <summary>Scrollbar hit-test part for the draggable thumb.</summary>
    public const ushort sbIndicator  = 8;

    // TScrollBar options for standard scroll bar
    /// <summary>Create a horizontal standard scrollbar.</summary>
    public const ushort sbHorizontal     = 0x000;
    /// <summary>Create a vertical standard scrollbar.</summary>
    public const ushort sbVertical       = 0x001;
    /// <summary>Enable keyboard scrolling on a standard scrollbar.</summary>
    public const ushort sbHandleKeyboard = 0x002;

    // TWindow Flags masks
    /// <summary>Allow interactive window movement.</summary>
    public const byte wfMove  = 0x01;
    /// <summary>Allow interactive window resizing.</summary>
    public const byte wfGrow  = 0x02;
    /// <summary>Enable the window close action.</summary>
    public const byte wfClose = 0x04;
    /// <summary>Enable toggling between saved and zoomed window bounds.</summary>
    public const byte wfZoom  = 0x08;

    // TView inhibit flags
    /// <summary>Inhibit construction of the standard menu bar.</summary>
    public const ushort noMenuBar    = 0x0001;
    /// <summary>Inhibit construction of the standard desktop.</summary>
    public const ushort noDeskTop    = 0x0002;
    /// <summary>Inhibit construction of the standard status line.</summary>
    public const ushort noStatusLine = 0x0004;
    /// <summary>Inhibit construction of the standard desktop background.</summary>
    public const ushort noBackground = 0x0008;
    /// <summary>Inhibit construction of the standard window frame.</summary>
    public const ushort noFrame      = 0x0010;
    /// <summary>Inhibit construction of the standard viewer component.</summary>
    public const ushort noViewer     = 0x0020;
    /// <summary>Inhibit construction of the standard input-history component.</summary>
    public const ushort noHistory    = 0x0040;

    // TWindow number constants
    /// <summary>Leave a window unnumbered for Alt+number selection.</summary>
    public const ushort wnNoNumber = 0;

    // TWindow palette entries
    /// <summary>Select the blue window palette mapping.</summary>
    public const ushort wpBlueWindow = 0;
    /// <summary>Select the cyan window palette mapping.</summary>
    public const ushort wpCyanWindow = 1;
    /// <summary>Select the gray window palette mapping.</summary>
    public const ushort wpGrayWindow = 2;

    // Color dialog broadcast commands.
    /// <summary>Broadcast a newly selected foreground palette index.</summary>
    public const ushort cmColorForegroundChanged = 71;
    /// <summary>Broadcast a newly selected background palette index.</summary>
    public const ushort cmColorBackgroundChanged = 72;
    /// <summary>Broadcast the current packed color attribute to color selectors.</summary>
    public const ushort cmColorSet               = 73;
    /// <summary>Broadcast a group's palette-item list to the color-item viewer.</summary>
    public const ushort cmNewColorItem           = 74;
    /// <summary>Broadcast the newly focused one-based palette-entry index.</summary>
    public const ushort cmNewColorIndex          = 75;
    /// <summary>Remember the focused item index for the currently selected color group.</summary>
    public const ushort cmSaveColorIndex         = 76;
    /// <summary>Broadcast that the edited colors should be redrawn.</summary>
    public const ushort cmUpdateColorsChanged    = 77;
    /// <summary>TSharpVision extension command for previewing edited colors; kept outside the Borland color-command range.</summary>
    public const ushort cmTryColors              = 78;
}
