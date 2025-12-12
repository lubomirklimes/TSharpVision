using System.Collections.Generic;

namespace TSharpVision;

// Mirrors the shape of upstream TVIntl
// a single static facade, a swappable provider, and a default English
// implementation. No gettext, no .resx, no .tvr, no external files.
//
// Usage:
//   TSharpVisionIntl.Get("Btn_OK", "~O~K")          // explicit key + fallback
//   TSharpVisionIntl._("~O~K")                       // literal used as both key and fallback
//   TSharpVisionIntl._("~O~K", "Btn_OK")             // literal fallback, custom key
//
// IMPORTANT: the default English provider returns the current hard-coded
// literals unchanged, so all UI looks identical with the default provider.
//
// Localization note: values may contain hotkey markers such as "~O~K".
// Translators must preserve exactly one "~X~" segment per label because
// TButton/TLabel strip the first such segment during drawing.

/// <summary>
/// Contract for a TSharpVision string provider.
/// </summary>
public interface ITSharpVisionStringProvider
{
    /// <summary>
    /// Returns the localized string for <paramref name="key"/>.
    /// If the key is unknown, returns <paramref name="fallback"/> unchanged.
    /// </summary>
    string Get(string key, string fallback);
}

/// <summary>
/// Built-in English string provider. Returns the canonical English literals
/// that match every hard-coded string in the TSharpVision library.
/// Unknown keys fall back to the caller-supplied literal.
/// </summary>
public sealed class DefaultEnglishStringProvider : ITSharpVisionStringProvider
{
    private static readonly Dictionary<string, string> _strings = new()
    {
        // Standard buttons
        ["Btn_OK"]              = "~O~K",
        ["Btn_Cancel"]          = "Cancel",
        ["Btn_Yes"]             = "~Y~es",
        ["Btn_No"]              = "~N~o",
        ["Btn_Help"]            = "~H~elp",
        ["Btn_Retry"]           = "~R~etry",
        ["Btn_Abort"]           = "~A~bort",
        ["Btn_Ignore"]          = "~I~gnore",
        ["Btn_Done"]            = "Done",

        // Message box titles
        ["MsgTitle_Error"]      = "Error",
        ["MsgTitle_Warning"]    = "Warning",
        ["MsgTitle_Information"]= "Information",
        ["MsgTitle_Confirm"]    = "Confirm",

        // File dialog
        ["File_Title_Open"]         = "Open File",
        ["File_Title_SaveAs"]       = "Save File As",
        ["File_Label_Name"]         = "~N~ame",
        ["File_Label_Files"]        = "~F~iles",
        ["File_Btn_Open"]           = "~O~pen",
        ["File_Btn_Add"]            = "~A~dd",
        ["File_Btn_Select"]         = "~S~elect",
        ["File_Btn_Replace"]        = "~R~eplace",
        ["File_Btn_Clear"]          = "~C~lear",
        ["File_ConfirmOverwrite"]   = "File '{0}' already exists. Overwrite?",
        ["File_Err_InvalidFileName"]= "Invalid file name.",
        ["File_Err_InvalidDir"]     = "Invalid drive or directory.",
        ["File_Err_CannotOpenDir"]  = "Cannot open directory: '{0}'",
        ["File_Err_AccessDenied"]   = "Access denied: '{0}'",
        ["File_Err_InvalidPath"]    = "Invalid path: '{0}'",

        // Change-directory dialog
        ["ChDir_Title"]         = "Change Directory",
        ["ChDir_Btn_Chdir"]     = "~C~hdir",
        ["ChDir_Btn_Revert"]    = "~R~evert",

        // Editor / file editor
        ["Edit_Untitled"]       = "Untitled",
        ["Edit_Clipboard"]      = "Clipboard",
        ["Edit_SaveModify"]     = "{0} has been modified. Save?",
        ["Edit_SaveUntitled"]   = "Save untitled file?",
        ["Edit_Err_Read"]       = "Error reading file '{0}'.",
        ["Edit_Err_Write"]      = "Error writing file '{0}'.",
        ["Edit_Err_Create"]     = "Error creating file '{0}'.",
        ["Edit_Err_OOM"]        = "Not enough memory for editor buffer.",
        ["Edit_SearchFailed"]   = "Search string not found.",
        ["Edit_ReplacePrompt"]  = "Replace this occurrence?",

        // Editor Find/Replace dialogs
        ["Edit_FindTitle"]         = "Find",
        ["Edit_ReplaceTitle"]      = "Replace",
        ["Edit_FindText"]          = "~T~ext to find",
        ["Edit_ReplaceText"]       = "~R~eplace with",
        ["Edit_Chk_CaseSensitive"] = "~C~ase sensitive",
        ["Edit_Chk_WholeWords"]    = "~W~hole words only",
        ["Edit_Chk_PromptOnReplace"] = "~P~rompt on replace",
        ["Edit_Chk_ReplaceAll"]    = "Replace ~A~ll",

        // Help
        ["Help_WindowTitle"]    = "Help",
        ["Help_NoContext"]      = "\n No help available in this context.",

        // Color dialog
        ["Color_Title"]         = "Colors",
        ["Color_Lbl_Group"]     = "~G~roup",
        ["Color_Lbl_Item"]      = "~I~tem",
        ["Color_Lbl_Foreground"]= "~F~oreground",
        ["Color_Lbl_Background"]= "~B~ackground",
        ["Color_Btn_Try"]       = "~T~ry",
    };

    /// <inheritdoc/>
    public string Get(string key, string fallback)
        => _strings.TryGetValue(key, out var v) ? v : fallback;
}

/// <summary>
/// Static localization facade. Matches the shape of upstream
/// <c>TVIntl</c> (a single class with static methods).
/// <para>
/// Thread-safety: <see cref="Current"/> is a simple auto-property and is
/// not safe to swap concurrently with event-dispatch. Swap only before or
/// between event-loop iterations (e.g., in tests). Tests must restore the
/// previous provider in a <c>finally</c> block.
/// </para>
/// </summary>
public static class TSharpVisionIntl
{
    /// <summary>
    /// The active string provider. Replace to inject a locale or test stub.
    /// </summary>
    public static ITSharpVisionStringProvider Current { get; set; }
        = new DefaultEnglishStringProvider();

    /// <summary>
    /// Translate <paramref name="key"/>, falling back to
    /// <paramref name="fallback"/> if the key is unknown.
    /// </summary>
    public static string Get(string key, string fallback)
        => Current.Get(key, fallback);

    /// <summary>
    /// Convenience wrapper matching the upstream <c>_()</c> macro shape.
    /// <para>
    /// <c>_("~O~K")</c> — uses the literal as the key;
    /// <c>_("~O~K", "Btn_OK")</c> — uses <paramref name="key"/> for lookup,
    /// literal as fallback.
    /// </para>
    /// </summary>
    public static string _(string englishLiteral, string? key = null)
        => Current.Get(key ?? englishLiteral, englishLiteral);
}
