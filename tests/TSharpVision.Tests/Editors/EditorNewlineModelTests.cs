using System;
using System.Linq;
using System.Text;
using TSharpVision;
using TSharpVision.Constants;
using TSharpVision.Tests.Infrastructure;
using Xunit;

namespace TSharpVision.Tests.Editors;

/// <summary>
/// KEYBOARD-CLOSURE N8: TEditor's buffer is LF-only — LF is the one line boundary, and a CR (lone or before LF) is an
/// ordinary one-column character. TFileEditor and clipboard paste normalize CR and CRLF to LF before text reaches the
/// buffer, NewLine inserts LF and CountLines (limit.y, curPos.y, drawLine) counts LF; LineStart / LineEnd / NextLine and
/// drawing must therefore see the same boundaries, whatever raw text the public API is given.
/// </summary>
[Collection("NonParallel")]
public sealed class EditorNewlineModelTests : IDisposable
{
    private const int Width = 20;
    private const int Height = 6;

    private readonly DriverScope _driver;
    private readonly TestGroup _host;

    public EditorNewlineModelTests()
    {
        _driver = new DriverScope();
        TEventQueue.Resume();
        _host = new TestGroup(new TRect(0, 0, 80, 25));
        _host.buffer = new ScreenBuffer(80 * 25 * ScreenBuffer.GetSize());
        _host.state |= (ushort)(Views.sfVisible | Views.sfExposed);
    }

    public void Dispose() => _driver.Dispose();

    public static TheoryData<string> Documents => new()
    {
        "a\nb",
        "a\rb",
        "a\r\nb",
        "a\rb\nc\r\nd",
        "\r\n\r\n",
        "x\r",
        "\ry\n\rz",
    };

    private TEditor Load(string text)
    {
        var editor = new TEditor(new TRect(0, 0, Width, Height), null, null, null, 4096);
        _host.Insert(editor);
        editor.InsertText(text);
        editor.SetCurPtr(0, 0);
        return editor;
    }

    private static string Text(TEditor editor)
    {
        var builder = new StringBuilder();
        for (uint p = 0; p < editor.bufLen; p++) builder.Append(editor.BufChar(p));
        return builder.ToString();
    }

    private static int ExpectedLineStart(string text, int p) => p == 0 ? 0 : text.LastIndexOf('\n', p - 1) + 1;

    private static int ExpectedLineEnd(string text, int p)
    {
        int end = text.IndexOf('\n', Math.Min(p, text.Length));
        return end < 0 ? text.Length : end;
    }

    private static int LineOf(string text, int p) => text.Take(p).Count(c => c == '\n');

    private string Row(int y)
    {
        var chars = new char[Width];
        for (int x = 0; x < Width; x++) chars[x] = _host.buffer!.Data[(y * 80) + x].Character;
        return new string(chars).TrimEnd();
    }

    [Theory]
    [MemberData(nameof(Documents))]
    public void EveryLineApiAgreesOnLfBoundaries(string text)
    {
        TEditor editor = Load(text);
        Assert.Equal(text, Text(editor));
        int lineCount = text.Count(c => c == '\n') + 1;

        Assert.True(lineCount == editor.limit.y, $"limit.y {editor.limit.y}, expected {lineCount}");

        // Walking NextLine from the top visits exactly limit.y lines.
        int walked = 1;
        for (uint p = 0; editor.LineEnd(p) < editor.bufLen; p = editor.NextLine(p))
            walked++;
        Assert.True(lineCount == walked, $"NextLine walk visits {walked} lines, expected {lineCount}");

        for (int p = 0; p <= text.Length; p++)
        {
            Assert.True(ExpectedLineStart(text, p) == editor.LineStart((uint)p), $"LineStart({p}) = {editor.LineStart((uint)p)}");
            Assert.True(ExpectedLineEnd(text, p) == editor.LineEnd((uint)p), $"LineEnd({p}) = {editor.LineEnd((uint)p)}");

            editor.SetCurPtr((uint)p, 0);
            Assert.True(LineOf(text, p) == editor.curPos.y, $"curPos.y at {p} = {editor.curPos.y}, expected {LineOf(text, p)}");
            Assert.True(p - ExpectedLineStart(text, p) == editor.curPos.x, $"curPos.x at {p} = {editor.curPos.x}");
            Assert.True(ExpectedLineStart(text, p) == (int)editor.drawPtr && LineOf(text, p) == editor.drawLine,
                $"drawPtr {editor.drawPtr} / drawLine {editor.drawLine} at {p}");
        }

        for (int line = 0; line < lineCount; line++)
        {
            int start = line == 0 ? 0 : IndexOfNth(text, '\n', line) + 1;
            Assert.True(start == (int)editor.LineMove(0, line), $"LineMove(0, {line}) = {editor.LineMove(0, line)}, expected {start}");
        }
    }

    [Theory]
    [MemberData(nameof(Documents))]
    public void DrawingShowsOneRowPerLfLineWithCrAsABlankCell(string text)
    {
        TEditor editor = Load(text);
        editor.Draw();
        string[] lines = text.Split('\n');
        for (int y = 0; y < Height; y++)
        {
            string expected = y < lines.Length ? lines[y].Replace('\r', ' ').TrimEnd() : string.Empty;
            Assert.True(expected == Row(y), $"row {y} is '{Row(y)}', expected '{expected}'");
        }
    }

    [Fact]
    public void InsertingACrDoesNotSplitTheLineAndUndoRestoresIt()
    {
        TEditor editor = Load("abc\ndef");
        editor.SetCurPtr(1, 0);
        editor.InsertText("\r");
        Assert.Equal("a\rbc\ndef", Text(editor));
        Assert.Equal(2, editor.limit.y);
        Assert.Equal(new TPoint(2, 0), editor.curPos);
        Assert.Equal(4u, editor.LineEnd(0));
        Assert.Equal(5u, editor.NextLine(0));

        editor.Undo();
        Assert.Equal("abc\ndef", Text(editor));
        Assert.Equal(2, editor.limit.y);
        Assert.Equal(3u, editor.LineEnd(0));
    }

    [Fact]
    public void ANewLineAfterACrStartsTheNextLine()
    {
        TEditor editor = Load("a\rbc\ndef");
        editor.SetCurPtr(2, 0);
        editor.NewLine();
        Assert.Equal("a\r\nbc\ndef", Text(editor));
        Assert.Equal(3, editor.limit.y);
        Assert.Equal(new TPoint(0, 1), editor.curPos);
        Assert.Equal(editor.LineMove(0, editor.drawLine), editor.drawPtr);   // the anchor is the start of drawLine
        Assert.Equal(2u, editor.LineEnd(0));
        Assert.Equal(3u, editor.LineMove(0, 1));
    }

    [Fact]
    public void ReplacingASelectionThatSpansACrKeepsTheLineCount()
    {
        TEditor editor = Load("ab\rcd\nef");
        editor.SetSelect(1, 4, false);
        editor.InsertText("X");
        Assert.Equal("aXd\nef", Text(editor));
        Assert.Equal(2, editor.limit.y);
        Assert.Equal(new TPoint(2, 0), editor.curPos);
        editor.Draw();
        Assert.Equal("aXd", Row(0));
        Assert.Equal("ef", Row(1));
    }

    [Fact]
    public void InsertingAtTheEndAfterACrStaysOnTheLine()
    {
        TEditor editor = Load("x\r");
        editor.SetCurPtr(2, 0);
        editor.InsertText("y");
        Assert.Equal("x\ry", Text(editor));
        Assert.Equal(1, editor.limit.y);
        Assert.Equal(new TPoint(3, 0), editor.curPos);
        Assert.Equal(0u, editor.drawPtr);
    }

    private static int IndexOfNth(string text, char c, int n)
    {
        int index = -1;
        for (int i = 0; i < n; i++) index = text.IndexOf(c, index + 1);
        return index;
    }
}
