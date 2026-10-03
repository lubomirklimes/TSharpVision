using TSharpVision.Constants;
using TSharpVision.Tests.Infrastructure;
using Xunit;

namespace TSharpVision.Terminal.Tests.View;

/// <summary>
/// cmd.exe interaction scenarios carried over from the pre-U-1b line-based TTerminal: cursor-right over text, the
/// backspace-space-backspace erase, ESC redrawing the prompt, cursor-addressed rewrites, and the command line. On the
/// cell grid they hold without the old line model's padding and base-column heuristics; the CUP scenarios address the
/// row and column cmd.exe really writes to, as a pseudo console of the view's size does.
/// </summary>
[Collection("TerminalView")]
public sealed class TTerminalCmdScenarioTests
{
    private static TTerminal CreateTerminal() => new(new TRect(0, 0, 80, 25));

    // ── CUF (cursor-right) ───────────────────────────────────────────────────

    [Fact]
    public void CursorRightThenACharacterLeavesAGap()
    {
        using var driver = new DriverScope();
        TTerminal term = CreateTerminal();

        term.Write("A\x1b[3CB\n");

        Assert.Single(term.Lines);
        Assert.Equal("A   B", term.Lines[0]);
    }

    [Fact]
    public void CursorRightAloneWritesNothing()
    {
        using var driver = new DriverScope();
        TTerminal term = CreateTerminal();

        term.Write("\x1b[5C\n");

        Assert.Single(term.Lines);
        Assert.Equal(string.Empty, term.Lines[0]);
    }

    [Fact]
    public void CursorRightOnAnEmptyLineThenACharacter()
    {
        using var driver = new DriverScope();
        TTerminal term = CreateTerminal();

        term.Write("\x1b[4C");
        term.Write("X\n");

        Assert.Equal("    X", Assert.Single(term.Lines));
    }

    [Fact]
    public void CursorRightOverTextDoesNotDuplicateIt()
    {
        using var driver = new DriverScope();
        TTerminal term = CreateTerminal();

        term.Write("ABCDE");
        term.Write("\x1b[1G");
        term.Write("\x1b[2C");
        term.Write("X\n");

        Assert.Equal("ABXDE", Assert.Single(term.Lines));
    }

    [Fact]
    public void CursorRightAfterCarriageReturnDoesNotExtendTheLine()
    {
        using var driver = new DriverScope();
        TTerminal term = CreateTerminal();

        term.Write("C:\\>123456");
        term.Write("\r");
        term.Write("\x1b[6C");
        term.Write("\n");

        Assert.Equal("C:\\>123456", Assert.Single(term.Lines));
    }

    // ── backspace-space-backspace ────────────────────────────────────────────

    [Fact]
    public void BackspaceSpaceBackspaceErasesTypedCharacters()
    {
        using var driver = new DriverScope();
        TTerminal term = CreateTerminal();

        term.Write("C:\\>ABC");
        term.Write("\b \b\b \b\b \b");
        term.Write("\n");

        Assert.Equal("C:\\>", Assert.Single(term.Lines));   // the erased cells are blank; text ends at the prompt
    }

    [Fact]
    public void EscapeRedrawsThePromptAndErasesTheRest()
    {
        using var driver = new DriverScope();
        TTerminal term = CreateTerminal();

        term.Write("C:\\>ABC");
        term.Write("\r");
        term.Write("C:\\>");
        term.Write("\x1b[K");
        term.Write("\n");

        Assert.Equal("C:\\>", Assert.Single(term.Lines));
    }

    [Fact]
    public void BackspaceEraseThenEraseInLineLeavesAClearPrompt()
    {
        using var driver = new DriverScope();
        TTerminal term = CreateTerminal();

        term.Write("C:\\>ABC");
        term.Write("\b \b\b \b\b \b");
        term.Write("\x1b[K");
        term.Write("\n");

        Assert.Equal("C:\\>", Assert.Single(term.Lines));
    }

    [Fact]
    public void EraseInLineWorksForAnyPromptLength()
    {
        using var driver = new DriverScope();
        TTerminal term = CreateTerminal();

        const string prompt = "D:\\Work\\Repo>";
        term.Write(prompt + "XYZ");
        term.Write("\r");
        term.Write(prompt);
        term.Write("\x1b[K");
        term.Write("\n");

        Assert.Equal(prompt, Assert.Single(term.Lines));
    }

    // ── the command line ─────────────────────────────────────────────────────

    [Fact]
    public void EscapeOnTheCommandLineInsertsNothing()
    {
        using var driver = new DriverScope();
        TTerminal term = CreateTerminal();
        term.InputMode = TerminalInputMode.Command;

        term.InputInsertChar('a');
        term.InputInsertChar('b');
        term.InputInsertChar('c');

        var ev = new TEvent { What = Events.evKeyDown };
        ev.keyDown.keyCode = Keys.kbEsc;
        ev.keyDown.charScan.charCode = 0x1B;
        term.HandleEvent(ref ev);

        Assert.Equal("abc", term.InputBuffer);
    }

    [Fact]
    public void BackspaceOnAnEmptyCommandLineDoesNothing()
    {
        using var driver = new DriverScope();
        TTerminal term = CreateTerminal();
        term.InputMode = TerminalInputMode.Command;

        term.InputBackspace();

        Assert.Equal(string.Empty, term.InputBuffer);
        Assert.Equal(0, term.InputCursor);
    }

    [Fact]
    public void BackspaceOnTheCommandLineDeletesBeforeTheCaret()
    {
        using var driver = new DriverScope();
        TTerminal term = CreateTerminal();
        term.InputMode = TerminalInputMode.Command;

        term.InputInsertChar('a');
        term.InputInsertChar('b');
        term.InputInsertChar('c');
        term.InputBackspace();

        Assert.Equal("ab", term.InputBuffer);
        Assert.Equal(2, term.InputCursor);
    }

    // ── cursor-addressed rewrites (CUP) ──────────────────────────────────────

    [Fact]
    public void CmdEscapeResponseErasesTypedInputAtItsRealPosition()
    {
        using var driver = new DriverScope();
        TTerminal term = CreateTerminal();

        // The prompt is on row 5; the user typed "ABC" after it.
        term.Write("\x1b[5;1HC:\\>ABC");
        Assert.Equal("C:\\>ABC", term.CurrentLine);

        // cmd.exe answers ESC: hide the cursor, blank the typed text where it is, return there, show the cursor.
        term.Write("\x1b[?25l\x1b[5;5H   \x1b[5;5H\x1b[?25h");
        Assert.Equal("C:\\>", term.CurrentLine);
        Assert.Equal(4, term.CursorColumn);

        term.Write("D");
        Assert.Equal("C:\\>D", term.CurrentLine);
    }

    [Fact]
    public void CursorAddressedRewriteOnAnotherRow()
    {
        using var driver = new DriverScope();
        TTerminal term = CreateTerminal();

        term.Write("\x1b[3;37HXY");
        term.Write("\x1b[3;37H  \x1b[3;37H");
        Assert.Equal(36, term.CursorColumn);

        term.Write("Z");
        Assert.Equal(new string(' ', 36) + "Z", term.CurrentLine);
    }

    [Fact]
    public void CupMidLineOverwritesTheAddressedCells()
    {
        using var driver = new DriverScope();
        TTerminal term = CreateTerminal();

        term.Write("\x1b[1;1HABCDE");
        Assert.Equal("ABCDE", term.CurrentLine);

        term.Write("\x1b[1;3HXX\n");

        Assert.Equal("ABXXE", Assert.Single(term.Lines));
    }

    [Fact]
    public void CupRepositionKeepsEmbeddedSpaces()
    {
        using var driver = new DriverScope();
        TTerminal term = CreateTerminal();

        term.Write("\x1b[1;1HA   B");
        Assert.Equal("A   B", term.CurrentLine);

        term.Write("\x1b[1;2H\n");

        Assert.Equal("A   B", Assert.Single(term.Lines));
    }
}
