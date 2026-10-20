using TSharpVision.CodeEditor;
using TSharpVision.Constants;
using TSharpVision.Samples.TVDemo;
using TSharpVision.Tests.Infrastructure;
using Xunit;

namespace TSharpVision.Tests.Samples;

[Collection("NonParallel")]
public sealed class TVDemoEditorExitTests : IDisposable
{
    private const string Text = "erwršěčřŘŽ";
    private readonly DriverScope _driver = new();
    private readonly TEditor.TEditorDialog _previous = TEditor.editorDialog;
    private readonly PromptApp _app;

    public TVDemoEditorExitTests()
    {
        // A standalone TVDemo starts with the library's cancel-only callback.
        TEditor.editorDialog = (_, _) => Views.cmCancel;
        TEventQueue.Resume();
        TEventQueue.ClearPosted();
        Drain();
        _app = new PromptApp();
    }

    [Fact]
    public void CleanApplicationAndUnmodifiedDemoNeedNoPrompt()
    {
        Assert.True(_app.Valid(Views.cmQuit));
        OpenEditor();
        Assert.True(_app.Valid(Views.cmQuit));
        Assert.Equal(0, _app.Prompts);
    }

    [Theory]
    [InlineData(Views.cmQuit)]
    [InlineData(Views.cmClose)]
    public void CancelPreservesTextAndDiscardAllowsValidation(ushort command)
    {
        var window = OpenEditor();
        var editor = window.CodeEditor;
        editor.InsertText(Text);
        string content = ReadText(editor);
        Assert.True(editor.modified);
        _app.Answers.Enqueue(Views.cmCancel);
        Assert.False(command == Views.cmQuit ? _app.Valid(command) : window.Valid(command));
        Assert.Equal(1, _app.Prompts);
        Assert.True(editor.modified);
        Assert.Equal(content, ReadText(editor));
        Assert.Same(_app.DeskTop, window.owner);
        editor.InsertText("!");
        string updatedContent = ReadText(editor);
        Assert.Contains(Text + "!", updatedContent);
        _app.Answers.Enqueue(Views.cmNo);
        Assert.True(command == Views.cmQuit ? _app.Valid(command) : window.Valid(command));
        Assert.Equal(2, _app.Prompts);
        Assert.Equal(command == Views.cmQuit, editor.modified);
        Assert.Equal(updatedContent, ReadText(editor));
    }

    [Fact]
    public void SaveAsCancelRetainsDocumentAndAcceptedSaveWritesUnicode()
    {
        var editor = OpenEditor().CodeEditor;
        editor.InsertText(Text);
        string content = ReadText(editor);
        _app.Answers.Enqueue(Views.cmYes);
        _app.Answers.Enqueue(Views.cmCancel);
        Assert.False(_app.Valid(Views.cmQuit));
        Assert.Equal(2, _app.Prompts);
        Assert.True(editor.modified);
        Assert.Empty(editor.fileName);
        Assert.Equal(content, ReadText(editor));
        string path = Path.Combine(Path.GetTempPath(), "rel-2a-" + Guid.NewGuid().ToString("N") + ".cs");
        try
        {
            _app.SavePath = path;
            _app.Answers.Enqueue(Views.cmYes);
            _app.Answers.Enqueue(Views.cmOK);
            Assert.True(_app.Valid(Views.cmQuit));
            Assert.Equal(4, _app.Prompts);
            Assert.False(editor.modified);
            Assert.Equal(content.Replace("\n", Environment.NewLine), File.ReadAllText(path));
            editor.InsertText("!");
            _app.Answers.Enqueue(Views.cmYes);
            Assert.True(_app.Valid(Views.cmQuit));
            Assert.False(editor.modified);
            Assert.Contains(Text + "!", File.ReadAllText(path));
        }
        finally
        {
            File.Delete(path);
            File.Delete(Path.ChangeExtension(path, ".bak"));
        }
    }

    [Fact]
    public void QuitLoopResumesAfterCancelThenExitsAfterDiscard()
    {
        var editor = OpenEditor().CodeEditor;
        editor.InsertText(Text);
        string content = ReadText(editor);
        _app.BeforeQuit = () =>
        {
            Assert.True(editor.modified);
            Assert.Equal(content, ReadText(editor));
        };
        _app.Answers.Enqueue(Views.cmCancel);
        _app.Answers.Enqueue(Views.cmNo);
        Assert.Equal(Views.cmQuit, _app.Execute());
        Assert.Equal(2, _app.QuitRequests);
        Assert.Equal(2, _app.Prompts);
    }

    [Fact]
    public void CancelingOneOfSeveralDocumentsRetainsAllWindowsAndText()
    {
        var first = OpenEditor();
        first.CodeEditor.InsertText(Text);
        var second = OpenEditor();
        second.CodeEditor.InsertText("second " + Text);
        string firstText = ReadText(first.CodeEditor), secondText = ReadText(second.CodeEditor);
        _app.Answers.Enqueue(Views.cmCancel);
        Assert.False(_app.Valid(Views.cmQuit));
        Assert.Equal(1, _app.Prompts);
        Assert.Same(_app.DeskTop, first.owner);
        Assert.Same(_app.DeskTop, second.owner);
        Assert.True(first.CodeEditor.modified);
        Assert.True(second.CodeEditor.modified);
        Assert.Equal(firstText, ReadText(first.CodeEditor));
        Assert.Equal(secondText, ReadText(second.CodeEditor));
        // A later document can veto after an earlier explicit discard choice.
        _app.Answers.Enqueue(Views.cmNo);
        _app.Answers.Enqueue(Views.cmCancel);
        Assert.False(_app.Valid(Views.cmQuit));
        Assert.Equal(3, _app.Prompts);
        Assert.Same(_app.DeskTop, first.owner);
        Assert.Same(_app.DeskTop, second.owner);
        Assert.Equal(firstText, ReadText(first.CodeEditor));
        Assert.Equal(secondText, ReadText(second.CodeEditor));
        Assert.True(first.CodeEditor.modified);
        Assert.True(second.CodeEditor.modified);
    }

    [Fact]
    public void DirectWindowCloseCancelsThenClosesAfterDiscard()
    {
        var window = OpenEditor();
        window.CodeEditor.InsertText(Text);
        _app.Answers.Enqueue(Views.cmCancel);
        window.Close();
        Assert.Equal(1, _app.Prompts);
        Assert.Same(_app.DeskTop, window.owner);
        Assert.True(window.CodeEditor.modified);
        _app.Answers.Enqueue(Views.cmNo);
        window.Close();
        Assert.Equal(2, _app.Prompts);
        Assert.Null(window.owner);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void LaterCloseAndRepeatedQuitPromptAfterNoThenCancel(bool reverseOrder)
    {
        var first = OpenEditor();
        first.CodeEditor.InsertText("FIRST " + Text);
        var second = OpenEditor();
        second.CodeEditor.InsertText("SECOND " + Text);
        if (reverseOrder) first.MakeFirst();
        var a = reverseOrder ? first : second;
        var b = reverseOrder ? second : first;
        a.CodeEditor.fileName = "rel-2b-A.cs";
        b.CodeEditor.fileName = "rel-2b-B.cs";
        string aText = ReadText(a.CodeEditor), bText = ReadText(b.CodeEditor);
        // Assert the actual traversal order rather than relying only on prompt counts.
        var originalDialog = TEditor.editorDialog;
        int choice = 0;
        TEditor.editorDialog = (kind, info) =>
        {
            if (choice < 2)
                Assert.Equal(choice == 0 ? a.CodeEditor.fileName : b.CodeEditor.fileName, info);
            if (choice++ == 0)
            {
                Assert.True(a.CodeEditor.modified);
                Assert.True(b.CodeEditor.modified);
            }
            else
            {
                // cmNo must not clear A while global validation is in progress.
                Assert.True(a.CodeEditor.modified);
            }
            return originalDialog(kind, info);
        };
        _app.Answers.Enqueue(Views.cmNo);
        _app.Answers.Enqueue(Views.cmCancel);
        Assert.False(_app.Valid(Views.cmQuit));
        AssertRetained(a, aText);
        AssertRetained(b, bText);
        _app.Answers.Enqueue(Views.cmCancel);
        a.Close();
        Assert.Equal(3, _app.Prompts);
        AssertRetained(a, aText);
        AssertRetained(b, bText);
        _app.Answers.Enqueue(Views.cmNo);
        _app.Answers.Enqueue(Views.cmCancel);
        Assert.False(_app.Valid(Views.cmQuit));
        Assert.Equal(5, _app.Prompts);
        AssertRetained(a, aText);
        AssertRetained(b, bText);
        _app.Answers.Enqueue(Views.cmNo);
        _app.Answers.Enqueue(Views.cmNo);
        Assert.Equal(Views.cmQuit, _app.Execute());
        Assert.Equal(7, _app.Prompts);
        Assert.Equal(1, _app.QuitRequests);
        Assert.Empty(_app.Answers);
        AssertRetained(a, aText);
        AssertRetained(b, bText);
    }

    [Fact]
    public void ThreeDocumentsNoNoCancelRetainsEveryUnsavedBuffer()
    {
        var windows = Enumerable.Range(0, 3).Select(i =>
        {
            var window = OpenEditor();
            window.CodeEditor.InsertText(i + Text);
            return window;
        }).ToArray();
        var contents = windows.Select(w => ReadText(w.CodeEditor)).ToArray();
        _app.Answers.Enqueue(Views.cmNo);
        _app.Answers.Enqueue(Views.cmNo);
        _app.Answers.Enqueue(Views.cmCancel);
        Assert.False(_app.Valid(Views.cmQuit));
        Assert.Equal(3, _app.Prompts);
        for (int i = 0; i < windows.Length; i++) AssertRetained(windows[i], contents[i]);
        foreach (var window in windows) _app.Answers.Enqueue(Views.cmNo);
        Assert.Equal(Views.cmQuit, _app.Execute());
        Assert.Equal(6, _app.Prompts);
        Assert.Empty(_app.Answers);
    }

    [Fact]
    public void SuccessfulSaveBeforeLaterCancelStaysSaved()
    {
        var b = OpenEditor();
        b.CodeEditor.InsertText("B " + Text);
        var a = OpenEditor();
        a.CodeEditor.InsertText("A " + Text);
        string aText = ReadText(a.CodeEditor), bText = ReadText(b.CodeEditor);
        string path = Path.Combine(Path.GetTempPath(), "rel-2b-" + Guid.NewGuid().ToString("N") + ".cs");
        try
        {
            _app.SavePath = path;
            _app.Answers.Enqueue(Views.cmYes);
            _app.Answers.Enqueue(Views.cmOK);
            _app.Answers.Enqueue(Views.cmCancel);
            Assert.False(_app.Valid(Views.cmQuit));
            Assert.Equal(3, _app.Prompts);
            Assert.Same(_app.DeskTop, a.owner);
            Assert.False(a.CodeEditor.modified);
            Assert.Equal(path, a.CodeEditor.fileName);
            Assert.Equal(aText, ReadText(a.CodeEditor));
            Assert.Equal(aText.Replace("\n", Environment.NewLine), File.ReadAllText(path));
            AssertRetained(b, bText);
            _app.Answers.Enqueue(Views.cmCancel);
            Assert.False(_app.Valid(Views.cmQuit));
            Assert.Equal(4, _app.Prompts); // Only the still-unsaved B prompts.
        }
        finally { File.Delete(path); File.Delete(Path.ChangeExtension(path, ".bak")); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DirectCloseYesPreservesCanceledSaveAsAndClosesAfterSave(bool cancelFirst)
    {
        var window = OpenEditor();
        var editor = window.CodeEditor;
        editor.InsertText(Text);
        string content = ReadText(editor);
        string path = Path.Combine(Path.GetTempPath(), "rel-2b-" + Guid.NewGuid().ToString("N") + ".cs");
        try
        {
            if (cancelFirst)
            {
                _app.Answers.Enqueue(Views.cmYes);
                _app.Answers.Enqueue(Views.cmCancel);
                window.Close();
                AssertRetained(window, content);
                Assert.Empty(editor.fileName);
            }
            _app.SavePath = path;
            _app.Answers.Enqueue(Views.cmYes);
            _app.Answers.Enqueue(Views.cmOK);
            window.Close();
            Assert.Null(window.owner);
            Assert.False(editor.modified);
            Assert.Equal(content.Replace("\n", Environment.NewLine), File.ReadAllText(path));
            Assert.Empty(_app.Answers);
        }
        finally { File.Delete(path); File.Delete(Path.ChangeExtension(path, ".bak")); }
    }

    private void AssertRetained(TCodeWindow window, string content)
    {
        Assert.Same(_app.DeskTop, window.owner);
        Assert.True(window.CodeEditor.modified);
        Assert.Equal(content, ReadText(window.CodeEditor));
    }

    private TCodeWindow OpenEditor()
    {
        var ev = new TEvent { What = Events.evCommand };
        ev.message.command = ShowcaseCmd.CodeEditor;
        _app.HandleEvent(ref ev);
        Assert.Equal(Events.evNothing, ev.What);
        var window = Assert.IsAssignableFrom<TCodeWindow>(_app.DeskTop!.current);
        Assert.False(window.CodeEditor.modified);
        return window;
    }

    private static string ReadText(TEditor editor)
        => new(Enumerable.Range(0, (int)editor.bufLen).Select(i => editor.BufChar((uint)i)).ToArray());

    public void Dispose()
    {
        _app.ShutDown();
        TEditor.editorDialog = _previous;
        GC.Collect();
        GC.WaitForPendingFinalizers();
        TEventQueue.ClearPosted();
        Drain();
        _driver.Dispose();
    }

    private static void Drain()
    {
        TEvent ev = default;
        do { TEventQueue.GetNextEvent(ref ev); } while (ev.What != Events.evNothing);
    }

    private sealed class PromptApp : TVDemoApp
    {
        private readonly HashSet<TDialog> _seen = new();
        public PromptApp() { Pending = default; }
        public Queue<ushort> Answers { get; } = new();
        public int Prompts { get; private set; }
        public int QuitRequests { get; private set; }
        public string? SavePath { get; set; }
        public Action? BeforeQuit { get; set; }

        public override void GetEvent(ref TEvent ev)
        {
            if (DeskTop!.current is TDialog dialog)
            {
                if (!_seen.Add(dialog)) { base.GetEvent(ref ev); return; }
                Prompts++;
                Assert.Same(DeskTop, dialog.owner);
                Assert.NotEqual(0, dialog.state & Views.sfVisible);
                Assert.NotEqual(0, dialog.state & Views.sfModal);
                Assert.NotNull(dialog.current);
                Assert.NotEqual(0, dialog.current!.state & Views.sfSelected);
                Assert.True(Answers.Count > 0, "Unexpected or duplicate modal prompt");
                ushort answer = Answers.Dequeue();
                if (dialog is TFileDialog fileDialog)
                {
                    if (answer == Views.cmOK) fileDialog.SetData(SavePath!);
                }
                else
                {
                    Assert.True(ViewTreeHelpers.ViewContainsText(dialog, "Save untitled file?") ||
                        ViewTreeHelpers.ViewContainsText(dialog, "has been modified. Save?"));
                    var commands = new List<ushort>();
                    dialog.ForEachView(v => { if (v is TButton b) commands.Add(b.Command); });
                    Assert.Contains(Views.cmYes, commands);
                    Assert.Contains(Views.cmNo, commands);
                    Assert.Contains(Views.cmCancel, commands);
                }
                ev = new TEvent { What = Events.evCommand };
                ev.message.command = answer;
                TEventQueue.Enqueue(ev);
                base.GetEvent(ref ev);
                return;
            }
            Assert.True(QuitRequests < 2, "Quit loop did not complete after the scripted choices");
            BeforeQuit?.Invoke();
            QuitRequests++;
            ev = new TEvent { What = Events.evCommand };
            ev.message.command = Views.cmQuit;
        }
    }
}
