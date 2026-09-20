using TSharpVision.Constants;
using TSharpVision.CodeEditor.Syntax;
using TSharpVision.CodeEditor.Syntax.TextMate;
using TSharpVision.Tests.Infrastructure;
using Xunit;

namespace TSharpVision.CodeEditor.Tests;

public sealed class EditorHighlightingTests
{
    private static string Lines(int count, Func<int, string>? line = null)
        => string.Join("\n", Enumerable.Range(0, count).Select(i => line?.Invoke(i) ?? $"line {i}"));

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

    private static ushort Selected(EditorScreen screen) => (byte)(screen.Editor.GetColor(0x0201) >> 8);

    // ── classification work ──────────────────────────────────────────────────

    [Fact]
    public void TheFirstDrawClassifiesOnlyTheVisibleLines()
    {
        (EditorScreen screen, ToyClassifier toy) = Build(Lines(500));
        using (screen)
        {
            Assert.Equal(8, toy.Calls.Count);
            Assert.Equal(Enumerable.Range(0, 8).Select(i => $"line {i}"), toy.Calls);
        }
    }

    [Fact]
    public void ScrollingBackReusesTheCache()
    {
        (EditorScreen screen, ToyClassifier toy) = Build(Lines(500));
        using (screen)
        {
            screen.Editor.ScrollTo(0, 100); EditorScreen.Pump();
            int afterScroll = toy.Calls.Count;
            Assert.Equal(108, afterScroll);

            screen.Editor.ScrollTo(0, 40); EditorScreen.Pump();
            screen.Editor.ScrollTo(0, 0); EditorScreen.Pump();
            screen.Draw();

            Assert.Equal(afterScroll, toy.Calls.Count);
        }
    }

    [Fact]
    public void TypingOnALineReclassifiesOnlyThatLine()
    {
        (EditorScreen screen, ToyClassifier toy) = Build(Lines(50));
        using (screen)
        {
            screen.Editor.SetCurPtr(screen.Editor.CharPtr(screen.Editor.LineMove(0, 3), 0), 0);
            toy.Calls.Clear();

            screen.Type("if ");

            Assert.All(toy.Calls, call => Assert.StartsWith("i", call));
            Assert.Equal("if line 3", toy.Calls[^1]);
            Assert.Equal(Normal(screen), screen.AttributeAt(3, 2));
            Assert.NotEqual(Normal(screen), screen.AttributeAt(0, 3));      // "if" is a keyword now
            Assert.Equal("if line 3", screen.Row(3));
        }
    }

    [Fact]
    public void OpeningACommentReclassifiesTheFollowingLinesUntilTheStateConverges()
    {
        (EditorScreen screen, ToyClassifier toy) = Build(Lines(20, i => i == 5 ? "end */ if" : $"if {i}"));
        using (screen)
        {
            screen.Editor.SetCurPtr(screen.Editor.LineMove(0, 1), 0);
            toy.Calls.Clear();

            screen.Type("/*");

            Assert.Equal(new[] { "/if 1", "/*if 1", "if 2", "if 3", "if 4", "end */ if" }, toy.Calls.Skip(0).ToArray()[^6..]);
            ushort keyword = SyntaxColorScheme.Default.Apply(SyntaxClass.Keyword, Normal(screen));
            ushort comment = SyntaxColorScheme.Default.Apply(SyntaxClass.Comment, Normal(screen));
            Assert.Equal(comment, screen.AttributeAt(0, 3));                // "if 3" is inside the comment now
            Assert.Equal(keyword, screen.AttributeAt(7, 5));                // "if" after the comment closes
            Assert.Equal(keyword, screen.AttributeAt(0, 6));                // line 6 kept its cached classification
        }
    }

    [Fact]
    public void UndoRestoresTheClassification()
    {
        (EditorScreen screen, ToyClassifier toy) = Build(Lines(10, i => $"word {i}"));
        using (screen)
        {
            screen.Editor.SetCurPtr(0, 0);
            screen.Type("if ");
            ushort keyword = SyntaxColorScheme.Default.Apply(SyntaxClass.Keyword, Normal(screen));
            Assert.Equal(keyword, screen.AttributeAt(0, 0));

            var undo = new TEvent { What = Events.evCommand };
            undo.message.command = Views.cmUndo;
            screen.Editor.HandleEvent(ref undo);

            Assert.Equal("word 0", screen.Row(0));
            Assert.Equal(Normal(screen), screen.AttributeAt(0, 0));
            Assert.Equal("word 0", toy.Calls[^1]);
        }
    }

    [Fact]
    public void AProgrammaticEditIsNeverDrawnWithTheOldClassification()
    {
        (EditorScreen screen, ToyClassifier toy) = Build(Lines(10, i => $"if {i}"));
        using (screen)
        {
            ushort keyword = SyntaxColorScheme.Default.Apply(SyntaxClass.Keyword, Normal(screen));
            Assert.Equal(keyword, screen.AttributeAt(0, 2));

            // Replace "if" on line 2 without any event: select it and insert over it.
            uint line2 = screen.Editor.LineMove(0, 2);
            screen.Editor.SetSelect(line2, line2 + 2, true);
            screen.Editor.InsertText("of", selectText: false);
            screen.Draw();

            Assert.Equal("of 2", screen.Row(2));
            Assert.Equal(Normal(screen), screen.AttributeAt(0, 2));
            Assert.Equal(keyword, screen.AttributeAt(0, 1));
            Assert.Contains("of 2", toy.Calls);
        }
    }

    [Fact]
    public void InsertingAndDeletingLinesShiftsTheCacheInsteadOfReclassifyingEverything()
    {
        (EditorScreen screen, ToyClassifier toy) = Build(Lines(40), height: 30);
        using (screen)
        {
            screen.Editor.SetCurPtr(screen.Editor.LineMove(0, 2), 0);
            toy.Calls.Clear();

            screen.Key(Keys.kbEnter);
            Assert.True(toy.Calls.Count <= 3, string.Join(" | ", toy.Calls));
            Assert.Equal("line 2", screen.Row(3));

            toy.Calls.Clear();
            screen.Key(Keys.kbBack);
            Assert.True(toy.Calls.Count <= 2, string.Join(" | ", toy.Calls));
            Assert.Equal("line 2", screen.Row(2));
        }
    }

    // ── editing is unchanged ─────────────────────────────────────────────────

    [Fact]
    public void EditingSelectionAndCursorBehaveExactlyAsInAPlainFileEditor()
    {
        string text = Lines(30, i => $"alpha {i} beta\tgamma");
        (EditorScreen screen, _) = Build(text);
        using (screen)
        {
            var plainHost = new TestGroup(new TRect(0, 0, 40, 8)) { buffer = new ScreenBuffer(320) };
            plainHost.state |= (ushort)(Views.sfVisible | Views.sfExposed);
            var plain = new TFileEditor(new TRect(0, 0, 40, 8), null!, null!, null!, null!);
            plainHost.Insert(plain);
            plain.InsertText(text, false);
            plain.SetCurPtr(0, 0);

            ushort[] keys =
            {
                Keys.kbDown, Keys.kbDown, Keys.kbRight, Keys.kbRight, 'x', Keys.kbEnter, 'y', Keys.kbBack,
                Keys.kbPgDn, Keys.kbEnd, 'z', Keys.kbCtrlLeft, Keys.kbLeft, Keys.kbDel, Keys.kbCtrlHome, '\t',
            };

            foreach (ushort key in keys)
            {
                char c = key < 128 ? (char)key : '\0';
                screen.Key(c != '\0' ? c : key, c);
                var ev = new TEvent { What = Events.evKeyDown };
                ev.keyDown.keyCode = c != '\0' ? c : key;
                ev.keyDown.charScan.charCode = (byte)c;
                if (c >= ' ' || c == '\t') ev.keyDown.text = c.ToString();
                plain.HandleEvent(ref ev);

                Assert.Equal(plain.curPtr, screen.Editor.curPtr);
                Assert.Equal(plain.selStart, screen.Editor.selStart);
                Assert.Equal(plain.selEnd, screen.Editor.selEnd);
                Assert.Equal(plain.curPos, screen.Editor.curPos);
                Assert.Equal(plain.delta, screen.Editor.delta);
            }

            var plainText = new System.Text.StringBuilder();
            for (uint p = 0; p < plain.bufLen; p++) plainText.Append(plain.BufChar(p));
            Assert.Equal(plainText.ToString(), screen.Text);
            Assert.Equal(plain.modified, screen.Editor.modified);
        }
    }

    [Fact]
    public void HighlightingNeverWritesIntoTheDocument()
    {
        var screen = new EditorScreen();
        using (screen)
        {
            const string source = "public class A { /* c */ int x = 1; }\nstring s = \"q\";";
            screen.Load(source);
            screen.Syntax.SetLanguage(TextMateSyntaxService.Default.DetectLanguage("A.cs", null));
            screen.Draw();

            Assert.Equal(source, screen.Text);
            Assert.False(screen.Editor.modified);
            Assert.NotEqual(Normal(screen), screen.AttributeAt(0, 0));      // "public" is a keyword
        }
    }

    // ── rendering ────────────────────────────────────────────────────────────

    [Fact]
    public void SelectionWinsOverSyntaxColour()
    {
        (EditorScreen screen, _) = Build(Lines(5, i => $"if {i}"));
        using (screen)
        {
            screen.Editor.SetSelect(0, 2, false);
            screen.Draw();

            Assert.Equal(Selected(screen), screen.AttributeAt(0, 0));
            Assert.Equal(Selected(screen), screen.AttributeAt(1, 0));
            Assert.Equal(Normal(screen), screen.AttributeAt(2, 0));
        }
    }

    [Fact]
    public void RolesRenderDifferentlyAndMonochromeFallsBackToPlainText()
    {
        (EditorScreen screen, _) = Build("if 42 plain");
        using (screen)
        {
            ushort normal = Normal(screen);
            Assert.NotEqual(normal, screen.AttributeAt(0, 0));               // keyword
            Assert.NotEqual(normal, screen.AttributeAt(3, 0));               // number
            Assert.NotEqual(screen.AttributeAt(0, 0), screen.AttributeAt(3, 0));
            Assert.Equal(normal, screen.AttributeAt(6, 0));                  // plain text stays the palette's

            screen.Syntax.ColorScheme = SyntaxColorScheme.Monochrome;
            screen.Draw();

            Assert.Equal(normal, screen.AttributeAt(0, 0));
            Assert.Equal(normal, screen.AttributeAt(3, 0));
            Assert.Equal("if 42 plain", screen.Row(0));
        }
    }

    [Fact]
    public void ARoleWhoseColourWouldMatchTheBackgroundIsDrawnPlain()
    {
        var scheme = new SyntaxColorScheme(new Dictionary<SyntaxClass, byte> { [SyntaxClass.Keyword] = 0x1 });

        Assert.Equal((ushort)0x17, scheme.Apply(SyntaxClass.Keyword, 0x17));   // blue on blue would vanish
        Assert.Equal((ushort)0x01, scheme.Apply(SyntaxClass.Keyword, 0x07));
        Assert.Equal((ushort)0x07, scheme.Apply(SyntaxClass.String, 0x07));    // not in the scheme
    }

    [Fact]
    public void AFarJumpIsDrawnPlainAtFirstAndCompletedByPostedContinuations()
    {
        (EditorScreen screen, ToyClassifier toy) = Build(Lines(400, i => $"if {i}"));
        using (screen)
        {
            screen.Syntax.MaxLinesPerRequest = 25;
            screen.Editor.ScrollTo(0, 300);

            Assert.Equal(Normal(screen), screen.AttributeAt(0, 0));          // not reachable in one draw
            Assert.True(TEventQueue.PostedCount > 0);
            Assert.True(screen.Syntax.Cache!.ValidLineCount < 300);

            EditorScreen.Pump();

            ushort keyword = SyntaxColorScheme.Default.Apply(SyntaxClass.Keyword, Normal(screen));
            Assert.Equal(keyword, screen.AttributeAt(0, 0));
            Assert.Equal("if 300", screen.Row(0));
            Assert.Equal(308, toy.Calls.Count);
            Assert.Equal(0, TEventQueue.PostedCount);
        }
    }

    [Fact]
    public void ADocumentOverTheSizeLimitIsDrawnPlainUntilItShrinks()
    {
        (EditorScreen screen, ToyClassifier toy) = Build(Lines(3, i => $"if {i}"));
        using (screen)
        {
            screen.Syntax.MaxHighlightedCharacters = 12;
            screen.Editor.SetCurPtr(screen.Editor.bufLen, 0);
            toy.Calls.Clear();

            screen.Type(" padding");
            Assert.True(screen.Syntax.IsSuspended);
            Assert.Empty(toy.Calls);
            Assert.Equal(Normal(screen), screen.AttributeAt(0, 0));

            screen.Syntax.MaxHighlightedCharacters = 1000;
            screen.Draw();
            Assert.False(screen.Syntax.IsSuspended);
            Assert.NotEqual(Normal(screen), screen.AttributeAt(0, 0));
        }
    }

    [Fact]
    public void HorizontalScrollingKeepsSpansAlignedWithTheirCharacters()
    {
        (EditorScreen screen, _) = Build("xxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx if 7", width: 20, height: 3);
        using (screen)
        {
            screen.Editor.ScrollTo(50, 0); EditorScreen.Pump();

            Assert.Equal("xxxxxx if 7", screen.Row(0));
            Assert.NotEqual(Normal(screen), screen.AttributeAt(7, 0));      // "if"
            Assert.Equal(Normal(screen), screen.AttributeAt(0, 0));
        }
    }

    // ── TCodeEditor public behaviour ─────────────────────────────────────────

    [Fact]
    public void TheCodeEditorDetectsItsLanguageFromTheFileName()
    {
        using var driver = new DriverScope();
        using var temp = new TempDirectory();

        var csharp = new TCodeEditor(new TRect(0, 0, 40, 10), null, null, null, Path.Combine(temp.Path, "Program.cs"));
        var text = new TCodeEditor(new TRect(0, 0, 40, 10), null, null, null, Path.Combine(temp.Path, "notes.txt"));

        Assert.Equal("csharp", csharp.SyntaxHighlighter.Language.Id);
        Assert.Equal("source.cs", csharp.SyntaxScopeName);
        Assert.True(text.SyntaxHighlighter.Language.IsPlainText);
        Assert.Null(text.SyntaxScopeName);
    }

    [Fact]
    public void SetSyntaxScopeSwitchesAndDisablesHighlighting()
    {
        using var driver = new DriverScope();
        var editor = new TCodeEditor(new TRect(0, 0, 40, 10), null, null, null, null);

        editor.SetSyntaxScope("source.json");
        Assert.Equal("json", editor.SyntaxHighlighter.Language.Id);
        Assert.Equal("source.json", editor.SyntaxScopeName);

        editor.SetSyntaxScope(null);
        Assert.Null(editor.SyntaxScopeName);
        Assert.True(editor.SyntaxHighlighter.Language.IsPlainText);
    }

    [Fact]
    public void LoadGrammarActivatesAGrammarFromAFile()
    {
        using var driver = new DriverScope();
        using var temp = new TempDirectory();
        string grammar = temp.CreateFile("toy.tmLanguage.json", """
            {
              "scopeName": "source.toy",
              "name": "Toy",
              "patterns": [ { "match": "\\bboom\\b", "name": "keyword.control.toy" } ]
            }
            """);

        var editor = new TCodeEditor(new TRect(0, 0, 40, 10), null, null, null, null);

        Assert.True(editor.LoadGrammar(grammar));
        Assert.Equal("source.toy", editor.SyntaxScopeName);
        SyntaxLineResult result = editor.SyntaxHighlighter.Classifier!.ClassifyLine("a boom b", editor.SyntaxHighlighter.Classifier.InitialState);
        Assert.Equal(new[] { new SyntaxSpan(2, 4, SyntaxClass.Keyword) }, result.Spans);

        Assert.False(editor.LoadGrammar(Path.Combine(temp.Path, "missing.json")));
    }
}
