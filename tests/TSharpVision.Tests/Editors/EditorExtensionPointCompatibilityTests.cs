using TSharpVision;
using TSharpVision.Constants;
using Xunit;

namespace TSharpVision.Tests.Editors;

// Borland EDITORS.H declares all four hooks virtual; these tests use their normal call sites.
public sealed class EditorExtensionPointCompatibilityTests
{
    [Fact]
    public void InitBufferOverrideRunsFromEditorInitialization()
    {
        var editor = new HookEditor();

        Assert.Equal(1, editor.InitBufferCalls);
        Assert.NotNull(editor.buffer);
    }

    [Fact]
    public void DoneBufferOverrideRunsFromShutdown()
    {
        var editor = new HookEditor();

        editor.ShutDown();

        Assert.Equal(1, editor.DoneBufferCalls);
        Assert.Null(editor.buffer);
    }

    [Fact]
    public void ConvertEventOverrideInterceptsNormalHandleEventPipeline()
    {
        var editor = new HookEditor { ConsumeConvertedEvent = true };
        TEvent key = default;
        key.What = Events.evKeyDown;
        key.keyDown.keyCode = Keys.kbRight;

        editor.HandleEvent(ref key);

        Assert.Equal(1, editor.ConvertEventCalls);
        Assert.Equal(Events.evNothing, key.What);
    }

    [Fact]
    public void InsertFromOverrideRunsThroughClipboardCopyWorkflow()
    {
        TEditor? savedClipboard = TEditor.clipboard;
        try
        {
            var clipboard = new HookEditor();
            var source = new TEditor(new TRect(0, 0, 20, 5), null, null, null, 64);
            source.InsertText("copy me");
            source.SetSelect(0, source.bufLen, false);
            TEditor.clipboard = clipboard;

            bool copied = source.ClipCopy();

            Assert.True(copied);
            Assert.Equal(1, clipboard.InsertFromCalls);
            Assert.Equal((uint)7, clipboard.bufLen);
        }
        finally
        {
            TEditor.clipboard = savedClipboard!;
        }
    }

    private sealed class HookEditor : TEditor
    {
        internal HookEditor() : base(new TRect(0, 0, 20, 5), null, null, null, 64) { }

        internal int InitBufferCalls { get; private set; }
        internal int DoneBufferCalls { get; private set; }
        internal int ConvertEventCalls { get; private set; }
        internal int InsertFromCalls { get; private set; }
        internal bool ConsumeConvertedEvent { get; init; }

        public override void InitBuffer()
        {
            InitBufferCalls++;
            base.InitBuffer();
        }

        public override void DoneBuffer()
        {
            DoneBufferCalls++;
            base.DoneBuffer();
        }

        public override void ConvertEvent(ref TEvent ev)
        {
            ConvertEventCalls++;
            if (ConsumeConvertedEvent)
                ClearEvent(ref ev);
            else
                base.ConvertEvent(ref ev);
        }

        public override bool InsertFrom(TEditor editor)
        {
            InsertFromCalls++;
            return base.InsertFrom(editor);
        }
    }
}
