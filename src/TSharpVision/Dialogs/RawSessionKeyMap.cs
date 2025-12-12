using TSharpVision.Constants;

namespace TSharpVision;

/// <summary>
/// Maps TV key-event codes to VT-compatible byte sequences for
/// <see cref="TerminalInputMode.RawSession"/> forwarding.
/// </summary>
/// <remarks>
/// Callers must handle <see cref="Keys.kbCtrlC"/> (copy/interrupt) and
/// <see cref="Keys.kbCtrlV"/> (paste) before calling
/// <see cref="GetSequence"/>, as those keys are not mapped here.
/// </remarks>
public static class RawSessionKeyMap
{
    /// <summary>
    /// Returns the VT sequence string for <paramref name="keyCode"/> /
    /// <paramref name="charCode"/>, or <see langword="null"/> when no mapping
    /// exists.
    /// </summary>
    /// <param name="keyCode">TV keyDown.keyCode value.</param>
    /// <param name="charCode">TV keyDown.charScan.charCode value.</param>
    /// <returns>
    /// The string to forward to the session, or <see langword="null"/> if the
    /// key should be ignored.
    /// </returns>
    public static string? GetSequence(ushort keyCode, byte charCode)
    {
        // Printable ASCII characters are forwarded as-is. This guard must come
        // before the keyCode switch because an uppercase letter's ASCII value
        // coincides with certain TV navigation-key constants (e.g. 'G' == kbPgUp,
        // 'L' == kbPgDn), which would otherwise send a VT escape sequence instead
        // of the character itself.
        if (charCode >= 32 && charCode < 127)
            return ((char)charCode).ToString();

        return keyCode switch
        {
            // Enter (also kbCtrlM)
            Keys.kbEnter or Keys.kbCtrlM     => "\r",

            // Backspace — \x7f (DEL) is the common PTY default.
            Keys.kbBack                      => "\x7f",

            // Tab (also kbCtrlI in TV)
            Keys.kbTab or Keys.kbCtrlI       => "\t",

            // Escape
            Keys.kbEsc                       => "\x1b",

            // Delete key → CSI 3~
            Keys.kbDel                       => "\x1b[3~",

            // Arrow keys (ANSI cursor sequences)
            Keys.kbUp                        => "\x1b[A",
            Keys.kbDown                      => "\x1b[B",
            Keys.kbRight                     => "\x1b[C",
            Keys.kbLeft                      => "\x1b[D",

            // Home / End (xterm-style)
            Keys.kbHome                      => "\x1b[H",
            Keys.kbEnd                       => "\x1b[F",

            // Page Up / Page Down → CSI 5~ / CSI 6~
            Keys.kbPgUp                      => "\x1b[5~",
            Keys.kbPgDn                      => "\x1b[6~",

            // Ctrl+D — EOF / end-of-input
            Keys.kbCtrlD                     => "\x04",

            // Ctrl+L — clear screen (form feed)
            Keys.kbCtrlL                     => "\x0c",

            // Ctrl+Z — suspend (POSIX SIGTSTP equivalent in raw PTY)
            Keys.kbCtrlZ                     => "\x1a",

            _ => null,
        };
    }
}
