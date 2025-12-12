// TSharpVision Resource Compiler
// Built-in command ID table, key name resolver, and palette name validator.
using TSharpVision.Constants;

namespace TSharpVision.ResourceCompiler;

/// <summary>
/// Resolves command identifier strings used in .trc files to ushort command
/// codes.  Built-in well-known names come first; user-defined <c>const</c>
/// values are supplied at runtime.
/// </summary>
public static class CommandIds
{
    // Fixed built-in map referencing TSharpVision.Constants.Views directly.
    private static readonly Dictionary<string, ushort> _builtins =
        new(StringComparer.Ordinal)
        {
            // Standard TV commands
            ["cmValid"]   = Views.cmValid,
            ["cmQuit"]    = Views.cmQuit,
            ["cmClose"]   = Views.cmClose,
            ["cmHelp"]    = Views.cmHelp,
            ["cmOK"]      = Views.cmOK,
            ["cmCancel"]  = Views.cmCancel,
            ["cmYes"]     = Views.cmYes,
            ["cmNo"]      = Views.cmNo,
            ["cmDefault"] = Views.cmDefault,
            ["cmNext"]    = Views.cmNext,
            ["cmPrev"]    = Views.cmPrev,
            ["cmZoom"]    = Views.cmZoom,
            ["cmResize"]  = Views.cmResize,
            // Editor / file commands
            ["cmCut"]     = Views.cmCut,
            ["cmCopy"]    = Views.cmCopy,
            ["cmPaste"]   = Views.cmPaste,
            ["cmUndo"]    = Views.cmUndo,
            ["cmSave"]    = Views.cmSave,
            ["cmSaveAs"]  = Views.cmSaveAs,
            ["cmOpen"]    = Views.cmOpen,
            ["cmNew"]     = Views.cmNew,
        };

    /// <summary>
    /// Tries to resolve an identifier to a command code.
    /// Returns <c>true</c> and sets <paramref name="code"/> on success.
    /// </summary>
    public static bool TryResolve(
        string identifier,
        IReadOnlyDictionary<string, int> userConsts,
        out ushort code)
    {
        // User-defined consts take precedence over built-ins.
        if (userConsts != null && userConsts.TryGetValue(identifier, out int userVal))
        {
            code = (ushort)userVal;
            return true;
        }

        if (_builtins.TryGetValue(identifier, out code))
            return true;

        code = 0;
        return false;
    }

    /// <summary>Returns all built-in names (for testing).</summary>
    public static IEnumerable<string> BuiltinNames => _builtins.Keys;

    // ── Key name resolver  ─────────────────────────────────────────

    // Maps the friendly key strings used in .trc files to TSharpVision key codes.
    // Format examples: "F1", "F2", "Alt+X", "Ctrl+Ins", "Shift+Ins", etc.
    private static readonly Dictionary<string, ushort> _keyNames =
        new(StringComparer.OrdinalIgnoreCase)
        {
            // Function keys
            ["F1"] = Keys.kbF1, ["F2"] = Keys.kbF2, ["F3"] = Keys.kbF3,
            ["F4"] = Keys.kbF4, ["F5"] = Keys.kbF5, ["F6"] = Keys.kbF6,
            ["F7"] = Keys.kbF7, ["F8"] = Keys.kbF8, ["F9"] = Keys.kbF9,
            ["F10"] = Keys.kbF10, ["F11"] = Keys.kbF11, ["F12"] = Keys.kbF12,

            // Shift+F keys
            ["Shift+F1"] = Keys.kbShiftF1, ["Shift+F2"] = Keys.kbShiftF2,
            ["Shift+F3"] = Keys.kbShiftF3, ["Shift+F4"] = Keys.kbShiftF4,
            ["Shift+F5"] = Keys.kbShiftF5, ["Shift+F6"] = Keys.kbShiftF6,
            ["Shift+F7"] = Keys.kbShiftF7, ["Shift+F8"] = Keys.kbShiftF8,
            ["Shift+F9"] = Keys.kbShiftF9, ["Shift+F10"] = Keys.kbShiftF10,

            // Ctrl+F keys
            ["Ctrl+F1"] = Keys.kbCtrlF1, ["Ctrl+F2"] = Keys.kbCtrlF2,
            ["Ctrl+F3"] = Keys.kbCtrlF3, ["Ctrl+F4"] = Keys.kbCtrlF4,
            ["Ctrl+F5"] = Keys.kbCtrlF5, ["Ctrl+F6"] = Keys.kbCtrlF6,
            ["Ctrl+F7"] = Keys.kbCtrlF7, ["Ctrl+F8"] = Keys.kbCtrlF8,
            ["Ctrl+F9"] = Keys.kbCtrlF9, ["Ctrl+F10"] = Keys.kbCtrlF10,

            // Alt+F keys
            ["Alt+F1"] = Keys.kbAltF1, ["Alt+F2"] = Keys.kbAltF2,
            ["Alt+F3"] = Keys.kbAltF3, ["Alt+F4"] = Keys.kbAltF4,
            ["Alt+F5"] = Keys.kbAltF5, ["Alt+F6"] = Keys.kbAltF6,
            ["Alt+F7"] = Keys.kbAltF7, ["Alt+F8"] = Keys.kbAltF8,
            ["Alt+F9"] = Keys.kbAltF9, ["Alt+F10"] = Keys.kbAltF10,

            // Alt+letter
            ["Alt+A"] = Keys.kbAltA, ["Alt+B"] = Keys.kbAltB, ["Alt+C"] = Keys.kbAltC,
            ["Alt+D"] = Keys.kbAltD, ["Alt+E"] = Keys.kbAltE, ["Alt+F"] = Keys.kbAltF,
            ["Alt+G"] = Keys.kbAltG, ["Alt+H"] = Keys.kbAltH, ["Alt+I"] = Keys.kbAltI,
            ["Alt+J"] = Keys.kbAltJ, ["Alt+K"] = Keys.kbAltK, ["Alt+L"] = Keys.kbAltL,
            ["Alt+M"] = Keys.kbAltM, ["Alt+N"] = Keys.kbAltN, ["Alt+O"] = Keys.kbAltO,
            ["Alt+P"] = Keys.kbAltP, ["Alt+Q"] = Keys.kbAltQ, ["Alt+R"] = Keys.kbAltR,
            ["Alt+S"] = Keys.kbAltS, ["Alt+T"] = Keys.kbAltT, ["Alt+U"] = Keys.kbAltU,
            ["Alt+V"] = Keys.kbAltV, ["Alt+W"] = Keys.kbAltW, ["Alt+X"] = Keys.kbAltX,
            ["Alt+Y"] = Keys.kbAltY, ["Alt+Z"] = Keys.kbAltZ,

            // Ctrl+letter — selected common ones
            ["Ctrl+A"] = Keys.kbCtrlA, ["Ctrl+B"] = Keys.kbCtrlB, ["Ctrl+C"] = Keys.kbCtrlC,
            ["Ctrl+D"] = Keys.kbCtrlD, ["Ctrl+E"] = Keys.kbCtrlE, ["Ctrl+F"] = Keys.kbCtrlF,
            ["Ctrl+G"] = Keys.kbCtrlG, ["Ctrl+H"] = Keys.kbCtrlH, ["Ctrl+I"] = Keys.kbCtrlI,
            ["Ctrl+J"] = Keys.kbCtrlJ, ["Ctrl+K"] = Keys.kbCtrlK, ["Ctrl+L"] = Keys.kbCtrlL,
            ["Ctrl+M"] = Keys.kbCtrlM, ["Ctrl+N"] = Keys.kbCtrlN, ["Ctrl+O"] = Keys.kbCtrlO,
            ["Ctrl+P"] = Keys.kbCtrlP, ["Ctrl+Q"] = Keys.kbCtrlQ, ["Ctrl+R"] = Keys.kbCtrlR,
            ["Ctrl+S"] = Keys.kbCtrlS, ["Ctrl+T"] = Keys.kbCtrlT, ["Ctrl+U"] = Keys.kbCtrlU,
            ["Ctrl+V"] = Keys.kbCtrlV, ["Ctrl+W"] = Keys.kbCtrlW, ["Ctrl+X"] = Keys.kbCtrlX,
            ["Ctrl+Y"] = Keys.kbCtrlY, ["Ctrl+Z"] = Keys.kbCtrlZ,

            // Navigation / editing
            ["Ctrl+Ins"]   = Keys.kbCtrlIns,
            ["Shift+Ins"]  = Keys.kbShiftIns,
            ["Ctrl+Del"]   = Keys.kbCtrlDel,
            ["Shift+Del"]  = Keys.kbShiftDel,
            ["Alt+Back"]   = Keys.kbAltBack,
            ["Esc"]        = Keys.kbEsc,
            ["Enter"]      = Keys.kbEnter,
            ["Tab"]        = Keys.kbTab,
            ["Shift+Tab"]  = Keys.kbShiftTab,
        };

    /// <summary>
    /// Tries to resolve a key name string (e.g. "F1", "Alt+X") to a
    /// TSharpVision key code (<see cref="Keys"/>).
    /// Returns <c>true</c> and sets <paramref name="keyCode"/> on success.
    /// On failure, <paramref name="keyCode"/> is set to <see cref="Keys.kbNoKey"/> (0).
    /// </summary>
    public static bool TryResolveKey(string name, out ushort keyCode)
    {
        if (string.IsNullOrEmpty(name)) { keyCode = Keys.kbNoKey; return true; }
        if (_keyNames.TryGetValue(name, out keyCode)) return true;
        keyCode = Keys.kbNoKey;
        return false;
    }

    /// <summary>Returns all registered key name strings (for testing).</summary>
    public static IEnumerable<string> KeyNames => _keyNames.Keys;

    // ── Palette name validator  ────────────────────────────────────
    // Known dialog palette names from the Borland Turbo Vision dialect.
    // TPalette is not TStreamable; these names are validated for compile-time
    // documentation purposes only — they are not applied to the runtime object.
    private static readonly HashSet<string> _knownPaletteNames = new(StringComparer.Ordinal)
    {
        "wpBlueDialog",
        "wpCyanDialog",
        "wpGrayDialog",
        "wpGreenDialog",
    };

    /// <summary>
    /// Returns <c>true</c> if <paramref name="name"/> is a recognized Borland
    /// Turbo Vision palette name for dialogs.
    /// </summary>
    public static bool IsKnownPaletteName(string name) =>
        name != null && _knownPaletteNames.Contains(name);

    /// <summary>Returns all recognized palette names (for testing).</summary>
    public static IEnumerable<string> PaletteNames => _knownPaletteNames;
}
