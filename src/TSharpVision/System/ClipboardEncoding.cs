using System;
using System.Text;

namespace TSharpVision;

// Encoding and newline policy at the editor / OS-clipboard boundary.
//
// The editor buffer is byte[] with LF newlines and ASCII / Latin-1
// semantics (no Unicode redesign in v1). The OS clipboard exposes
// strings (UTF-16 on Win32 with CRLF newlines).
//
// Conversion policy (deliberately minimal — see
// docs/os-clipboard-design-audit.md §9):
//
//   editor bytes -> string : each byte b becomes char (char)b (Latin-1).
//                            Newlines are preserved (LF).
//   string -> editor bytes : CRLF/CR are normalised to LF.
//                            char c <= 0x00FF becomes (byte)c.
//                            char c >  0x00FF becomes '?' (0x3F).
//                            embedded NUL becomes ' ' (0x20) to keep the
//                            gap-buffer terminator assumption intact.
//
// Newline transforms onto / off of CRLF are an OS-service responsibility
// (see Win32ClipboardService). The editor side defensively normalises
// incoming CRLF/CR anyway, so any service implementation that forwards
// raw OS bytes does the right thing.

/// <summary>
/// Helpers for the editor / clipboard boundary.
/// </summary>
public static class ClipboardEncoding
{
    /// <summary>
    /// Replacement byte emitted when an incoming character cannot be
    /// represented in the Latin-1 byte buffer.
    /// </summary>
    public const byte UnsupportedReplacement = 0x3F; // '?'

    /// <summary>
    /// Replacement byte for embedded NUL characters in incoming clipboard
    /// text. NUL would corrupt the gap-buffer terminator assumption.
    /// </summary>
    public const byte NulReplacement = 0x20; // ' '

    /// <summary>
    /// Default upper bound (in characters) on a paste payload accepted from
    /// the OS clipboard. Mutable so smoke tests may dial it down to a
    /// testable size without allocating the full 16 MiB.
    /// </summary>
    public static int MaxPasteChars = 16 * 1024 * 1024;

    /// <summary>
    /// Converts a Latin-1 byte slice to a string. Used by the editor when
    /// mirroring a selection to <see cref="ClipboardService.Current"/>.
    /// Newlines are preserved (callers' own responsibility, e.g. Win32
    /// service applies LF→CRLF before allocating the clipboard buffer).
    /// </summary>
    public static string BytesToClipboardString(byte[] buffer, int start, int length)
    {
        if (buffer == null || length <= 0) return string.Empty;
        var sb = new StringBuilder(length);
        int end = start + length;
        for (int i = start; i < end; i++)
            sb.Append((char)buffer[i]);
        return sb.ToString();
    }

    /// <summary>
    /// Converts a clipboard string into editor-shaped bytes per the v1
    /// policy: CRLF/CR → LF, char→byte (Latin-1, replacement on overflow),
    /// NUL → space. Returns an empty array on null input. If the input is
    /// longer than <see cref="MaxPasteChars"/> the result is empty (paste
    /// is treated as a safe no-op).
    /// </summary>
    public static byte[] ClipboardStringToBytes(string? text)
    {
        if (string.IsNullOrEmpty(text)) return Array.Empty<byte>();
        if (text.Length > MaxPasteChars) return Array.Empty<byte>();

        // Worst-case length is the input length; CRLF→LF only shrinks.
        var buf = new byte[text.Length];
        int n = 0;
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (c == '\r')
            {
                // CRLF or bare CR → LF.
                buf[n++] = 0x0A;
                if (i + 1 < text.Length && text[i + 1] == '\n')
                    i++;
                continue;
            }
            if (c == '\0')
            {
                buf[n++] = NulReplacement;
                continue;
            }
            buf[n++] = c <= 0x00FF ? (byte)c : UnsupportedReplacement;
        }
        if (n == buf.Length) return buf;
        var result = new byte[n];
        Array.Copy(buf, result, n);
        return result;
    }

    /// <summary>
    /// Normalises LF → CRLF for OS clipboards that prefer CRLF (Win32).
    /// Leaves existing CRLF / lone CR alone.
    /// </summary>
    public static string NormalizeToCrLf(string text)
    {
        if (string.IsNullOrEmpty(text)) return text ?? string.Empty;
        var sb = new StringBuilder(text.Length + 16);
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (c == '\n')
            {
                // Lone LF → CRLF; existing CRLF (preceded by CR) → leave LF.
                if (i == 0 || text[i - 1] != '\r')
                    sb.Append('\r');
                sb.Append('\n');
            }
            else
            {
                sb.Append(c);
            }
        }
        return sb.ToString();
    }

    /// <summary>
    /// Normalises CRLF / lone CR → LF. Used by Win32 service when reading
    /// from the OS clipboard, but safe to call on any string.
    /// </summary>
    public static string NormalizeFromCrLf(string text)
    {
        if (string.IsNullOrEmpty(text)) return text ?? string.Empty;
        var sb = new StringBuilder(text.Length);
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (c == '\r')
            {
                sb.Append('\n');
                if (i + 1 < text.Length && text[i + 1] == '\n')
                    i++;
                continue;
            }
            sb.Append(c);
        }
        return sb.ToString();
    }
}
