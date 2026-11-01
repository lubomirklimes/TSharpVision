using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

namespace TSharpVision;

// Mirrors the shape of upstream TVIntl
// a single static facade, a swappable provider, and a default English
// implementation. No gettext, no .resx.
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
    [return: NotNullIfNotNull(nameof(fallback))]
    string? Get(string key, string? fallback);
}

/// <summary>
/// Optional hit/miss-aware provider contract.
/// </summary>
public interface ITSharpVisionStringLookupProvider : ITSharpVisionStringProvider
{
    /// <summary>
    /// Returns true when <paramref name="key"/> is present and provides the
    /// localized value without consulting a caller fallback.
    /// </summary>
    bool TryGet(string key, [NotNullWhen(true)] out string? value);
}

/// <summary>
/// Raised when a hit/miss-aware provider definitively misses a localization key.
/// </summary>
public sealed class MissingLocalizationKeyEventArgs : System.EventArgs
{
    /// <summary>Captures an unresolved localization key, the text used instead, and the provider that lacked it.</summary>
    public MissingLocalizationKeyEventArgs(
        string key,
        string fallback,
        ITSharpVisionStringProvider provider)
    {
        Key = key;
        Fallback = fallback;
        Provider = provider;
    }

    /// <summary>Localization key that could not be resolved.</summary>
    public string Key { get; }
    /// <summary>Caller-supplied text returned for the unresolved key.</summary>
    public string Fallback { get; }
    /// <summary>Provider queried when the key was found to be missing.</summary>
    public ITSharpVisionStringProvider Provider { get; }
}

/// <summary>
/// Built-in English string provider. Returns the canonical English literals
/// that match every hard-coded string in the TSharpVision library.
/// Unknown keys fall back to the caller-supplied literal.
/// </summary>
public sealed class DefaultEnglishStringProvider : ITSharpVisionStringLookupProvider
{
    private static readonly Dictionary<string, string> _strings = new()
    {
        // Standard buttons
        ["Btn_OK"]              = "~O~K",
        ["Btn_Cancel"]          = "~C~ancel",
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
        ["File_Btn_Clear"]          = "C~l~ear",
        ["File_ConfirmOverwrite"]   = "File '{0}' already exists. Overwrite?",
        ["File_Err_InvalidFileName"]= "Invalid file name.",
        ["File_Err_InvalidDir"]     = "Invalid drive or directory.",
        ["File_Err_CannotOpenDir"]  = "Cannot open directory: '{0}'",
        ["File_Err_AccessDenied"]   = "Access denied: '{0}'",
        ["File_Err_InvalidPath"]    = "Invalid path: '{0}'",
        ["File_Err_PathTooLong"]    = "Path is too long.",
        ["File_Err_NetworkUnavailable"] = "Network location is unavailable.",
        ["File_Err_NotFound"]       = "File or directory not found.",
        ["File_Label_Encoding"]     = "~E~ncoding",
        ["File_Err_EncodingDecode"] = "The file could not be decoded using the selected encoding.",
        ["File_Err_EncodingEncode"] = "The file contains characters that cannot be saved using the selected encoding.",
        ["Encoding_Auto"]           = "Auto",
        ["Encoding_UTF8"]           = "UTF-8",
        ["Encoding_Latin1"]         = "Latin-1",
        ["Encoding_CP437"]          = "CP437",
        ["Encoding_CP852"]          = "CP852",
        ["Encoding_Windows1250"]    = "Windows-1250",
        ["Encoding_ISO8859_2"]      = "ISO-8859-2",
        ["Encoding_Kamenicky"]      = "Kamenicky / KEYBCS2",

        // Change-directory dialog
        ["ChDir_Title"]         = "Change Directory",
        ["ChDir_Label_DirectoryName"] = "Directory ~n~ame",
        ["ChDir_Label_DirectoryTree"] = "Directory ~t~ree",
        ["ChDir_Btn_Chdir"]     = "~C~hdir",
        ["ChDir_Btn_Revert"]    = "~R~evert",
        ["DirList_Drives"]      = "Drives",

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
        ["Edit_Chk_CaseSensitive"] = "Case ~s~ensitive",
        ["Edit_Chk_WholeWords"]    = "~W~hole words only",
        ["Edit_Chk_PromptOnReplace"] = "~P~rompt on replace",
        ["Edit_Chk_ReplaceAll"]    = "Replace ~A~ll",

        // Editor menus
        ["Menu_File"]           = "~F~ile",
        ["Menu_New"]            = "~N~ew",
        ["Menu_Open"]           = "~O~pen...",
        ["Menu_Save"]           = "~S~ave",
        ["Menu_SaveAs"]         = "S~a~ve As...",
        ["Menu_Exit"]           = "E~x~it",
        ["Menu_Edit"]           = "~E~dit",
        ["Menu_Undo"]           = "~U~ndo",
        ["Menu_Cut"]            = "Cu~t~",
        ["Menu_Copy"]           = "~C~opy",
        ["Menu_Paste"]          = "~P~aste",
        ["Menu_Clear"]          = "C~l~ear",
        ["Menu_Search"]         = "~S~earch",
        ["Menu_Find"]           = "~F~ind...",
        ["Menu_Replace"]        = "~R~eplace...",
        ["Menu_Again"]          = "~A~gain",
        ["Menu_Window"]         = "~W~indow",
        ["Menu_SizeMove"]       = "~S~ize/Move",
        ["Menu_Zoom"]           = "~Z~oom",
        ["Menu_Tile"]           = "~T~ile",
        ["Menu_Cascade"]        = "C~a~scade",
        ["Menu_Next"]           = "~N~ext",
        ["Menu_Previous"]       = "~P~revious",
        ["Menu_Close"]          = "~C~lose",

        // Status lines
        ["Status_F10_Menu"]     = "~F10~ Menu",
        ["Status_F2_Save"]      = "~F2~ Save",
        ["Status_F3_Open"]      = "~F3~ Open",
        ["Status_AltX_Exit"]    = "~Alt+X~ Exit",
        ["Status_AltX_ExitDash"]= "~Alt-X~ Exit",
        ["Status_F5_Zoom"]      = "~F5~ Zoom",
        ["Status_F6_Next"]      = "~F6~ Next",

        // Help
        ["Help_WindowTitle"]    = "Help",
        ["Help_NoContext"]      = "\n No help available in this context.",
        ["Help_Search_Title"] = "Search Help",
        ["Help_Search_Words"] = "~W~ords",
        ["Help_Search_NoResults"] = "No help topics contain '{0}'.",
        ["Help_Search_ResultsTitle"] = "Help Topics Found",
        ["Help_Search_Topics"] = "~T~opics ({0})",
        ["Help_Search_Unavailable"] = "That help file is no longer available.",

        // Lists
        ["List_Empty"]          = "<empty>",

        // Color dialog
        ["Color_Title"]         = "Colors",
        ["Color_Lbl_Group"]     = "~G~roup",
        ["Color_Lbl_Item"]      = "~I~tem",
        ["Color_Lbl_Foreground"]= "~F~oreground",
        ["Color_Lbl_Background"]= "~B~ackground",
        ["Color_Lbl_Color"]     = "Color",
        ["Color_PreviewText"]   = "Text ",
        ["Color_Btn_Try"]       = "~T~ry",

        // Graphics (TSharpVision.Graphics: GView text-driver fallback)
        ["Graphics_Unavailable"] = "Pixel graphics need an SDL display driver.",

        // Table view (TSharpVision.TableView)
        ["Table_Loading"]       = "Loading…",
        ["Table_NoColumns"]     = "<no columns>",
        ["Table_RowUnavailable"] = "! row not supplied",
    };

    /// <inheritdoc/>
    [return: NotNullIfNotNull(nameof(fallback))]
    public string? Get(string key, string? fallback)
        => TryGet(key, out var v) ? v : fallback;

    /// <inheritdoc/>
    public bool TryGet(string key, [NotNullWhen(true)] out string? value)
        => _strings.TryGetValue(key, out value);

    // English key inventory; translation parity checks compare against it.
    internal static IReadOnlyDictionary<string, string> Entries => _strings;
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
    // The stock provider instance. Configuration-driven localization only replaces
    // Current while it is still this exact instance (or the provider configuration
    // itself installed), so a provider assigned by the application always wins.
    internal static readonly DefaultEnglishStringProvider BuiltInDefault = new();

    /// <summary>
    /// The active string provider. Replace to inject a locale or test stub.
    /// <para>
    /// <see cref="TSharpVisionRuntime.Configure"/> installs the translation named by the
    /// <c>[localization] language</c> configuration key only while this property still
    /// holds the built-in English provider; any provider assigned by the application
    /// (including a new <see cref="DefaultEnglishStringProvider"/>, or one installed by
    /// <see cref="UseBuiltInLanguage"/>) is never replaced.
    /// </para>
    /// </summary>
    public static ITSharpVisionStringProvider Current { get; set; }
        = BuiltInDefault;

    /// <summary>
    /// Codes of the framework translations compiled into TSharpVision, in ordinal order:
    /// <c>cs</c>, <c>de</c>, <c>en</c>, <c>es</c>, <c>fr</c>, <c>it</c>, <c>pl</c>, <c>sk</c>.
    /// Each is accepted by <see cref="UseBuiltInLanguage"/> and <see cref="CreateBuiltInProvider"/>.
    /// </summary>
    public static IReadOnlyList<string> BuiltInLanguages => TSharpVisionLocalization.BuiltInLanguages;

    /// <summary>
    /// Switches the framework strings to a built-in translation now, replacing
    /// <see cref="Current"/> whatever it holds.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The value is matched like the <c>[localization] language</c> configuration key: by its
    /// primary subtag, case-insensitively (<c>cs</c>, <c>CS</c>, <c>cs-CZ</c> and
    /// <c>cs_CZ.UTF-8</c> all select Czech). The operating-system culture is never consulted.
    /// The installed provider is the one <see cref="CreateBuiltInProvider"/> returns, so an
    /// optional <c>tsharpvision_{lang}.tvr</c> in <see cref="System.AppContext.BaseDirectory"/>
    /// still overrides single keys, and keys missing from a translation show in English.
    /// </para>
    /// <para>
    /// The choice counts as the application's own provider: a later
    /// <see cref="TSharpVisionRuntime.Configure"/> does not replace it. <c>en</c> installs English
    /// in the same way.
    /// </para>
    /// <para>
    /// Only text looked up after the switch is translated. Views copy their text when they are
    /// constructed, so open dialogs, menus and status lines keep their old captions until the
    /// application recreates them. Call this on the UI thread, before the event loop starts or
    /// between events.
    /// </para>
    /// </remarks>
    /// <param name="language">Language code such as <c>de</c> or <c>de-AT</c>.</param>
    /// <returns>
    /// True when the translation is active; false when TSharpVision has no translation for
    /// <paramref name="language"/> (unknown, blank or malformed), in which case
    /// <see cref="Current"/> is left unchanged.
    /// </returns>
    public static bool UseBuiltInLanguage(string? language)
        => TSharpVisionLocalization.UseLanguage(language);

    /// <summary>
    /// Creates a provider for a built-in translation without installing it, so an application
    /// can put its own strings in front of it with <see cref="TSharpVisionStringProviderChain"/>.
    /// </summary>
    /// <remarks>
    /// The provider consults, in order, an optional <c>tsharpvision_{lang}.tvr</c> in
    /// <see cref="System.AppContext.BaseDirectory"/>, the translation embedded in TSharpVision,
    /// and English. Language matching is the same as for <see cref="UseBuiltInLanguage"/>.
    /// Each call returns a new instance.
    /// </remarks>
    /// <param name="language">Language code such as <c>de</c> or <c>de-AT</c>.</param>
    /// <returns>The provider, or null when TSharpVision has no translation for <paramref name="language"/>.</returns>
    public static ITSharpVisionStringProvider? CreateBuiltInProvider(string? language)
        => TSharpVisionLocalization.CreateRuntimeProvider(language, System.AppContext.BaseDirectory);

    /// <summary>Raised synchronously when a lookup-capable current provider lacks a key, before its fallback is returned.</summary>
    public static event System.EventHandler<MissingLocalizationKeyEventArgs>? MissingKey;

    /// <summary>
    /// Translate <paramref name="key"/>, falling back to
    /// <paramref name="fallback"/> if the key is unknown.
    /// </summary>
    public static string Get(string key, string fallback)
    {
        var provider = Current;
        if (provider is ITSharpVisionStringLookupProvider lookupProvider)
        {
            if (lookupProvider.TryGet(key, out string? value))
                return value;

            MissingKey?.Invoke(
                null,
                new MissingLocalizationKeyEventArgs(key, fallback, provider));
            return fallback;
        }

        return provider.Get(key, fallback);
    }

    /// <summary>
    /// Convenience wrapper matching the upstream <c>_()</c> macro shape.
    /// <para>
    /// <c>_("~O~K")</c> — uses the literal as the key;
    /// <c>_("~O~K", "Btn_OK")</c> — uses <paramref name="key"/> for lookup,
    /// literal as fallback.
    /// </para>
    /// </summary>
    public static string _(string englishLiteral, string? key = null)
        => Get(key ?? englishLiteral, englishLiteral);
}

/// <summary>
/// Translation extension methods for TSharpVision types. These are not strictly
/// necessary, but provide a convenient way to access localized strings.
/// </summary>
public static class TInternationalizationExtensions
{
    /// <summary>Resolves a localization key, using the supplied fallback or the key itself when fallback is null.</summary>
    public static string Loc(string key, string? fallback = null)
        => TSharpVisionIntl.Get(key, fallback ?? key);
}
