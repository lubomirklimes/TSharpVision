using System.Text;
using TSharpVision;
using TSharpVision.Constants;
using TSharpVision.Tests.Infrastructure;
using Xunit;

namespace TSharpVision.Tests.Help;

[Collection("NonParallel")]
public sealed class HelpProgramContractTests : IDisposable
{
    private readonly DriverScope _driver = new();
    private readonly StreamableRegistryScope _streams = new();
    private readonly TempDirectory _tmp = new();

    public void Dispose()
    {
        _tmp.Dispose();
        _streams.Dispose();
        _driver.Dispose();
    }

    [Fact]
    public void F1_FromDialog_OpensHelpWindow_ForFocusedView()
    {
        using var help = BuildHelpFile();
        var app = new HelpContractProgram(help.File) { InsertHelpWindow = false };
        try
        {
            var (dialog, child) = InsertFocusedDialogChild(app, 42);
            TEvent ev = HelpCommand();

            app.HandleEvent(ref ev);

            Assert.Equal(Events.evNothing, ev.What);
            Assert.NotNull(app.LastHelpWindow);
            Assert.Same(dialog, app.DeskTop.current);
            Assert.Same(child, dialog.current);
            Assert.Equal("Focused topic.\n", TopicText(FindViewer(app.LastHelpWindow).topic));
        }
        finally
        {
            app.ShutDown();
        }
    }

    [Fact]
    public void F1_FromDialog_RestoresFocus_OnClose()
    {
        using var help = BuildHelpFile();
        var app = new HelpContractProgram(help.File) { InsertHelpWindow = true };
        try
        {
            var (dialog, child) = InsertFocusedDialogChild(app, 42);
            TEvent ev = HelpCommand();

            app.HandleEvent(ref ev);

            Assert.Equal(Events.evNothing, ev.What);
            Assert.Equal(1, app.ExecuteHelpCount);
            Assert.Same(app.LastHelpWindow, app.DeskTop.current);
            Assert.True(app.LastHelpWindow.GetState(Views.sfFocused));
            TEvent close = default;
            close.What = Events.evCommand;
            close.message.command = Views.cmClose;
            app.HandleEvent(ref close);

            Assert.Equal(Events.evNothing, close.What);
            Assert.Null(app.LastHelpWindow.owner);
            Assert.False(app.LastHelpWindow.GetState(Views.sfFocused));
            Assert.Same(dialog, app.DeskTop.current);
            Assert.Same(child, dialog.current);
            Assert.True(child.GetState(Views.sfFocused));
        }
        finally
        {
            app.ShutDown();
        }
    }

    [Fact]
    public void CmHelp_ConsumesEvent_WhenHelpFileExists()
    {
        using var help = BuildHelpFile();
        var app = new HelpContractProgram(help.File) { InsertHelpWindow = false };
        try
        {
            InsertFocusedDialogChild(app, 42);
            TEvent ev = HelpCommand();

            app.HandleEvent(ref ev);

            Assert.Equal(Events.evNothing, ev.What);
        }
        finally
        {
            app.ShutDown();
        }
    }

    [Fact]
    public void CmHelp_NoHelpFile_DoesNotOpenWindow()
    {
        var app = new HelpContractProgram(null);
        try
        {
            InsertFocusedDialogChild(app, 42);
            TEvent ev = HelpCommand();

            app.HandleEvent(ref ev);

            Assert.Equal(Events.evCommand, ev.What);
            Assert.Null(app.LastHelpWindow);
        }
        finally
        {
            app.ShutDown();
        }
    }

    [Fact]
    public void CmHelp_HcNoContext_OpensInvalidContextTopic()
    {
        using var help = BuildHelpFile();
        var app = new HelpContractProgram(help.File) { InsertHelpWindow = false };
        try
        {
            InsertFocusedDialogChild(app, Views.hcNoContext);
            TEvent ev = HelpCommand();

            app.HandleEvent(ref ev);

            Assert.Equal(Events.evNothing, ev.What);
            string text = TopicText(FindViewer(app.LastHelpWindow).topic);
            Assert.Contains("No help available in this context", text);
        }
        finally
        {
            app.ShutDown();
        }
    }

    [Fact]
    public void RepeatedHelpCloseBeforeNotificationCreatesFreshWindow()
    {
        using var help = BuildHelpFile();
        var app = new HelpContractProgram(help.File) { InsertHelpWindow = true };
        try
        {
            var (dialog, child) = InsertFocusedDialogChild(app, 42);
            for (int i = 0; i < 3; i++)
            {
                var request = HelpCommand();
                app.HandleEvent(ref request);
                var window = app.LastHelpWindow;
                Assert.Same(window, app.DeskTop.current);
                window.Close();
                Assert.Null(window.owner);
                Assert.False(window.GetState(Views.sfFocused));
                Assert.Same(dialog, app.DeskTop.current);
                Assert.Same(child, dialog.current);
            }
            Assert.Equal(3, app.ExecuteHelpCount);
        }
        finally { app.ShutDown(); }
    }

    [Fact]
    public void ClosingBackgroundHelpPreservesChosenWindowAndIgnoresOldNotification()
    {
        using var help = BuildHelpFile();
        var app = new HelpContractProgram(help.File) { InsertHelpWindow = true };
        try
        {
            var (dialog, child) = InsertFocusedDialogChild(app, 42);
            app.DeskTop.Insert(new TWindow(new TRect(3, 3, 32, 12), "Other", Views.wnNoNumber));
            var request = HelpCommand(); app.HandleEvent(ref request);
            var oldHelp = app.LastHelpWindow;
            // Select without changing the z-order, so help remains the front child.
            dialog.options &= unchecked((ushort)~Views.ofTopSelect);
            dialog.Select();
            oldHelp.Close();
            Assert.Same(dialog, app.DeskTop.current);
            Assert.True(child.GetState(Views.sfFocused));
            request = HelpCommand(); app.HandleEvent(ref request);
            var newHelp = app.LastHelpWindow;
            Assert.NotSame(oldHelp, newHelp);
            TEvent notification = default;
            notification.What = Events.evBroadcast;
            notification.message.command = Views.cmClosingWindow;
            notification.message.infoPtr = oldHelp;
            app.HandleEvent(ref notification);
            request = HelpCommand(); app.HandleEvent(ref request);
            Assert.Equal(2, app.ExecuteHelpCount);
            Assert.Same(newHelp, app.DeskTop.current);
        }
        finally { app.ShutDown(); }
    }

    [Fact]
    public void DesktopHelpCloseDoesNotSelectRemovedPreviousDialog()
    {
        using var help = BuildHelpFile();
        var app = new HelpContractProgram(help.File) { InsertHelpWindow = true };
        try
        {
            var (dialog, _) = InsertFocusedDialogChild(app, 42);
            var request = HelpCommand(); app.HandleEvent(ref request);
            dialog.ShutDown();
            app.LastHelpWindow.Close();
            Assert.Null(dialog.owner);
            Assert.NotSame(dialog, app.DeskTop.current);
            request = HelpCommand(); app.HandleEvent(ref request);
            Assert.Equal(Events.evNothing, request.What);
            Assert.Same(app.LastHelpWindow, app.DeskTop.current);
            app.LastHelpWindow.Close();
            Assert.NotSame(app.LastHelpWindow, app.DeskTop.current);
        }
        finally { app.ShutDown(); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void F1StatusBindingConsumesOnlyAvailableHelp(bool available)
    {
        using var help = BuildHelpFile();
        var app = new HelpContractProgram(available ? help.File : null) { InsertHelpWindow = true };
        try
        {
            InsertFocusedDialogChild(app, 42);
            var status = new TStatusLine(new TRect(0, 0, 80, 1),
                new TStatusDef(0, 0xffff) + new TStatusItem("Help", Keys.kbF1, Views.cmHelp));
            TEvent request = default;
            request.What = Events.evKeyDown;
            request.keyDown.keyCode = Keys.kbF1;
            status.HandleEvent(ref request);
            Assert.Equal(Events.evCommand, request.What);
            Assert.Equal(Views.cmHelp, request.message.command);
            app.HandleEvent(ref request);
            Assert.Equal(available ? Events.evNothing : Events.evCommand, request.What);
            if (available) Assert.NotNull(app.LastHelpWindow);
            else { Assert.Null(app.LastHelpWindow); Assert.Equal(Views.cmHelp, request.message.command); }
            status.ShutDown();
        }
        finally { app.ShutDown(); }
    }

    [Fact]
    public void ShutdownDoesNotLeakHelpIntoNextProgram()
    {
        using var help = BuildHelpFile();
        var first = new HelpContractProgram(help.File) { InsertHelpWindow = true };
        var request = HelpCommand();
        first.HandleEvent(ref request);
        first.ShutDown();
        var second = new HelpContractProgram(null) { InsertHelpWindow = true };
        try
        {
            request = HelpCommand(); second.HandleEvent(ref request);
            Assert.Equal(Events.evCommand, request.What);
            Assert.Null(second.LastHelpWindow);
            Assert.NotSame(first.LastHelpWindow, second.DeskTop.current);
        }
        finally { second.ShutDown(); }
    }

    private HelpFileHandle BuildHelpFile()
    {
        THelpFile.RegisterStreamableTypes();
        string path = Path.Combine(_tmp.Path, Guid.NewGuid().ToString("N") + ".hlp");
        var fpw = new Fpstream(path);
        var hfw = new THelpFile(fpw);

        var index = new THelpTopic();
        byte[] indexText = Encoding.Latin1.GetBytes("Index topic.\n");
        index.AddParagraph(new TParagraph
        {
            text = indexText,
            size = (ushort)indexText.Length,
            wrap = false,
        });
        hfw.RecordPositionInIndex(THelpViewer.IndexContext);
        hfw.PutTopic(index);

        var focused = new THelpTopic();
        byte[] focusedText = Encoding.Latin1.GetBytes("Focused topic.\n");
        focused.AddParagraph(new TParagraph
        {
            text = focusedText,
            size = (ushort)focusedText.Length,
            wrap = false,
        });
        hfw.RecordPositionInIndex(42);
        hfw.PutTopic(focused);

        hfw.Flush();
        fpw.Close();

        var fpr = new Fpstream(path);
        return new HelpFileHandle(fpr, new THelpFile(fpr));
    }

    private static (TDialog dialog, HelpProbeView child) InsertFocusedDialogChild(
        TProgram app, ushort helpCtx)
    {
        var dialog = new TDialog(new TRect(2, 2, 30, 10), "Dialog");
        var child = new HelpProbeView(new TRect(1, 1, 10, 2), helpCtx);
        dialog.Insert(child);
        child.Select();
        app.DeskTop.Insert(dialog);
        dialog.Select();
        return (dialog, child);
    }

    private static TEvent HelpCommand()
    {
        TEvent ev = default;
        ev.What = Events.evCommand;
        ev.message.command = Views.cmHelp;
        return ev;
    }

    private static THelpViewer FindViewer(THelpWindow window)
    {
        THelpViewer viewer = null;
        window.ForEachView(v =>
        {
            if (v is THelpViewer helpViewer)
                viewer = helpViewer;
        });
        return viewer ?? throw new InvalidOperationException("THelpViewer not found.");
    }

    private static string TopicText(THelpTopic topic)
    {
        var sb = new StringBuilder();
        for (var p = topic.paragraphs; p != null; p = p.next)
            sb.Append(p.Text);
        return sb.ToString();
    }

    private sealed class HelpProbeView : TView
    {
        public HelpProbeView(TRect bounds, ushort ctx)
            : base(bounds)
        {
            options |= Views.ofSelectable;
            helpCtx = ctx;
        }
    }

    private sealed class HelpContractProgram : TProgram
    {
        private readonly THelpFile _helpFile;

        public HelpContractProgram(THelpFile helpFile) => _helpFile = helpFile;

        public bool InsertHelpWindow { get; init; }
        public int ExecuteHelpCount { get; private set; }
        public THelpWindow LastHelpWindow { get; private set; }

        public override THelpFile GetHelpFile() => _helpFile;

        protected override void ExecuteHelp(THelpWindow window)
        {
            ExecuteHelpCount++;
            LastHelpWindow = window;
            if (!InsertHelpWindow)
                return;

            base.ExecuteHelp(window);
        }

    }

    private sealed class HelpFileHandle : IDisposable
    {
        private readonly Fpstream _stream;

        public HelpFileHandle(Fpstream stream, THelpFile file)
        {
            _stream = stream;
            File = file;
        }

        public THelpFile File { get; }

        public void Dispose() => _stream.Close();
    }
}
