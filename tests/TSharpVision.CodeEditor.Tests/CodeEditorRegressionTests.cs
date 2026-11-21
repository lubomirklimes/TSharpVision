using System.Text;
using TSharpVision.Constants;
using TSharpVision.CodeEditor.Syntax;
using Xunit;

namespace TSharpVision.CodeEditor.Tests;

/// <summary>
/// TV13f regressions: undo after highlighted edits, background classification after edits, very long horizontal
/// scroll, CR, and non-BMP / combining / ZWJ text in <see cref="TCodeEditor"/>.
/// </summary>
public sealed class CodeEditorRegressionTests
{
    private static (EditorScreen Screen, ToyClassifier Toy) Build(string text, int width = 40, int height = 8)
    {
        var screen = new EditorScreen(width, height);
        screen.Load(text);
        var toy = new ToyClassifier();
        screen.Syntax.SetClassifier(toy);
        EditorScreen.Pump();
        return (screen, toy);
    }

    private static ushort Normal(EditorScreen screen) => (byte)screen.Editor.GetColor(0x0201);

    private static ushort Role(EditorScreen screen, SyntaxClass role) => SyntaxColorScheme.Default.Apply(role, Normal(screen));

    private static void Command(EditorScreen screen, ushort command)
    {
        var ev = new TEvent { What = Events.evCommand };
        ev.message.command = command;
        screen.Editor.HandleEvent(ref ev);
        EditorScreen.Pump();
    }

    // ── undo after highlighted edits ─────────────────────────────────────────

    [Fact]
    public void UndoOfAnOpenedCommentReHighlightsEveryFollowingLine()
    {
        (EditorScreen screen, _) = Build("if 1\nif 2\nif 3\nif 4");
        using (screen)
        {
            screen.Editor.SetCurPtr(0, 0);
            screen.Type("/*");
            for (int y = 0; y < 4; y++) Assert.Equal(Role(screen, SyntaxClass.Comment), screen.AttributeAt(y == 0 ? 2 : 0, y));

            Command(screen, Views.cmUndo);

            Assert.Equal("if 1\nif 2\nif 3\nif 4", screen.Text);
            for (int y = 0; y < 4; y++)
            {
                Assert.Equal($"if {y + 1}", screen.Row(y));
                Assert.Equal(Role(screen, SyntaxClass.Keyword), screen.AttributeAt(0, y));
                Assert.Equal(Role(screen, SyntaxClass.Number), screen.AttributeAt(3, y));
            }
        }
    }

    [Fact]
    public void UndoOfAJoinedLineSplitsItAgainAndReHighlightsBothLines()
    {
        (EditorScreen screen, ToyClassifier toy) = Build("if 1\nif 2\nword 3");
        using (screen)
        {
            screen.Editor.SetCurPtr(screen.Editor.LineMove(0, 1), 0);
            Command(screen, Views.cmBackSpace);
            Assert.Equal("if 1if 2", screen.Row(0));
            Assert.Equal("word 3", screen.Row(1));
            Assert.Equal(Role(screen, SyntaxClass.Keyword), screen.AttributeAt(4, 0));

            toy.Calls.Clear();
            Command(screen, Views.cmUndo);

            Assert.Equal("if 1\nif 2\nword 3", screen.Text);
            Assert.Equal("if 1", screen.Row(0));
            Assert.Equal("if 2", screen.Row(1));
            Assert.Equal("word 3", screen.Row(2));
            Assert.Equal(Role(screen, SyntaxClass.Keyword), screen.AttributeAt(0, 1));
            Assert.Equal(Role(screen, SyntaxClass.Number), screen.AttributeAt(3, 1));
            Assert.Equal(Normal(screen), screen.AttributeAt(0, 2));
            Assert.Contains("if 2", toy.Calls);
            Assert.DoesNotContain("word 3", toy.Calls);
        }
    }

    [Theory]
    [InlineData("\U0001F600 ")]
    [InlineData("e\u0301 ")]
    [InlineData("\U0001F468\u200D\U0001F469\u200D\U0001F467 ")]
    public void TypingAfterNonBmpCombiningOrZwjTextHighlightsAndUndoRestores(string prefix)
    {
        string text = prefix + "x 7\nif 2";
        (EditorScreen screen, _) = Build(text);
        using (screen)
        {
            screen.Editor.SetCurPtr((uint)prefix.Length, 0);
            screen.Type("if ");

            int x = EditorScreen.Columns(prefix);   // visual columns: a wide or combining cluster is one cell group (TV13o)
            Assert.Equal(prefix + "if x 7", screen.Row(0));
            Assert.Equal(Role(screen, SyntaxClass.Keyword), screen.AttributeAt(x, 0));
            Assert.Equal(Role(screen, SyntaxClass.Keyword), screen.AttributeAt(x + 1, 0));
            Assert.Equal(Role(screen, SyntaxClass.Number), screen.AttributeAt(x + 5, 0));

            Command(screen, Views.cmUndo);

            Assert.Equal(text, screen.Text);
            Assert.Equal(prefix + "x 7", screen.Row(0));
            Assert.Equal(Normal(screen), screen.AttributeAt(x, 0));
            Assert.Equal(Role(screen, SyntaxClass.Number), screen.AttributeAt(x + 2, 0));
            Assert.Equal(Role(screen, SyntaxClass.Keyword), screen.AttributeAt(0, 1));
        }
    }

    // ── redo (TV13b multi-level history) ────────────────────────────────────

    [Fact]
    public void RedoOfAnOpenedCommentHighlightsTheFollowingLinesAsCommentAgain()
    {
        (EditorScreen screen, _) = Build("if 1\nif 2\nif 3");
        using (screen)
        {
            screen.Editor.SetCurPtr(0, 0);
            screen.Type("/*");
            Command(screen, Views.cmUndo);
            Assert.Equal(Role(screen, SyntaxClass.Keyword), screen.AttributeAt(0, 2));

            Command(screen, Views.cmRedo);

            Assert.Equal("/*if 1\nif 2\nif 3", screen.Text);
            Assert.Equal("/*if 1", screen.Row(0));
            for (int y = 1; y < 3; y++)
            {
                Assert.Equal(Role(screen, SyntaxClass.Comment), screen.AttributeAt(0, y));
                Assert.Equal(Role(screen, SyntaxClass.Comment), screen.AttributeAt(3, y));
            }
        }
    }

    [Fact]
    public void UndoAndRedoAcrossTwoSeparateEditsReHighlightEachState()
    {
        (EditorScreen screen, _) = Build("word 1\nword 2");
        using (screen)
        {
            screen.Editor.SetCurPtr(0, 0);
            screen.Type("if ");
            screen.Editor.SetCurPtr(screen.Editor.LineMove(0, 1), 0);   // a caret move closes the first group
            screen.Type("if ");
            Assert.Equal(Role(screen, SyntaxClass.Keyword), screen.AttributeAt(0, 0));
            Assert.Equal(Role(screen, SyntaxClass.Keyword), screen.AttributeAt(0, 1));

            Command(screen, Views.cmUndo);
            Assert.Equal("if word 1\nword 2", screen.Text);
            Assert.Equal(Role(screen, SyntaxClass.Keyword), screen.AttributeAt(0, 0));
            Assert.Equal(Normal(screen), screen.AttributeAt(0, 1));

            Command(screen, Views.cmUndo);
            Assert.Equal("word 1\nword 2", screen.Text);
            Assert.Equal(Normal(screen), screen.AttributeAt(0, 0));
            Assert.Equal(Role(screen, SyntaxClass.Number), screen.AttributeAt(5, 0));

            Command(screen, Views.cmRedo);
            Command(screen, Views.cmRedo);
            Assert.Equal("if word 1\nif word 2", screen.Text);
            Assert.Equal("if word 1", screen.Row(0));
            Assert.Equal("if word 2", screen.Row(1));
            Assert.Equal(Role(screen, SyntaxClass.Keyword), screen.AttributeAt(0, 0));
            Assert.Equal(Role(screen, SyntaxClass.Keyword), screen.AttributeAt(0, 1));
            Assert.Equal(Role(screen, SyntaxClass.Number), screen.AttributeAt(8, 1));
        }
    }

    [Theory]
    [InlineData("\U0001F600 ")]
    [InlineData("e\u0301 ")]
    [InlineData("\U0001F468\u200D\U0001F469\u200D\U0001F467 ")]
    public void RedoAfterNonBmpCombiningOrZwjTextHighlightsAgain(string prefix)
    {
        (EditorScreen screen, _) = Build(prefix + "x 7");
        using (screen)
        {
            screen.Editor.SetCurPtr((uint)prefix.Length, 0);
            screen.Type("if ");
            Command(screen, Views.cmUndo);
            Command(screen, Views.cmRedo);

            int x = EditorScreen.Columns(prefix);
            Assert.Equal(prefix + "if x 7", screen.Text);
            Assert.Equal(prefix + "if x 7", screen.Row(0));
            Assert.Equal(Role(screen, SyntaxClass.Keyword), screen.AttributeAt(x, 0));
            Assert.Equal(Role(screen, SyntaxClass.Number), screen.AttributeAt(x + 5, 0));
        }
    }

    // ── background classification after edits ──────────────────────────────

    [Fact]
    public void AnEditIsDrawnPlainUntilItsClassificationIsDeliveredThenColoured()
    {
        (EditorScreen screen, _) = Build("if 1\nword 2\nword 3\nif 4");
        using (screen)
        {
            // Replace "word" on line 2 with "if" without pumping: the draw must not wait for the classifier.
            uint line2 = screen.Editor.LineMove(0, 2);
            screen.Editor.SetSelect(line2, line2 + 4, true);
            screen.Editor.InsertText("if", selectText: false);
            screen.Editor.Draw();

            Assert.Equal("if 3", screen.Row(2));
            Assert.Equal(Normal(screen), screen.AttributeAt(0, 2));
            Assert.Equal(Role(screen, SyntaxClass.Keyword), screen.AttributeAt(0, 0));

            EditorScreen.Pump();

            Assert.Equal("if 3", screen.Row(2));
            Assert.Equal(Role(screen, SyntaxClass.Keyword), screen.AttributeAt(0, 2));
            Assert.Equal(Role(screen, SyntaxClass.Number), screen.AttributeAt(3, 2));
            Assert.Equal(Role(screen, SyntaxClass.Keyword), screen.AttributeAt(0, 3));
        }
    }

    [Fact]
    public void AnUndoneEditIsDrawnPlainUntilItsClassificationIsDeliveredThenColoured()
    {
        (EditorScreen screen, _) = Build("word 1\nword 2");
        using (screen)
        {
            screen.Editor.SetCurPtr(0, 0);
            screen.Type("if ");
            Assert.Equal(Role(screen, SyntaxClass.Keyword), screen.AttributeAt(0, 0));

            var undo = new TEvent { What = Events.evCommand };
            undo.message.command = Views.cmUndo;
            screen.Editor.HandleEvent(ref undo);       // no pump yet

            Assert.Equal("word 1", screen.Row(0));
            Assert.Equal(Normal(screen), screen.AttributeAt(0, 0));
            Assert.Equal(Normal(screen), screen.AttributeAt(5, 0));

            EditorScreen.Pump();

            Assert.Equal(Role(screen, SyntaxClass.Number), screen.AttributeAt(5, 0));
        }
    }

    // ── very long lines and horizontal scroll ───────────────────────────────

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AOneMebicharLineScrolledToItsEndDrawsWithoutAllocatingAlongTheLine(bool tabs)
    {
        const int Length = 1 << 20;
        string unit = tabs ? "..\t.\r." : ".";
        var builder = new StringBuilder(Length + 16);
        while (builder.Length < Length - 16) builder.Append(unit);
        builder.Append(" if 7 end");
        string line = builder.ToString();
        string text = line + "\nif 2";

        (EditorScreen screen, ToyClassifier toy) = Build(text, width: 40, height: 3);
        using (screen)
        {
            int lineColumns = screen.Editor.CharPos(0, (uint)line.Length);
            int left = lineColumns - 30;
            screen.Editor.limit.x = lineColumns + 100;
            screen.Editor.delta.x = left;
            screen.Draw();                              // classifies and commits

            Assert.Equal(Reference(line, left, 40), screen.Row(0).PadRight(40));
            Assert.Equal(string.Empty, screen.Row(1));
            int keyword = screen.Editor.CharPos(0, (uint)line.IndexOf(" if 7", StringComparison.Ordinal) + 1) - left;
            Assert.Equal(Role(screen, SyntaxClass.Keyword), screen.AttributeAt(keyword, 0));
            Assert.Equal(Role(screen, SyntaxClass.Keyword), screen.AttributeAt(keyword + 1, 0));
            Assert.Equal(Role(screen, SyntaxClass.Number), screen.AttributeAt(keyword + 3, 0));
            Assert.Equal(Normal(screen), screen.AttributeAt(keyword + 5, 0));
            Assert.Contains(toy.Calls, call => call.Length == line.Length);

            // Steady state: nothing in the draw path may grow with the scroll distance (old code: a 4 MiB row per draw).
            screen.Editor.Draw();
            long before = GC.GetAllocatedBytesForCurrentThread();
            screen.Editor.Draw();
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.True(allocated < 64 * 1024, $"a draw at column {left} allocated {allocated} bytes");

            // A selection inside the window is drawn with the selected attribute, after the far tab expansion too.
            uint selectionStart = screen.Editor.CharPtr(0, left + 2);
            screen.Editor.SetSelect(selectionStart, selectionStart + 3, false);
            screen.Editor.delta.x = left;
            screen.Draw();
            int sx = screen.Editor.CharPos(0, selectionStart) - left;
            Assert.Equal((ushort)(byte)(screen.Editor.GetColor(0x0201) >> 8), screen.AttributeAt(sx, 0));
            Assert.Equal(Reference(line, left, 40), screen.Row(0).PadRight(40));
        }
    }

    [Fact]
    public void ScrollingFarRightPastTheLastCharacterOfEveryLineDrawsBlankRows()
    {
        (EditorScreen screen, _) = Build("if 1\n\tif\r2\n");
        using (screen)
        {
            screen.Editor.limit.x = 200000;
            screen.Editor.delta.x = 100000;
            screen.Draw();
            for (int y = 0; y < 8; y++) Assert.Equal(string.Empty, screen.Row(y));
            Assert.Equal(Normal(screen), screen.AttributeAt(0, 0));
            Assert.Equal(Normal(screen), screen.AttributeAt(39, 2));
        }
    }

    [Fact]
    public void AnEditWhileScrolledFarRightIsDrawnAndReHighlightedAtThatColumn()
    {
        string line = new string('.', 5000) + " word 7";
        (EditorScreen screen, ToyClassifier toy) = Build(line + "\nif 2", width: 30, height: 3);
        using (screen)
        {
            screen.Editor.limit.x = 6000;
            screen.Editor.delta.x = 4990;
            screen.Draw();
            Assert.Equal(".......... word 7", screen.Row(0));

            // "word" -> "if" programmatically, then undo through the command.
            screen.Editor.SetSelect(5001, 5005, true);
            screen.Editor.InsertText("if", selectText: false);
            screen.Editor.limit.x = 6000;
            screen.Editor.delta.x = 4990;
            screen.Draw();
            Assert.Equal(".......... if 7", screen.Row(0));
            Assert.Equal(Role(screen, SyntaxClass.Keyword), screen.AttributeAt(11, 0));

            Command(screen, Views.cmUndo);
            uint end = screen.Editor.LineEnd(0);
            screen.Editor.SetSelect(end, end, true);   // undo may restore the replaced selection; drop it
            screen.Editor.limit.x = 6000;
            screen.Editor.delta.x = 4990;
            screen.Draw();
            Assert.Equal(line + "\nif 2", screen.Text);
            Assert.Equal(".......... word 7", screen.Row(0));
            Assert.Equal(Normal(screen), screen.AttributeAt(11, 0));
            Assert.Equal(Role(screen, SyntaxClass.Number), screen.AttributeAt(16, 0));
            Assert.Equal(line, toy.Calls[^1]);
        }
    }

    /// <summary>An independent reference of TEditor's line formatting: columns [left, left + width) of one line.</summary>
    private static string Reference(string line, int left, int width)
    {
        long tab = Math.Max(1L, TEditor.tabSize);
        var cells = new char[width];
        Array.Fill(cells, ' ');
        long x = 0;
        foreach (char c in line)
        {
            long next = c == '\t' ? (x / tab + 1) * tab : x + 1;
            for (long column = x; column < next; column++)
                if (column >= left && column < left + width)
                    cells[column - left] = c is '\t' or '\r' ? ' ' : c;
            x = next;
            if (x >= left + width) break;
        }

        return new string(cells);
    }
}
