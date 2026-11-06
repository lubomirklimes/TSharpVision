using TSharpVision.CodeEditor.Syntax;
using TSharpVision.CodeEditor.Syntax.TextMate;
using Xunit;

namespace TSharpVision.CodeEditor.Consumer.Tests;

/// <summary>
/// The documented pattern for an editor that is not a <see cref="TFileEditor"/>: an application's own
/// <see cref="TEditor"/> forwards Draw and HandleEvent to an <see cref="EditorSyntaxHighlighter"/> and highlights an
/// application-registered language - through the public API alone, also when scrolled far to the right.
/// </summary>
public sealed class HighlighterHostTests
{
    private const int Width = 30;

    private sealed class HostedEditor : TEditor
    {
        public HostedEditor(TRect bounds, uint bufferSize, ISyntaxService service)
            : base(bounds, null, null, null, bufferSize)
        {
            // Inline scheduling: a draw classifies synchronously and the result arrives as a post.
            Highlighter = new EditorSyntaxHighlighter(this, service, work => work());
        }

        public EditorSyntaxHighlighter Highlighter { get; }

        public Dictionary<int, TScreenChar[]> Rows { get; } = new();

        public override void Draw() => Highlighter.Draw();

        public override void HandleEvent(ref TEvent ev)
        {
            if (Highlighter.HandleEvent(ref ev)) return;
            base.HandleEvent(ref ev);
        }

        public override void WriteLine(int x, int y, int w, int h, Span<TScreenChar> b) => Rows[y] = b.ToArray();
    }

    [Fact]
    public void AnApplicationEditorHighlightsARegisteredLanguageScrolledFarRight()
    {
        TEventQueue.ClearPosted();
        TEventQueue.ClaimUiThread();
        try
        {
            var service = new TextMateSyntaxService();
            service.RegisterLanguage(Mos6502.Definition(), () => new Mos6502.Classifier());

            string line = new string(' ', 3000) + "loop: LDA #$10 ; load";
            string text = line + "\nRTS";
            var group = new TGroup(new TRect(0, 0, Width, 4));
            var editor = new HostedEditor(new TRect(0, 0, Width, 4), (uint)text.Length + 64, service);
            group.Insert(editor);
            editor.InsertText(text, false);
            editor.SetCurPtr(0, 0);

            editor.Highlighter.SetLanguage(service.DetectLanguage("game.a65", null));
            Assert.Equal(Mos6502.Language, editor.Highlighter.Language);

            int left = 2995;
            editor.limit.x = line.Length + 100;
            editor.delta.x = left;
            editor.Draw();
            while (TEventQueue.DeliverPostedEvent())
            {
            }

            editor.Draw();

            TScreenChar[] row = editor.Rows[0];
            Assert.Equal(Width, row.Length);
            string expected = line.Substring(left).PadRight(Width)[..Width];
            Assert.Equal(expected, new string(row.Select(cell => cell.Character == '\0' ? ' ' : cell.Character).ToArray()));

            // Every visible cell carries the colour of the span the application's classifier gave its character.
            ushort normal = (byte)editor.GetColor(0x0201);
            IReadOnlyList<SyntaxSpan> spans = new Mos6502.Classifier().ClassifyLine(line, LineStart()).Spans;
            bool coloured = false;
            for (int x = 0; x < Width && left + x < line.Length; x++)
            {
                int index = left + x;
                SyntaxSpan? span = spans.Cast<SyntaxSpan?>().FirstOrDefault(s => s!.Value.Start <= index && index < s.Value.End);
                ushort want = span is { } s ? editor.Highlighter.ColorScheme.Apply(s.Class, normal) : normal;
                Assert.Equal(want, (ushort)(byte)row[x].Attr);
                coloured |= want != normal;
            }

            Assert.True(coloured, "the visible window should contain highlighted text");
            editor.Highlighter.Shutdown();
        }
        finally
        {
            TEventQueue.ClearPosted();
        }
    }

    private static SyntaxLineState LineStart() => new Mos6502.Classifier().InitialState;
}
