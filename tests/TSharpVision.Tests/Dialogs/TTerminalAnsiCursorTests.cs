using TSharpVision;
using TSharpVision.Constants;
using TSharpVision.Tests.Infrastructure;
using Xunit;

namespace TSharpVision.Tests.Dialogs;

/// <summary>
/// Tests for TTerminal ANSI cursor movement, erase patterns, and shell-mode
/// (RawSession) ESC/Backspace behavior.
///
/// VT100 semantics enforced here:
///   - Cursor-right (CUF) moves _cursorColumn only; it does NOT pad _currentLine
///     with spaces unless a character is subsequently written there (lazy padding).
///   - Backspace (BS / 0x08) moves _cursorColumn left without erasing content.
///   - Erase-in-line (ESC[K) truncates _currentLine at the cursor column.
///   - The backspace-space-backspace pattern (\b SPACE \b) is used by cmd.exe to
///     erase typed characters one by one; the terminal overwrites the position with
///     a space and returns the cursor to the same location.
/// </summary>
[Collection("NonParallel")]
public sealed class TTerminalAnsiCursorTests
{
    private static TTerminal CreateTerminal(DriverScope driver)
    {
        var term = new TTerminal(new TRect(0, 0, 80, 25));
        term.AnsiEnabled = true;
        return term;
    }

    // ── CUF (cursor-right) semantics ──────────────────────────────────────────

    /// <summary>
    /// CUF followed by a character produces correct output via lazy padding in
    /// the character-write path.  "A\x1b[3CB\n" must produce "A   B".
    /// </summary>
    [Fact]
    public void CursorRight_FollowedByChar_ProducesLazyPadding()
    {
        using var driver = new DriverScope();
        var term = CreateTerminal(driver);

        term.Write("A\x1b[3CB\n");

        Assert.Single(term.Lines);
        Assert.Equal("A   B", term.Lines[0]);
    }

    /// <summary>
    /// CUF with no following character does NOT insert spaces into the line.
    /// The committed line is empty because no characters were written.
    /// </summary>
    [Fact]
    public void CursorRight_WithNoFollowingChar_DoesNotPadLine()
    {
        using var driver = new DriverScope();
        var term = CreateTerminal(driver);

        term.Write("\x1b[5C\n");

        Assert.Single(term.Lines);
        Assert.Equal(string.Empty, term.Lines[0]);
    }

    /// <summary>
    /// CUF on an empty line followed by a character inserts a gap (lazy padding).
    /// </summary>
    [Fact]
    public void CursorRight_OnEmptyLine_ThenChar_ProducesGap()
    {
        using var driver = new DriverScope();
        var term = CreateTerminal(driver);

        term.Write("\x1b[4C");
        term.Write("X\n");

        Assert.Single(term.Lines);
        Assert.Equal("    X", term.Lines[0]);
    }

    /// <summary>
    /// CUF over existing text positions cursor without duplicating characters.
    /// A subsequent write overwrites the character at that column.
    /// </summary>
    [Fact]
    public void CursorRight_DoesNotDuplicateExistingChars()
    {
        using var driver = new DriverScope();
        var term = CreateTerminal(driver);

        term.Write("ABCDE");
        term.Write("\x1b[1G");   // CHA: cursor to column 1 (1-based = index 0)
        term.Write("\x1b[2C");   // CUF: cursor forward 2 → index 2
        term.Write("X\n");       // overwrite 'C' with 'X'

        Assert.Single(term.Lines);
        Assert.Equal("ABXDE", term.Lines[0]);
    }

    /// <summary>
    /// CR + CUF after existing content: cursor repositions over existing text
    /// without extending the line.  The committed line is the original content.
    /// This is a regression guard: CUF must not pad when the line already has
    /// content at or beyond the target column.
    /// </summary>
    [Fact]
    public void CursorRight_AfterCR_PositionsWithoutExtendingLine()
    {
        using var driver = new DriverScope();
        var term = CreateTerminal(driver);

        term.Write("C:\\>123456");   // "C:\>123456", col=10
        term.Write("\r");            // col=0; line unchanged
        term.Write("\x1b[6C");      // CUF 6: col=6; line not modified
        term.Write("\n");

        Assert.Single(term.Lines);
        Assert.Equal("C:\\>123456", term.Lines[0]);
    }

    // ── Backspace-space-backspace erase pattern ───────────────────────────────

    /// <summary>
    /// cmd.exe erases typed characters with \b(space)\b per character.
    /// After three such patterns for "ABC", the prompt chars must remain and the
    /// typed chars must be overwritten with spaces.
    /// </summary>
    [Fact]
    public void BackspaceSpaceBackspace_ErasesTypedChars()
    {
        using var driver = new DriverScope();
        var term = CreateTerminal(driver);

        term.Write("C:\\>ABC");       // _currentLine="C:\>ABC", col=7
        term.Write("\b \b\b \b\b \b"); // BSP-SPC-BSP × 3
        term.Write("\n");

        Assert.Single(term.Lines);
        string line = term.Lines[0];
        // Prompt must be intact.
        Assert.StartsWith("C:\\>", line);
        // "ABC" must no longer appear after the prompt.
        Assert.Equal("C:\\>   ", line);  // spaces where ABC was; trailing whitespace is harmless
    }

    /// <summary>
    /// cmd.exe ESC response using CR + prompt redraw + ESC[K:
    /// the visible line after commit must be exactly the prompt.
    /// </summary>
    [Fact]
    public void CmdEsc_CrPromptEraseInLine_ClearsTypedInput()
    {
        using var driver = new DriverScope();
        var term = CreateTerminal(driver);

        term.Write("C:\\>ABC");    // line: "C:\>ABC", col=7
        term.Write("\r");          // col=0
        term.Write("C:\\>");      // overwrite cols 0-3 with prompt; col=4
        term.Write("\x1b[K");     // ESC[K: erase from col 4 to end
        term.Write("\n");

        Assert.Single(term.Lines);
        Assert.Equal("C:\\>", term.Lines[0]);
    }

    /// <summary>
    /// After BSP-SPC-BSP erasure, an explicit erase-in-line cleans trailing
    /// spaces so the committed line is exactly the prompt.
    /// </summary>
    [Fact]
    public void BackspaceErase_FollowedByEraseInLine_LeavesCleanPrompt()
    {
        using var driver = new DriverScope();
        var term = CreateTerminal(driver);

        term.Write("C:\\>ABC");
        term.Write("\b \b\b \b\b \b");  // BSP-SPC-BSP × 3; cursor at col 4
        term.Write("\x1b[K");            // erase from col 4 to end
        term.Write("\n");

        Assert.Single(term.Lines);
        Assert.Equal("C:\\>", term.Lines[0]);
    }

    /// <summary>
    /// Erase-in-line mode 0 works with any prompt length — no hardcoded values.
    /// </summary>
    [Fact]
    public void EraseInLine_DifferentPrompt_NoPadding()
    {
        using var driver = new DriverScope();
        var term = CreateTerminal(driver);

        const string prompt = "D:\\Work\\Repo>";
        term.Write(prompt + "XYZ");
        term.Write("\r");
        term.Write(prompt);
        term.Write("\x1b[K");
        term.Write("\n");

        Assert.Single(term.Lines);
        Assert.Equal(prompt, term.Lines[0]);
    }

    // ── ESC in Command mode must not produce spaces ───────────────────────────

    [Fact]
    public void EscKey_CommandMode_DoesNotInsertCharacters()
    {
        using var driver = new DriverScope();
        var term = CreateTerminal(driver);
        term.InputMode = TerminalInputMode.Command;
        term.InputEnabled = true;

        term.InputInsertChar('a');
        term.InputInsertChar('b');
        term.InputInsertChar('c');

        TEvent ev = default;
        ev.What = Events.evKeyDown;
        ev.keyDown.keyCode = Keys.kbEsc;
        ev.keyDown.charScan.charCode = 0x1B;
        term.HandleEvent(ref ev);

        // ESC without selection must not modify the input buffer.
        Assert.Equal("abc", term.InputBuffer);
    }

    // ── Backspace in Command mode respects the start of the input buffer ──────

    [Fact]
    public void Backspace_CommandMode_CannotGoBeforeStart()
    {
        using var driver = new DriverScope();
        var term = CreateTerminal(driver);
        term.InputMode = TerminalInputMode.Command;
        term.InputEnabled = true;

        term.InputBackspace();  // empty buffer — must be a no-op
        Assert.Equal(string.Empty, term.InputBuffer);
        Assert.Equal(0, term.InputCursor);
    }

    [Fact]
    public void Backspace_CommandMode_DeletesAtCursor()
    {
        using var driver = new DriverScope();
        var term = CreateTerminal(driver);
        term.InputMode = TerminalInputMode.Command;
        term.InputEnabled = true;

        term.InputInsertChar('a');
        term.InputInsertChar('b');
        term.InputInsertChar('c');
        Assert.Equal("abc", term.InputBuffer);
        Assert.Equal(3, term.InputCursor);

        term.InputBackspace();
        Assert.Equal("ab", term.InputBuffer);
        Assert.Equal(2, term.InputCursor);
    }

    // ── CUP (Cursor Position / ESC[row;colH) semantics ───────────────────────

    /// <summary>
    /// CUP followed by writing chars overwrites existing content at the logical
    /// position derived from the absolute terminal column.  The first CUP for a
    /// new line bootstraps the base column so that subsequent CUPs with the same
    /// absolute column map to logical column 0.
    ///
    /// Sequence: write "ABC", send ESC cmd.exe response, write "D", commit.
    /// Expected committed line: "D" (not "ABC   D").
    /// </summary>
    [Fact]
    public void CupEscSequence_ErasesTypedInput_AndAllowsNewContent()
    {
        using var driver = new DriverScope();
        var term = CreateTerminal(driver);

        // Simulate raw char echoes from cmd.exe as user types "ABC".
        term.Write("ABC");
        Assert.Equal("ABC", term.CurrentLine);

        // cmd.exe responds to ESC: hide cursor, reposition to col 86 (1-based),
        // overwrite 3 chars with spaces, reposition again, show cursor.
        term.Write("\x1B[?25l\x1B[5;86H   \x1B[5;86H\x1B[?25h");

        // After the sequence the current line should be empty and cursor at 0.
        Assert.Equal(string.Empty, term.CurrentLine);
        Assert.Equal(0, term.CursorColumn);

        // Typing "D" should produce a clean line with just "D".
        term.Write("D\n");

        Assert.Single(term.Lines);
        Assert.Equal("D", term.Lines[0]);
    }

    /// <summary>
    /// CUP-based ESC erase with a different prompt column (col 40) to ensure the
    /// base-column bootstrap is not hardcoded to 86.
    /// </summary>
    [Fact]
    public void CupEscSequence_DifferentColumn_ErasesTypedInput()
    {
        using var driver = new DriverScope();
        var term = CreateTerminal(driver);

        term.Write("XY");

        // ESC response positions cursor at col 40 (1-based) = absolute 39.
        term.Write("\x1B[3;40H  \x1B[3;40H");

        Assert.Equal(string.Empty, term.CurrentLine);
        Assert.Equal(0, term.CursorColumn);

        term.Write("Z\n");

        Assert.Single(term.Lines);
        Assert.Equal("Z", term.Lines[0]);
    }

    /// <summary>
    /// CUP that repositions cursor mid-line leaves content before the cursor
    /// intact.  "ABCDE" with CUP to col 3 (logical 2) followed by overwrite "XX"
    /// → "ABXXE".
    /// </summary>
    [Fact]
    public void CupMidLine_OverwritesCorrectCells()
    {
        using var driver = new DriverScope();
        var term = CreateTerminal(driver);

        // Bootstrap base at col 1 (absolute 0) via first CUP.
        term.Write("\x1B[1;1HABCDE");
        Assert.Equal("ABCDE", term.CurrentLine);

        // CUP to col 3 (1-based) → logical col 2.
        term.Write("\x1B[1;3HXX\n");

        Assert.Single(term.Lines);
        Assert.Equal("ABXXE", term.Lines[0]);
    }

    /// <summary>
    /// Trailing spaces after the cursor (produced by overwriting with spaces and
    /// repositioning) are NOT trimmed when followed by non-space content.
    /// "A   B" must survive a CUP that repositions the cursor elsewhere.
    /// </summary>
    [Fact]
    public void CupReposition_PreservesEmbeddedSpaces()
    {
        using var driver = new DriverScope();
        var term = CreateTerminal(driver);

        // Bootstrap at col 1.
        term.Write("\x1B[1;1HA   B");
        Assert.Equal("A   B", term.CurrentLine);

        // CUP back to col 2 (logical 1) — content from col 1 onward is "   B",
        // which is NOT all spaces (ends with 'B'), so no trimming should occur.
        term.Write("\x1B[1;2H\n");

        Assert.Single(term.Lines);
        Assert.Equal("A   B", term.Lines[0]);
    }
}
