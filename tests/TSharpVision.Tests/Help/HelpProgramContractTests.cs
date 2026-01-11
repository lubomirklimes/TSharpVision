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
        var app = new HelpContractProgram(help.File) { RunHelpModally = false };
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
        var app = new HelpContractProgram(help.File) { RunHelpModally = true };
        try
        {
            var (dialog, child) = InsertFocusedDialogChild(app, 42);
            TEvent ev = HelpCommand();

            app.HandleEvent(ref ev);

            Assert.Equal(Events.evNothing, ev.What);
            Assert.Equal(1, app.ExecuteHelpCount);
            Assert.Same(dialog, app.DeskTop.current);
            Assert.Same(child, dialog.current);
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
        var app = new HelpContractProgram(help.File) { RunHelpModally = false };
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
        var app = new HelpContractProgram(help.File) { RunHelpModally = false };
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
        private bool _closingHelp;
        private readonly THelpFile _helpFile;

        public HelpContractProgram(THelpFile helpFile) => _helpFile = helpFile;

        public bool RunHelpModally { get; init; }
        public int ExecuteHelpCount { get; private set; }
        public THelpWindow LastHelpWindow { get; private set; }

        public override THelpFile GetHelpFile() => _helpFile;

        protected override void ExecuteHelp(THelpWindow window)
        {
            ExecuteHelpCount++;
            LastHelpWindow = window;
            if (!RunHelpModally)
                return;

            _closingHelp = true;
            try
            {
                base.ExecuteHelp(window);
            }
            finally
            {
                _closingHelp = false;
            }
        }

        public override void GetEvent(ref TEvent @event)
        {
            if (_closingHelp)
            {
                @event = default;
                @event.What = Events.evCommand;
                @event.message.command = Views.cmClose;
                _closingHelp = false;
                return;
            }
            base.GetEvent(ref @event);
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
